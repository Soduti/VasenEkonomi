using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Vasen.Web.Data;
using Vasen.Web.Models;
namespace Vasen.Web.Pages.Withdrawals;
public class IndexModel(VasenDb db) : PageModel
{
    public List<Withdrawal> Withdrawals { get; set; } = [];
    public async Task OnGetAsync() => Withdrawals = await db.Withdrawals.AsNoTracking().Where(w => !w.IsUndone).Include(w => w.Member).OrderByDescending(w => w.Date).ToListAsync();
}
