using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Vasen.Web.Data;
using Vasen.Web.Models;

namespace Vasen.Web.Services;

public class MemberService(VasenDb db, UserManager<Member> users, RecurringDepositService recurring, TimeProvider clock)
{
    private static string TemporaryPassword() => Convert.ToHexString(RandomNumberGenerator.GetBytes(8)) + "aA1";

    private async Task<Member> ActorAsync(string actor)
    {
        var member = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == actor);
        if (member is not { IsActive: true, IsAnonymized: false, MustChangePassword: false })
            throw new InvalidOperationException("Logga in igen innan du hanterar konton.");
        if (!member.IsOwner) throw new UnauthorizedAccessException();
        return member;
    }

    private async Task<Member> EditableAsync(string id, string? stamp)
    {
        var member = await users.FindByIdAsync(id) ?? throw new InvalidOperationException("Kontot finns inte.");
        if (member.IsAnonymized) throw new InvalidOperationException("Kontot är anonymiserat och kan inte ändras.");
        if (member.ConcurrencyStamp != stamp) throw new InvalidOperationException("Kontot har ändrats. Ladda om sidan.");
        return member;
    }

    private static void Check(IdentityResult result)
    {
        if (result.Succeeded) return;
        throw new InvalidOperationException(string.Join(" ", result.Errors.Select(e => e.Code switch
        {
            "DuplicateUserName" => "E-postadressen används redan.",
            "InvalidUserName" => "Kontrollera e-postadressen.",
            "ConcurrencyFailure" => "Kontot har ändrats. Ladda om sidan.",
            _ => "Kontot kunde inte sparas. Kontrollera uppgifterna och försök igen."
        })));
    }

    public async Task ValidateContactAsync(string name, string email, string? exceptId = null)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100)
            throw new InvalidOperationException("Ange namn, högst 100 tecken.");
        if (string.IsNullOrWhiteSpace(email) || email.Trim().Length > 254 || !new EmailAddressAttribute().IsValid(email.Trim()))
            throw new InvalidOperationException("Ange en giltig e-postadress, högst 254 tecken.");
        var nameKey = users.NormalizeName(name.Trim());
        var emailKey = users.NormalizeEmail(email.Trim());
        // Normalize in .NET: SQLite upper() does not handle Swedish letters correctly.
        var others = await db.Users.AsNoTracking().Where(x => !x.IsAnonymized && x.Id != exceptId)
            .Select(x => new { x.DisplayName, x.UserName, x.Email }).ToListAsync();
        if (others.Any(x => users.NormalizeName(x.DisplayName) == nameKey || users.NormalizeName(x.UserName) == nameKey || users.NormalizeEmail(x.Email) == nameKey))
            throw new InvalidOperationException("Namnet används redan. Ange ett namn som skiljer personerna åt.");
        if (others.Any(x => users.NormalizeEmail(x.Email) == emailKey || users.NormalizeName(x.UserName) == emailKey || users.NormalizeName(x.DisplayName) == emailKey))
            throw new InvalidOperationException("E-postadressen används redan.");
    }

    private void Audit(string actor, string id, string action) => db.AuditEntries.Add(new AuditEntry
        { ActorId = actor, Entity = "Användare", Action = action, Detail = JsonSerializer.Serialize(new { MemberId = id }) });

    public async Task<(Member Member, string Password)> CreateAsync(string name, string email, string actor, MemberAccess accessLevel = MemberAccess.Reader)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await ActorAsync(actor);
        if (!Enum.IsDefined(accessLevel)) throw new InvalidOperationException("Välj en giltig behörighet.");
        await ValidateContactAsync(name, email);
        var member = new Member { AccessLevel = accessLevel, DisplayName = name.Trim(), Email = email.Trim(), UserName = email.Trim(), MustChangePassword = true };
        var password = TemporaryPassword();
        Check(await users.CreateAsync(member, password));
        Audit(actor, member.Id, "Skapat");
        await db.SaveChangesAsync(); await tx.CommitAsync();
        return (member, password);
    }

    public async Task SaveAsync(string id, string? stamp, string name, string email, bool isOwner, string actor, MemberAccess accessLevel = MemberAccess.Reader)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await ActorAsync(actor);
        var member = await EditableAsync(id, stamp);
        await ValidateContactAsync(name, email, id);
        if (!Enum.IsDefined(accessLevel)) throw new InvalidOperationException("Välj en giltig behörighet.");
        if (member.IsOwner != isOwner || member.AccessLevel != accessLevel)
        {
            // Access is read from the database on every request; existing sessions immediately use the new level.
            db.AuditEntries.Add(new AuditEntry { ActorId = actor, Entity = "Användare", Action = "Behörighet ändrad",
                Detail = JsonSerializer.Serialize(new { MemberId = id, Before = member.AccessLevel, After = accessLevel, IsOwner = isOwner }) });
        }
        member.AccessLevel = accessLevel;
        if (member.IsOwner != isOwner)
        {
            if (!isOwner && member.IsActive && await db.Users.CountAsync(x => x.IsOwner && x.IsActive && !x.IsAnonymized) <= 1)
                throw new InvalidOperationException("Minst en ägare måste ha ett aktivt konto.");
            // Settle due periods before changing ownership, so later promotion cannot backfill non-owner months.
            await recurring.ProcessDueAsync(VasenDate.Today(clock));
            db.AuditEntries.Add(new AuditEntry { ActorId = actor, Entity = "Användare", Action = "Ägarmarkering ändrad",
                Detail = JsonSerializer.Serialize(new { MemberId = id, Before = member.IsOwner, After = isOwner }) });
            member.IsOwner = isOwner;
        }
        member.DisplayName = name.Trim(); member.UserName = email.Trim();
        if (!string.Equals(member.Email, email.Trim(), StringComparison.OrdinalIgnoreCase)) member.EmailConfirmed = false;
        member.Email = email.Trim();
        Check(await users.UpdateAsync(member));
        Audit(actor, id, "Ändrat");
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }

    public async Task SetActiveAsync(string id, string? stamp, bool active, string actor)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await ActorAsync(actor);
        var member = await EditableAsync(id, stamp);
        if (id == actor && !active) throw new InvalidOperationException("Du kan inte pausa ditt eget konto.");
        if (!active && member.IsOwner && await db.Users.CountAsync(x => x.IsOwner && x.IsActive && !x.IsAnonymized) <= 1)
            throw new InvalidOperationException("Minst en ägare måste ha ett aktivt konto.");
        // Consume due periods under the previous state, including months spent paused.
        await recurring.ProcessDueAsync(VasenDate.Today(clock));
        member.IsActive = active;
        member.SecurityStamp = Guid.NewGuid().ToString();
        Check(await users.UpdateAsync(member));
        Audit(actor, id, active ? "Återaktiverat" : "Pausat");
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }

    public async Task<string> ResetPasswordAsync(string id, string? stamp, string actor)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await ActorAsync(actor);
        var member = await EditableAsync(id, stamp);
        if (id == actor) throw new InvalidOperationException("Byt ditt eget lösenord under Mitt konto.");
        var password = TemporaryPassword();
        member.MustChangePassword = true;
        var token = await users.GeneratePasswordResetTokenAsync(member);
        Check(await users.ResetPasswordAsync(member, token, password));
        // Reset also releases a forgotten-password account from sign-in lockout.
        member.LockoutEnd = null; member.AccessFailedCount = 0;
        Check(await users.UpdateAsync(member));
        Audit(actor, id, "Lösenord återställt");
        await db.SaveChangesAsync(); await tx.CommitAsync();
        return password;
    }

    public async Task AnonymizeAsync(string id, string? stamp, string actor)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        if (!(await ActorAsync(actor)).IsOwner) throw new UnauthorizedAccessException();
        var member = await EditableAsync(id, stamp);
        if (member.IsActive) throw new InvalidOperationException("Pausa kontot innan det anonymiseras.");
        if (id == actor) throw new InvalidOperationException("Du kan inte anonymisera ditt eget konto.");
        member.DisplayName = "Anonymiserad person";
        member.UserName = "removed-" + Guid.NewGuid().ToString("N");
        member.Email = null; member.NormalizedEmail = null; member.EmailConfirmed = false;
        member.PhoneNumber = null; member.PhoneNumberConfirmed = false;
        member.PasswordHash = null; member.TwoFactorEnabled = false;
        member.LockoutEnd = null; member.AccessFailedCount = 0;
        member.MustChangePassword = false; member.IsAnonymized = true;
        member.SecurityStamp = Guid.NewGuid().ToString();
        Check(await users.UpdateAsync(member));
        await db.Set<IdentityUserLogin<string>>().Where(x => x.UserId == id).ExecuteDeleteAsync();
        await db.Set<IdentityUserToken<string>>().Where(x => x.UserId == id).ExecuteDeleteAsync();
        await db.Set<IdentityUserClaim<string>>().Where(x => x.UserId == id).ExecuteDeleteAsync();
        await db.Set<IdentityUserRole<string>>().Where(x => x.UserId == id).ExecuteDeleteAsync();
        var rules = await db.DepositRules.Where(x => x.MemberId == id).ToListAsync();
        foreach (var rule in rules) { rule.IsPaused = true; rule.Version = Guid.NewGuid(); }
        Audit(actor, id, "Anonymiserat");
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
}
