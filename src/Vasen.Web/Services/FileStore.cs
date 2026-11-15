using Vasen.Web.Models;
namespace Vasen.Web.Services;
public class FileStore(DataPaths paths)
{
    public const long MaxFileBytes = 10 * 1024 * 1024;
    public async Task<List<ReceiptFile>> SaveAsync(IReadOnlyList<IFormFile> uploads)
    {
        if (uploads.Count > 8) throw new InvalidOperationException("Välj högst åtta filer åt gången.");
        var saved = new List<ReceiptFile>();
        try
        {
            foreach (var file in uploads)
            {
                if (file.Length <= 0 || file.Length > MaxFileBytes) throw new InvalidOperationException("Varje fil måste vara mellan 1 byte och 10 MB.");
                await using var input = file.OpenReadStream();
                var header = new byte[12];
                var length = await input.ReadAtLeastAsync(header, 12, throwOnEndOfStream: false);
                var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
                var type = extension switch
                {
                    ".pdf" when length >= 5 && header.AsSpan(0, 5).SequenceEqual("%PDF-"u8) => "application/pdf",
                    ".jpg" or ".jpeg" when length >= 3 && header[0] == 255 && header[1] == 216 && header[2] == 255 => "image/jpeg",
                    ".png" when length >= 8 && header.AsSpan(0, 8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}) => "image/png",
                    ".webp" when length == 12 && header.AsSpan(0,4).SequenceEqual("RIFF"u8) && header.AsSpan(8,4).SequenceEqual("WEBP"u8) => "image/webp",
                    _ => throw new InvalidOperationException("Välj JPG, PNG, WebP eller PDF. Om mobilen använder HEIC, välj en JPG-bild eller spara som PDF.")
                };
                var name = Path.GetFileName(file.FileName);
                var entry = new ReceiptFile { StorageName = Guid.NewGuid().ToString("N") + extension,
                    OriginalName = name[..Math.Min(name.Length, 255)], ContentType = type };
                saved.Add(entry);
                await using var output = new FileStream(PathFor(entry), FileMode.CreateNew);
                await output.WriteAsync(header.AsMemory(0, length));
                await input.CopyToAsync(output);
            }
            return saved;
        }
        catch { Delete(saved); throw; }
    }
    public string PathFor(ReceiptFile file) => Path.Combine(paths.Receipts, file.StorageName);
    public void Delete(IEnumerable<ReceiptFile> files) { foreach (var file in files) File.Delete(PathFor(file)); }
}
