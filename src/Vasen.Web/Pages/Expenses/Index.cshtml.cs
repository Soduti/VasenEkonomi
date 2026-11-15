using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Vasen.Web.Data;
using Vasen.Web.Models;
using Vasen.Web.Services;
namespace Vasen.Web.Pages.Expenses;
public class IndexModel(VasenDb db, TimeProvider clock) : PageModel
{
    [BindProperty(SupportsGet = true)] public int Year { get; set; } = VasenDate.Today(clock).Year;
    public List<SharedExpense> Expenses { get; set; } = [];
    public async Task<IActionResult> OnGetAsync()
    {
        if(Year is < 2000 or > 2100) return BadRequest();
        Expenses = await db.SharedExpenses.AsNoTracking().Where(x => x.Year == Year && !x.IsVoided).OrderBy(x => x.Category).ThenByDescending(x => x.Id).ToListAsync(); return Page();
    }
}
