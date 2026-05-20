using System.Globalization;
using System.Security.Claims;
using BudgetApp;
using BudgetApp.Data;
using BudgetApp.Options;
using BudgetApp.Services;
using BudgetApp.ViewModels;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<BudgetOptions>(builder.Configuration.GetSection(BudgetOptions.SectionName));

builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 209_715_200);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 209_715_200);

builder.Services.AddDbContext<BudgetDbContext>((sp, o) =>
{
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    var opt = sp.GetRequiredService<IOptions<BudgetOptions>>().Value;
    var path = Path.IsPathRooted(opt.DatabasePath)
        ? opt.DatabasePath
        : Path.Combine(env.ContentRootPath, opt.DatabasePath);
    var dir = Path.GetDirectoryName(path);
    if (!string.IsNullOrEmpty(dir))
        Directory.CreateDirectory(dir);
    o.UseSqlite($"Data Source={path}");
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUserService>();
builder.Services.AddScoped<PdfTextExtractor>();
builder.Services.AddScoped<ExpenseClassifier>();
builder.Services.AddScoped<BudgetEtlService>();
builder.Services.AddScoped<SpendingService>();
builder.Services.AddHttpClient();

// Persist data protection keys to the Data volume so cookies survive container restarts
var keysDir = Path.Combine(
    builder.Environment.ContentRootPath,
    builder.Configuration["Budget:DatabasePath"] is { } p
        ? Path.Combine(Path.GetDirectoryName(p) ?? "Data", "keys")
        : "Data/keys");
Directory.CreateDirectory(keysDir);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysDir))
    .SetApplicationName("BudgetApp");

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/Account/Login";
        o.AccessDeniedPath = "/Account/AccessDenied";
        o.ExpireTimeSpan = TimeSpan.FromHours(10);
        o.SlidingExpiration = true;
    });

builder.Services.AddAuthorization();
builder.Services.AddControllersWithViews();

var app = builder.Build();

CultureInfo.DefaultThreadCurrentCulture = MoneyFormat.Usd;
CultureInfo.DefaultThreadCurrentUICulture = MoneyFormat.Usd;

using (var scope = app.Services.CreateScope())
{
    var env = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
    var opt = scope.ServiceProvider.GetRequiredService<IOptions<BudgetOptions>>().Value;
    foreach (var rel in new[] { opt.StatementInboxPath, opt.StatementProcessedPath })
    {
        var full = Path.IsPathRooted(rel) ? rel : Path.Combine(env.ContentRootPath, rel);
        Directory.CreateDirectory(full);
    }

    var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
    db.Database.EnsureCreated();
    await db.EnsureSchemaAsync().ConfigureAwait(false);
}

if (args.Length > 0 && string.Equals(args[0], "etl", StringComparison.OrdinalIgnoreCase))
{
    static string? Arg(string[] a, string name)
    {
        for (var i = 0; i < a.Length - 1; i++)
            if (string.Equals(a[i], name, StringComparison.OrdinalIgnoreCase))
                return a[i + 1];
        return null;
    }

    var ym = Arg(args, "--ym") ?? MonthHelper.FormatYm(MonthHelper.ParseMonth(null));
    var month = MonthHelper.ParseMonth(ym);
    var etlUserId = Arg(args, "--user") ?? "";
    using var cliScope = app.Services.CreateScope();
    var etl = cliScope.ServiceProvider.GetRequiredService<BudgetEtlService>();
    var folder = MonthInboxFolderFactory.MonthFolderName(month);
    var run = await etl.RunAsync(month.Year, folder, etlUserId, CancellationToken.None).ConfigureAwait(false);
    Console.WriteLine(run.Success
        ? $"OK: {run.TransactionsInserted} new, {run.TransactionsSkippedDuplicate} dupes, {run.FilesSeen} PDFs."
        : "ERROR: Import failed. See server logs.");
    return;
}

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Dashboard/Index");

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

app.UseRouting();
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(MoneyFormat.Usd),
    SupportedCultures = [MoneyFormat.Usd],
    SupportedUICultures = [MoneyFormat.Usd]
});

app.UseAuthentication();

// Redirect authenticated-but-unapproved users to /Account/Pending
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true)
    {
        var path = context.Request.Path.Value ?? "";
        var isPublicPath = path.StartsWith("/Account", StringComparison.OrdinalIgnoreCase)
                        || path.StartsWith("/favicon", StringComparison.OrdinalIgnoreCase)
                        || path.StartsWith("/_", StringComparison.OrdinalIgnoreCase);

        if (!isPublicPath && context.User.FindFirstValue("budget_approved") != "true")
        {
            context.Response.Redirect("/Account/Pending");
            return;
        }
    }
    await next(context).ConfigureAwait(false);
});

app.UseAuthorization();
app.MapStaticAssets();
app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Dashboard}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
