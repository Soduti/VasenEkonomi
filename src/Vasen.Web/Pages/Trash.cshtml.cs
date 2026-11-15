using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Vasen.Web.Data;
using Vasen.Web.Models;
using Vasen.Web.Services;
namespace Vasen.Web.Pages;
public class TrashModel(VasenDb db, ReceiptService service) : PageModel
{
    public List<Receipt> Receipts { get; set; } = [];
    public async Task OnGetAsync() => Receipts = await db.Receipts.AsNoTracking().Include(r => r.Member).Where(r => r.DeletedUtc != null).OrderByDescending(r => r.DeletedUtc).ToListAsync();
    public async Task<IActionResult> OnPostAsync(int id, Guid version, string action)
    {
        try
        {
            var actor = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
            if (action == "restore") await service.TrashAsync(id, version, true, actor);
            else if (action == "delete") await service.DeletePermanentlyAsync(id, version, actor);
            else return BadRequest();
            TempData["Message"] = action == "restore" ? "Kvittot är återställt." : "Kvittot och dess bilagor har raderats permanent från appen.";
        }
        catch(Exception e) when(e is InvalidOperationException or DbUpdateConcurrencyException) { TempData["Message"] = "Kvittot har ändrats. Ladda om och försök igen."; }
        return RedirectToPage();
    }
}
