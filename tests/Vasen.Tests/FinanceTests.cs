using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Vasen.Web.Data;
using Vasen.Web.Models;
using Vasen.Web.Services;

namespace Vasen.Tests;

public class FinanceTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(),"VasenFinanceTests",Guid.NewGuid().ToString("N"));
    private VasenDb db = null!;
    private FinanceService finance = null!;
    private RecurringDepositService recurring = null!;
    private BudgetSummaryService summaries = null!;
    private VasenDb NewContext() => new(new DbContextOptionsBuilder<VasenDb>().UseSqlite($"Data Source={Path.Combine(root,"test.db")}").Options);
    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(root); db = NewContext(); await db.Database.MigrateAsync();
        db.Users.AddRange(new Member { Id="a",UserName="a",DisplayName="Anna",IsOwner=true },
            new Member { Id="b",UserName="b",DisplayName="Bo",IsOwner=true },new Member { Id="c",UserName="c",DisplayName="Cecilia",IsOwner=true });
        await db.SaveChangesAsync(); finance = new(db); recurring = new(db); summaries = new(db);
    }
    public async Task DisposeAsync() { await db.DisposeAsync(); SqliteConnection.ClearAllPools(); Directory.Delete(root,true); }
    private Task<int> Rule(DateOnly start, int day=25, long ore=100000, DateOnly? end=null, DateOnly? today=null, bool paused=false) =>
        recurring.SaveRuleAsync(null,Guid.Empty,"a",ore,day,start,end,paused,today ?? start,"a");

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 10001, 900)]
    [InlineData(2, 5001, 500)]
    [InlineData(3, 3334, 300)]
    [InlineData(4, 2501, 300)]
    [InlineData(7, 1429, 200)]
    public void FundingSuggestionDividesAcrossActualOwnerCountAndRoundsMonthlyToWholeKronor(int count, long annual, long monthly)
    {
        var summary = new BudgetSummary { OwnerCount = count, Categories = [new(ExpenseSource.SharedAccount, "El", 10001, 0, null)] };
        Assert.Equal(annual, summary.AnnualPerOwnerOre);
        Assert.Equal(monthly, summary.MonthlyPerOwnerOre);
    }

    [Fact]
    public async Task FundingSuggestionCountsOnlyActiveOwnersAndRespondsToOwnerChanges()
    {
        await finance.SaveBudgetAsync(null, Guid.Empty, 2026, ExpenseSource.SharedAccount, "El", 1200000, "a");
        db.Users.Add(new Member { Id = "d", UserName = "d", DisplayName = "Doris", IsOwner = true });
        db.Users.Add(new Member { Id = "e", UserName = "e", DisplayName = "Erik", IsOwner = false });
        await db.SaveChangesAsync();
        var four = await summaries.ForYearAsync(2026);
        Assert.Equal(4, four.OwnerCount); Assert.Equal(300000, four.AnnualPerOwnerOre); Assert.Equal(25000, four.MonthlyPerOwnerOre);
        var paused = (await db.Users.FindAsync("d"))!; paused.IsActive = false;
        await db.SaveChangesAsync();
        var three = await summaries.ForYearAsync(2026);
        Assert.Equal(3, three.OwnerCount); Assert.Equal(400000, three.AnnualPerOwnerOre); Assert.Equal(33400, three.MonthlyPerOwnerOre);
        var former = (await db.Users.FindAsync("c"))!; former.IsOwner = false;
        await db.SaveChangesAsync();
        var two = await summaries.ForYearAsync(2026);
        Assert.Equal(2, two.OwnerCount); Assert.Equal(600000, two.AnnualPerOwnerOre); Assert.Equal(50000, two.MonthlyPerOwnerOre);
    }

    [Fact]
    public async Task SummaryCombinesExpensePartsAndReceiptsWithoutCountingReimbursementsTwice()
    {
        await finance.SaveBudgetAsync(null,Guid.Empty,2026,ExpenseSource.SharedAccount,"El",2500000,"a");
        await finance.SaveBudgetAsync(null,Guid.Empty,2026,ExpenseSource.SharedAccount,"Lån",4000000,"a");
        await finance.SaveBudgetAsync(null,Guid.Empty,2026,ExpenseSource.PrivateReceipt,"Färg",1500000,"a");
        await finance.SaveOpeningAsync(2026,500000,Guid.Empty,"a");
        await finance.SaveExpenseAsync(null,Guid.Empty,2026,"El",1000000,"Första halvåret","a");
        await finance.SaveExpenseAsync(null,Guid.Empty,2026," el ",1300000,"Andra halvåret","a");
        await finance.SaveExpenseAsync(null,Guid.Empty,2026,"Lån",4000000,null,"a");
        db.Receipts.Add(new Receipt { MemberId="a",Date=new(2026,12,31),AmountOre=250000,Description="Färg",Category="färg",
            Withdrawal=new Withdrawal { MemberId="a",Date=new(2027,1,2),AmountOre=260000,OriginalReceiptTotalOre=250000 } });
        await db.SaveChangesAsync();
        await finance.SaveDepositAsync(null,Guid.Empty,new(2026,1,1),"a",500000,null,"a");
        var s = await summaries.ForYearAsync(2026);
        Assert.Equal(8000000,s.BudgetOre); Assert.Equal(6550000,s.ActualOre);
        Assert.Equal(7500000,s.NeededOre); Assert.Equal(2500000,s.AnnualPerOwnerOre); Assert.Equal(208400,s.MonthlyPerOwnerOre);
        Assert.Equal(-6050000,s.NetOre); Assert.Equal(-5550000,s.RemainingOre); Assert.Equal(0,s.OutstandingOre);
        Assert.Equal(3,s.Categories.Count); Assert.Equal(3,s.Owners.Count);
        Assert.Equal(2300000,s.Categories.Single(x => x.Category == "El").ActualOre);
        Assert.Equal(0,(await summaries.ForYearAsync(2027)).ActualOre);
    }
    [Fact]
    public async Task UnknownOutcomeIsDistinctFromKnownZeroAndPrivateCategoriesAreSeparate()
    {
        await finance.SaveBudgetAsync(null,Guid.Empty,2026,ExpenseSource.SharedAccount,"El",0,"a");
        await finance.SaveBudgetAsync(null,Guid.Empty,2026,ExpenseSource.PrivateReceipt,"El",10000,"a");
        var s = await summaries.ForYearAsync(2026);
        Assert.Null(s.Categories.Single(x => x.Source == ExpenseSource.SharedAccount).ActualOre);
        Assert.Equal(0,s.Categories.Single(x => x.Source == ExpenseSource.PrivateReceipt).ActualOre);
        await finance.SaveExpenseAsync(null,Guid.Empty,2026,"El",0,null,"a");
        s = await summaries.ForYearAsync(2026);
        Assert.Equal(0,s.Categories.Single(x => x.Source == ExpenseSource.SharedAccount).ActualOre);
        await Assert.ThrowsAsync<InvalidOperationException>(() => finance.SaveBudgetAsync(null,Guid.Empty,2026,ExpenseSource.SharedAccount," el ",10000,"a"));
    }
    [Fact]
    public async Task DirectAccountExpenseCanBeCorrectedAndVoidedWithoutCreatingReceiptsOrWithdrawals()
    {
        var id = await finance.SaveExpenseAsync(null,Guid.Empty,2026,"El",120000,"Autogiro","a");
        var item = (await db.SharedExpenses.FindAsync(id))!;
        var list = new Vasen.Web.Pages.Expenses.IndexModel(db,TimeProvider.System) { Year=2026 };
        await list.OnGetAsync(); Assert.Single(list.Expenses);
        Assert.Equal(120000,(await summaries.ForYearAsync(2026)).ActualOre);
        await finance.SaveExpenseAsync(id,item.Version,2026,"El",130000,"Rättat autogiro","a");
        await list.OnGetAsync(); Assert.Equal(130000,Assert.Single(list.Expenses).AmountOre);
        var summary = await summaries.ForYearAsync(2026);
        Assert.Equal(130000,summary.ActualOre); Assert.Equal(0,summary.OutstandingOre); Assert.Equal(0,summary.OwnContributionOre);
        await finance.VoidExpenseAsync(id,item.Version,"a");
        await list.OnGetAsync(); Assert.Empty(list.Expenses);
        Assert.Equal(0,(await summaries.ForYearAsync(2026)).ActualOre);
        Assert.Empty(await db.Receipts.ToListAsync()); Assert.Empty(await db.Withdrawals.ToListAsync());
    }
    [Fact]
    public async Task VoidedEntriesAndTrashedReceiptsAreExcluded()
    {
        var eid = await finance.SaveExpenseAsync(null,Guid.Empty,2026,"El",10000,null,"a");
        var expense = await db.SharedExpenses.FindAsync(eid); await finance.VoidExpenseAsync(eid,expense!.Version,"a");
        var did = await finance.SaveDepositAsync(null,Guid.Empty,new(2026,1,1),"a",10000,null,"a");
        var deposit = await db.Deposits.FindAsync(did); await finance.VoidDepositAsync(did,deposit!.Version,"a");
        db.Receipts.Add(new Receipt { MemberId="a",Date=new(2026,1,1),AmountOre=10000,Description="Borttaget",DeletedUtc=DateTime.UtcNow });
        await db.SaveChangesAsync(); var s = await summaries.ForYearAsync(2026);
        Assert.Equal(0,s.ActualOre); Assert.Equal(0,s.DepositsOre);
        var emptyPool = Assert.Single(s.Categories); Assert.Equal(ExpenseSource.PrivateReceipt,emptyPool.Source); Assert.Null(emptyPool.BudgetOre);
    }
    [Fact]
    public async Task CashCanReduceNeedToZeroOrIncreaseItWhenNegative()
    {
        await finance.SaveBudgetAsync(null,Guid.Empty,2026,ExpenseSource.SharedAccount,"El",10000,"a");
        await finance.SaveOpeningAsync(2026,20000,Guid.Empty,"a");
        Assert.Equal(0,(await summaries.ForYearAsync(2026)).MonthlyPerOwnerOre);
        var settings = await db.AnnualBudgets.FindAsync(2026);
        var oldVersion = settings!.Version;
        await finance.SaveOpeningAsync(2026,-10000,oldVersion,"a");
        Assert.Equal(20000,(await summaries.ForYearAsync(2026)).NeededOre);
        await Assert.ThrowsAsync<InvalidOperationException>(() => finance.SaveOpeningAsync(2026,0,oldVersion,"a"));
    }
    [Fact]
    public async Task EndOfMonthAndLeapYearAreHandledAndCatchupIsIdempotent()
    {
        await Rule(new(2028,1,1),31,today:new(2028,3,31));
        await recurring.GenerateDueAsync(new(2028,3,31));
        var deposits = await db.Deposits.OrderBy(x => x.Date).ToListAsync();
        Assert.Equal(new[] { new DateOnly(2028,1,31),new(2028,2,29),new(2028,3,31) },deposits.Select(x => x.Date));
        Assert.Equal(3,deposits.Select(x => x.Period).Distinct().Count());
    }
    [Fact]
    public async Task PausedMonthsAreSkippedEvenWhenApplicationWasNotRunning()
    {
        var id = await Rule(new(2026,1,1),today:new(2026,1,31));
        var rule = await db.DepositRules.FindAsync(id);
        await recurring.SaveRuleAsync(id,rule!.Version,"a",100000,25,rule.Start,null,true,new(2026,1,31),"a");
        await recurring.SaveRuleAsync(id,rule.Version,"a",100000,25,rule.Start,null,false,new(2026,5,1),"a");
        await recurring.GenerateDueAsync(new(2026,5,25));
        Assert.Equal(new[] { 202601,202605 },await db.Deposits.OrderBy(x => x.Period).Select(x => x.Period!.Value).ToArrayAsync());
    }
    [Fact]
    public async Task RuleChangesDoNotRewritePastOrMissedOldAmounts()
    {
        var id = await Rule(new(2026,1,1),1);
        var rule = await db.DepositRules.FindAsync(id);
        await recurring.SaveRuleAsync(id,rule!.Version,"a",200000,1,rule.Start,null,false,new(2026,2,15),"a");
        await recurring.GenerateDueAsync(new(2026,3,1));
        Assert.Equal(new long[] { 100000,100000,200000 },await db.Deposits.OrderBy(x => x.Date).Select(x => x.AmountOre).ToArrayAsync());
    }
    [Fact]
    public async Task VoidedAutomaticDepositCannotBeRecreatedAndCorrectionKeepsItsPeriod()
    {
        var id = await Rule(new(2026,1,1),1);
        var deposit = await db.Deposits.SingleAsync();
        await finance.SaveDepositAsync(deposit.Id,deposit.Version,new(2026,2,2),"a",120000,"Rättat","a");
        Assert.Equal(202601,deposit.Period);
        await finance.VoidDepositAsync(deposit.Id,deposit.Version,"a");
        var rule = await db.DepositRules.FindAsync(id); rule!.NextMonth = new(2026,1,1); await db.SaveChangesAsync();
        await recurring.GenerateDueAsync(new(2026,1,31));
        Assert.Single(await db.Deposits.ToListAsync()); Assert.True(deposit.IsVoided);
    }
    [Fact]
    public async Task StartEndAndFutureDatesBoundGeneration()
    {
        await Rule(new(2026,1,26),25,end:new(2026,3,24),today:new(2026,5,1));
        var deposits = await db.Deposits.ToListAsync(); Assert.Single(deposits); Assert.Equal(new DateOnly(2026,2,25),deposits[0].Date);
        await Rule(new(2027,1,1),1,today:new(2026,12,31));
        Assert.Single(await db.Deposits.ToListAsync());
    }
    [Fact]
    public async Task ConcurrentGeneratorsCannotCreateDuplicateMonths()
    {
        await Rule(new(2026,1,1),25);
        await using var other = NewContext();
        await Task.WhenAll(recurring.GenerateDueAsync(new(2026,3,31)),new RecurringDepositService(other).GenerateDueAsync(new(2026,3,31)));
        Assert.Equal(3,await db.Deposits.CountAsync());
    }
    [Fact]
    public async Task ExtendingAnEndedRuleDoesNotFillMonthsWhenItWasEnded()
    {
        var id = await Rule(new(2026,1,1),1,end:new(2026,1,31));
        var rule = await db.DepositRules.FindAsync(id);
        await recurring.SaveRuleAsync(id,rule!.Version,"a",100000,1,rule.Start,new(2026,12,31),false,new(2026,5,15),"a");
        await recurring.GenerateDueAsync(new(2026,6,1));
        Assert.Equal(new[] { 202601,202606 },await db.Deposits.OrderBy(x => x.Period).Select(x => x.Period!.Value).ToArrayAsync());
    }
    [Fact]
    public async Task MigrationKeepsExistingReceiptsAndUsers()
    {
        await using var upgrade = new VasenDb(new DbContextOptionsBuilder<VasenDb>().UseSqlite($"Data Source={Path.Combine(root,"upgrade.db")}").Options);
        var migrator = upgrade.GetService<IMigrator>();
        await migrator.MigrateAsync("20261106192117_InitialCreate");
        await upgrade.Database.ExecuteSqlRawAsync("INSERT INTO AspNetUsers (Id,DisplayName,IsOwner,IsActive,EmailConfirmed,PhoneNumberConfirmed,TwoFactorEnabled,LockoutEnabled,AccessFailedCount) VALUES ('legacy','Befintlig ägare',1,1,0,0,0,0,0)");
        await upgrade.Database.ExecuteSqlRawAsync("INSERT INTO Receipts (Date,MemberId,AmountOre,Description,CreatedUtc,Version) VALUES ('2026-01-01','legacy',12345,'Befintligt kvitto','2026-01-01','00000000-0000-0000-0000-000000000001')");
        await migrator.MigrateAsync();
        Assert.Equal("Befintligt kvitto",(await upgrade.Receipts.SingleAsync()).Description);
        Assert.Equal(12345,(await upgrade.Receipts.SingleAsync()).AmountOre);
        var legacy = Assert.Single(await upgrade.Users.ToListAsync());
        Assert.False(legacy.MustChangePassword); Assert.False(legacy.IsAnonymized);
        Assert.Equal("Befintlig ägare", legacy.DisplayName);
        Assert.Empty(await upgrade.Deposits.ToListAsync());
    }
    [Fact]
    public async Task PrivatePoolIncludesEveryReceiptRegardlessOfCategoryAndPaymentStatus()
    {
        await finance.SaveBudgetAsync(null,Guid.Empty,2026,ExpenseSource.PrivateReceipt,"Valfritt namn",1000000,"a");
        db.Receipts.AddRange(
            new Receipt { MemberId="a",Date=new(2026,11,1),AmountOre=12345,Description="Virke",Category="Virke" },
            new Receipt { MemberId="b",Date=new(2026,11,2),AmountOre=34567,Description="Färg",Category="Färg" },
            new Receipt { MemberId="c",Date=new(2026,11,3),AmountOre=2000,Description="Utan kategori",Withdrawal=new Withdrawal { MemberId="c",Date=new(2026,11,4),AmountOre=2100 } },
            new Receipt { MemberId="a",Date=new(2026,11,4),AmountOre=99999,Description="Papperskorg",DeletedUtc=DateTime.UtcNow },
            new Receipt { MemberId="a",Date=new(2027,1,1),AmountOre=3000,Description="Nästa år" });
        await db.SaveChangesAsync();
        await finance.SaveExpenseAsync(null,Guid.Empty,2026,"Privata utlägg",1000,null,"a");
        var summary = await summaries.ForYearAsync(2026);
        var pool = Assert.Single(summary.Categories,x => x.Source == ExpenseSource.PrivateReceipt);
        Assert.Equal(BudgetLine.PrivateCategory,pool.Category); Assert.Equal(1000000,pool.BudgetOre); Assert.Equal(48912,pool.ActualOre);
        Assert.Equal(49912,summary.ActualOre); Assert.Equal(46912,summary.OutstandingOre); Assert.Equal(2,summary.Categories.Count);
    }
    [Fact]
    public async Task OnlyOnePrivateBudgetIsAllowedPerYearEvenWithDifferentNames()
    {
        var id = await finance.SaveBudgetAsync(null,Guid.Empty,2026,ExpenseSource.PrivateReceipt,"Färg",100000,"a");
        var budget = (await db.BudgetLines.FindAsync(id))!; Assert.Equal(BudgetLine.PrivateCategory,budget.Category);
        await Assert.ThrowsAsync<InvalidOperationException>(() => finance.SaveBudgetAsync(null,Guid.Empty,2026,ExpenseSource.PrivateReceipt,"Virke",200000,"a"));
        await finance.SaveBudgetAsync(id,budget.Version,2026,ExpenseSource.PrivateReceipt,"",250000,"a");
        Assert.Equal(250000,(await summaries.ForYearAsync(2026)).BudgetOre);
        await finance.SaveBudgetAsync(null,Guid.Empty,2027,ExpenseSource.PrivateReceipt,"",300000,"a");
        db.BudgetLines.Add(new BudgetLine { Year=2026,Source=ExpenseSource.PrivateReceipt,Category="Annat",CategoryKey="ANNAT",AmountOre=1 });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
    [Fact]
    public async Task RemovingPrivateBudgetDoesNotRemoveReceiptsOrTheirOutcome()
    {
        var id = await finance.SaveBudgetAsync(null,Guid.Empty,2026,ExpenseSource.PrivateReceipt,"",500000,"a");
        db.Receipts.Add(new Receipt { MemberId="a",Date=new(2026,1,1),AmountOre=10000,Description="Verktyg" }); await db.SaveChangesAsync();
        var item = (await db.BudgetLines.FindAsync(id))!;
        await finance.DeleteBudgetAsync(id,item.Version,"a");
        var pool = Assert.Single((await summaries.ForYearAsync(2026)).Categories);
        Assert.Null(pool.BudgetOre); Assert.Equal(10000,pool.ActualOre); Assert.Single(await db.Receipts.ToListAsync());
    }
    [Fact]
    public async Task PoolMigrationResetsOldPrivateTestBudgetsAndKeepsSharedBudgets()
    {
        await using var old = new VasenDb(new DbContextOptionsBuilder<VasenDb>().UseSqlite($"Data Source={Path.Combine(root,"old-budget.db")}").Options);
        var migrator = old.GetService<IMigrator>();
        var previous = old.Database.GetMigrations().Single(x => x.EndsWith("_AddBudgetAndDeposits"));
        await migrator.MigrateAsync(previous);
        old.BudgetLines.AddRange(new BudgetLine { Year=2026,Source=ExpenseSource.PrivateReceipt,Category="Färg",CategoryKey="FÄRG",AmountOre=10000 },
            new BudgetLine { Year=2026,Source=ExpenseSource.PrivateReceipt,Category="Virke",CategoryKey="VIRKE",AmountOre=20000 },
            new BudgetLine { Year=2026,Source=ExpenseSource.SharedAccount,Category="El",CategoryKey="EL",AmountOre=2300000 });
        await old.SaveChangesAsync(); await migrator.MigrateAsync(); old.ChangeTracker.Clear();
        var remaining = Assert.Single(await old.BudgetLines.ToListAsync()); Assert.Equal("El",remaining.Category); Assert.Equal(2300000,remaining.AmountOre);
        old.Users.Add(new Member { Id = "a", UserName = "a", DisplayName = "Anna", IsOwner = true });
        await old.SaveChangesAsync();
        await new FinanceService(old).SaveBudgetAsync(null,Guid.Empty,2026,ExpenseSource.PrivateReceipt,"",30000,"a");
        Assert.Equal(30000,(await old.BudgetLines.SingleAsync(x => x.Source == ExpenseSource.PrivateReceipt)).AmountOre);
    }
}
