using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Vasen.Web.Data;
using Vasen.Web.Models;
namespace Vasen.Web.Pages.Members;
public class IndexModel(VasenDb db, UserManager<Member> users) : PageModel
{
    public List<Member> Members { get; set; } = [];
    public bool IsOwner { get; set; }
    public async Task OnGetAsync()
    {
        Members = await db.Users.AsNoTracking().Where(x => !x.IsAnonymized)
            .OrderByDescending(x => x.IsActive).ThenBy(x => x.DisplayName).ToListAsync();
        IsOwner = (await users.GetUserAsync(User))!.IsOwner;
    }
}
