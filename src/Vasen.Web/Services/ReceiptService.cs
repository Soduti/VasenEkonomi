using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Vasen.Web.Data;
using Vasen.Web.Models;
namespace Vasen.Web.Services;

public class ReceiptService(VasenDb db, FileStore files)
{
    public async Task<int> CreateAsync(DateOnly date, string memberId, long ore, string description,
        string? category, IReadOnlyList<IFormFile> uploads, string actor)
    {
        if (!AccessRules.CanRegister(await AccessRules.ActorAsync(db, actor))) throw new UnauthorizedAccessException();
        Validate(date, ore, description, category);
        if (!await db.Users.AnyAsync(x => x.Id == memberId && x.IsActive && !x.IsAnonymized)) throw new InvalidOperationException("Välj en aktiv person.");
        var attachments = await files.SaveAsync(uploads);
        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            var receipt = new Receipt { CreatedById = actor, Date = date, MemberId = memberId, AmountOre = ore,
                Description = description.Trim(), Category = Clean(category), Files = attachments };
            db.Receipts.Add(receipt);
            await db.SaveChangesAsync();
            Audit(actor, "Kvitto", receipt.Id, "Skapat", Snapshot(receipt));
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return receipt.Id;
        }
        catch { files.Delete(attachments); throw; }
    }

    public async Task EditAsync(int id, Guid version, DateOnly date, long ore, string description,
        string? category, IReadOnlyList<IFormFile> uploads, string actor)
    {
        Validate(date, ore, description, category);
        var receipt = await db.Receipts.Include(r => r.Files).SingleAsync(r => r.Id == id && r.DeletedUtc == null);
        if (!AccessRules.CanEdit(await AccessRules.ActorAsync(db, actor), receipt)) throw new UnauthorizedAccessException();
        CheckVersion(receipt, version);
        var before = Snapshot(receipt);
        var attachments = await files.SaveAsync(uploads);
        try
        {
            receipt.Date = date; receipt.AmountOre = ore; receipt.Description = description.Trim();
            receipt.Category = Clean(category); receipt.Version = Guid.NewGuid(); receipt.Files.AddRange(attachments);
            Audit(actor, "Kvitto", id, "Ändrat", new { Before = before, After = Snapshot(receipt) });
            await db.SaveChangesAsync();
        }
        catch { files.Delete(attachments); throw; }
    }

    public async Task<int> ReimburseAsync(int[] ids, DateOnly date, long amount, string? comment, string actor,
        bool countDifferenceAsOwnContribution = false)
    {
        await AccessRules.RequireOwnerAsync(db, actor);
        if (ids.Length == 0 || ids.Length != ids.Distinct().Count()) throw new InvalidOperationException("Markera minst ett kvitto.");
        Validate(date, amount, "Ersättning", null, allowZero: countDifferenceAsOwnContribution);
        if (comment?.Length > 300) throw new InvalidOperationException("Kommentaren får vara högst 300 tecken.");
        await using var tx = await db.Database.BeginTransactionAsync();
        var receipts = await db.Receipts.Where(r => ids.Contains(r.Id) && r.DeletedUtc == null && r.WithdrawalId == null).ToListAsync();
        if (receipts.Count != ids.Length) throw new InvalidOperationException("Urvalet har ändrats. Något kvitto är redan ersatt eller borttaget.");
        if (receipts.Select(r => r.MemberId).Distinct().Count() != 1) throw new InvalidOperationException("Välj kvitton för en person åt gången.");
        var receiptTotal = receipts.Sum(r => r.AmountOre);
        if (countDifferenceAsOwnContribution && amount > receiptTotal)
            throw new InvalidOperationException("Uttaget får inte överstiga kvittosumman när skillnaden ska räknas som eget bidrag.");
        var withdrawal = new Withdrawal { MemberId = receipts[0].MemberId, Date = date, AmountOre = amount,
            OriginalReceiptTotalOre = receiptTotal,
            OwnContributionOre = countDifferenceAsOwnContribution ? receiptTotal - amount : 0,
            Comment = Clean(comment), Receipts = receipts };
        foreach (var receipt in receipts) receipt.Version = Guid.NewGuid();
        db.Withdrawals.Add(withdrawal);
        await db.SaveChangesAsync();
        Audit(actor, "Uttag", withdrawal.Id, "Registrerat", new { ReceiptIds = ids, date, amount, withdrawal.OriginalReceiptTotalOre, withdrawal.OwnContributionOre });
        await db.SaveChangesAsync(); await tx.CommitAsync();
        return withdrawal.Id;
    }

    public async Task UndoAsync(int id, Guid version, string actor)
    {
        await AccessRules.RequireOwnerAsync(db, actor);
        var withdrawal = await db.Withdrawals.Include(w => w.Receipts).SingleAsync(w => w.Id == id);
        if (withdrawal.Version != version || withdrawal.IsUndone) throw new InvalidOperationException("Uttaget har redan ändrats. Ladda om sidan.");
        foreach (var receipt in withdrawal.Receipts) { receipt.WithdrawalId = null; receipt.Version = Guid.NewGuid(); }
        withdrawal.IsUndone = true; withdrawal.Version = Guid.NewGuid();
        Audit(actor, "Uttag", id, "Ångrat", new { withdrawal.AmountOre, withdrawal.MemberId, withdrawal.OwnContributionOre });
        await db.SaveChangesAsync();
    }

    public async Task TrashAsync(int id, Guid version, bool restore, string actor)
    {
        await AccessRules.RequireOwnerAsync(db, actor);
        var receipt = await db.Receipts.SingleAsync(r => r.Id == id);
        CheckVersion(receipt, version);
        receipt.DeletedUtc = restore ? null : DateTime.UtcNow; receipt.Version = Guid.NewGuid();
        Audit(actor, "Kvitto", id, restore ? "Återställt" : "Till papperskorg", Snapshot(receipt));
        await db.SaveChangesAsync();
    }

    public async Task DeletePermanentlyAsync(int id, Guid version, string actor)
    {
        await AccessRules.RequireOwnerAsync(db, actor);
        var receipt = await db.Receipts.Include(r => r.Files).SingleAsync(r => r.Id == id && r.DeletedUtc != null);
        CheckVersion(receipt, version);
        var attachments = receipt.Files.ToList();
        Audit(actor, "Kvitto", id, "Permanent raderat", Snapshot(receipt));
        db.Receipts.Remove(receipt);
        await db.SaveChangesAsync();
        files.Delete(attachments);
    }

    private static void CheckVersion(Receipt r, Guid version)
    { if (r.Version != version) throw new InvalidOperationException("Kvittot har ändrats av någon annan. Ladda om sidan."); }
    private void Audit(string actor, string entity, int id, string action, object detail) =>
        db.AuditEntries.Add(new AuditEntry { ActorId = actor, Entity = entity, EntityId = id, Action = action, Detail = JsonSerializer.Serialize(detail) });
    private static object Snapshot(Receipt r) => new { r.Date, r.MemberId, r.AmountOre, r.Description, r.Category, r.WithdrawalId, r.DeletedUtc };
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static void Validate(DateOnly date, long ore, string description, string? category, bool allowZero = false)
    {
        if (date == default || ore < (allowZero ? 0 : 1) || ore > 10_000_000_000 || string.IsNullOrWhiteSpace(description) || description.Length > 300 || category?.Length > 80)
            throw new InvalidOperationException("Kontrollera datum, belopp, beskrivning och kostnadsslag.");
    }
}
