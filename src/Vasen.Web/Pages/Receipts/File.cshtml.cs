using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Vasen.Web.Data;
using Vasen.Web.Services;
namespace Vasen.Web.Pages.Receipts;
public class FileModel(VasenDb db, FileStore files) : PageModel
{
    public async Task<IActionResult> OnGetAsync(int id)
    {
        var file = await db.ReceiptFiles.AsNoTracking().SingleOrDefaultAsync(f => f.Id == id);
        if (file is null || !System.IO.File.Exists(files.PathFor(file))) return NotFound();
        return new PhysicalFileResult(files.PathFor(file), file.ContentType) { EnableRangeProcessing = true };
    }
}
