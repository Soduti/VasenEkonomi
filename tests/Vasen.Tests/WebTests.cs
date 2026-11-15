using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Vasen.Web.Models;
using Microsoft.EntityFrameworkCore;
using Vasen.Web.Data;
using Vasen.Web.Services;
namespace Vasen.Tests;

public class WebTests
{
    [Fact]
    public async Task LoginReceiptFiltersAndLogoutWorkThroughRazorPages()
    {
        using var factory = new VasenFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<Member>>();
        var member = new Member { DisplayName = "Testägare", UserName = "owner@example.test", Email = "owner@example.test", IsOwner = true };
        var created = await users.CreateAsync(member,"TestPassword123"); Assert.True(created.Succeeded);

        Assert.Equal(HttpStatusCode.Redirect,(await client.GetAsync("/")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect,(await client.GetAsync("/Receipts/File/1")).StatusCode);
        var login = await client.GetStringAsync("/Account/Login");
        var signedIn = await client.PostAsync("/Account/Login", Form(login,new() { ["LoginName"] = member.Email, ["Password"] = "TestPassword123", ["RememberMe"] = "true" }));
        Assert.Equal(HttpStatusCode.Redirect,signedIn.StatusCode);
        Assert.Contains(signedIn.Headers.GetValues("Set-Cookie"),s => s.StartsWith("Vasen.Identity=") && s.Contains("expires="));

        var edit = await client.GetStringAsync("/Receipts/Edit");
        var saved = await client.PostAsync("/Receipts/Edit",Form(edit,new() { ["Date"] = "2026-12-20", ["Person"] = member.Id,
            ["Amount"] = "1234,50", ["Description"] = "Virke till bryggan" }));
        Assert.Equal(HttpStatusCode.Redirect,saved.StatusCode);
        var list = await client.GetStringAsync("/");
        Assert.Contains("Virke till bryggan",list); Assert.Contains("Saknar kvitto",list);
        Assert.Contains("Testägare",WebUtility.HtmlDecode(list));
        var filtered = await client.GetStringAsync("/?From=2027-01-01&Status=unpaid");
        Assert.DoesNotContain("Virke till bryggan",filtered);
        Assert.Contains("Kvitton",await client.GetStringAsync("/?Sort=person-desc"));

        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("/Receipts/Edit/1")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("/Withdrawals/Create?ids=1")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("/Withdrawals/Index")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("/Trash")).StatusCode);
        var profile = await client.GetStringAsync("/Account/Profile");
        var loggedOut = await client.PostAsync("/Account/Profile?handler=Logout",Form(profile,new()));
        Assert.Equal(HttpStatusCode.Redirect,loggedOut.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect,(await client.GetAsync("/")).StatusCode);
    }
    [Theory]
    [InlineData("30", "70", 7000)]
    [InlineData("100", "0", 0)]
    [InlineData("130", "-30", -3000)]
    public async Task ReturnCheckboxAndSignedSettlementWorkThroughForms(string returned, string net, long expected)
    {
        using var factory = new VasenFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<Member>>();
        var owner = new Member { DisplayName="Returägare",UserName="returns@example.test",Email="returns@example.test",IsOwner=true };
        Assert.True((await users.CreateAsync(owner,"TestPassword123")).Succeeded);
        var login = await client.GetStringAsync("/Account/Login");
        Assert.Equal(HttpStatusCode.Redirect,(await client.PostAsync("/Account/Login",Form(login,new() { ["LoginName"]=owner.Email,["Password"]="TestPassword123" }))).StatusCode);
        foreach (var isReturn in new[] { false, true })
        {
            var form = await client.GetStringAsync("/Receipts/Edit");
            Assert.Contains("Retur",form);
            var result = await client.PostAsync("/Receipts/Edit",Form(form,new() {
                ["Date"]="2026-12-20",["Person"]=owner.Id,["Amount"]=isReturn ? returned : "100",
                ["IsReturn"]=isReturn.ToString(),["Description"]=isReturn ? "Retur" : "Köp" }));
            Assert.Equal(HttpStatusCode.Redirect,result.StatusCode);
        }
        var db = scope.ServiceProvider.GetRequiredService<VasenDb>();
        var receipts = await db.Receipts.OrderBy(r => r.Id).ToListAsync();
        Assert.True(receipts[1].AmountOre < 0);
        var edit = await client.GetStringAsync($"/Receipts/Edit/{receipts[1].Id}");
        Assert.Matches("checked=\"checked\"[^>]*id=\"IsReturn\"",edit);
        var url = $"/Withdrawals/Create?ids={receipts[0].Id}&ids={receipts[1].Id}";
        var settlement = await client.GetStringAsync(url);
        var content = Form(settlement,new() { ["Date"]="2026-12-21",["Amount"]=net });
        content = new FormUrlEncodedContent((await content.ReadAsStringAsync()).Split('&').Select(pair => pair.Split('=',2))
            .Select(pair => new KeyValuePair<string,string>(WebUtility.UrlDecode(pair[0]),WebUtility.UrlDecode(pair[1])))
            .Concat(receipts.Select(r => new KeyValuePair<string,string>("Ids",r.Id.ToString()))));
        Assert.Equal(HttpStatusCode.Redirect,(await client.PostAsync("/Withdrawals/Create",content)).StatusCode);
        Assert.Equal(expected,(await db.Withdrawals.SingleAsync()).AmountOre);
        var summary = await new BudgetSummaryService(db).ForYearAsync(2026);
        Assert.Equal(expected,summary.ActualOre); Assert.Equal(0,summary.OutstandingOre);
    }

    [Fact]
    public async Task OcrAssetsAndWebAssemblyPolicyAreAvailableLocally()
    {
        using var factory = new VasenFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        foreach (var path in new[] {
            "/lib/tesseract/tesseract.min.js", "/lib/tesseract/worker.min.js",
            "/lib/tesseract/core/tesseract-core-lstm.wasm.js",
            "/lib/tesseract/core/tesseract-core-simd-lstm.wasm.js",
            "/lib/tesseract/core/tesseract-core-relaxedsimd-lstm.wasm.js",
            "/lib/tesseract/lang/swe.traineddata.gz", "/lib/tesseract/lang/eng.traineddata.gz",
            "/lib/tesseract/licenses/tesseract.js-Apache-2.0.txt",
            "/js/receipt-ocr-worker.js"
        })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Content.Headers.ContentLength > 0);
            var policy = response.Headers.GetValues("Content-Security-Policy").Single();
            Assert.Contains("'wasm-unsafe-eval'", policy);
            Assert.DoesNotContain("'unsafe-eval'", policy);
            Assert.Contains("worker-src 'self'", policy);
            if (path.EndsWith(".gz"))
            {
                var bytes = await response.Content.ReadAsByteArrayAsync();
                Assert.Equal(0x1f, bytes[0]);
                Assert.Equal(0x8b, bytes[1]);
                Assert.Empty(response.Content.Headers.ContentEncoding);
            }
        }
        using var login = await client.GetAsync("/Account/Login");
        Assert.DoesNotContain("'wasm-unsafe-eval'", login.Headers.GetValues("Content-Security-Policy").Single());
    }
    [Fact]
    public async Task StateChangesRequireAntiForgeryToken()
    {
        using var factory = new VasenFactory(); using var client = factory.CreateClient();
        var response = await client.PostAsync("/Account/Login",new FormUrlEncodedContent(new Dictionary<string,string> { ["LoginName"]="x@example.test", ["Password"]="abc" }));
        Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
    }
    [Fact]
    public async Task BudgetExpensesAndDepositFormsSaveAndDisplayCorrectTotals()
    {
        using var factory = new VasenFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<Member>>();
        var owner = new Member { DisplayName="Testägare",UserName="budget@example.test",Email="budget@example.test",IsOwner=true };
        Assert.True((await users.CreateAsync(owner,"TestPassword123")).Succeeded);
        var login = await client.GetStringAsync("/Account/Login");
        Assert.Equal(HttpStatusCode.Redirect,(await client.PostAsync("/Account/Login",Form(login,new() { ["LoginName"]=owner.Email,["Password"]="TestPassword123" }))).StatusCode);
        var budget = await client.GetStringAsync("/Budget/Edit?Year=2026");
        Assert.Contains("validation-summary-valid",budget);
        var result = await client.PostAsync("/Budget/Edit",Form(budget,new() { ["Year"]="2026",["Source"]="SharedAccount",["Category"]="El",["Amount"]="25000" }));
        Assert.Equal(HttpStatusCode.Redirect,result.StatusCode);
        var overview = await client.GetStringAsync("/Budget?Year=2026");
        Assert.Contains("25&#xA0;000,00 kr",overview.Replace("&#x202F;","&#xA0;"));
        var opening = await client.PostAsync("/Budget?handler=Opening",Form(overview,new() { ["Year"]="2026",["opening"]="1000",["version"]=Guid.Empty.ToString() }));
        Assert.Equal(HttpStatusCode.Redirect,opening.StatusCode);
        var expense = await client.GetStringAsync("/Expenses/Edit?Year=2026");
        Assert.Equal(HttpStatusCode.Redirect,(await client.PostAsync("/Expenses/Edit",Form(expense,new() { ["Year"]="2026",["Category"]="El",["Amount"]="23000",["Comment"]="Årets el" }))).StatusCode);
        Assert.Contains("El",await client.GetStringAsync("/Expenses?Year=2026"));
        var deposit = await client.GetStringAsync("/Deposits/Edit");
        Assert.Equal(HttpStatusCode.Redirect,(await client.PostAsync("/Deposits/Edit",Form(deposit,new() { ["Person"]=owner.Id,["Date"]="2026-01-01",["Amount"]="5000" }))).StatusCode);
        var today = VasenDate.Today(TimeProvider.System);
        var firstFuture = new DateOnly(today.Year+1,1,1);
        var rule = await client.GetStringAsync("/Deposits/Rule");
        Assert.Equal(HttpStatusCode.Redirect,(await client.PostAsync("/Deposits/Rule",Form(rule,new() { ["Person"]=owner.Id,["Start"]=firstFuture.ToString("yyyy-MM-dd"),["Day"]="25",["Amount"]="1000" }))).StatusCode);
        var deposits = await client.GetStringAsync("/Deposits?Year=2026"); Assert.Contains("Månadsregler",WebUtility.HtmlDecode(deposits));
        Assert.Contains("Testägare",WebUtility.HtmlDecode(deposits));
        var db = scope.ServiceProvider.GetRequiredService<VasenDb>();
        var summary = await new BudgetSummaryService(db).ForYearAsync(2026);
        Assert.Equal(2500000,summary.BudgetOre); Assert.Equal(2300000,summary.ActualOre); Assert.Equal(500000,summary.DepositsOre);
        Assert.Equal(100000,summary.OpeningOre); Assert.Equal(200000,summary.MonthlyPerOwnerOre);
        var privateForm = await client.GetStringAsync("/Budget/Edit?Year=2026&Source=PrivateReceipt");
        Assert.DoesNotContain("list=\"budget-categories\"",privateForm);
        Assert.Equal(HttpStatusCode.Redirect,(await client.PostAsync("/Budget/Edit",Form(privateForm,new() { ["Year"]="2026",["Source"]="PrivateReceipt",["Amount"]="20000" }))).StatusCode);
        var privateRow = await db.BudgetLines.AsNoTracking().SingleAsync(x => x.Source == ExpenseSource.PrivateReceipt);
        Assert.Equal(BudgetLine.PrivateCategory,privateRow.Category);
        overview = await client.GetStringAsync("/Budget?Year=2026");
        Assert.Contains("Årsbudget 2026",WebUtility.HtmlDecode(overview));
        Assert.Contains("Alla privata kvitton",overview);
        Assert.Equal(HttpStatusCode.Redirect,(await client.GetAsync("/Budget/Edit?Year=2026&Source=PrivateReceipt")).StatusCode);
        foreach(var path in new[] { "/Budget/Edit/1","/Expenses/Edit/1","/Deposits/Edit/1","/Deposits/Rule/1" })
            Assert.Equal(HttpStatusCode.OK,(await client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync("/Budget?Year=0")).StatusCode);
    }
    private static FormUrlEncodedContent Form(string html, Dictionary<string,string> values)
    {
        var match = Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success);
        values["__RequestVerificationToken"] = WebUtility.HtmlDecode(match.Groups[1].Value);
        return new FormUrlEncodedContent(values);
    }
}
internal class VasenFactory : WebApplicationFactory<Program>
{
    private readonly string root = Path.Combine(Path.GetTempPath(),"VasenWebTests",Guid.NewGuid().ToString("N"));
    protected override void ConfigureWebHost(IWebHostBuilder builder) { builder.UseEnvironment("Development"); builder.UseSetting("Vasen:DataPath",root); builder.UseSetting("Vasen:RunDepositWorker","false"); }
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing); SqliteConnection.ClearAllPools();
        if (Directory.Exists(root)) Directory.Delete(root,true);
    }
}
