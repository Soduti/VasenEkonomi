using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vasen.Web.Data;
using Vasen.Web.Models;
using Vasen.Web.Services;

namespace Vasen.Tests;

public class MemberTests
{
    private static HttpClient Client(VasenFactory factory) => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    private static string Value(string html, string name)
    {
        var match = Regex.Match(html, "(?:name|id)=\"" + Regex.Escape(name) + "\"[^>]*value=\"([^\"]*)\"");
        Assert.True(match.Success, "Missing input: " + name);
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
    private static FormUrlEncodedContent Form(string html, Dictionary<string, string> values)
    {
        values["__RequestVerificationToken"] = Value(html, "__RequestVerificationToken");
        return new(values);
    }
    private static async Task<Member> Seed(VasenFactory factory, string login, bool owner = false, bool temporary = false)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<Member>>();
        var member = new Member { DisplayName = "Person " + login, UserName = login, Email = login + "@example.test", IsOwner = owner, MustChangePassword = temporary };
        Assert.True((await users.CreateAsync(member, "TestPassword123")).Succeeded);
        return member;
    }
    private static async Task<Member> Read(VasenFactory factory, string id)
    {
        using var scope = factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<UserManager<Member>>().FindByIdAsync(id))!;
    }
    private static async Task<HttpResponseMessage> Login(HttpClient client, string login, string password = "TestPassword123")
    {
        var html = await client.GetStringAsync("/Account/Login");
        return await client.PostAsync("/Account/Login", Form(html, new() { ["LoginName"] = login, ["Password"] = password, ["RememberMe"] = "true" }));
    }
    private static async Task<HttpResponseMessage> Action(HttpClient client, string id, string handler, Dictionary<string, string>? values = null)
    {
        var html = await client.GetStringAsync("/Members/Edit/" + id);
        values ??= new(); values["Stamp"] = Value(html, "Stamp");
        return await client.PostAsync("/Members/Edit/" + id + "?handler=" + handler, Form(html, values));
    }

    [Fact]
    public async Task OwnerCreatesAccountWithNameAndEmailAndTemporaryLoginIsConfinedToPasswordChoice()
    {
        using var factory = new VasenFactory(); using var admin = Client(factory); using var person = Client(factory);
        var actor = await Seed(factory, "ordinary", owner: true);
        Assert.Equal(HttpStatusCode.Redirect, (await Login(admin, actor.UserName!)).StatusCode);
        Assert.Contains("Användare", await admin.GetStringAsync("/Account/Profile"));
        var form = await admin.GetStringAsync("/Members/Edit");
        Assert.Contains("type=\"email\"", form);
        var created = await admin.PostAsync("/Members/Edit", Form(form, new() { ["DisplayName"] = "Åsa Söderhäll", ["Email"] = "asa@example.test" }));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var password = Value(await created.Content.ReadAsStringAsync(), "temporary-password");
        using var scope = factory.Services.CreateScope();
        var member = (await scope.ServiceProvider.GetRequiredService<UserManager<Member>>().FindByEmailAsync("asa@example.test"))!;
        Assert.Equal("asa@example.test", member.Email); Assert.Equal("Åsa Söderhäll", member.DisplayName); Assert.True(member.MustChangePassword); Assert.False(member.IsOwner);
        var signedIn = await Login(person, " åSA sÖDERHÄLL ", password);
        Assert.Equal("/Account/ChoosePassword", signedIn.Headers.Location!.OriginalString);
        foreach (var path in new[] { "/", "/Account/Profile", "/Members", "/Receipts/File/1", "/Members/Cleanup" })
        {
            var blocked = await person.GetAsync(path);
            Assert.Equal("/Account/ChoosePassword", blocked.Headers.Location!.OriginalString);
        }
        var blockedPost = await person.PostAsync("/Members/Edit/" + actor.Id + "?handler=Active", new FormUrlEncodedContent(new Dictionary<string, string> { ["active"] = "false" }));
        Assert.Equal("/Account/ChoosePassword", blockedPost.Headers.Location!.OriginalString);
        Assert.True((await Read(factory, actor.Id)).IsActive);
        var choose = await person.GetStringAsync("/Account/ChoosePassword");
        var same = await person.PostAsync("/Account/ChoosePassword", Form(choose, new() { ["NewPassword"] = password, ["ConfirmPassword"] = password }));
        Assert.Contains("tillfälliga", WebUtility.HtmlDecode(await same.Content.ReadAsStringAsync()));
        var mismatch = await person.PostAsync("/Account/ChoosePassword", Form(choose, new() { ["NewPassword"] = "MyNewPassword456", ["ConfirmPassword"] = "OtherPassword456" }));
        Assert.Equal(HttpStatusCode.OK, mismatch.StatusCode);
        Assert.True((await Read(factory, member.Id)).MustChangePassword);
        var weak = await person.PostAsync("/Account/ChoosePassword", Form(choose, new() { ["NewPassword"] = "abcdefghijk", ["ConfirmPassword"] = "abcdefghijk" }));
        Assert.Equal(HttpStatusCode.OK, weak.StatusCode);
        Assert.True((await Read(factory, member.Id)).MustChangePassword);
        var saved = await person.PostAsync("/Account/ChoosePassword", Form(choose, new() { ["NewPassword"] = "MyNewPassword456", ["ConfirmPassword"] = "MyNewPassword456" }));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        Assert.False((await Read(factory, member.Id)).MustChangePassword);
        Assert.Equal(HttpStatusCode.OK, (await person.GetAsync("/")).StatusCode);
        // Temporary credentials are never placed in HTML on subsequent visits.
        Assert.DoesNotContain(password, await admin.GetStringAsync("/Members/Edit/" + member.Id));
    }

    [Fact]
    public async Task ResetInvalidatesExistingSessionAndUnlocksAccountAndForcesNewPassword()
    {
        using var factory = new VasenFactory(); using var admin = Client(factory); using var person = Client(factory);
        var actor = await Seed(factory, "admin", owner: true); var target = await Seed(factory, "target");
        await Login(admin, actor.UserName!); await Login(person, target.UserName!);
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<Member>>();
            var member = (await users.FindByIdAsync(target.Id))!;
            await users.SetLockoutEndDateAsync(member, DateTimeOffset.UtcNow.AddHours(1));
        }
        var reset = await Action(admin, target.Id, "Reset");
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        var password = Value(await reset.Content.ReadAsStringAsync(), "temporary-password");
        Assert.Equal(HttpStatusCode.Redirect, (await person.GetAsync("/")).StatusCode);
        var memberAfter = await Read(factory, target.Id);
        Assert.True(memberAfter.MustChangePassword); Assert.Null(memberAfter.LockoutEnd);
        using var old = Client(factory);
        Assert.Equal(HttpStatusCode.OK, (await Login(old, target.UserName!)).StatusCode);
        Assert.Equal("/Account/ChoosePassword", (await Login(person, target.UserName!, password)).Headers.Location!.OriginalString);
        // A reset for oneself must use the normal current-password flow instead.
        var selfReset = await Action(admin, actor.Id, "Reset");
        Assert.DoesNotContain("id=\"temporary-password\"", await selfReset.Content.ReadAsStringAsync());
        Assert.False((await Read(factory, actor.Id)).MustChangePassword);
    }

    [Fact]
    public async Task PauseAndResumeRetainHistoryRejectSelfPauseAndInvalidateSessions()
    {
        using var factory = new VasenFactory(); using var admin = Client(factory); using var person = Client(factory);
        var actor = await Seed(factory, "admin", owner: true); var target = await Seed(factory, "target");
        await Login(admin, actor.UserName!); await Login(person, target.UserName!);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VasenDb>();
            db.Receipts.Add(new Receipt { MemberId = target.Id, Date = new(2026, 11, 10), AmountOre = 12345, Description = "Virke" });
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Redirect, (await Action(admin, target.Id, "Active", new() { ["active"] = "false" })).StatusCode);
        Assert.False((await Read(factory, target.Id)).IsActive);
        Assert.Equal(HttpStatusCode.Redirect, (await person.GetAsync("/")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Login(person, target.UserName!)).StatusCode);
        Assert.DoesNotContain("value=\"" + target.Id + "\"", await admin.GetStringAsync("/Receipts/Edit"));
        Assert.Contains("Person target", await admin.GetStringAsync("/Receipts/Edit/1"));
        Assert.Equal(HttpStatusCode.Redirect, (await Action(admin, target.Id, "Active", new() { ["active"] = "true" })).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await Login(person, target.UserName!)).StatusCode);
        await Action(admin, actor.Id, "Active", new() { ["active"] = "false" });
        Assert.True((await Read(factory, actor.Id)).IsActive);
        // Another ordinary member cannot pause the last active owner.
        Assert.Equal(HttpStatusCode.Redirect, (await person.PostAsync("/Members/Edit/" + actor.Id + "?handler=Active", Form(await person.GetStringAsync("/Account/Profile"), new() { ["active"] = "false" }))).StatusCode);
        Assert.True((await Read(factory, actor.Id)).IsActive);
    }

    [Fact]
    public async Task OnlyOwnersCanAnonymizePausedAccountsAndFinancialRelationsSurvive()
    {
        using var factory = new VasenFactory(); using var admin = Client(factory); using var ordinary = Client(factory);
        var owner = await Seed(factory, "owner", owner: true); var actor = await Seed(factory, "ordinary"); var target = await Seed(factory, "target", owner: true);
        await Login(admin, owner.UserName!); await Login(ordinary, actor.UserName!);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VasenDb>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<Member>>();
            var member = (await users.FindByIdAsync(target.Id))!;
            member.Email = "private@example.test"; member.PhoneNumber = "12345";
            Assert.True((await users.UpdateAsync(member)).Succeeded);
            await users.AddClaimAsync(member, new("private", "private information"));
            await users.SetAuthenticationTokenAsync(member, "provider", "token", "private-token");
            await users.AddLoginAsync(member, new("provider", "private-login", "Provider"));
            var withdrawal = new Withdrawal { MemberId = target.Id, Date = new(2026, 1, 2), AmountOre = 10000, OwnContributionOre = 2345, OriginalReceiptTotalOre = 12345 };
            db.Receipts.Add(new Receipt { MemberId = target.Id, Date = new(2026, 1, 1), AmountOre = 12345, Description = "Virke", Withdrawal = withdrawal });
            db.Deposits.Add(new Deposit { MemberId = target.Id, Date = new(2026, 1, 1), AmountOre = 50000 });
            db.DepositRules.Add(new DepositRule { MemberId = target.Id, AmountOre = 10000, Day = 1, Start = new(2027, 1, 1), NextMonth = new(2027, 1, 1) });
            db.AuditEntries.Add(new AuditEntry { ActorId = target.Id, Entity = "Kvitto", EntityId = 1, Action = "Skapat" });
            await db.SaveChangesAsync();
        }
        var denied = await ordinary.GetAsync("/Members/Cleanup");
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.StartsWith("/Account/AccessDenied", denied.Headers.Location!.PathAndQuery);
        Assert.Equal(HttpStatusCode.Forbidden, (await ordinary.GetAsync(denied.Headers.Location)).StatusCode);
        var deniedPost = await ordinary.PostAsync("/Members/Cleanup", Form(await ordinary.GetStringAsync("/Account/Profile"), new() { ["Id"] = target.Id, ["Confirm"] = "true" }));
        Assert.Equal(HttpStatusCode.Redirect, deniedPost.StatusCode);
        Assert.StartsWith("/Account/AccessDenied", deniedPost.Headers.Location!.PathAndQuery);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/Members/Cleanup?Id=" + target.Id)).StatusCode);
        await Action(admin, target.Id, "Active", new() { ["active"] = "false" });
        var cleanup = await admin.GetStringAsync("/Members/Cleanup?Id=" + target.Id);
        var notConfirmed = await admin.PostAsync("/Members/Cleanup", Form(cleanup, new() { ["Id"] = target.Id, ["Stamp"] = Value(cleanup, "Stamp") }));
        Assert.Equal(HttpStatusCode.OK, notConfirmed.StatusCode);
        Assert.False((await Read(factory, target.Id)).IsAnonymized);
        var cleaned = await admin.PostAsync("/Members/Cleanup", Form(cleanup, new() { ["Id"] = target.Id, ["Stamp"] = Value(cleanup, "Stamp"), ["Confirm"] = "true" }));
        Assert.Equal(HttpStatusCode.Redirect, cleaned.StatusCode);
        var after = await Read(factory, target.Id);
        Assert.True(after.IsAnonymized); Assert.False(after.IsActive); Assert.True(after.IsOwner);
        Assert.Equal("Anonymiserad person", after.DisplayName); Assert.Null(after.Email); Assert.Null(after.PhoneNumber); Assert.Null(after.PasswordHash);
        Assert.NotEqual(target.UserName, after.UserName);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VasenDb>();
            Assert.Equal(12345, (await db.Receipts.SingleAsync()).AmountOre);
            Assert.Equal(target.Id, (await db.Receipts.SingleAsync()).MemberId);
            Assert.Equal(2345, (await db.Withdrawals.SingleAsync()).OwnContributionOre);
            Assert.Equal(50000, (await db.Deposits.SingleAsync()).AmountOre);
            Assert.True((await db.DepositRules.SingleAsync()).IsPaused);
            Assert.Contains(await db.AuditEntries.ToListAsync(), x => x.ActorId == target.Id && x.Entity == "Kvitto");
            Assert.Empty(await db.Set<IdentityUserClaim<string>>().Where(x => x.UserId == target.Id).ToListAsync());
            Assert.Empty(await db.Set<IdentityUserToken<string>>().Where(x => x.UserId == target.Id).ToListAsync());
            Assert.Empty(await db.Set<IdentityUserLogin<string>>().Where(x => x.UserId == target.Id).ToListAsync());
        }
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/Members/Edit/" + target.Id)).StatusCode);
        Assert.DoesNotContain("value=\"" + target.Id + "\"", await admin.GetStringAsync("/Receipts/Edit"));
        Assert.Contains("Anonymiserad person", await admin.GetStringAsync("/Receipts/Edit/1"));
    }

    [Fact]
    public async Task EmailAndFullSwedishNameBothWorkAfterContactChangesAndOldIdentifiersStopWorking()
    {
        using var factory = new VasenFactory(); using var admin = Client(factory);
        var actor = await Seed(factory, "admin", owner: true); var target = await Seed(factory, "legacy");
        await Login(admin, actor.Email!);
        var html = await admin.GetStringAsync("/Members/Edit/" + target.Id);
        Assert.Equal(target.Email, Value(html, "Email"));
        var saved = await admin.PostAsync("/Members/Edit/" + target.Id, Form(html, new()
        {
            ["Stamp"] = Value(html, "Stamp"), ["DisplayName"] = "Östen Åberg", ["Email"] = "osten@example.test"
        }));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        foreach (var identifier in new[] { "ÖSTEN ÅBERG", " östen åberg ", "OSTEN@EXAMPLE.TEST", " osten@example.test " })
        {
            using var person = Client(factory);
            Assert.Equal(HttpStatusCode.Redirect, (await Login(person, identifier)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await person.GetAsync("/Account/Profile")).StatusCode);
        }
        foreach (var identifier in new[] { "legacy", "Person legacy", "legacy@example.test" })
        {
            using var person = Client(factory);
            Assert.Equal(HttpStatusCode.OK, (await Login(person, identifier)).StatusCode);
            Assert.Equal(HttpStatusCode.Redirect, (await person.GetAsync("/")).StatusCode);
        }
        await Action(admin, target.Id, "Active", new() { ["active"] = "false" });
        foreach (var identifier in new[] { "Östen Åberg", "osten@example.test" })
        {
            using var person = Client(factory);
            Assert.Equal(HttpStatusCode.OK, (await Login(person, identifier)).StatusCode);
        }
    }

    [Theory]
    [InlineData("Ny person", "")]
    [InlineData("Ny person", "inte-en-mailadress")]
    [InlineData("", "new@example.test")]
    [InlineData("person ADMIN", "new@example.test")]
    [InlineData("Ny person", "ADMIN@EXAMPLE.TEST")]
    [InlineData("admin@example.test", "new@example.test")]
    public async Task NameAndEmailAreRequiredValidAndUnambiguous(string name, string email)
    {
        using var factory = new VasenFactory(); using var admin = Client(factory);
        var actor = await Seed(factory, "admin", owner: true);
        await Login(admin, actor.UserName!);
        var html = await admin.GetStringAsync("/Members/Edit");
        var rejected = await admin.PostAsync("/Members/Edit", Form(html, new() { ["DisplayName"] = name, ["Email"] = email }));
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        Assert.DoesNotContain("id=\"temporary-password\"", await rejected.Content.ReadAsStringAsync());
        using var scope = factory.Services.CreateScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<VasenDb>().Users.CountAsync());
    }

    [Fact]
    public async Task LegacyDuplicateNamesNeverSelectTheWrongAccountButDistinctEmailStillWorks()
    {
        using var factory = new VasenFactory();
        var first = await Seed(factory, "first"); var second = await Seed(factory, "second");
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<Member>>();
            foreach (var id in new[] { first.Id, second.Id })
            {
                var member = (await users.FindByIdAsync(id))!;
                member.DisplayName = "Samma Namn";
                Assert.True((await users.UpdateAsync(member)).Succeeded);
            }
        }
        using var ambiguous = Client(factory);
        Assert.Equal(HttpStatusCode.OK, (await Login(ambiguous, "samma namn")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await ambiguous.GetAsync("/")).StatusCode);
        foreach (var identifier in new[] { first.Email!, second.Email! })
        {
            using var person = Client(factory);
            Assert.Equal(HttpStatusCode.Redirect, (await Login(person, identifier)).StatusCode);
        }
    }

    private static async Task<HttpResponseMessage> SaveOwner(HttpClient client, Member member, bool isOwner, string? name = null)
    {
        var html = await client.GetStringAsync("/Members/Edit/" + member.Id);
        return await client.PostAsync("/Members/Edit/" + member.Id, Form(html, new()
        {
            ["Stamp"] = Value(html, "Stamp"), ["DisplayName"] = name ?? member.DisplayName,
            ["Email"] = member.Email!, ["IsOwner"] = isOwner.ToString()
        }));
    }

    [Fact]
    public async Task OwnerCanPromoteAndDemoteMemberAndAccessChangesImmediately()
    {
        using var factory = new VasenFactory(); using var admin = Client(factory); using var person = Client(factory);
        var actor = await Seed(factory, "ordinary", owner: true); var owner = await Seed(factory, "owner");
        var target = await Seed(factory, "target");
        await Login(admin, actor.Email!); await Login(person, target.Email!);
        Assert.Equal(HttpStatusCode.Redirect, (await person.GetAsync("/Members/Cleanup")).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VasenDb>();
            db.Receipts.Add(new Receipt { MemberId = target.Id, Date = new(2026, 1, 1), AmountOre = 12345, Description = "Befintligt utlägg" });
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Redirect, (await SaveOwner(admin, target, true)).StatusCode);
        Assert.True((await Read(factory, target.Id)).IsOwner);
        Assert.Equal(HttpStatusCode.OK, (await person.GetAsync("/Members/Cleanup")).StatusCode);
        using (var scope = factory.Services.CreateScope())
            Assert.Equal(2, (await scope.ServiceProvider.GetRequiredService<BudgetSummaryService>().ForYearAsync(2026)).OwnerCount);
        Assert.Equal(HttpStatusCode.Redirect, (await SaveOwner(admin, target, false)).StatusCode);
        Assert.False((await Read(factory, target.Id)).IsOwner);
        var denied = await person.GetAsync("/Members/Cleanup");
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.StartsWith("/Account/AccessDenied", denied.Headers.Location!.PathAndQuery);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VasenDb>();
            var receipt = await db.Receipts.SingleAsync();
            Assert.Equal(target.Id, receipt.MemberId); Assert.Equal(12345, receipt.AmountOre);
            var changes = await db.AuditEntries.Where(x => x.Action == "Ägarmarkering ändrad").ToListAsync();
            Assert.Equal(2, changes.Count);
            Assert.All(changes, x => Assert.Equal(actor.Id, x.ActorId));
            Assert.Contains(changes, x => x.Detail.Contains("\"After\":true"));
            Assert.Contains(changes, x => x.Detail.Contains("\"After\":false"));
        }
    }

    [Fact]
    public async Task LastActiveOwnerCannotBeDemotedButCanBecomeMemberWhenAnotherOwnerExists()
    {
        using var factory = new VasenFactory(); using var client = Client(factory);
        var owner = await Seed(factory, "owner", owner: true);
        await Login(client, owner.Email!);
        var rejected = await SaveOwner(client, owner, false, "Ska inte sparas");
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        Assert.Contains("Minst en", WebUtility.HtmlDecode(await rejected.Content.ReadAsStringAsync()));
        var after = await Read(factory, owner.Id);
        Assert.True(after.IsOwner); Assert.Equal(owner.DisplayName, after.DisplayName);
        await Seed(factory, "other-owner", owner: true);
        Assert.Equal(HttpStatusCode.Redirect, (await SaveOwner(client, owner, false)).StatusCode);
        Assert.False((await Read(factory, owner.Id)).IsOwner);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Account/Profile")).StatusCode);
    }

    [Fact]
    public async Task MoreThanThreeOwnersCanBeAddedWithoutUnmarkingExistingOwners()
    {
        using var factory = new VasenFactory(); using var client = Client(factory);
        var actor = await Seed(factory, "admin", owner: true);
        await Seed(factory, "second", owner: true); await Seed(factory, "third", owner: true);
        var fourth = await Seed(factory, "fourth"); var fifth = await Seed(factory, "fifth");
        await Login(client, actor.Email!);
        Assert.Equal(HttpStatusCode.Redirect, (await SaveOwner(client, fourth, true)).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await SaveOwner(client, fifth, true)).StatusCode);
        Assert.True((await Read(factory, fourth.Id)).IsOwner); Assert.True((await Read(factory, fifth.Id)).IsOwner);
        using var scope = factory.Services.CreateScope();
        Assert.Equal(5, await scope.ServiceProvider.GetRequiredService<VasenDb>().Users.CountAsync(x => x.IsOwner));
        Assert.Equal(5, (await scope.ServiceProvider.GetRequiredService<BudgetSummaryService>().ForYearAsync(2026)).OwnerCount);
    }

    [Fact]
    public async Task FormerOwnerKeepsFinancialHistoryAndCorrectionsButCannotReceiveNewOwnerDeposits()
    {
        using var factory = new VasenFactory(); using var client = Client(factory);
        var actor = await Seed(factory, "owner", owner: true); var target = await Seed(factory, "target", owner: true);
        int depositId, ruleId;
        using (var scope = factory.Services.CreateScope())
        {
            var finance = scope.ServiceProvider.GetRequiredService<FinanceService>();
            depositId = await finance.SaveDepositAsync(null, Guid.Empty, new(2026, 1, 1), target.Id, 10000, null, actor.Id);
            ruleId = await scope.ServiceProvider.GetRequiredService<RecurringDepositService>().SaveRuleAsync(
                null, Guid.Empty, target.Id, 10000, 1, new(2027, 1, 1), null, false, new(2026, 1, 1), actor.Id);
        }
        await Login(client, actor.Email!);
        Assert.Equal(HttpStatusCode.Redirect, (await SaveOwner(client, target, false)).StatusCode);
        Assert.Contains("value=\"" + target.Id + "\"", await client.GetStringAsync("/Deposits/Edit/" + depositId));
        Assert.Contains("value=\"" + target.Id + "\"", await client.GetStringAsync("/Deposits?Year=2026"));
        using var verify = factory.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<VasenDb>();
        var financeAfter = verify.ServiceProvider.GetRequiredService<FinanceService>();
        var recurringAfter = verify.ServiceProvider.GetRequiredService<RecurringDepositService>();
        var deposit = await db.Deposits.SingleAsync();
        await financeAfter.SaveDepositAsync(deposit.Id, deposit.Version, deposit.Date, target.Id, 12000, "Rättelse", actor.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => financeAfter.SaveDepositAsync(null, Guid.Empty, new(2026, 1, 2), target.Id, 10000, null, actor.Id));
        var rule = await db.DepositRules.SingleAsync(x => x.Id == ruleId);
        await recurringAfter.SaveRuleAsync(rule.Id, rule.Version, target.Id, rule.AmountOre, rule.Day, rule.Start, null, true, new(2026, 1, 2), actor.Id);
        Assert.True(rule.IsPaused);
        Assert.Equal(12000, (await db.Deposits.SingleAsync()).AmountOre);
        Assert.Equal(target.Id, (await db.Deposits.SingleAsync()).MemberId);
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task ResumeSkipsDormantPausedMonthsAndExistingFinancialEntriesRemainCorrectable()
    {
        using var factory = new VasenFactory();
        var actor = await Seed(factory, "owner", owner: true);
        var target = await Seed(factory, "other-owner", owner: true);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VasenDb>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<Member>>();
        var clock = new TestClock { Now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero) };
        var recurring = new RecurringDepositService(db);
        var service = new MemberService(db, users, recurring, clock);
        var finance = new FinanceService(db);
        var ruleId = await recurring.SaveRuleAsync(null, Guid.Empty, target.Id, 10000, 1, new(2026, 1, 1), null, false, new(2026, 1, 1), actor.Id);
        var deposit = await db.Deposits.SingleAsync();
        clock.Now = new(2026, 1, 2, 12, 0, 0, TimeSpan.Zero);
        await service.SetActiveAsync(target.Id, (await users.FindByIdAsync(target.Id))!.ConcurrencyStamp, false, actor.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => finance.SaveDepositAsync(null, Guid.Empty, new(2026, 2, 1), target.Id, 10000, null, actor.Id));
        // A correction to existing history can retain its paused person.
        await finance.SaveDepositAsync(deposit.Id, deposit.Version, deposit.Date, target.Id, 11000, "Rättelse", actor.Id);
        var rule = await db.DepositRules.SingleAsync(x => x.Id == ruleId);
        await recurring.SaveRuleAsync(rule.Id, rule.Version, target.Id, rule.AmountOre, rule.Day, rule.Start, null, false, new(2026, 1, 2), actor.Id);
        clock.Now = new(2026, 4, 2, 12, 0, 0, TimeSpan.Zero);
        await service.SetActiveAsync(target.Id, (await users.FindByIdAsync(target.Id))!.ConcurrencyStamp, true, actor.Id);
        await recurring.GenerateDueAsync(new(2026, 5, 1));
        Assert.Equal(new[] { 202601, 202605 }, await db.Deposits.OrderBy(x => x.Period).Select(x => x.Period!.Value).ToArrayAsync());
        Assert.Equal(11000, (await db.Deposits.SingleAsync(x => x.Period == 202601)).AmountOre);
    }

    [Fact]
    public async Task ReinstatingOwnershipDoesNotBackfillMonthsAsOrdinaryMember()
    {
        using var factory = new VasenFactory();
        var actor = await Seed(factory, "owner", owner: true); var target = await Seed(factory, "target", owner: true);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VasenDb>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<Member>>();
        var clock = new TestClock { Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero) };
        var recurring = new RecurringDepositService(db);
        var service = new MemberService(db, users, recurring, clock);
        await recurring.SaveRuleAsync(null, Guid.Empty, target.Id, 10000, 1, new(2026, 1, 1), null, false, new(2026, 1, 1), actor.Id);
        clock.Now = new(2026, 1, 2, 12, 0, 0, TimeSpan.Zero);
        await service.SaveAsync(target.Id, (await users.FindByIdAsync(target.Id))!.ConcurrencyStamp, target.DisplayName, target.Email!, false, actor.Id);
        clock.Now = new(2026, 4, 2, 12, 0, 0, TimeSpan.Zero);
        await service.SaveAsync(target.Id, (await users.FindByIdAsync(target.Id))!.ConcurrencyStamp, target.DisplayName, target.Email!, true, actor.Id);
        await recurring.GenerateDueAsync(new(2026, 5, 1));
        Assert.Equal(new[] { 202601, 202605 }, await db.Deposits.OrderBy(x => x.Period).Select(x => x.Period!.Value).ToArrayAsync());
    }

    [Fact]
    public async Task DuplicateNamesStaleFormsAndMissingAntiForgeryDoNotChangeAccounts()
    {
        using var factory = new VasenFactory(); using var client = Client(factory);
        var actor = await Seed(factory, "admin", owner: true); var target = await Seed(factory, "target");
        await Login(client, actor.UserName!);
        var html = await client.GetStringAsync("/Members/Edit/" + target.Id);
        var fields = new Dictionary<string, string> { ["Stamp"] = Value(html, "Stamp"), ["DisplayName"] = "Updated", ["Email"] = "renamed@example.test" };
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Members/Edit/" + target.Id, Form(html, fields))).StatusCode);
        var stale = await client.PostAsync("/Members/Edit/" + target.Id + "?handler=Active", Form(html, new() { ["Stamp"] = Value(html, "Stamp"), ["active"] = "false" }));
        Assert.Contains("Ladda om", await stale.Content.ReadAsStringAsync());
        Assert.True((await Read(factory, target.Id)).IsActive);
        var create = await client.GetStringAsync("/Members/Edit");
        var duplicate = await client.PostAsync("/Members/Edit", Form(create, new() { ["DisplayName"] = "Duplicate", ["Email"] = "RENAMED@example.test" }));
        Assert.Contains("används redan", WebUtility.HtmlDecode(await duplicate.Content.ReadAsStringAsync()));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Members/Edit/" + target.Id + "?handler=Reset", new FormUrlEncodedContent(new Dictionary<string, string>()))).StatusCode);
        using var anonymous = Client(factory);
        foreach (var path in new[] { "/Members", "/Members/Edit", "/Members/Cleanup", "/Account/ChoosePassword" })
            Assert.Equal(HttpStatusCode.Redirect, (await anonymous.GetAsync(path)).StatusCode);
    }
}
