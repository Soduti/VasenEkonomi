using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Vasen.Web.Data;
using Vasen.Web.Models;

namespace Vasen.Web.Services;

public class RecurringDepositService(VasenDb db)
{
    public async Task GenerateDueAsync(DateOnly today)
    {
        // SQLite serializes writers; cursor and generated deposits commit together.
        await using var tx = await db.Database.BeginTransactionAsync();
        await ProcessDueAsync(today);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    internal async Task ProcessDueAsync(DateOnly today)
    {
        var rules = await db.DepositRules.Include(x => x.Member).ToListAsync();
        foreach (var rule in rules)
        {
            var month = rule.NextMonth;
            var periods = (await db.Deposits.Where(x => x.RuleId == rule.Id).Select(x => x.Period).ToListAsync()).ToHashSet();
            while (month <= today)
            {
                var due = new DateOnly(month.Year,month.Month,Math.Min(rule.Day,DateTime.DaysInMonth(month.Year,month.Month)));
                if (due > today) break;
                var period = month.Year * 100 + month.Month;
                if (!rule.IsPaused && rule.Member.IsActive && rule.Member.IsOwner && due >= rule.Start
                    && (!rule.End.HasValue || due <= rule.End.Value) && !periods.Contains(period))
                {
                    db.Deposits.Add(new Deposit { RuleId = rule.Id, Period = period, Date = due,
                        MemberId = rule.MemberId, AmountOre = rule.AmountOre, Comment = "Automatisk månadsinsättning" });
                    db.AuditEntries.Add(new AuditEntry { ActorId = "system", Entity = "Insättningsregel", EntityId = rule.Id,
                        Action = "Månadsinsättning skapad", Detail = JsonSerializer.Serialize(new { period, due, rule.MemberId, rule.AmountOre }) });
                }
                month = month.AddMonths(1);
            }
            if (month != rule.NextMonth) { rule.NextMonth = month; rule.Version = Guid.NewGuid(); }
        }
    }

    public async Task<int> SaveRuleAsync(int? id, Guid version, string person, long ore, int day,
        DateOnly start, DateOnly? end, bool paused, DateOnly today, string actor)
    {
        await AccessRules.RequireOwnerAsync(db, actor);
        FinanceService.ValidateYear(start.Year);
        if (end.HasValue) FinanceService.ValidateYear(end.Value.Year);
        if (ore <= 0 || ore > 10_000_000_000 || day is < 1 or > 31 || end < start)
            throw new InvalidOperationException("Kontrollera belopp, betalningsdag och start-/slutdatum.");
        await using var tx = await db.Database.BeginTransactionAsync();
        var rule = id.HasValue ? await db.DepositRules.SingleAsync(x => x.Id == id) : null;
        if (rule != null && rule.Version != version) throw new InvalidOperationException("Regeln har ändrats. Ladda om sidan.");
        var keepPerson = rule != null && rule.MemberId == person;
        if (!await db.Users.AnyAsync(x => x.Id == person && ((x.IsOwner && x.IsActive) || keepPerson)))
            throw new InvalidOperationException("Välj en aktiv ägare.");
        if(rule != null && (rule.MemberId != person || rule.Start != start))
            throw new InvalidOperationException("Person och startdatum kan inte ändras. Avsluta regeln och skapa en ny vid behov.");
        // Settle the old settings first. Paused months are consumed before a rule resumes.
        await ProcessDueAsync(today);
        var before = rule == null ? null : JsonSerializer.Serialize(new { rule.AmountOre, rule.Day, rule.End, rule.IsPaused });
        if(rule == null)
        {
            rule = new DepositRule { MemberId = person, Start = start, NextMonth = new DateOnly(start.Year,start.Month,1) };
            db.DepositRules.Add(rule);
        }
        rule.AmountOre = ore; rule.Day = day; rule.End = end; rule.IsPaused = paused; rule.Version = Guid.NewGuid();
        await db.SaveChangesAsync();
        db.AuditEntries.Add(new AuditEntry { ActorId = actor, Entity = "Insättningsregel", EntityId = rule.Id,
            Action = "Sparad", Detail = JsonSerializer.Serialize(new { Before = before, person, ore, day, start, end, paused }) });
        await ProcessDueAsync(today);
        await db.SaveChangesAsync(); await tx.CommitAsync();
        return rule.Id;
    }
}

public class DepositWorker(IServiceScopeFactory scopes, TimeProvider clock, ILogger<DepositWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<RecurringDepositService>().GenerateDueAsync(VasenDate.Today(clock));
            }
            catch (Exception e) when (!stoppingToken.IsCancellationRequested) { logger.LogError(e,"Kunde inte skapa förfallna månadsinsättningar. Försöker igen nästa gång."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
