using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Vasen.Web.Data;
using Vasen.Web.Models;
using Vasen.Web.Services;

namespace Vasen.Tests;

public class AccessTests
{
    private static HttpClient Client(VasenFactory f) => f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    private static string Value(string html, string name)
    {
        var match = Regex.Match(html, "name=\"" + Regex.Escape(name) + "\"[^>]*value=\"([^\"]*)\"");
        Assert.True(match.Success, "Missing " + name);
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
    private static FormUrlEncodedContent Form(string html, Dictionary<string, string>? values = null)
    {
        values ??= new();
        values["__RequestVerificationToken"] = Value(html, "__RequestVerificationToken");
        return new(values);
    }
    private static void Denied(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/AccessDenied", response.Headers.Location!.PathAndQuery);
    }
    private static async Task<Member> Seed(VasenFactory f, string name, MemberAccess level = MemberAccess.Reader, bool owner = false)
    {
        using var scope = f.Services.CreateScope();
        var member = new Member { UserName = name, DisplayName = name, Email = name + "@example.test", IsOwner = owner, AccessLevel = level };
        Assert.True((await scope.ServiceProvider.GetRequiredService<UserManager<Member>>().CreateAsync(member, "TestPassword123")).Succeeded);
        return member;
    }
    private static async Task Login(HttpClient client, Member member)
    {
        var login = await client.GetStringAsync("/Account/Login");
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Account/Login", Form(login, new()
            { ["LoginName"] = member.Email!, ["Password"] = "TestPassword123" }))).StatusCode);
    }
    private static async Task<int> Receipt(VasenFactory f, Member actor, Member person, bool attachment = false)
    {
        using var scope = f.Services.CreateScope();
        var bytes = Encoding.ASCII.GetBytes("%PDF-1.4\nTest receipt");
        using var stream = new MemoryStream(bytes);
        IFormFile[] files = attachment ? [new FormFile(stream, 0, bytes.Length, "Uploads", "receipt.pdf")] : [];
        return await scope.ServiceProvider.GetRequiredService<ReceiptService>()
            .CreateAsync(new(2026, 11, 10), person.Id, 12345, "Registrerat av " + actor.DisplayName, null, files, actor.Id);
    }
    private static async Task<Receipt> ReadReceipt(VasenFactory f, int id)
    {
        using var scope = f.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<VasenDb>().Receipts.AsNoTracking().SingleAsync(x => x.Id == id);
    }

    [Fact]
    public async Task ReaderCanReadAllEconomyAndAttachmentsButCannotOpenOrPostAnyMutation()
    {
        using var f = new VasenFactory(); using var client = Client(f);
        var owner = await Seed(f, "owner", owner: true); var reader = await Seed(f, "reader");
        var receipt = await Receipt(f, owner, owner, attachment: true);
        await Login(client, reader);
        foreach (var path in new[] { "/", "/Budget", "/Expenses", "/Deposits", "/Withdrawals", "/Receipts/Edit/" + receipt, "/Receipts/File/1" })
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);
        var view = await client.GetStringAsync("/Receipts/Edit/" + receipt);
        Assert.DoesNotContain("Spara utlägg", view); Assert.DoesNotContain("type=\"file\"", view);
        Assert.Contains("/Receipts/File/1", view); Assert.Contains("Registrerat av", view);
        var list = await client.GetStringAsync("/");
        Assert.DoesNotContain("Lägg till kvitto", list); Assert.DoesNotContain("name=\"ids\"", list);
        var profile = await client.GetStringAsync("/Account/Profile");
        Assert.DoesNotContain("href=\"/Members", profile); Assert.DoesNotContain("href=\"/Trash", profile);
        Assert.Contains("Läsare", WebUtility.HtmlDecode(profile));
        foreach (var path in new[] { "/Receipts/Edit", "/Budget/Edit", "/Expenses/Edit", "/Deposits/Edit", "/Deposits/Rule",
            "/Withdrawals/Create?ids=" + receipt, "/Members", "/Members/Edit", "/Members/Cleanup", "/Trash" })
            Denied(await client.GetAsync(path));
        foreach (var path in new[] { "/Receipts/Edit", "/Receipts/Edit/" + receipt, "/Receipts/Edit/" + receipt + "?handler=Trash",
            "/Budget?handler=Opening", "/Budget/Edit", "/Budget/Edit/1?handler=Delete", "/Expenses/Edit", "/Expenses/Edit/1?handler=Void",
            "/Deposits/Edit", "/Deposits/Edit/1?handler=Void", "/Deposits/Rule", "/Withdrawals/Create", "/Withdrawals/Details/1?handler=Undo",
            "/Members/Edit", "/Members/Edit/" + owner.Id + "?handler=Reset", "/Members/Edit/" + owner.Id + "?handler=Active", "/Members/Cleanup", "/Trash?handler=Restore", "/Trash?handler=Delete" })
            Denied(await client.PostAsync(path, Form(profile, new() { ["Id"] = receipt.ToString(), ["Amount"] = "999,00", ["Description"] = "Forged", ["Person"] = reader.Id, ["Date"] = "2026-11-10" })));
        Assert.Equal(12345, (await ReadReceipt(f, receipt)).AmountOre);
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VasenDb>();
        Assert.Equal(2, await db.Users.CountAsync()); Assert.Equal(1, await db.Receipts.CountAsync());
        Assert.Empty(await db.Withdrawals.ToListAsync()); Assert.Empty(await db.BudgetLines.ToListAsync());
        Assert.Empty(await db.SharedExpenses.ToListAsync()); Assert.Empty(await db.Deposits.ToListAsync());
        var artifacts = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/access-check"));
        Directory.CreateDirectory(artifacts);
        await File.WriteAllTextAsync(Path.Combine(artifacts, "reader-receipt.html"), view);
        await File.WriteAllTextAsync(Path.Combine(artifacts, "reader-budget.html"), await client.GetStringAsync("/Budget"));
        await File.WriteAllTextAsync(Path.Combine(artifacts, "reader-list.html"), list);
    }

    [Fact]
    public async Task RegistrarChoosesAnyActivePersonAndOnlyEditsTheirOwnRegistrationsUntilClosure()
    {
        using var f = new VasenFactory(); using var client = Client(f);
        var owner = await Seed(f, "owner", owner: true);
        var registrar = await Seed(f, "registrar", MemberAccess.ReceiptRegistrar);
        var reader = await Seed(f, "reader");
        var paused = await Seed(f, "paused");
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VasenDb>();
            (await db.Users.FindAsync(paused.Id))!.IsActive = false; await db.SaveChangesAsync();
        }
        await Login(client, registrar);
        var create = await client.GetStringAsync("/Receipts/Edit");
        foreach (var id in new[] { owner.Id, reader.Id, registrar.Id }) Assert.Contains("value=\"" + id + "\"", create);
        Assert.DoesNotContain("value=\"" + paused.Id + "\"", create);
        var created = await client.PostAsync("/Receipts/Edit", Form(create, new()
        {
            ["Amount"] = "123,45", ["Description"] = "På uppdrag", ["Date"] = "2026-11-10", ["Person"] = reader.Id,
            ["CreatedById"] = owner.Id, ["AccessLevel"] = "999", ["IsOwner"] = "true"
        }));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var receipt = await ReadReceipt(f, 1);
        Assert.Equal(registrar.Id, receipt.CreatedById); Assert.Equal(reader.Id, receipt.MemberId);
        var edit = await client.GetStringAsync("/Receipts/Edit/1");
        Assert.Contains("Spara utlägg", edit);
        var artifacts = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/access-check"));
        Directory.CreateDirectory(artifacts);
        await File.WriteAllTextAsync(Path.Combine(artifacts, "registrar-receipt.html"), edit);
        var saved = await client.PostAsync("/Receipts/Edit/1", Form(edit, new()
        {
            ["Id"] = "1", ["Version"] = receipt.Version.ToString(), ["Amount"] = "234,56",
            ["Date"] = "2026-11-10", ["Person"] = owner.Id, ["Description"] = "Rättat"
        }));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        receipt = await ReadReceipt(f, 1);
        Assert.Equal(23456, receipt.AmountOre); Assert.Equal(reader.Id, receipt.MemberId); Assert.Equal(registrar.Id, receipt.CreatedById);
        var someoneElse = await Receipt(f, owner, registrar);
        Assert.DoesNotContain("Spara utlägg", await client.GetStringAsync("/Receipts/Edit/" + someoneElse));
        var token = await client.GetStringAsync("/Account/Profile");
        Denied(await client.PostAsync("/Receipts/Edit/" + someoneElse, Form(token, new() { ["Id"] = someoneElse.ToString() })));
        Denied(await client.PostAsync("/Receipts/Edit/1?handler=Trash", Form(token, new() { ["Id"] = "1", ["Version"] = receipt.Version.ToString() })));
        using (var scope = f.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ReceiptService>().ReimburseAsync([1], new(2026,11,11), 23456, null, owner.Id);
        Assert.DoesNotContain("Spara utlägg", await client.GetStringAsync("/Receipts/Edit/1"));
        Denied(await client.PostAsync("/Receipts/Edit/1", Form(edit, new() { ["Id"] = "1", ["Version"] = receipt.Version.ToString() })));
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VasenDb>();
            var withdrawal = await db.Withdrawals.SingleAsync();
            await scope.ServiceProvider.GetRequiredService<ReceiptService>().UndoAsync(withdrawal.Id, withdrawal.Version, owner.Id);
        }
        Assert.Contains("Spara utlägg", await client.GetStringAsync("/Receipts/Edit/1"));
        using var ownerClient = Client(f); await Login(ownerClient, owner);
        Assert.Contains("Spara utlägg", await ownerClient.GetStringAsync("/Receipts/Edit/" + someoneElse));
        Assert.Contains("Spara utlägg", await ownerClient.GetStringAsync("/Receipts/Edit/1"));
    }

    [Theory]
    [InlineData(MemberAccess.Reader)]
    [InlineData(MemberAccess.ReceiptRegistrar)]
    public async Task NonOwnersCannotManageAccountsOrWriteFinanceEvenThroughServices(MemberAccess level)
    {
        using var f = new VasenFactory();
        var owner = await Seed(f, "owner", owner: true); var other = await Seed(f, "other", level);
        var receiptId = await Receipt(f, owner, other);
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VasenDb>();
        var receipts = scope.ServiceProvider.GetRequiredService<ReceiptService>();
        var receipt = await db.Receipts.SingleAsync(x => x.Id == receiptId);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => receipts.EditAsync(receipt.Id, receipt.Version, receipt.Date, 200, "Forged", null, [], other.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => receipts.ReimburseAsync([receipt.Id], receipt.Date, 12345, null, other.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => receipts.TrashAsync(receipt.Id, receipt.Version, false, other.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => receipts.DeletePermanentlyAsync(receipt.Id, receipt.Version, other.Id));
        if (level == MemberAccess.Reader) await Assert.ThrowsAsync<UnauthorizedAccessException>(() => receipts.CreateAsync(receipt.Date, other.Id, 10, "Forged", null, [], other.Id));
        var members = scope.ServiceProvider.GetRequiredService<MemberService>();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => members.CreateAsync("Forged", "forged@example.test", other.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => members.SaveAsync(owner.Id, owner.ConcurrencyStamp, owner.DisplayName, owner.Email!, true, other.Id));
        var finance = scope.ServiceProvider.GetRequiredService<FinanceService>();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => finance.SaveOpeningAsync(2026, 10, Guid.Empty, other.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => finance.SaveBudgetAsync(null, Guid.Empty, 2026, ExpenseSource.PrivateReceipt, "", 10, other.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => finance.SaveExpenseAsync(null, Guid.Empty, 2026, "El", 10, null, other.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => finance.SaveDepositAsync(null, Guid.Empty, new(2026,11,11), owner.Id, 10, null, other.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => scope.ServiceProvider.GetRequiredService<RecurringDepositService>().SaveRuleAsync(null, Guid.Empty, owner.Id, 10, 25, new(2026,1,1), null, false, new(2026,11,11), other.Id));
        Assert.Equal(2, await db.Users.CountAsync()); Assert.Empty(await db.BudgetLines.ToListAsync());
    }

    [Fact]
    public async Task AccessChangesTakeEffectOnExistingSessionAndNewAccountsDefaultToReader()
    {
        using var f = new VasenFactory(); using var client = Client(f);
        var owner = await Seed(f, "owner", owner: true); var registrar = await Seed(f, "registrar", MemberAccess.ReceiptRegistrar);
        await Login(client, registrar); Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Receipts/Edit")).StatusCode);
        using (var scope = f.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<Member>>();
            var member = (await users.FindByIdAsync(registrar.Id))!;
            await scope.ServiceProvider.GetRequiredService<MemberService>().SaveAsync(member.Id, member.ConcurrencyStamp, member.DisplayName, member.Email!, false, owner.Id, MemberAccess.Reader);
        }
        Denied(await client.GetAsync("/Receipts/Edit")); Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);
        using (var scope = f.Services.CreateScope())
        {
            var created = await scope.ServiceProvider.GetRequiredService<MemberService>().CreateAsync("New reader", "new@example.test", owner.Id);
            Assert.Equal(MemberAccess.Reader, created.Member.AccessLevel); Assert.False(created.Member.IsOwner); Assert.True(created.Member.MustChangePassword);
            var registrarAccount = await scope.ServiceProvider.GetRequiredService<MemberService>().CreateAsync("New registrar", "registrar2@example.test", owner.Id, MemberAccess.ReceiptRegistrar);
            Assert.Equal(MemberAccess.ReceiptRegistrar, registrarAccount.Member.AccessLevel);
        }
    }

    [Fact]
    public async Task OwnerCanChooseRegistrarWhenInvitingAndInvalidLevelsAreRejected()
    {
        using var f = new VasenFactory(); using var client = Client(f);
        var owner = await Seed(f, "owner", owner: true); await Login(client, owner);
        var html = await client.GetStringAsync("/Members/Edit");
        var rejected = await client.PostAsync("/Members/Edit", Form(html, new()
            { ["DisplayName"] = "Invalid", ["Email"] = "invalid@example.test", ["AccessLevel"] = "999" }));
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        using (var scope = f.Services.CreateScope()) Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<VasenDb>().Users.CountAsync());
        var invited = await client.PostAsync("/Members/Edit", Form(html, new()
            { ["DisplayName"] = "New registrar", ["Email"] = "invited@example.test", ["AccessLevel"] = "1" }));
        Assert.Equal(HttpStatusCode.OK, invited.StatusCode);
        Assert.Contains("temporary-password", await invited.Content.ReadAsStringAsync());
        using (var scope = f.Services.CreateScope())
        {
            var member = await scope.ServiceProvider.GetRequiredService<VasenDb>().Users.SingleAsync(x => x.Email == "invited@example.test");
            Assert.Equal(MemberAccess.ReceiptRegistrar, member.AccessLevel); Assert.True(member.MustChangePassword);
            var artifacts = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/access-check"));
            Directory.CreateDirectory(artifacts);
            await File.WriteAllTextAsync(Path.Combine(artifacts, "owner-member.html"), await client.GetStringAsync("/Members/Edit/" + member.Id));
        }
    }

    [Fact]
    public async Task UpgradePreservesOwnershipAndFindsOriginalCreatorWithoutGuessingFromResponsiblePerson()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new VasenDb(new DbContextOptionsBuilder<VasenDb>().UseSqlite(connection).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(db.Database.GetMigrations().Single(x => x.EndsWith("_AddMemberAdministration")));
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO AspNetUsers (Id,UserName,DisplayName,IsOwner,IsActive,IsAnonymized,MustChangePassword,AccessFailedCount,EmailConfirmed,LockoutEnabled,PhoneNumberConfirmed,TwoFactorEnabled)
            VALUES ('owner','owner','Owner',1,1,0,0,0,0,0,0,0),('actor','actor','Actor',0,1,0,0,0,0,0,0,0),('person','person','Person',0,1,0,0,0,0,0,0,0);
            INSERT INTO Receipts (Id,MemberId,Date,AmountOre,Description,CreatedUtc,Version)
            VALUES (1,'person','2026-11-10',12345,'Historical','2026-11-10','11111111-1111-1111-1111-111111111111'),
            (2,'actor','2026-11-10',12345,'Unknown creator','2026-11-10','11111111-1111-1111-1111-111111111111');
            INSERT INTO AuditEntries (ActorId,Entity,EntityId,Action,Detail,AtUtc)
            VALUES ('actor','Kvitto',1,'Skapat','{{}}','2026-11-10'),('owner','Kvitto',1,'Skapat','{{}}','2026-11-11');
            """);
        await migrator.MigrateAsync();
        Assert.True((await db.Users.SingleAsync(x => x.Id == "owner")).IsOwner);
        Assert.Equal(MemberAccess.ReceiptRegistrar, (await db.Users.SingleAsync(x => x.Id == "actor")).AccessLevel);
        var receipt = await db.Receipts.SingleAsync(x => x.Id == 1);
        Assert.Equal("actor", receipt.CreatedById); Assert.Equal("person", receipt.MemberId); Assert.Equal(12345, receipt.AmountOre);
        Assert.Null((await db.Receipts.SingleAsync(x => x.Id == 2)).CreatedById);
    }
}
