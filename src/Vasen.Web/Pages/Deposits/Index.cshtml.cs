using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Vasen.Web.Data;
using Vasen.Web.Models;
using Vasen.Web.Services;
namespace Vasen.Web.Pages.Deposits;
public class IndexModel(VasenDb db, RecurringDepositService recurring, TimeProvider clock) : PageModel
{
    [BindProperty(SupportsGet = true)] public int Year { get; set; } = VasenDate.Today(clock).Year;
    [BindProperty(SupportsGet = true)] public string? Person { get; set; }
    public List<Deposit> Deposits { get; set; } = [];
    public List<DepositRule> Rules { get; set; } = [];
    public List<Member> Members { get; set; } = [];
    public DateOnly Today => VasenDate.Today(clock);
    public async Task<IActionResult> OnGetAsync()
    {
        if(Year is < 2000 or > 2100) return BadRequest();
        await recurring.GenerateDueAsync(Today);
        var first = new DateOnly(Year,1,1); var last = new DateOnly(Year,12,31);
        Members = await db.Users.AsNoTracking().Where(x => x.IsOwner || db.Deposits.Any(d => d.MemberId == x.Id) || db.DepositRules.Any(r => r.MemberId == x.Id)).OrderBy(x => x.DisplayName).ToListAsync();
        var query = db.Deposits.AsNoTracking().Include(x => x.Member).Where(x => x.Date >= first && x.Date <= last && !x.IsVoided);
        if(!string.IsNullOrEmpty(Person)) query = query.Where(x => x.MemberId == Person);
        Deposits = await query.OrderByDescending(x => x.Date).ThenByDescending(x => x.Id).ToListAsync();
        Rules = await db.DepositRules.AsNoTracking().Include(x => x.Member).Where(x => string.IsNullOrEmpty(Person) || x.MemberId == Person).OrderBy(x => x.Member.DisplayName).ThenBy(x => x.Start).ToListAsync();
        return Page();
    }
}
