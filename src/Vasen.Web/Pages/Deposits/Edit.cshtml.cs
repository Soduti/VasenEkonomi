using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Vasen.Web.Data;
using Vasen.Web.Models;
using Vasen.Web.Services;
namespace Vasen.Web.Pages.Deposits;
public class EditModel(VasenDb db, FinanceService service, TimeProvider clock) : PageModel
{
    [BindProperty(SupportsGet = true)] public int? Id { get; set; }
    [BindProperty] public DateOnly Date { get; set; } = VasenDate.Today(clock);
    [BindProperty, Required] public string Person { get; set; } = "";
    [BindProperty, Required] public string Amount { get; set; } = "";
    [BindProperty, StringLength(300)] public string? Comment { get; set; }
    [BindProperty] public Guid Version { get; set; }
    public List<Member> Members { get; set; } = [];
    public Deposit? Deposit { get; set; }
    private async Task<bool> LoadAsync()
    {
        if (Id != null)
            Deposit = await db.Deposits.AsNoTracking().Include(x => x.Member).SingleOrDefaultAsync(x => x.Id == Id && !x.IsVoided);
        var existingPerson = Deposit?.MemberId;
        Members = await db.Users.AsNoTracking().Where(x => (x.IsOwner && x.IsActive) || x.Id == existingPerson).OrderBy(x => x.DisplayName).ToListAsync();
        return Id == null || Deposit != null;
    }
    public async Task<IActionResult> OnGetAsync()
    {
        if(!await LoadAsync()) return NotFound();
        if(Deposit is {} d) { Date = d.Date; Person = d.MemberId; Amount = Money.Input(d.AmountOre); Comment = d.Comment; Version = d.Version; }
        else Person = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        return Page();
    }
    public async Task<IActionResult> OnPostAsync()
    {
        if(!await LoadAsync()) return NotFound();
        if(!Money.TryParse(Amount,out var ore)) ModelState.AddModelError(nameof(Amount),"Ange ett positivt belopp med högst två decimaler.");
        if(!ModelState.IsValid) return Page();
        try { await service.SaveDepositAsync(Id,Version,Date,Person,ore,Comment,User.FindFirstValue(ClaimTypes.NameIdentifier)!); TempData["Message"] = "Insättningen är sparad."; return RedirectToPage("Index",new { Year = Date.Year }); }
        catch(Exception e) when(e is InvalidOperationException or DbUpdateException) { ModelState.AddModelError("",e is InvalidOperationException ? e.Message : "Insättningen har ändrats. Ladda om sidan."); return Page(); }
    }
    public async Task<IActionResult> OnPostVoidAsync()
    {
        if(Id == null || !await LoadAsync()) return NotFound();
        try { await service.VoidDepositAsync(Id.Value,Version,User.FindFirstValue(ClaimTypes.NameIdentifier)!); TempData["Message"] = "Insättningen är makulerad. En automatisk insättning för samma regel och månad skapas inte på nytt."; }
        catch(Exception e) when(e is InvalidOperationException or DbUpdateException) { TempData["Message"] = "Insättningen har ändrats. Ladda om sidan."; }
        return RedirectToPage("Index",new { Year = Deposit!.Date.Year });
    }
}
