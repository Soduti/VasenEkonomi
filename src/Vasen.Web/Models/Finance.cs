using System.ComponentModel.DataAnnotations;

namespace Vasen.Web.Models;

public enum ExpenseSource { SharedAccount, PrivateReceipt }

public class AnnualBudget
{
    [Key] public int Year { get; set; }
    public long OpeningBalanceOre { get; set; }
    [ConcurrencyCheck] public Guid Version { get; set; } = Guid.NewGuid();
}

public class BudgetLine
{
    public const string PrivateCategory = "Privata utlägg";
    public int Id { get; set; }
    public int Year { get; set; }
    public ExpenseSource Source { get; set; }
    [MaxLength(80)] public string Category { get; set; } = "";
    [MaxLength(80)] public string CategoryKey { get; set; } = "";
    public long AmountOre { get; set; }
    [ConcurrencyCheck] public Guid Version { get; set; } = Guid.NewGuid();
}

public class SharedExpense
{
    public int Id { get; set; }
    public int Year { get; set; }
    [MaxLength(80)] public string Category { get; set; } = "";
    public long AmountOre { get; set; }
    [MaxLength(300)] public string? Comment { get; set; }
    public bool IsVoided { get; set; }
    [ConcurrencyCheck] public Guid Version { get; set; } = Guid.NewGuid();
}

public class Deposit
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    public string MemberId { get; set; } = "";
    public Member Member { get; set; } = null!;
    public long AmountOre { get; set; }
    [MaxLength(300)] public string? Comment { get; set; }
    public int? RuleId { get; set; }
    public DepositRule? Rule { get; set; }
    public int? Period { get; set; }
    public bool IsVoided { get; set; }
    [ConcurrencyCheck] public Guid Version { get; set; } = Guid.NewGuid();
}

public class DepositRule
{
    public int Id { get; set; }
    public string MemberId { get; set; } = "";
    public Member Member { get; set; } = null!;
    public long AmountOre { get; set; }
    public int Day { get; set; }
    public DateOnly Start { get; set; }
    public DateOnly? End { get; set; }
    public bool IsPaused { get; set; }
    public DateOnly NextMonth { get; set; }
    [ConcurrencyCheck] public Guid Version { get; set; } = Guid.NewGuid();
}
