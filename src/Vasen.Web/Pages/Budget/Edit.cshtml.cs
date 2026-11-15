using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Vasen.Web.Data;
using Vasen.Web.Models;
using Vasen.Web.Services;
namespace Vasen.Web.Pages.Budget;
public class EditModel(VasenDb db, FinanceService service, TimeProvider clock) : PageModel
{
    [BindProperty(SupportsGet = true)] public int? Id { get; set; }
    [BindProperty(SupportsGet = true), Range(2000,2100)] public int Year { get; set; } = VasenDate.Today(clock).Year;
    [BindProperty(SupportsGet = true)] public ExpenseSource Source { get; set; }
    [BindProperty(SupportsGet = true), Required, StringLength(80)] public string Category { get; set; } = "";
    [BindProperty, Required] public string Amount { get; set; } = "";
    [BindProperty] public Guid Version { get; set; }
    public async Task<IActionResult> OnGetAsync()
    {
        if(Id is {} id)
        {
            var item = await db.BudgetLines.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
            if(item == null) return NotFound();
            Year = item.Year; Source = item.Source; Category = item.Category; Amount = Money.Input(item.AmountOre); Version = item.Version;
        }
        if (Source == ExpenseSource.PrivateReceipt)
        {
            Category = BudgetLine.PrivateCategory;
            if (!Id.HasValue)
            {
                var existing = await db.BudgetLines.AsNoTracking().SingleOrDefaultAsync(x => x.Year == Year && x.Source == ExpenseSource.PrivateReceipt);
                if (existing != null) return RedirectToPage(new { id = existing.Id });
            }
        }
        ModelState.Remove(nameof(Category));
        return Page();
    }
    public async Task<IActionResult> OnPostAsync()
    {
        if (Source == ExpenseSource.PrivateReceipt)
        {
            Category = BudgetLine.PrivateCategory;
            ModelState.Remove(nameof(Category));
        }
        if(!Money.TryParseNonNegative(Amount,out var ore)) ModelState.AddModelError(nameof(Amount),"Ange ett budgetbelopp på minst noll kronor.");
        if(!ModelState.IsValid) return Page();
        try { await service.SaveBudgetAsync(Id,Version,Year,Source,Category,ore,User.FindFirstValue(ClaimTypes.NameIdentifier)!); TempData["Message"] = "Budgetposten är sparad."; return RedirectToPage("Index",new { Year }); }
        catch(Exception e) when(e is InvalidOperationException or DbUpdateException) { ModelState.AddModelError("",e is InvalidOperationException ? e.Message : "Budgetposten har ändrats eller finns redan. Ladda om sidan."); return Page(); }
    }
    public async Task<IActionResult> OnPostDeleteAsync()
    {
        if(Id == null) return BadRequest();
        try { await service.DeleteBudgetAsync(Id.Value,Version,User.FindFirstValue(ClaimTypes.NameIdentifier)!); TempData["Message"] = "Budgetposten är borttagen. Registrerade utgifter påverkas inte."; }
        catch(Exception e) when(e is InvalidOperationException or DbUpdateException) { TempData["Message"] = "Budgetposten har ändrats. Ladda om sidan."; }
        return RedirectToPage("Index",new { Year });
    }
}
