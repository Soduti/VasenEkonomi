using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Vasen.Web.Data;
using Vasen.Web.Models;

namespace Vasen.Web.Pages.Account;

public class ChoosePasswordModel(UserManager<Member> users, SignInManager<Member> signIn, VasenDb db) : PageModel
{
    [BindProperty, Required(ErrorMessage = "Välj ett lösenord."), MinLength(10, ErrorMessage = "Lösenordet måste ha minst 10 tecken.")]
    public string NewPassword { get; set; } = "";
    [BindProperty, Compare(nameof(NewPassword), ErrorMessage = "Lösenorden stämmer inte överens.")]
    public string ConfirmPassword { get; set; } = "";

    public async Task<IActionResult> OnGetAsync() => await users.GetUserAsync(User) is { MustChangePassword: true }
        ? Page() : RedirectToPage("/Index");

    public async Task<IActionResult> OnPostAsync()
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var member = await users.GetUserAsync(User);
        if (member is not { IsActive: true, IsAnonymized: false, MustChangePassword: true }) return RedirectToPage("/Index");
        if (!ModelState.IsValid) return Page();
        if (await users.CheckPasswordAsync(member, NewPassword))
        {
            ModelState.AddModelError("", "Välj ett annat lösenord än det tillfälliga.");
            return Page();
        }
        member.MustChangePassword = false;
        var token = await users.GeneratePasswordResetTokenAsync(member);
        var result = await users.ResetPasswordAsync(member, token, NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError("", error.Code == "ConcurrencyFailure" ? "Kontot har ändrats. Logga in igen." : "Välj minst 10 tecken med stor och liten bokstav samt en siffra.");
            return Page();
        }
        await tx.CommitAsync();
        await signIn.RefreshSignInAsync(member);
        TempData["Message"] = "Ditt lösenord är sparat.";
        return RedirectToPage("/Index");
    }

    public async Task<IActionResult> OnPostLogoutAsync()
    {
        await signIn.SignOutAsync();
        return RedirectToPage("Login");
    }
}
