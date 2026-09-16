using System.Text.Json.Serialization;
using AI.DocumentReader.Api.Infrastructure;
using AI.DocumentReader.Api.Services;
using AI.DocumentReader.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc.Controllers;
using AI.DocumentReader.Api.Controllers;

var builder = WebApplication.CreateBuilder(args);

// 1. Add Controllers with JSON String Enum conversion
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });
builder.Services.AddAuthentication("AppCookie").AddCookie("AppCookie", options =>
{
    options.Cookie.Name = "ai_document_reader_session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
});
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(options => { options.HeaderName = "X-XSRF-TOKEN"; options.Cookie.Name = "ai_document_reader_csrf"; options.Cookie.HttpOnly = false; options.Cookie.SameSite = SameSiteMode.Lax; });
builder.Services.AddScoped<IPasswordHasher<ApplicationUser>, PasswordHasher<ApplicationUser>>();

// 2. Configure Swagger / OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title = "AI Document Reader API",
        Version = "v1",
        Description = "Medical document intelligence backend for pathology/laboratory reports."
    });
});

// 3. Configure Database
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<DocumentDbContext>(options =>
{
    if (!string.IsNullOrWhiteSpace(connectionString) && !connectionString.Contains("sqlite", StringComparison.OrdinalIgnoreCase))
    {
        options.UseSqlServer(connectionString, sqlOptions =>
        {
            sqlOptions.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(3), errorNumbersToAdd: null);
        });
    }
    else
    {
        // Standalone local development fallback
        options.UseSqlite("Data Source=ai_document_reader.db");
    }
});

// 4. Register Application Services
builder.Services.AddScoped<ILocalStorageService, LocalStorageService>();
builder.Services.AddSingleton<IReferenceRangeClassifier, ReferenceRangeClassifier>();
builder.Services.AddScoped<IMedicalResourceAuthorizationService, MedicalResourceAuthorizationService>();
builder.Services.AddScoped<ISecurityAuditService, SecurityAuditService>();
builder.Services.AddScoped<IReportProcessingService, ReportProcessingService>();
builder.Services.AddSingleton<OllamaConcurrencyLimiter>();
builder.Services.AddHostedService<ProcessingWorker>();
builder.Services.AddRateLimiter(options =>
{
    var authLimit = builder.Configuration.GetValue("Security:AuthRequestsPerMinute", 10);
    var uploadLimit = builder.Configuration.GetValue("Security:UploadRequestsPerMinute", 30);
    var explanationLimit = builder.Configuration.GetValue("Security:ExplanationRequestsPerMinute", 20);
    var ingestionLimit = builder.Configuration.GetValue("Security:IngestionRequestsPerMinute", 30);
    options.AddFixedWindowLimiter("auth", o => { o.PermitLimit = authLimit; o.Window = TimeSpan.FromMinutes(1); o.QueueLimit = 0; });
    options.AddFixedWindowLimiter("upload", o => { o.PermitLimit = uploadLimit; o.Window = TimeSpan.FromMinutes(1); o.QueueLimit = 0; });
    options.AddFixedWindowLimiter("explanation", o => { o.PermitLimit = explanationLimit; o.Window = TimeSpan.FromMinutes(1); o.QueueLimit = 0; });
    options.AddFixedWindowLimiter("ingestion", o => { o.PermitLimit = ingestionLimit; o.Window = TimeSpan.FromMinutes(1); o.QueueLimit = 0; });
});

var aiServiceUrl = builder.Configuration["AiService:BaseUrl"] ?? "http://localhost:8000";
builder.Services.AddHttpClient<IAiServiceClient, AiServiceClient>(client =>
{
    client.BaseAddress = new Uri(aiServiceUrl);
    client.Timeout = TimeSpan.FromSeconds(60);
});

// 5. Configure CORS
var frontendOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:3001", "http://127.0.0.1:3001"];
// These exact origins also form the PDF frame allowlist. Reject CSP syntax,
// wildcards and URL paths rather than interpreting them as broader permissions.
frontendOrigins = frontendOrigins.Select(origin =>
{
    if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)
        || uri.Scheme is not ("http" or "https") || origin.Contains('*')
        || uri.UserInfo.Length != 0 || uri.AbsolutePath != "/"
        || uri.Query.Length != 0 || uri.Fragment.Length != 0)
        throw new InvalidOperationException("Cors:AllowedOrigins must contain exact HTTP(S) origins.");
    return uri.GetLeftPart(UriPartial.Authority);
}).Distinct().ToArray();
builder.Services.AddCors(options =>
{
    options.AddPolicy("LocalFrontend", policy => policy.WithOrigins(frontendOrigins).AllowAnyMethod().AllowAnyHeader().AllowCredentials());
});

var app = builder.Build();

// Auto-create / migrate database tables if needed
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var dbContext = scope.ServiceProvider.GetRequiredService<DocumentDbContext>();

    try
    {
        logger.LogInformation("Verifying database connectivity and schema...");
        await dbContext.Database.EnsureCreatedAsync();
        await AI.DocumentReader.Api.Infrastructure.Stage2SchemaUpgrade.ApplyAsync(dbContext);
        logger.LogInformation("Database ready.");
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Could not initialize primary database provider. Will attempt SQLite fallback if SQL Server is not reachable.");
    }
}

// 6. Middleware Pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "AI Document Reader API v1");
    });
}

app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        var action = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
        var inlinePdf = action?.ControllerTypeInfo.AsType() == typeof(ReportsController)
            && action.ActionName == nameof(ReportsController.GetReportFile)
            && context.Response.StatusCode is 200 or 206
            && context.Response.ContentType == "application/pdf" && frontendOrigins.Length > 0;
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        if (inlinePdf) context.Response.Headers.Remove("X-Frame-Options");
        else context.Response.Headers["X-Frame-Options"] = "DENY";
        var ancestors = inlinePdf ? string.Join(' ', frontendOrigins) : "'none'";
        context.Response.Headers["Content-Security-Policy"] = inlinePdf
            ? $"frame-ancestors {ancestors}"
            : $"default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self' {string.Join(' ', frontendOrigins)}; frame-src 'self'; object-src 'none'; base-uri 'self'; frame-ancestors 'none'";
        return Task.CompletedTask;
    });
    await next();
});
app.UseRouting();
app.UseCors("LocalFrontend");
app.UseRateLimiter();
app.UseAuthentication();
if (!app.Environment.IsDevelopment()) { app.UseHttpsRedirection(); app.UseHsts(); }
app.Use(async (context, next) =>
{
    if (HttpMethods.IsPost(context.Request.Method) || HttpMethods.IsPut(context.Request.Method) || HttpMethods.IsPatch(context.Request.Method) || HttpMethods.IsDelete(context.Request.Method))
    {
        var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
        try { await antiforgery.ValidateRequestAsync(context); }
        catch (AntiforgeryValidationException) { context.Response.StatusCode = StatusCodes.Status400BadRequest; await context.Response.WriteAsJsonAsync(new { error = "A valid CSRF token is required." }); return; }
    }
    await next();
});

app.UseAuthorization();

app.MapControllers();

app.Run();

// Needed for WebApplicationFactory in integration tests
public partial class Program { }
