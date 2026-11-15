using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Vasen.Web.Data;
using Vasen.Web.Models;
using Vasen.Web.Services;
namespace Vasen.Web.Pages.Deposits;
public class RuleModel(VasenDb db, RecurringDepositService service, TimeProvider clock) : PageModel
{
    [BindProperty(SupportsGet = true)] public int? Id { get; set; }
    [BindProperty, Required] public string Person { get; set; } = "";
    [BindProperty, Required] public string Amount { get; set; } = "";
    [BindProperty, Range(1,31)] public int Day { get; set; } = 25;
    [BindProperty] public DateOnly Start { get; set; } = VasenDate.Today(clock);
    [BindProperty] public DateOnly? End { get; set; }
    [BindProperty] public bool IsPaused { get; set; }
    [BindProperty] public Guid Version { get; set; }
    public DepositRule? Rule { get; set; }
    public List<Member> Members { get; set; } = [];
    private async Task<bool> LoadAsync()
    {
        Members = await db.Users.AsNoTracking().Where(x => x.IsOwner && x.IsActive).OrderBy(x => x.DisplayName).ToListAsync();
        if(Id == null) return true;
        Rule = await db.DepositRules.AsNoTracking().Include(x => x.Member).SingleOrDefaultAsync(x => x.Id == Id);
        return Rule != null;
    }
    public async Task<IActionResult> OnGetAsync()
    {
        await service.GenerateDueAsync(VasenDate.Today(clock));
        if(!await LoadAsync()) return NotFound();
        if(Rule is {} r) { Person = r.MemberId; Amount = Money.Input(r.AmountOre); Day = r.Day; Start = r.Start; End = r.End; IsPaused = r.IsPaused; Version = r.Version; }
        else Person = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        return Page();
    }
    public async Task<IActionResult> OnPostAsync()
    {
        if(!await LoadAsync()) return NotFound();
        if(!Money.TryParse(Amount,out var ore)) ModelState.AddModelError(nameof(Amount),"Ange ett positivt månadsbelopp.");
        if(!ModelState.IsValid) return Page();
        try { await service.SaveRuleAsync(Id,Version,Person,ore,Day,Start,End,IsPaused,VasenDate.Today(clock),User.FindFirstValue(ClaimTypes.NameIdentifier)!); TempData["Message"] = "Månadsregeln är sparad. Förfallna insättningar har hanterats."; return RedirectToPage("Index"); }
        catch(Exception e) when(e is InvalidOperationException or DbUpdateException) { ModelState.AddModelError("",e is InvalidOperationException ? e.Message : "Regeln har ändrats. Ladda om sidan."); return Page(); }
    }
}
