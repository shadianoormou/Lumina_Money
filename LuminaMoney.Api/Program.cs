using System.Text;
using LuminaMoney.Api.Data;
using LuminaMoney.Api.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using LuminaMoney.Api.Services;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Diagnostics;
using System.Net.Mail;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("FinanceDatabase") ?? throw new InvalidOperationException("ConnectionStrings:FinanceDatabase is required.");
var jwtKey = builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key is required.");
if (Encoding.UTF8.GetByteCount(jwtKey) < 32) throw new InvalidOperationException("Jwt:Key must contain at least 32 bytes.");
if (!builder.Environment.IsDevelopment() && jwtKey.StartsWith("LOCAL-DEV-ONLY", StringComparison.Ordinal)) throw new InvalidOperationException("Set a production Jwt__Key secret before starting the API.");
ValidateCommercialConfiguration(builder);
builder.Services.AddDbContext<FinanceDbContext>(options => options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(5), null)));
builder.Services.AddIdentityCore<AppUser>(options =>
{
    options.Password.RequiredLength = 10; options.Password.RequireDigit = true; options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true; options.Password.RequireNonAlphanumeric = true;
    options.Lockout.MaxFailedAccessAttempts = 5; options.User.RequireUniqueEmail = true;
}).AddRoles<IdentityRole<Guid>>().AddEntityFrameworkStores<FinanceDbContext>().AddSignInManager().AddDefaultTokenProviders();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new()
    {
        ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"], ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)), ClockSkew = TimeSpan.FromSeconds(30)
    };
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            var value = context.Principal?.FindFirst("uid")?.Value;
            if (!Guid.TryParse(value, out var userId)) { context.Fail("Missing user identity."); return; }
            var db = context.HttpContext.RequestServices.GetRequiredService<FinanceDbContext>();
            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId, context.HttpContext.RequestAborted);
            if (user is null) context.Fail("The account no longer exists.");
            else if (builder.Configuration.GetValue("Identity:RequireConfirmedEmail", false) && !user.EmailConfirmed) context.Fail("Email verification is required.");
        }
    };
});
builder.Services.AddAuthorization(); builder.Services.AddControllers(); builder.Services.AddOpenApi(); builder.Services.AddScoped<TokenService>();
builder.Services.AddSingleton<IAccountEmailSender, SmtpAccountEmailSender>();
builder.Services.AddHttpClient("store-verification", client => client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddScoped<IStorePurchaseVerifier, StorePurchaseVerifier>();
builder.Services.AddHttpClient("truelayer", client => client.Timeout = TimeSpan.FromSeconds(20));
builder.Services.AddScoped<IBankConnectionProvider, TrueLayerBankProvider>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    var authPermitLimit = Math.Clamp(builder.Configuration.GetValue("RateLimiting:AuthPermitLimit", 12), 4, 10_000);
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = authPermitLimit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
});
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1_048_576);
builder.Services.AddResponseCompression(options => options.EnableForHttps = true);
builder.Services.Configure<ForwardedHeadersOptions>(options => { options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto; options.ForwardLimit = 2; });
builder.Services.AddHealthChecks().AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"]).AddDbContextCheck<FinanceDbContext>("sql-server", tags: ["ready"]);
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context => context.ProblemDetails.Extensions["traceId"] = Activity.Current?.TraceId.ToString() ?? context.HttpContext.TraceIdentifier);
var app = builder.Build(); app.UseForwardedHeaders(); app.UseExceptionHandler(); app.UseResponseCompression();
if (app.Configuration.GetValue("Database:ApplyMigrationsOnStartup", false))
{
    using var scope = app.Services.CreateScope();
    var migrationDatabase = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
    app.Logger.LogInformation("Applying pending SQL Server migrations before accepting traffic");
    migrationDatabase.Database.Migrate();
}
if (app.Environment.IsDevelopment()) app.MapOpenApi();
else app.UseHsts();
app.Use(async (context, next) =>
{
    var started = Stopwatch.GetTimestamp();
    var requestId = context.Request.Headers["X-Request-Id"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(requestId) || requestId.Length > 80) requestId = Guid.NewGuid().ToString("N");
    context.TraceIdentifier = requestId; context.Response.Headers["X-Request-Id"] = requestId;
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "DENY";
    context.Response.Headers.Append("Referrer-Policy", "no-referrer");
    context.Response.Headers.Append("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
    context.Response.Headers.CacheControl = "no-store";
    try { await next(); }
    finally
    {
        app.Logger.LogInformation("HTTP {Method} {Path} returned {StatusCode} in {ElapsedMs:F1} ms request={RequestId}",
            context.Request.Method, context.Request.Path, context.Response.StatusCode, Stopwatch.GetElapsedTime(started).TotalMilliseconds, requestId);
    }
});
app.UseHttpsRedirection(); app.UseRateLimiter(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = check => check.Tags.Contains("live") });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.MapHealthChecks("/health");
app.MapGet("/version", () => Results.Ok(new { service = "LuminaMoney.Api", version = typeof(Program).Assembly.GetName().Version?.ToString(), utc = DateTime.UtcNow }));
app.Run();
static void ValidateCommercialConfiguration(WebApplicationBuilder builder)
{
    if (builder.Environment.IsDevelopment()) return;
    if (builder.Configuration["AllowedHosts"] is null or "*" or "") throw new InvalidOperationException("Set AllowedHosts to the production API host.");
    if (builder.Configuration.GetValue("Commercial:RequireStoreBilling", true))
    {
        foreach (var key in new[] { "Subscriptions:Iaphub:AppId", "Subscriptions:Iaphub:ServerApiKey", "Subscriptions:Iaphub:WebhookToken" })
            if (string.IsNullOrWhiteSpace(builder.Configuration[key])) throw new InvalidOperationException($"Set {key.Replace(':', '_')} for commercial store billing.");
    }
    if (builder.Configuration.GetValue("Commercial:RequireBanking", true))
    {
        foreach (var key in new[] { "Banking:TrueLayer:ClientId", "Banking:TrueLayer:ClientSecret", "Banking:TrueLayer:ReturnUri" })
            if (string.IsNullOrWhiteSpace(builder.Configuration[key])) throw new InvalidOperationException($"Set {key.Replace(':', '_')} for commercial Open Banking.");
        if (!builder.Configuration.GetValue("Banking:TrueLayer:Enabled", false)) throw new InvalidOperationException("Enable Banking:TrueLayer for commercial Open Banking.");
    }
    foreach (var key in new[] { "Legal:OperatorName", "Legal:OperatorCountry", "Legal:SupportEmail", "Legal:PrivacyContactEmail", "Legal:EffectiveDate" })
        if (string.IsNullOrWhiteSpace(builder.Configuration[key])) throw new InvalidOperationException($"Set {key.Replace(':', '_')} before publishing legal pages.");
    if (!DateOnly.TryParseExact(builder.Configuration["Legal:EffectiveDate"], "yyyy-MM-dd", out _))
        throw new InvalidOperationException("Legal__EffectiveDate must use yyyy-MM-dd.");
    if (builder.Configuration.GetValue("Legal:MinimumAge", 0) is < 13 or > 18)
        throw new InvalidOperationException("Legal__MinimumAge must be between 13 and 18.");
    if (!builder.Configuration.GetValue("Identity:RequireConfirmedEmail", false))
        throw new InvalidOperationException("Set Identity__RequireConfirmedEmail=true for commercial accounts.");
    foreach (var key in new[] { "Identity:AccountCodePepper", "Email:Smtp:Host", "Email:FromAddress" })
        if (string.IsNullOrWhiteSpace(builder.Configuration[key])) throw new InvalidOperationException($"Set {key.Replace(':', '_')} for verified email and account recovery.");
    if (Encoding.UTF8.GetByteCount(builder.Configuration["Identity:AccountCodePepper"]!) < 32)
        throw new InvalidOperationException("Identity__AccountCodePepper must contain at least 32 bytes.");
    if (!MailAddress.TryCreate(builder.Configuration["Email:FromAddress"], out _))
        throw new InvalidOperationException("Email__FromAddress must be a valid mailbox.");
    if (builder.Configuration.GetValue("Email:Smtp:Port", 587) is < 1 or > 65535)
        throw new InvalidOperationException("Email__Smtp__Port must be between 1 and 65535.");
    var smtpUser = builder.Configuration["Email:Smtp:Username"];
    var smtpPassword = builder.Configuration["Email:Smtp:Password"];
    if (string.IsNullOrWhiteSpace(smtpUser) != string.IsNullOrWhiteSpace(smtpPassword))
        throw new InvalidOperationException("Set both Email__Smtp__Username and Email__Smtp__Password, or leave both empty for a trusted relay.");
}
public partial class Program;
