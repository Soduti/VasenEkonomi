using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Vasen.Web.Data;
using Vasen.Web.Models;

namespace Vasen.Web.Services;

public class FinanceService(VasenDb db)
{
    public static void ValidateYear(int year)
    { if (year is < 2000 or > 2100) throw new InvalidOperationException("Välj ett år mellan 2000 och 2100."); }
    public static string Category(string? text)
    {
        var value = text?.Trim();
        if (string.IsNullOrWhiteSpace(value) || value.Length > 80) throw new InvalidOperationException("Ange ett kostnadsslag med högst 80 tecken.");
        return value;
    }
    private static void Amount(long ore, bool zero = false)
    { if (ore < (zero ? 0 : 1) || ore > 10_000_000_000) throw new InvalidOperationException("Ange ett giltigt belopp."); }
    private static void Comment(string? value)
    { if (value?.Length > 300) throw new InvalidOperationException("Kommentaren får vara högst 300 tecken."); }
    private void Audit(string actor, string entity, int id, string action, object detail) => db.AuditEntries.Add(
        new AuditEntry { ActorId = actor, Entity = entity, EntityId = id, Action = action, Detail = JsonSerializer.Serialize(detail) });
    private static void Version(Guid actual, Guid expected)
    { if (actual != expected) throw new InvalidOperationException("Posten har ändrats av någon annan. Ladda om sidan."); }
    public async Task SaveOpeningAsync(int year, long ore, Guid version, string actor)
    {
        await AccessRules.RequireOwnerAsync(db, actor);
        ValidateYear(year);
        if (Math.Abs((decimal)ore) > 10_000_000_000) throw new InvalidOperationException("Ange en giltig ingående kassa.");
        var item = await db.AnnualBudgets.FindAsync(year);
        if (item is null)
        {
            if (version != Guid.Empty) throw new InvalidOperationException("Årsinställningen har ändrats. Ladda om sidan.");
            item = new AnnualBudget { Year = year }; db.AnnualBudgets.Add(item);
        }
        else Version(item.Version, version);
        var before = item.OpeningBalanceOre;
        item.OpeningBalanceOre = ore; item.Version = Guid.NewGuid();
        Audit(actor,"Årsbudget",year,"Kassa ändrad",new { Before = before, After = ore });
        await db.SaveChangesAsync();
    }
    public async Task<int> SaveBudgetAsync(int? id, Guid version, int year, ExpenseSource source, string category, long ore, string actor)
    {
        await AccessRules.RequireOwnerAsync(db, actor);
        ValidateYear(year); Amount(ore,true);
        if (!Enum.IsDefined(source)) throw new InvalidOperationException("Välj en giltig typ av utgift.");
        category = source == ExpenseSource.PrivateReceipt ? BudgetLine.PrivateCategory : Category(category);
        var key = category.ToUpperInvariant();
        if (await db.BudgetLines.AnyAsync(x => x.Year == year && x.Source == source
            && (source == ExpenseSource.PrivateReceipt || x.CategoryKey == key) && x.Id != id))
            throw new InvalidOperationException(source == ExpenseSource.PrivateReceipt
                ? "Det finns redan en budget för privata utlägg det året. Ändra den befintliga potten."
                : "Det finns redan en budgetpost för det kostnadsslaget. Ändra den befintliga posten.");
        var item = id.HasValue ? await db.BudgetLines.SingleAsync(x => x.Id == id) : new BudgetLine();
        if (id.HasValue) Version(item.Version,version); else db.BudgetLines.Add(item);
        var before = new { item.Year, item.Source, item.Category, item.AmountOre };
        item.Year = year; item.Source = source; item.Category = category; item.CategoryKey = key; item.AmountOre = ore; item.Version = Guid.NewGuid();
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.SaveChangesAsync(); Audit(actor,"Budgetpost",item.Id,"Sparad",new { Before = before, year, source, category, ore });
        await db.SaveChangesAsync(); await tx.CommitAsync(); return item.Id;
    }
    public async Task DeleteBudgetAsync(int id, Guid version, string actor)
    {
        await AccessRules.RequireOwnerAsync(db, actor);
        var item = await db.BudgetLines.SingleAsync(x => x.Id == id); Version(item.Version,version);
        Audit(actor,"Budgetpost",id,"Borttagen",new { item.Year, item.Source, item.Category, item.AmountOre });
        db.BudgetLines.Remove(item); await db.SaveChangesAsync();
    }
    public async Task<int> SaveExpenseAsync(int? id, Guid version, int year, string category, long ore, string? comment, string actor)
    {
        await AccessRules.RequireOwnerAsync(db, actor);
        ValidateYear(year); Amount(ore,true); Comment(comment); category = Category(category);
        var item = id.HasValue ? await db.SharedExpenses.SingleAsync(x => x.Id == id && !x.IsVoided) : new SharedExpense();
        if(id.HasValue) Version(item.Version,version); else db.SharedExpenses.Add(item);
        var before = new { item.Year, item.Category, item.AmountOre, item.Comment };
        item.Year = year; item.Category = category; item.AmountOre = ore; item.Comment = comment?.Trim(); item.Version = Guid.NewGuid();
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.SaveChangesAsync(); Audit(actor,"Gemensam utgift",item.Id,"Sparad",new { Before = before, year, category, ore, comment });
        await db.SaveChangesAsync(); await tx.CommitAsync(); return item.Id;
    }
    public async Task<int> SaveDepositAsync(int? id, Guid version, DateOnly date, string person, long ore, string? comment, string actor)
    {
        await AccessRules.RequireOwnerAsync(db, actor);
        ValidateYear(date.Year); Amount(ore); Comment(comment);
        var item = id.HasValue ? await db.Deposits.SingleAsync(x => x.Id == id && !x.IsVoided) : new Deposit();
        var keepPerson = id.HasValue && item.MemberId == person;
        if (!await db.Users.AnyAsync(x => x.Id == person && ((x.IsOwner && x.IsActive) || keepPerson)))
            throw new InvalidOperationException("Välj en aktiv ägare.");
        if(id.HasValue)
        {
            Version(item.Version,version);
            if(item.RuleId != null && item.MemberId != person) throw new InvalidOperationException("Personen på en automatisk insättning kan inte ändras.");
        }
        else db.Deposits.Add(item);
        var before = new { item.Date, item.MemberId, item.AmountOre, item.Comment };
        item.Date = date; item.MemberId = person; item.AmountOre = ore; item.Comment = comment?.Trim(); item.Version = Guid.NewGuid();
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.SaveChangesAsync(); Audit(actor,"Insättning",item.Id,"Sparad",new { Before = before, date, person, ore, comment });
        await db.SaveChangesAsync(); await tx.CommitAsync(); return item.Id;
    }
    public async Task VoidExpenseAsync(int id, Guid version, string actor)
    {
        await AccessRules.RequireOwnerAsync(db, actor);
        var item = await db.SharedExpenses.SingleAsync(x => x.Id == id && !x.IsVoided); Version(item.Version,version);
        item.IsVoided = true; item.Version = Guid.NewGuid();
        Audit(actor,"Gemensam utgift",id,"Makulerad",new { item.Year, item.Category, item.AmountOre }); await db.SaveChangesAsync();
    }
    public async Task VoidDepositAsync(int id, Guid version, string actor)
    {
        await AccessRules.RequireOwnerAsync(db, actor);
        var item = await db.Deposits.SingleAsync(x => x.Id == id && !x.IsVoided); Version(item.Version,version);
        item.IsVoided = true; item.Version = Guid.NewGuid();
        Audit(actor,"Insättning",id,"Makulerad",new { item.Date, item.MemberId, item.AmountOre, item.RuleId, item.Period }); await db.SaveChangesAsync();
    }
}
