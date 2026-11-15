using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Vasen.Web.Services;

namespace Vasen.Web.Pages.Budget;

public class IndexModel(BudgetSummaryService summaries, FinanceService finance, RecurringDepositService recurring, TimeProvider clock) : PageModel
{
    [BindProperty(SupportsGet = true)] public int Year { get; set; } = VasenDate.Today(clock).Year;
    public BudgetSummary Summary { get; set; } = null!;
    public async Task<IActionResult> OnGetAsync()
    {
        if (Year is < 2000 or > 2100) return BadRequest();
        await recurring.GenerateDueAsync(VasenDate.Today(clock));
        Summary = await summaries.ForYearAsync(Year); return Page();
    }
    public async Task<IActionResult> OnPostOpeningAsync(string? opening, Guid version)
    {
        try
        {
            if (!Money.TryParseSigned(opening,out var ore)) throw new InvalidOperationException("Ange en giltig ingående kassa. Noll och negativa belopp är tillåtna.");
            await finance.SaveOpeningAsync(Year,ore,version,User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            TempData["Message"] = "Ingående kassa är sparad.";
        }
        catch(Exception e) when (e is InvalidOperationException or DbUpdateException)
        { TempData["Message"] = e is InvalidOperationException ? e.Message : "Årsinställningen har ändrats. Ladda om och försök igen."; }
        return RedirectToPage(new { Year });
    }
}
