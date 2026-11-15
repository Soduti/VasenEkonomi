using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Vasen.Web.Models;
using Microsoft.EntityFrameworkCore;
namespace Vasen.Web.Pages.Account;
[AllowAnonymous]
public class LoginModel(SignInManager<Member> signIn, UserManager<Member> users) : PageModel
{
    [BindProperty, Required(ErrorMessage = "Ange namn eller e-post.")] public string LoginName { get; set; } = "";
    [BindProperty, Required(ErrorMessage = "Ange ditt lösenord.")] public string Password { get; set; } = "";
    [BindProperty] public bool RememberMe { get; set; } = true;
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }
    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();
        var key = users.NormalizeName(LoginName.Trim());
        var candidates = await users.Users.AsNoTracking().Where(x => !x.IsAnonymized)
            .Select(x => new { x.Id, x.DisplayName, x.Email, x.UserName }).ToListAsync();
        var matches = candidates.Where(x => users.NormalizeName(x.DisplayName) == key
            || users.NormalizeEmail(x.Email) == key || users.NormalizeName(x.UserName) == key).Take(2).ToList();
        // Reject ambiguous legacy identifiers instead of choosing another person's account.
        var member = matches.Count == 1 ? await users.FindByIdAsync(matches[0].Id) : null;
        var result = member is { IsActive: true, IsAnonymized: false } ? await signIn.PasswordSignInAsync(member, Password, RememberMe, true) : Microsoft.AspNetCore.Identity.SignInResult.Failed;
        if (result.Succeeded && member!.MustChangePassword) return RedirectToPage("ChoosePassword");
        if (result.Succeeded) return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/");
        ModelState.AddModelError("", "Inloggningen misslyckades. Kontrollera uppgifterna eller försök igen senare.");
        return Page();
    }
}
