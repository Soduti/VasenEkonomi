using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Vasen.Web.Data;
using Vasen.Web.Models;
using Vasen.Web.Services;
namespace Vasen.Tests;

public class ReceiptTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "VasenTests", Guid.NewGuid().ToString("N"));
    private VasenDb db = null!;
    private ReceiptService service = null!;
    private const string Anna = "anna", Lars = "lars";
    public async Task InitializeAsync()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Vasen:DataPath"] = root }).Build();
        var paths = new DataPaths(config, new TestEnvironment());
        db = new VasenDb(new DbContextOptionsBuilder<VasenDb>().UseSqlite($"Data Source={paths.Database}").Options);
        await db.Database.MigrateAsync();
        db.Users.AddRange(new Member { Id = Anna, UserName = "anna", DisplayName = "Anna", IsOwner = true }, new Member { Id = Lars, UserName = "lars", DisplayName = "Lars", IsOwner = true });
        await db.SaveChangesAsync();
        service = new ReceiptService(db, new FileStore(paths));
    }
    public async Task DisposeAsync()
    {
        await db.DisposeAsync();
        SqliteConnection.ClearAllPools();
        Directory.Delete(root, true);
    }
    private Task<int> Create(string person = Anna, long ore = 12345) => service.CreateAsync(new DateOnly(2026,12,20), person, ore, "Virke", null, [], Anna);

    [Theory]
    [InlineData("1 234,50", 123450)] [InlineData("1234.5",123450)] [InlineData("0,01",1)]
    public void SwedishAmountsAreExact(string input, long expected) { Assert.True(Money.TryParse(input,out var result)); Assert.Equal(expected,result); }
    [Theory]
    [InlineData("-1")] [InlineData("1,234")] [InlineData("0")] [InlineData("abc")] [InlineData("100000001")]
    public void InvalidAmountsAreRejected(string input) => Assert.False(Money.TryParse(input,out _));

    [Theory]
    [InlineData(30000, 70000)]
    [InlineData(100000, 0)]
    [InlineData(130000, -30000)]
    public async Task ReturnsReduceExpensesAndCanSettlePositiveZeroOrNegativeTotals(long returned, long net)
    {
        var purchase = await Create(ore:100000);
        var refund = await Create(ore:-returned);
        var summaries = new BudgetSummaryService(db);
        var summary = await summaries.ForYearAsync(2026);
        Assert.Equal(net, summary.ActualOre); Assert.Equal(net, summary.OutstandingOre);
        var wid = await service.ReimburseAsync([purchase,refund],new(2026,12,21),net,null,Anna);
        var withdrawal = await db.Withdrawals.SingleAsync();
        Assert.Equal(net, withdrawal.AmountOre); Assert.Equal(net, withdrawal.OriginalReceiptTotalOre);
        Assert.Equal(0, withdrawal.OwnContributionOre);
        summary = await summaries.ForYearAsync(2026);
        Assert.Equal(net, summary.ActualOre); Assert.Equal(0, summary.OutstandingOre);
        Assert.Equal(0, summary.DepositsOre); Assert.Equal(0, summary.OwnContributionOre);
        await service.UndoAsync(wid,withdrawal.Version,Anna);
        Assert.Equal(net,(await summaries.ForYearAsync(2026)).OutstandingOre);
        var receipt = await db.Receipts.SingleAsync(r => r.Id == refund);
        await service.EditAsync(refund,receipt.Version,receipt.Date,-20000,"Rättad retur",null,[],Anna);
        Assert.Equal(80000,(await summaries.ForYearAsync(2026)).ActualOre);
        await service.TrashAsync(refund,receipt.Version,false,Anna);
        Assert.Equal(100000,(await summaries.ForYearAsync(2026)).ActualOre);
        await service.TrashAsync(refund,receipt.Version,true,Anna);
        Assert.Equal(80000,(await summaries.ForYearAsync(2026)).ActualOre);
    }

    [Fact]
    public async Task ReturnCannotCreateOwnContribution()
    {
        var refund = await Create(ore:-30000);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReimburseAsync([refund],new(2026,12,21),-30000,null,Anna,true));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReimburseAsync([refund],new(2026,12,21),0,null,Anna,true));
        Assert.Empty(await db.Withdrawals.ToListAsync());
    }

    [Theory]
    [InlineData(0)] [InlineData(-10000000001)] [InlineData(long.MinValue)]
    public async Task InvalidReceiptAmountsAreRejectedByService(long ore)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Create(ore:ore));
        Assert.Empty(await db.Receipts.ToListAsync());
    }

    [Fact]
    public async Task MissingAttachmentAndCategoryAreAllowed()
    {
        var id = await Create();
        var r = await db.Receipts.Include(r => r.Files).SingleAsync(r => r.Id == id);
        Assert.Empty(r.Files); Assert.Null(r.Category); Assert.Equal(12345,r.AmountOre);
    }

    [Fact]
    public async Task BatchReimbursementAllowsDifferenceAndCannotRunTwice()
    {
        var ids = new[] { await Create(), await Create(ore:23456) };
        var id = await service.ReimburseAsync(ids,new DateOnly(2027,1,2),36000,null,Anna);
        var w = await db.Withdrawals.SingleAsync(w => w.Id == id);
        Assert.Equal(35801,w.OriginalReceiptTotalOre); Assert.Equal(36000,w.AmountOre);
        Assert.Equal(0,w.OwnContributionOre);
        Assert.Equal(2,await db.Receipts.CountAsync(r => r.WithdrawalId == id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReimburseAsync(ids,new DateOnly(2027,1,2),36000,null,Anna));
        Assert.All(await db.Receipts.ToListAsync(),r => Assert.Equal(2026,r.Date.Year));
    }
    [Fact]
    public async Task MixedPeopleCannotBeReimbursedTogether()
    {
        var ids = new[] { await Create(), await Create(Lars) };
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReimburseAsync(ids,new DateOnly(2026,12,21),25000,null,Anna));
        Assert.Empty(await db.Withdrawals.ToListAsync());
    }
    [Fact]
    public async Task PaidReceiptCanBeEditedWithoutChangingPayment()
    {
        var id = await Create();
        var wid = await service.ReimburseAsync([id],new DateOnly(2027,1,2),12500,null,Anna);
        var r = await db.Receipts.SingleAsync(r => r.Id == id);
        await service.EditAsync(id,r.Version,r.Date,13000,"Virke, rättat",null,[],Anna);
        var w = await db.Withdrawals.SingleAsync(w => w.Id == wid);
        Assert.Equal(12500,w.AmountOre); Assert.Equal(12345,w.OriginalReceiptTotalOre);
        Assert.Equal(wid,r.WithdrawalId); Assert.Equal(13000,r.AmountOre);
        Assert.Contains(await db.AuditEntries.ToListAsync(),a => a.Action == "Ändrat");
    }
    [Fact]
    public async Task UndoReleasesReceiptsButPreservesPaymentHistory()
    {
        var id = await Create();
        var wid = await service.ReimburseAsync([id],new DateOnly(2027,1,2),12500,null,Anna);
        var w = await db.Withdrawals.SingleAsync(w => w.Id == wid);
        await service.UndoAsync(wid,w.Version,Anna);
        Assert.True(w.IsUndone); Assert.Equal(12500,w.AmountOre);
        Assert.Null((await db.Receipts.SingleAsync()).WithdrawalId);
    }
    [Fact]
    public async Task TrashRestoreAndPermanentDeletionPreserveWithdrawal()
    {
        var id = await Create();
        var wid = await service.ReimburseAsync([id],new DateOnly(2027,1,2),12500,null,Anna);
        var r = await db.Receipts.SingleAsync();
        await service.TrashAsync(id,r.Version,false,Anna);
        Assert.Empty(await db.Receipts.Where(x => x.DeletedUtc == null).ToListAsync());
        await service.TrashAsync(id,r.Version,true,Anna); Assert.Null(r.DeletedUtc);
        await service.TrashAsync(id,r.Version,false,Anna);
        await service.DeletePermanentlyAsync(id,r.Version,Anna);
        Assert.Empty(await db.Receipts.ToListAsync());
        Assert.Equal(12500,(await db.Withdrawals.SingleAsync(w => w.Id == wid)).AmountOre);
    }
    [Fact]
    public async Task StaleVersionCannotOverwriteAnotherChange()
    {
        var id = await Create(); var r = await db.Receipts.SingleAsync(); var old = r.Version;
        await service.EditAsync(id,old,r.Date,13000,"Rättat",null,[],Anna);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EditAsync(id,old,r.Date,14000,"Gammal sida",null,[],Anna));
        Assert.Equal(13000,r.AmountOre);
    }
    [Fact]
    public async Task FifteenThousandReceiptCanCloseWithTenThousandPaidAndFiveThousandOwnContribution()
    {
        db.Users.Add(new Member { Id="cecilia",UserName="cecilia",DisplayName="Cecilia",IsOwner=true });
        foreach(var person in await db.Users.ToListAsync()) person.IsOwner = true;
        await db.SaveChangesAsync();
        var id = await Create(ore:1500000);
        var finance = new FinanceService(db);
        await finance.SaveDepositAsync(null,Guid.Empty,new(2026,12,20),Lars,500000,null,Lars);
        await finance.SaveDepositAsync(null,Guid.Empty,new(2026,12,20),"cecilia",500000,null,"cecilia");
        var wid = await service.ReimburseAsync([id],new(2026,12,21),1000000,null,Anna,true);
        var receipt = await db.Receipts.SingleAsync(); var withdrawal = await db.Withdrawals.SingleAsync();
        Assert.Equal(1500000,receipt.AmountOre); Assert.Equal(wid,receipt.WithdrawalId);
        Assert.Equal(1000000,withdrawal.AmountOre); Assert.Equal(500000,withdrawal.OwnContributionOre);
        Assert.Equal(2,await db.Deposits.CountAsync());
        var summary = await new BudgetSummaryService(db).ForYearAsync(2026);
        Assert.Equal(1500000,summary.ActualOre); Assert.Equal(1000000,summary.DepositsOre);
        Assert.Equal(500000,summary.OwnContributionOre); Assert.Equal(1500000,summary.FundingOre);
        Assert.Equal(0,summary.NetOre); Assert.Equal(0,summary.OutstandingOre);
        var anna = Assert.Single(summary.Owners,x => x.Name == "Anna");
        Assert.Equal(0,anna.Ore); Assert.Equal(500000,anna.OwnContributionOre); Assert.Equal(500000,anna.TotalOre);
        await service.UndoAsync(wid,withdrawal.Version,Anna);
        summary = await new BudgetSummaryService(db).ForYearAsync(2026);
        Assert.Equal(0,summary.OwnContributionOre); Assert.Equal(1500000,summary.OutstandingOre);
        Assert.Equal(1000000,summary.DepositsOre); Assert.Equal(1500000,summary.ActualOre);
    }
    [Fact]
    public async Task OwnContributionUsesClosingDateAndDoesNotChangeWhenReceiptIsCorrected()
    {
        var id = await Create(ore:1500000);
        await service.ReimburseAsync([id],new(2027,1,2),1000000,null,Anna,true);
        var receipt = await db.Receipts.SingleAsync();
        await service.EditAsync(id,receipt.Version,receipt.Date,1600000,"Rättat kvitto",null,[],Anna);
        var serviceSummary = new BudgetSummaryService(db);
        var previous = await serviceSummary.ForYearAsync(2026); var current = await serviceSummary.ForYearAsync(2027);
        Assert.Equal(1600000,previous.ActualOre); Assert.Equal(0,previous.OwnContributionOre);
        Assert.Equal(0,current.ActualOre); Assert.Equal(500000,current.OwnContributionOre);
        Assert.Equal(500000,(await db.Withdrawals.SingleAsync()).OwnContributionOre);
        Assert.Single(current.Owners,x => x.Name.StartsWith("Anna"));
    }
    [Fact]
    public async Task FullySelfFundedReceiptCanCloseWithoutBankWithdrawal()
    {
        var id = await Create(ore:1500000);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReimburseAsync([id],new(2026,12,21),0,null,Anna));
        await service.ReimburseAsync([id],new(2026,12,21),0,null,Anna,true);
        var summary = await new BudgetSummaryService(db).ForYearAsync(2026);
        Assert.Equal(0,summary.DepositsOre); Assert.Equal(1500000,summary.OwnContributionOre);
        Assert.Equal(1500000,summary.ActualOre); Assert.Equal(0,summary.OutstandingOre); Assert.Equal(0,summary.NetOre);
    }
    [Fact]
    public async Task SmallerWithdrawalDoesNotBecomeOwnContributionWithoutExplicitChoice()
    {
        var id = await Create(ore:1500000);
        await service.ReimburseAsync([id],new(2026,12,21),1000000,null,Anna);
        Assert.Equal(0,(await db.Withdrawals.SingleAsync()).OwnContributionOre);
        var summary = await new BudgetSummaryService(db).ForYearAsync(2026);
        Assert.Equal(0,summary.OwnContributionOre); Assert.Equal(1500000,summary.ActualOre); Assert.Equal(0,summary.OutstandingOre);
    }
    [Fact]
    public async Task OwnContributionCannotBeNegative()
    {
        var id = await Create(ore:1500000);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReimburseAsync([id],new(2026,12,21),1600000,null,Anna,true));
        Assert.Empty(await db.Withdrawals.ToListAsync()); Assert.Null((await db.Receipts.SingleAsync()).WithdrawalId);
    }
    [Fact]
    public async Task ValidAttachmentIsStoredAndDeletedWithReceipt()
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes("%PDF-1.7\nTest fixture\n%%EOF");
        using var stream = new MemoryStream(bytes);
        var file = new Microsoft.AspNetCore.Http.FormFile(stream,0,bytes.Length,"Uploads","kvitto.pdf");
        var id = await service.CreateAsync(new DateOnly(2026,11,6),Anna,10000,"Testbilaga",null,[file],Anna);
        var r = await db.Receipts.Include(x => x.Files).SingleAsync(x => x.Id == id);
        var attachment = Assert.Single(r.Files);
        var storedPath = Path.Combine(root,"receipts",attachment.StorageName);
        Assert.True(File.Exists(storedPath)); Assert.Equal(bytes,await File.ReadAllBytesAsync(storedPath));
        await service.TrashAsync(id,r.Version,false,Anna);
        await service.DeletePermanentlyAsync(id,r.Version,Anna);
        Assert.False(File.Exists(storedPath)); Assert.Empty(await db.ReceiptFiles.ToListAsync());
    }
    [Fact]
    public async Task InvalidAttachmentRollsBackPreviouslySavedFiles()
    {
        var pdf = System.Text.Encoding.ASCII.GetBytes("%PDF-1.7\nTest fixture");
        var html = System.Text.Encoding.ASCII.GetBytes("<script>alert(1)</script>");
        using var first = new MemoryStream(pdf); using var second = new MemoryStream(html);
        var files = new Microsoft.AspNetCore.Http.IFormFile[] {
            new Microsoft.AspNetCore.Http.FormFile(first,0,pdf.Length,"Uploads","kvitto.pdf"),
            new Microsoft.AspNetCore.Http.FormFile(second,0,html.Length,"Uploads","falsk.jpg") };
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(new DateOnly(2026,11,6),Anna,10000,"Test",null,files,Anna));
        Assert.Empty(await db.Receipts.ToListAsync()); Assert.Empty(Directory.GetFiles(Path.Combine(root,"receipts")));
    }
}

internal sealed class TestEnvironment : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "Vasen.Web";
    public string EnvironmentName { get; set; } = "Development";
    public string WebRootPath { get; set; } = "";
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
