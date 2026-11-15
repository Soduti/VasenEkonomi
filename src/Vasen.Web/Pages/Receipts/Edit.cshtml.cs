using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Vasen.Web.Data;
using Vasen.Web.Models;
using Vasen.Web.Services;
namespace Vasen.Web.Pages.Receipts;
public class EditModel(VasenDb db, ReceiptService service, RequestAccess access) : PageModel
{
    [BindProperty(SupportsGet = true)] public int? Id { get; set; }
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }
    [BindProperty] public Guid Version { get; set; }
    [BindProperty] public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    [BindProperty, Required(ErrorMessage = "Välj en person.")] public string Person { get; set; } = "";
    [BindProperty, Required(ErrorMessage = "Ange ett belopp.")] public string Amount { get; set; } = "";
    [BindProperty, Required(ErrorMessage = "Ange en beskrivning."), StringLength(300)] public string Description { get; set; } = "";
    [BindProperty, StringLength(80)] public string? Category { get; set; }
    [BindProperty] public List<IFormFile> Uploads { get; set; } = [];
    public Receipt? Receipt { get; set; }
    public bool CanEdit => Receipt == null ? access.CanRegisterReceipts : access.CanEdit(Receipt);
    public string? RegisteredByName { get; set; }
    public List<Member> Members { get; set; } = [];
    public List<AuditEntry> History { get; set; } = [];
    public string BackUrl => Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/";

    private async Task<bool> LoadAsync()
    {
        Members = await db.Users.Where(m => (m.IsActive && !m.IsAnonymized) || m.Id == Person).OrderBy(m => m.DisplayName).ToListAsync();
        if (Id == null) return true;
        Receipt = await db.Receipts.AsNoTracking().Include(r => r.Files).Include(r => r.Member).Include(r => r.Withdrawal).SingleOrDefaultAsync(r => r.Id == Id && r.DeletedUtc == null);
        if (Receipt?.CreatedById is {} creator) RegisteredByName = await db.Users.Where(m => m.Id == creator).Select(m => m.DisplayName).SingleOrDefaultAsync();
        History = await db.AuditEntries.Where(a => a.Entity == "Kvitto" && a.EntityId == Id).OrderByDescending(a => a.AtUtc).ToListAsync();
        return Receipt != null;
    }
    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadAsync()) return NotFound();
        if (Receipt is {} r) { Version = r.Version; Date = r.Date; Person = r.MemberId; Amount = Money.Input(r.AmountOre); Description = r.Description; Category = r.Category; }
        else Person = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        return Page();
    }
    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadAsync()) return NotFound();
        if (!Money.TryParse(Amount, out var ore)) ModelState.AddModelError(nameof(Amount), "Ange ett positivt belopp med högst två decimaler.");
        if (!ModelState.IsValid) return Page();
        try
        {
            var actor = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
            if (Id.HasValue) await service.EditAsync(Id.Value, Version, Date, ore, Description, Category, Uploads, actor);
            else Id = await service.CreateAsync(Date, Person, ore, Description, Category, Uploads, actor);
            TempData["Message"] = "Utlägget är sparat.";
            return LocalRedirect(BackUrl);
        }
        catch (DbUpdateConcurrencyException) { ModelState.AddModelError("", "Kvittot ändrades av någon annan. Ladda om sidan innan du sparar igen."); }
        catch (InvalidOperationException e) { ModelState.AddModelError("", e.Message); }
        return Page();
    }
    public async Task<IActionResult> OnPostTrashAsync()
    {
        if (Id == null) return BadRequest();
        try { await service.TrashAsync(Id.Value, Version, false, User.FindFirstValue(ClaimTypes.NameIdentifier)!); TempData["Message"] = "Kvittot har flyttats till papperskorgen."; }
        catch (Exception e) when (e is InvalidOperationException or DbUpdateConcurrencyException) { TempData["Message"] = "Kvittot har ändrats. Ladda om och försök igen."; }
        return LocalRedirect(BackUrl);
    }
}
