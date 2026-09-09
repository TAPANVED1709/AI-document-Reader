using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using AI.DocumentReader.Api.Domain;
using AI.DocumentReader.Api.Infrastructure;
using AI.DocumentReader.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;

namespace AI.DocumentReader.Api.Controllers;

[ApiController, Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly DocumentDbContext _db;
    private readonly IPasswordHasher<ApplicationUser> _hasher;
    private readonly ISecurityAuditService _audit;
    private readonly IAntiforgery _antiforgery;
    public AuthController(DocumentDbContext db, IPasswordHasher<ApplicationUser> hasher, ISecurityAuditService audit, IAntiforgery antiforgery) { _db = db; _hasher = hasher; _audit = audit; _antiforgery = antiforgery; }

    public record RegisterRequest(string Email, string Password, string FirstName, string LastName, string Role = "PATIENT");
    public record LoginRequest(string Email, string Password);

    [HttpGet("csrf")]
    public IActionResult Csrf() { var tokens = _antiforgery.GetAndStoreTokens(HttpContext); return Ok(new { token = tokens.RequestToken }); }

    [HttpPost("register"), EnableRateLimiting("auth")]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken ct)
    {
        if (request.Password.Length < 12 || !request.Password.Any(char.IsUpper) || !request.Password.Any(char.IsDigit)) return BadRequest(new { error = "Password does not meet the minimum requirements." });
        var email = request.Email.Trim().ToLowerInvariant();
        if (await _db.ApplicationUsers.AnyAsync(x => x.Email == email, ct)) return BadRequest(new { error = "Registration could not be completed." });
        // Public registration cannot self-assign a privileged role; staff provisioning is administrative.
        var role = "PATIENT";
        var user = new ApplicationUser { Email = email, FirstName = request.FirstName.Trim(), LastName = request.LastName.Trim(), Role = role };
        user.PasswordHash = _hasher.HashPassword(user, request.Password);
        _db.ApplicationUsers.Add(user);
        if (role == "PATIENT") _db.PatientProfiles.Add(new PatientProfile { UserId = user.Id, FirstName = user.FirstName, LastName = user.LastName, PatientCode = $"P-{user.Id:N}"[..12] });
        await _db.SaveChangesAsync(ct);
        return Created("/api/auth/me", Safe(user));
    }

    [HttpPost("login"), EnableRateLimiting("auth")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        var user = await _db.ApplicationUsers.FirstOrDefaultAsync(x => x.Email == request.Email.Trim().ToLowerInvariant(), ct);
        if (user is null || !user.IsActive || _hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password) == PasswordVerificationResult.Failed) { await _audit.RecordAsync(User, "LOGIN_FAILURE", false, "AUTH", null, ct); return Unauthorized(new { error = "Invalid credentials." }); }
        user.LastLoginAt = DateTimeOffset.UtcNow; await _db.SaveChangesAsync(ct);
        var identity = new ClaimsIdentity("AppCookie"); identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())); identity.AddClaim(new Claim(ClaimTypes.Email, user.Email)); identity.AddClaim(new Claim(ClaimTypes.Role, user.Role)); if (user.OrganizationId.HasValue) identity.AddClaim(new Claim("organization_id", user.OrganizationId.Value.ToString()));
        await HttpContext.SignInAsync("AppCookie", new ClaimsPrincipal(identity));
        await _audit.RecordAsync(new ClaimsPrincipal(identity), "LOGIN_SUCCESS", true, "AUTH", user.Id, ct);
        return Ok(Safe(user));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout() { await HttpContext.SignOutAsync("AppCookie"); await _audit.RecordAsync(User, "LOGOUT", true, "AUTH"); return NoContent(); }

    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct) { if (!User.Identity?.IsAuthenticated ?? true) return Unauthorized(); var id = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!); var user = await _db.ApplicationUsers.FindAsync([id], ct); return user is null || !user.IsActive ? Unauthorized() : Ok(Safe(user)); }
    private static object Safe(ApplicationUser u) => new { id = u.Id, email = u.Email, firstName = u.FirstName, lastName = u.LastName, role = u.Role, organizationId = u.OrganizationId };
}
