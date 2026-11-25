using System.Globalization;
using System.Threading.RateLimiting;
using Intouch_67173986_L003.Data;
using Intouch_67173986_L003.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

// Thai formatting, but always the Gregorian calendar. When Windows uses a Thai regional format,
// .NET defaults to the Buddhist calendar: "2026-12-01" was parsed as the year 1483 and date
// inputs were rendered as 2569-xx-xx, so bookings and birthdays were saved with wrong years.
var appCulture = (CultureInfo)CultureInfo.GetCultureInfo("th-TH").Clone();
appCulture.DateTimeFormat.Calendar = new GregorianCalendar();
CultureInfo.DefaultThreadCurrentCulture = appCulture;

var builder = WebApplication.CreateBuilder(args);

// The real database password of this computer lives in appsettings.Local.json, which is not in git
// (.gitignore); it overrides appsettings.json. Environment variables and command-line options are
// added again after it, so they still win (test runs point the site at another database with them).
builder.Configuration
    .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args);

// Every role has its own fixed port (Infrastructure/RolePortals.cs); logging in on any port moves
// the user to the port of their role (Infrastructure/LoginHandoff.cs).
var portals = RolePortals.Fixed;
builder.Services.AddSingleton(portals);
builder.Services.AddSingleton<LoginHandoff>();
// Cancels online bookings left without a payment method (Infrastructure/PaymentDeadlineService.cs).
builder.Services.AddHostedService<PaymentDeadlineService>();

builder.Services.AddControllersWithViews(options =>
{
    // Every POST must carry the anti-forgery token, even if an action forgets [ValidateAntiForgeryToken].
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    // Keeps the signed-in user's name and role in the session in sync with the database.
    options.Filters.Add<CurrentUserFilter>(order: -1000);
    // Then keeps every role on its own port.
    options.Filters.Add<RolePortalFilter>(order: -999);
})
    // Status messages ("ยินดีต้อนรับ …") live in the session too, so they stay on their own port.
    .AddSessionStateTempDataProvider();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession();

// Slows down password guessing: at most N log-in (and N sign-up) attempts per minute from one IP
// address. Successful log-ins count too, so the limit leaves room to switch roles during a demo.
// The limit can be changed in appsettings.json ("Security:LoginAttemptsPerMinute").
var attemptsPerMinute = Math.Max(builder.Configuration.GetValue("Security:LoginAttemptsPerMinute", 20), 1);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(RateLimits.Auth, context => RateLimitPartition.GetFixedWindowLimiter(
        $"{context.Request.Path}|{context.Connection.RemoteIpAddress}",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = attemptsPerMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
});

// "DatabaseProvider" in appsettings.json picks the database: "MySql" (default) or "Sqlite"
// (the old cyber_users.db file, kept as a fallback).
var useMySql = !string.Equals(builder.Configuration["DatabaseProvider"], "Sqlite", StringComparison.OrdinalIgnoreCase);
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    if (useMySql)
    {
        options.UseMySQL(builder.Configuration.GetConnectionString("MySql")
            ?? throw new InvalidOperationException("ConnectionStrings:MySql is missing in appsettings.json"));
    }
    else
    {
        options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=cyber_users.db");
    }
});

var app = builder.Build();

// "dotnet run -- --print-schema" prints the CREATE TABLE script of the current model and stops
// (used to regenerate Database/02_schema.sql). Nothing is written to the database.
if (args.Contains("--print-schema"))
{
    // UTF-8 even when redirected to a file: some CHECK rules contain Thai text (bed types, shifts),
    // and a Thai Windows console would otherwise write them in its own code page (TIS-620).
    Console.OutputEncoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    using var schemaScope = app.Services.CreateScope();
    Console.WriteLine(schemaScope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.GenerateCreateScript());
    return;
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Database");
    try
    {
        DbInitializer.Initialize(db, logger);
    }
    catch (Exception ex)
    {
        var hint = useMySql
            ? "Check that the MySQL service (MySQL267) is running and that ConnectionStrings:MySql is correct " +
              "(appsettings.Local.json, or appsettings.json when there is no such file) " +
              "(run Database/01_create_database.sql once as root). To use the old SQLite file instead, set \"DatabaseProvider\": \"Sqlite\"."
            : "Check ConnectionStrings:DefaultConnection in appsettings.json.";
        logger.LogCritical(ex, "Cannot open the database. {Hint}", hint);
        throw;
    }

    // "dotnet run -- --seed-demo" adds the sample rooms, members, bookings and shifts
    // (Data/DemoDataSeeder.cs) once, then stops without starting the web site.
    if (args.Contains("--seed-demo"))
    {
        Console.WriteLine(DemoDataSeeder.Seed(db, logger));
        return;
    }

    // "dotnet run -- --seed-more" adds a large amount of extra sample data (Data/BulkDataSeeder.cs).
    if (args.Contains("--seed-more"))
    {
        Console.WriteLine(DemoDataSeeder.Seed(db, logger)); // the base sample set first (skipped if present)
        Console.WriteLine(BulkDataSeeder.Seed(db, logger));
        return;
    }

    // "dotnet run -- --seed-reviews" adds sample reviews to the sample members' finished stays (Data/ReviewSeeder.cs).
    if (args.Contains("--seed-reviews"))
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        Console.WriteLine(ReviewSeeder.Seed(db, logger));
        return;
    }
}

// The site listens on one http address per role. "PortalHost" in appsettings.json picks the network
// interface ("localhost" by default; "*" would also accept other computers on the network).
// The ports are fixed: these addresses replace applicationUrl in launchSettings.json, --urls and
// ASPNETCORE_URLS.
app.Urls.Clear();
foreach (var url in portals.ListenUrls(app.Configuration["PortalHost"] ?? "localhost"))
{
    app.Urls.Add(url);
}

// A request that reached the server on a port that belongs to no role is refused.
app.Use(async (context, next) =>
{
    if (portals.RoleOf(context) == null)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        await context.Response.WriteAsync("This port does not belong to any Royal Hotel portal.");
        return;
    }

    await next();
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

// 404 / 403 / 429 … show a friendly page inside the site layout instead of an empty browser page.
app.UseStatusCodePagesWithReExecute("/Home/HttpError", "?code={0}");
// No redirect to https: a single https port cannot tell the roles apart, so each portal is
// served over http on its own port (fine on localhost; put a reverse proxy with TLS in front
// of each port when the site goes on a real server).

// Basic protection headers for every response.
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "SAMEORIGIN";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        return Task.CompletedTask;
    });
    await next();
});

app.UseRouting();
app.UseRateLimiter();

// One session cookie per port. Browsers send a cookie to every port of the same host, so with a
// single shared cookie, logging in as Staff on one port would also replace the Customer session on
// another port. With its own cookie name, each portal keeps its own log-in.
foreach (var (_, port) in portals.All)
{
    var sessionOptions = new SessionOptions
    {
        IdleTimeout = TimeSpan.FromHours(2)
    };
    sessionOptions.Cookie.Name = $".RoyalHotel.Session.{port}";
    sessionOptions.Cookie.HttpOnly = true;
    sessionOptions.Cookie.IsEssential = true;
    sessionOptions.Cookie.SameSite = SameSiteMode.Lax;

    app.UseWhen(context => context.Connection.LocalPort == port, branch => branch.UseSession(sessionOptions));
}

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
