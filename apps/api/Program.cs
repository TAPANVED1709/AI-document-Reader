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
builder.Services.AddRateLimiter(options =>
{
    var authLimit = builder.Configuration.GetValue("Security:AuthRequestsPerMinute", 10);
    var uploadLimit = builder.Configuration.GetValue("Security:UploadRequestsPerMinute", 30);
    var explanationLimit = builder.Configuration.GetValue("Security:ExplanationRequestsPerMinute", 20);
    options.AddFixedWindowLimiter("auth", o => { o.PermitLimit = authLimit; o.Window = TimeSpan.FromMinutes(1); o.QueueLimit = 0; });
    options.AddFixedWindowLimiter("upload", o => { o.PermitLimit = uploadLimit; o.Window = TimeSpan.FromMinutes(1); o.QueueLimit = 0; });
    options.AddFixedWindowLimiter("explanation", o => { o.PermitLimit = explanationLimit; o.Window = TimeSpan.FromMinutes(1); o.QueueLimit = 0; });
});

var aiServiceUrl = builder.Configuration["AiService:BaseUrl"] ?? "http://localhost:8000";
builder.Services.AddHttpClient<IAiServiceClient, AiServiceClient>(client =>
{
    client.BaseAddress = new Uri(aiServiceUrl);
    client.Timeout = TimeSpan.FromSeconds(60);
});

// 5. Configure CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("LocalFrontend", policy => policy.WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["http://localhost:3000", "http://127.0.0.1:3000"]).AllowAnyMethod().AllowAnyHeader().AllowCredentials());
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

app.Use(async (context, next) => { context.Response.Headers["X-Content-Type-Options"] = "nosniff"; context.Response.Headers["Referrer-Policy"] = "no-referrer"; context.Response.Headers["X-Frame-Options"] = "DENY"; context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self' http://localhost:3000 http://127.0.0.1:3000 http://localhost:5000 http://127.0.0.1:5000; frame-src 'self' http://localhost:5000 http://127.0.0.1:5000; object-src 'none'; base-uri 'self'; frame-ancestors 'none'"; await next(); });
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
