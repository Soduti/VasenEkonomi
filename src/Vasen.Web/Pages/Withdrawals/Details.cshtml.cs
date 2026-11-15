using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Vasen.Web.Data;
using Vasen.Web.Models;
using Vasen.Web.Services;
namespace Vasen.Web.Pages.Withdrawals;
public class DetailsModel(VasenDb db, ReceiptService service) : PageModel
{
    public Withdrawal Withdrawal { get; set; } = null!;
    public long CurrentTotal => Withdrawal.Receipts.Where(r => r.DeletedUtc == null).Sum(r => r.AmountOre);
    public List<AuditEntry> History { get; set; } = [];
    public async Task<IActionResult> OnGetAsync(int id)
    {
        Withdrawal = (await db.Withdrawals.AsNoTracking().Include(w => w.Member).Include(w => w.Receipts).SingleOrDefaultAsync(w => w.Id == id))!;
        if (Withdrawal is null) return NotFound();
        History = await db.AuditEntries.Where(a => a.Entity == "Uttag" && a.EntityId == id).OrderByDescending(a => a.AtUtc).ToListAsync();
        return Page();
    }
    public async Task<IActionResult> OnPostUndoAsync(int id, Guid version)
    {
        try { await service.UndoAsync(id, version, User.FindFirstValue(ClaimTypes.NameIdentifier)!); TempData["Message"] = "Ersättningskopplingen är ångrad. Kvittona är åter oersatta. Ingen banköverföring har gjorts."; }
        catch(Exception e) when(e is InvalidOperationException or DbUpdateConcurrencyException) { TempData["Message"] = "Uttaget har ändrats. Ladda om och försök igen."; }
        return RedirectToPage(new { id });
    }
}
