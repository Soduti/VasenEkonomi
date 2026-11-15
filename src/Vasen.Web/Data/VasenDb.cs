using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Vasen.Web.Models;
namespace Vasen.Web.Data;

public class VasenDb(DbContextOptions<VasenDb> options) : IdentityDbContext<Member>(options)
{
    public DbSet<Receipt> Receipts => Set<Receipt>();
    public DbSet<ReceiptFile> ReceiptFiles => Set<ReceiptFile>();
    public DbSet<Withdrawal> Withdrawals => Set<Withdrawal>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<AnnualBudget> AnnualBudgets => Set<AnnualBudget>();
    public DbSet<BudgetLine> BudgetLines => Set<BudgetLine>();
    public DbSet<SharedExpense> SharedExpenses => Set<SharedExpense>();
    public DbSet<Deposit> Deposits => Set<Deposit>();
    public DbSet<DepositRule> DepositRules => Set<DepositRule>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<Receipt>().HasIndex(r => new { r.DeletedUtc, r.Date });
        b.Entity<Receipt>().HasOne(r => r.Member).WithMany().OnDelete(DeleteBehavior.Restrict);
        b.Entity<Receipt>().HasOne(r => r.Withdrawal).WithMany(w => w.Receipts).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Withdrawal>().HasOne(w => w.Member).WithMany().OnDelete(DeleteBehavior.Restrict);
        b.Entity<BudgetLine>().HasIndex(x => new { x.Year, x.Source, x.CategoryKey }).IsUnique();
        b.Entity<BudgetLine>().HasIndex(x => x.Year).HasDatabaseName("IX_BudgetLines_PrivateYear")
            .IsUnique().HasFilter("\"Source\" = 1");
        b.Entity<Deposit>().HasIndex(x => new { x.RuleId, x.Period }).IsUnique();
        b.Entity<Deposit>().HasIndex(x => x.Date);
        b.Entity<Deposit>().HasOne(x => x.Member).WithMany().OnDelete(DeleteBehavior.Restrict);
        b.Entity<Deposit>().HasOne(x => x.Rule).WithMany().OnDelete(DeleteBehavior.Restrict);
        b.Entity<DepositRule>().HasOne(x => x.Member).WithMany().OnDelete(DeleteBehavior.Restrict);
    }
}
