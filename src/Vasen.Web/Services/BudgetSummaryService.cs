using Microsoft.EntityFrameworkCore;
using Vasen.Web.Data;
using Vasen.Web.Models;

namespace Vasen.Web.Services;

public record BudgetCategory(ExpenseSource Source, string Category, long? BudgetOre, long? ActualOre, BudgetLine? Line);
public record OwnerDeposits(string Name, long Ore, long OwnContributionOre = 0)
{
    public long TotalOre => Ore + OwnContributionOre;
}
public class BudgetSummary
{
    public int Year { get; init; }
    public AnnualBudget? Settings { get; init; }
    public List<BudgetCategory> Categories { get; init; } = [];
    public List<OwnerDeposits> Owners { get; init; } = [];
    public int OwnerCount { get; init; }
    public long DepositsOre { get; init; }
    public long AutomaticDepositsOre { get; init; }
    public long OwnContributionOre { get; init; }
    public long FundingOre => DepositsOre + OwnContributionOre;
    public long OutstandingOre { get; init; }
    public long BudgetOre => Categories.Sum(x => x.BudgetOre ?? 0);
    public long ActualOre => Categories.Sum(x => x.ActualOre ?? 0);
    public long OpeningOre => Settings?.OpeningBalanceOre ?? 0;
    public long NeededOre => Math.Max(0,BudgetOre - OpeningOre);
    public long AnnualPerOwnerOre => OwnerCount > 0 ? (long)decimal.Ceiling(NeededOre / (decimal)OwnerCount) : 0;
    public long MonthlyPerOwnerOre => OwnerCount > 0 ? (long)decimal.Ceiling(NeededOre / (OwnerCount * 12m * 100m)) * 100 : 0;
    public long NetOre => FundingOre - ActualOre;
    public long RemainingOre => OpeningOre + NetOre;
}

public class BudgetSummaryService(VasenDb db)
{
    public async Task<BudgetSummary> ForYearAsync(int year)
    {
        FinanceService.ValidateYear(year);
        var first = new DateOnly(year,1,1); var last = new DateOnly(year,12,31);
        var budgets = await db.BudgetLines.AsNoTracking().Where(x => x.Year == year).ToListAsync();
        var expenses = await db.SharedExpenses.AsNoTracking().Where(x => x.Year == year && !x.IsVoided).ToListAsync();
        var receipts = await db.Receipts.AsNoTracking().Where(x => x.Date >= first && x.Date <= last && x.DeletedUtc == null).ToListAsync();
        var deposits = await db.Deposits.AsNoTracking().Include(x => x.Member).Where(x => x.Date >= first && x.Date <= last && !x.IsVoided).ToListAsync();
        var contributions = await db.Withdrawals.AsNoTracking().Include(x => x.Member)
            .Where(x => x.Date >= first && x.Date <= last && !x.IsUndone && x.OwnContributionOre > 0).ToListAsync();
        var owners = await db.Users.AsNoTracking().Where(x => x.IsOwner && x.IsActive).OrderBy(x => x.DisplayName).ToListAsync();
        static string Key(string value) => value.ToUpperInvariant();
        var actuals = expenses.Select(x => (Source:ExpenseSource.SharedAccount,Category:x.Category,Amount:x.AmountOre))
            .GroupBy(x => (x.Source,Key(x.Category)))
            .ToDictionary(g => g.Key,g => (Category:g.First().Category,Amount:g.Sum(x => x.Amount)));
        var sharedBudgets = budgets.Where(x => x.Source == ExpenseSource.SharedAccount).ToList();
        var keys = sharedBudgets.Select(x => (x.Source,x.CategoryKey)).Union(actuals.Keys).ToList();
        var lines = keys.Select(key =>
        {
            var budget = sharedBudgets.SingleOrDefault(b => b.CategoryKey == key.Item2);
            var exists = actuals.TryGetValue(key,out var actual);
            return new BudgetCategory(key.Item1,budget?.Category ?? actual.Category,budget?.AmountOre,
                exists ? actual.Amount : null,budget);
        }).OrderBy(x => x.Source).ThenBy(x => x.Category,StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("sv-SE"),true)).ToList();
        var privateBudget = budgets.SingleOrDefault(x => x.Source == ExpenseSource.PrivateReceipt);
        lines.Add(new BudgetCategory(ExpenseSource.PrivateReceipt,BudgetLine.PrivateCategory,
            privateBudget?.AmountOre,receipts.Sum(x => x.AmountOre),privateBudget));
        var allPeople = owners.Concat(deposits.Select(x => x.Member)).Concat(contributions.Select(x => x.Member)).DistinctBy(x => x.Id).ToList();
        var people = allPeople.Select(x => new OwnerDeposits(x.DisplayName + (owners.Any(o => o.Id == x.Id) ? "" : " (tidigare ägare)"),
            deposits.Where(d => d.MemberId == x.Id).Sum(d => d.AmountOre),
            contributions.Where(c => c.MemberId == x.Id).Sum(c => c.OwnContributionOre))).ToList();
        return new BudgetSummary { Year = year, Settings = await db.AnnualBudgets.AsNoTracking().SingleOrDefaultAsync(x => x.Year == year),
            Categories = lines, Owners = people, OwnerCount = owners.Count, DepositsOre = deposits.Sum(x => x.AmountOre),
            AutomaticDepositsOre = deposits.Where(x => x.RuleId != null).Sum(x => x.AmountOre),
            OwnContributionOre = contributions.Sum(x => x.OwnContributionOre),
            OutstandingOre = receipts.Where(x => x.WithdrawalId == null).Sum(x => x.AmountOre) };
    }
}
