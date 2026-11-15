using Microsoft.AspNetCore.Authentication;
using System.Globalization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Vasen.Web.Data;
using Vasen.Web.Models;
using Vasen.Web.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<DataPaths>();
builder.Services.AddDbContext<VasenDb>((services, options) =>
    options.UseSqlite($"Data Source={services.GetRequiredService<DataPaths>().Database};Foreign Keys=True"));
builder.Services.AddIdentity<Member, IdentityRole>(options =>
{
    options.Password.RequiredLength = 10;
    options.Password.RequireNonAlphanumeric = false;
    // Required, unique email is checked transactionally in MemberService when creating/editing.
    // Identity must also allow legacy and anonymized accounts without an email.
    options.User.RequireUniqueEmail = false;
    options.Lockout.MaxFailedAccessAttempts = 5;
}).AddEntityFrameworkStores<VasenDb>().AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "Vasen.Identity";
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromDays(90);
    options.SlidingExpiration = true;
    options.Events.OnValidatePrincipal = async context =>
    {
        await SecurityStampValidator.ValidatePrincipalAsync(context);
        if (context.Principal?.Identity?.IsAuthenticated == true)
        {
            var users = context.HttpContext.RequestServices.GetRequiredService<UserManager<Member>>();
            var member = await users.GetUserAsync(context.Principal);
            var identityOptions = context.HttpContext.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<IdentityOptions>>().Value;
            var stamp = context.Principal.FindFirst(identityOptions.ClaimsIdentity.SecurityStampClaimType)?.Value;
            if (member is not { IsActive: true, IsAnonymized: false } || member.SecurityStamp != stamp)
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            }
        }
    };
});
builder.Services.AddDataProtection().SetApplicationName("Vasen").PersistKeysToFileSystem(
    new DirectoryInfo(new DataPaths(builder.Configuration, builder.Environment).Keys));
builder.Services.AddScoped<RequestAccess>();
builder.Services.AddScoped<Vasen.Web.Security.AccessFilter>();
builder.Services.AddRazorPages(options => {
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.ConfigureFilter(new Microsoft.AspNetCore.Mvc.ServiceFilterAttribute(typeof(Vasen.Web.Security.AccessFilter)));
});
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o => o.MultipartBodyLengthLimit = 85 * 1024 * 1024);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 85 * 1024 * 1024);
builder.Services.AddScoped<FileStore>();
builder.Services.AddScoped<ReceiptService>();
builder.Services.AddScoped<MemberService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<FinanceService>();
builder.Services.AddScoped<RecurringDepositService>();
builder.Services.AddScoped<BudgetSummaryService>();
if (builder.Configuration.GetValue("Vasen:RunDepositWorker", true)) builder.Services.AddHostedService<DepositWorker>();
var app = builder.Build();
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<VasenDb>().Database.MigrateAsync();
    if (args.Contains("--create-owner")) { await OwnerProvisioner.RunAsync(scope.ServiceProvider); return; }
}
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture("sv-SE"),
    SupportedCultures = [CultureInfo.GetCultureInfo("sv-SE")],
    SupportedUICultures = [CultureInfo.GetCultureInfo("sv-SE")]
});
if (!app.Environment.IsDevelopment()) { app.UseExceptionHandler("/Error"); app.UseHsts(); }
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "same-origin";
    // OCR uses a same-origin worker and WebAssembly; keep JavaScript eval disabled.
    var isReceiptEdit = context.Request.Path.StartsWithSegments("/Receipts/Edit");
    var ocrPolicy = isReceiptEdit || context.Request.Path.StartsWithSegments("/lib/tesseract")
        || context.Request.Path == "/js/receipt-ocr-worker.js"
        ? " 'wasm-unsafe-eval'" : "";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; img-src 'self' blob:; style-src 'self'; script-src 'self'" + ocrPolicy + "; worker-src 'self'; frame-ancestors 'none'; object-src 'none'; base-uri 'self'; form-action 'self'";
    if (!context.Request.Path.StartsWithSegments("/css") && !context.Request.Path.StartsWithSegments("/js"))
    {
        context.Response.Headers.CacheControl = "no-cache, no-store";
        context.Response.Headers.Pragma = "no-cache";
    }
    await next();
});
if (!app.Environment.IsDevelopment()) app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true && context.Request.Path != "/Account/ChoosePassword")
    {
        var users = context.RequestServices.GetRequiredService<UserManager<Member>>();
        if (await users.GetUserAsync(context.User) is { MustChangePassword: true })
        {
            context.Response.Redirect("/Account/ChoosePassword");
            return;
        }
    }
    await next();
});
app.MapRazorPages();
app.Run();
public partial class Program { }
