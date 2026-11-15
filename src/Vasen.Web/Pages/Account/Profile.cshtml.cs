using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Vasen.Web.Models;
namespace Vasen.Web.Pages.Account;
public class ProfileModel(UserManager<Member> users, SignInManager<Member> signIn) : PageModel
{
    public Member Member { get; set; } = null!;
    [BindProperty, Required] public string CurrentPassword { get; set; } = "";
    [BindProperty, Required, MinLength(10)] public string NewPassword { get; set; } = "";
    public async Task OnGetAsync() => Member = (await users.GetUserAsync(User))!;
    public async Task<IActionResult> OnPostLogoutAsync() { await signIn.SignOutAsync(); return RedirectToPage("Login"); }
    public async Task<IActionResult> OnPostAsync()
    {
        await OnGetAsync();
        if (!ModelState.IsValid) return Page();
        var result = await users.ChangePasswordAsync(Member, CurrentPassword, NewPassword);
        if (result.Succeeded) { await signIn.RefreshSignInAsync(Member); TempData["Message"] = "Lösenordet är ändrat."; return RedirectToPage(); }
        foreach (var error in result.Errors) ModelState.AddModelError("", error.Description);
        return Page();
    }
}
