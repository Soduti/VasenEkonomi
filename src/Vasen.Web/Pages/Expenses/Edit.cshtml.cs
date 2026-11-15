using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Vasen.Web.Data;
using Vasen.Web.Services;
namespace Vasen.Web.Pages.Expenses;
public class EditModel(VasenDb db, FinanceService service, TimeProvider clock) : PageModel
{
    [BindProperty(SupportsGet = true)] public int? Id { get; set; }
    [BindProperty(SupportsGet = true), Range(2000,2100)] public int Year { get; set; } = VasenDate.Today(clock).Year;
    [BindProperty, Required, StringLength(80)] public string Category { get; set; } = "";
    [BindProperty, Required] public string Amount { get; set; } = "";
    [BindProperty, StringLength(300)] public string? Comment { get; set; }
    [BindProperty] public Guid Version { get; set; }
    public async Task<IActionResult> OnGetAsync()
    {
        if(Id is {} id)
        {
            var item = await db.SharedExpenses.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && !x.IsVoided);
            if(item == null) return NotFound();
            Year = item.Year; Category = item.Category; Amount = Money.Input(item.AmountOre); Comment = item.Comment; Version = item.Version;
        }
        return Page();
    }
    public async Task<IActionResult> OnPostAsync()
    {
        if(!Money.TryParseNonNegative(Amount,out var ore)) ModelState.AddModelError(nameof(Amount),"Ange ett belopp på minst noll kronor.");
        if(!ModelState.IsValid) return Page();
        try { await service.SaveExpenseAsync(Id,Version,Year,Category,ore,Comment,User.FindFirstValue(ClaimTypes.NameIdentifier)!); TempData["Message"] = "Utgiften är sparad."; return RedirectToPage("Index",new { Year }); }
        catch(Exception e) when(e is InvalidOperationException or DbUpdateException) { ModelState.AddModelError("",e is InvalidOperationException ? e.Message : "Utgiften har ändrats. Ladda om sidan."); return Page(); }
    }
    public async Task<IActionResult> OnPostVoidAsync()
    {
        if(Id == null) return BadRequest();
        try { await service.VoidExpenseAsync(Id.Value,Version,User.FindFirstValue(ClaimTypes.NameIdentifier)!); TempData["Message"] = "Utgiften är makulerad och ingår inte längre i summorna."; }
        catch(Exception e) when(e is InvalidOperationException or DbUpdateException) { TempData["Message"] = "Utgiften har ändrats. Ladda om sidan."; }
        return RedirectToPage("Index",new { Year });
    }
}
