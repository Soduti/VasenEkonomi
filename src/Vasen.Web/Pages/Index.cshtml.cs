using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Vasen.Web.Data;
using Vasen.Web.Models;
namespace Vasen.Web.Pages;
public class IndexModel(VasenDb db) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Person { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? To { get; set; }
    [BindProperty(SupportsGet = true)] public string Status { get; set; } = "all";
    [BindProperty(SupportsGet = true)] public string Sort { get; set; } = "date-desc";
    [BindProperty(SupportsGet = true)] public string? Category { get; set; }
    public List<Receipt> Receipts { get; set; } = [];
    public List<Member> Members { get; set; } = [];
    public List<string> Categories { get; set; } = [];
    public long Total => Receipts.Sum(r => r.AmountOre);
    public long Outstanding => Receipts.Where(r => r.WithdrawalId == null).Sum(r => r.AmountOre);
    public async Task OnGetAsync()
    {
        Members = await db.Users.OrderBy(m => m.DisplayName).ToListAsync();
        Categories = await db.Receipts.Where(r => r.DeletedUtc == null && r.Category != null).Select(r => r.Category!).Distinct().OrderBy(c => c).ToListAsync();
        var query = db.Receipts.AsNoTracking().Include(r => r.Member).Include(r => r.Files).Where(r => r.DeletedUtc == null);
        if (!string.IsNullOrEmpty(Person)) query = query.Where(r => r.MemberId == Person);
        if (From.HasValue) query = query.Where(r => r.Date >= From.Value);
        if (To.HasValue) query = query.Where(r => r.Date <= To.Value);
        if (Status == "unpaid") query = query.Where(r => r.WithdrawalId == null);
        if (Status == "paid") query = query.Where(r => r.WithdrawalId != null);
        if (Category == "_none") query = query.Where(r => r.Category == null);
        else if (!string.IsNullOrEmpty(Category)) query = query.Where(r => r.Category == Category);
        query = Sort switch
        {
            "date-asc" => query.OrderBy(r => r.Date),
            "amount-desc" => query.OrderByDescending(r => r.AmountOre),
            "amount-asc" => query.OrderBy(r => r.AmountOre),
            "person-asc" => query.OrderBy(r => r.Member.DisplayName).ThenByDescending(r => r.Date),
            "person-desc" => query.OrderByDescending(r => r.Member.DisplayName).ThenByDescending(r => r.Date),
            _ => query.OrderByDescending(r => r.Date)
        };
        Receipts = await ((IOrderedQueryable<Receipt>)query).ThenByDescending(r => r.Id).ToListAsync();
    }
}
