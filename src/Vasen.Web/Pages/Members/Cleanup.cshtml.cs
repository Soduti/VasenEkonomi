using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Vasen.Web.Data;
using Vasen.Web.Models;
using Vasen.Web.Services;
namespace Vasen.Web.Pages.Members;
public class CleanupModel(VasenDb db, UserManager<Member> users, MemberService service) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Id { get; set; }
    [BindProperty] public string? Stamp { get; set; }
    [BindProperty] public bool Confirm { get; set; }
    public Member? Member { get; set; }
    public List<Member> Members { get; set; } = [];
    private async Task<bool> IsOwnerAsync() => await users.GetUserAsync(User) is { IsOwner: true, IsActive: true, IsAnonymized: false };
    private async Task<bool> LoadAsync()
    {
        Members = await db.Users.AsNoTracking().Where(x => !x.IsActive && !x.IsAnonymized).OrderBy(x => x.DisplayName).ToListAsync();
        Member = Members.SingleOrDefault(x => x.Id == Id);
        return Id == null || Member != null;
    }
    public async Task<IActionResult> OnGetAsync()
    {
        if (!await IsOwnerAsync()) return Forbid();
        if (!await LoadAsync()) return NotFound();
        Stamp = Member?.ConcurrencyStamp;
        return Page();
    }
    public async Task<IActionResult> OnPostAsync()
    {
        if (!await IsOwnerAsync()) return Forbid();
        if (Id == null || !await LoadAsync()) return NotFound();
        if (!Confirm) ModelState.AddModelError("", "Bekräfta att kontot ska anonymiseras.");
        if (!ModelState.IsValid) return Page();
        try
        {
            await service.AnonymizeAsync(Id, Stamp, User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            TempData["Message"] = "Kontot är anonymiserat. Ekonomisk historik finns kvar.";
            return RedirectToPage();
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (Exception e) when (e is InvalidOperationException or DbUpdateException)
        { ModelState.AddModelError("", e is InvalidOperationException ? e.Message : "Kontot har ändrats. Ladda om sidan."); return Page(); }
    }
}
