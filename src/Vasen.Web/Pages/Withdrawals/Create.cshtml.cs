using System.Security.Claims;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Vasen.Web.Data;
using Vasen.Web.Models;
using Vasen.Web.Services;
namespace Vasen.Web.Pages.Withdrawals;
public class CreateModel(VasenDb db, ReceiptService service) : PageModel
{
    [BindProperty(SupportsGet = true)] public int[] Ids { get; set; } = [];
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }
    [BindProperty] public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    [BindProperty, Required] public string Amount { get; set; } = "";
    [BindProperty, StringLength(300)] public string? Comment { get; set; }
    [BindProperty] public bool CountDifferenceAsOwnContribution { get; set; }
    public List<Receipt> Receipts { get; set; } = [];
    public string BackUrl => Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/";
    public long ReceiptTotal => Receipts.Sum(r => r.AmountOre);
    public long OwnContribution => CountDifferenceAsOwnContribution && Money.TryParseNonNegative(Amount,out var ore)
        ? Math.Max(0,ReceiptTotal-ore) : 0;
    private async Task<bool> LoadAsync()
    {
        Receipts = await db.Receipts.Include(r => r.Member).Where(r => Ids.Contains(r.Id) && r.DeletedUtc == null && r.WithdrawalId == null).ToListAsync();
        if (Ids.Length == 0 || Receipts.Count != Ids.Distinct().Count() || Receipts.Select(r => r.MemberId).Distinct().Count() != 1)
        { ModelState.AddModelError("", "Välj oersatta kvitton för en och samma person. Gå tillbaka och kontrollera urvalet."); return false; }
        return true;
    }
    public async Task OnGetAsync() { if(await LoadAsync()) Amount = Money.Input(Receipts.Sum(r => r.AmountOre)); }
    public async Task<IActionResult> OnPostAsync()
    {
        await LoadAsync();
        if (!Money.TryParseNonNegative(Amount, out var ore) || (ore == 0 && !CountDifferenceAsOwnContribution))
            ModelState.AddModelError(nameof(Amount), "Ange ett positivt uttag, eller välj eget bidrag om hela kvittot bekostas privat.");
        if (!ModelState.IsValid) return Page();
        try
        {
            var id = await service.ReimburseAsync(Ids, Date, ore, Comment, User.FindFirstValue(ClaimTypes.NameIdentifier)!, CountDifferenceAsOwnContribution);
            TempData["Message"] = "Avslutet är registrerat och kvittona är klara.";
            return RedirectToPage("Details", new { id });
        }
        catch(Exception e) when(e is InvalidOperationException or DbUpdateConcurrencyException) { ModelState.AddModelError("", "Urvalet kunde inte ersättas. " + (e is InvalidOperationException ? e.Message : "Det har ändrats av någon annan.")); return Page(); }
    }
}
