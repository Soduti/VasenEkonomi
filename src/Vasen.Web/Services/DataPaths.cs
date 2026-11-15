namespace Vasen.Web.Services;
public sealed class DataPaths
{
    public string Root { get; }
    public string Receipts => Path.Combine(Root, "receipts");
    public string Keys => Path.Combine(Root, "identity-keys");
    public string Database => Path.Combine(Root, "database", "vasen.db");
    public DataPaths(IConfiguration config, IWebHostEnvironment environment)
    {
        var configured = config["Vasen:DataPath"];
        if (environment.IsProduction() && string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException("Ange Vasen__DataPath till en beständig mapp utanför Current och Release.");
        Root = Path.GetFullPath(configured ?? Path.Combine(environment.ContentRootPath, "..", "..", "Vasen-data"));
        Directory.CreateDirectory(Receipts);
        Directory.CreateDirectory(Keys);
        Directory.CreateDirectory(Path.GetDirectoryName(Database)!);
    }
}
