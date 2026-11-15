using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Vasen.Web.Models;
using Vasen.Web.Services;
namespace Vasen.Web.Pages.Members;
public class EditModel(UserManager<Member> users, MemberService service) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Id { get; set; }
    [BindProperty, Required(ErrorMessage = "Ange ett namn."), StringLength(100)] public string DisplayName { get; set; } = "";
    [BindProperty, Required(ErrorMessage = "Ange en e-postadress."), EmailAddress(ErrorMessage = "Ange en giltig e-postadress."), StringLength(254)] public string Email { get; set; } = "";
    [BindProperty] public bool IsOwner { get; set; }
    [BindProperty] public MemberAccess AccessLevel { get; set; }
    [BindProperty] public string? Stamp { get; set; }
    public Member? Member { get; set; }
    public string? TemporaryPassword { get; set; }
    public bool IsSelf => Id == User.FindFirstValue(ClaimTypes.NameIdentifier);
    private string Actor => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    private async Task<bool> LoadAsync()
    {
        if (Id == null) return true;
        Member = await users.FindByIdAsync(Id);
        return Member is { IsAnonymized: false };
    }
    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadAsync()) return NotFound();
        if (Member is {} member) { DisplayName = member.DisplayName; Email = member.Email ?? ""; IsOwner = member.IsOwner; AccessLevel = member.AccessLevel; Stamp = member.ConcurrencyStamp; }
        return Page();
    }
    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadAsync()) return NotFound();
        if (!ModelState.IsValid) return Page();
        try
        {
            if (Id == null)
            {
                var created = await service.CreateAsync(DisplayName, Email, Actor, AccessLevel);
                Member = created.Member; Id = Member.Id; IsOwner = Member.IsOwner; AccessLevel = Member.AccessLevel; Stamp = Member.ConcurrencyStamp;
                TemporaryPassword = created.Password;
                ModelState.Clear();
                return Page();
            }
            await service.SaveAsync(Id, Stamp, DisplayName, Email, IsOwner, Actor, AccessLevel);
            TempData["Message"] = "Uppgifterna är sparade.";
            return RedirectToPage("Index");
        }
        catch (Exception e) when (e is InvalidOperationException or DbUpdateException)
        { ModelState.AddModelError("", e is InvalidOperationException ? e.Message : "Kontot har ändrats. Ladda om sidan."); return Page(); }
    }
    public async Task<IActionResult> OnPostActiveAsync(bool active) => await ActionAsync(async () =>
    {
        await service.SetActiveAsync(Id!, Stamp, active, Actor);
        TempData["Message"] = active ? "Kontot är aktivt igen." : "Kontot är pausat. Historiken finns kvar.";
    });
    public async Task<IActionResult> OnPostResetAsync() => await ActionAsync(async () =>
    {
        TemporaryPassword = await service.ResetPasswordAsync(Id!, Stamp, Actor);
    });
    private async Task<IActionResult> ActionAsync(Func<Task> action)
    {
        if (Id == null || !await LoadAsync()) return NotFound();
        // These actions use only the account id and concurrency stamp, not the edit fields.
        ModelState.Clear();
        try
        {
            await action();
            if (TemporaryPassword == null) return RedirectToPage(new { id = Id });
            await LoadAsync(); DisplayName = Member!.DisplayName; Email = Member.Email ?? ""; IsOwner = Member.IsOwner; AccessLevel = Member.AccessLevel; Stamp = Member.ConcurrencyStamp;
            return Page();
        }
        catch (Exception e) when (e is InvalidOperationException or DbUpdateException)
        {
            DisplayName = Member!.DisplayName; Email = Member.Email ?? ""; IsOwner = Member.IsOwner; AccessLevel = Member.AccessLevel;
            ModelState.AddModelError("", e is InvalidOperationException ? e.Message : "Kontot har ändrats. Ladda om sidan.");
            return Page();
        }
    }
}
