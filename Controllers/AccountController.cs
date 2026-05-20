using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using BudgetApp.Data;
using BudgetApp.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Controllers;

public sealed class AccountController(
    IConfiguration config,
    IHttpClientFactory httpFactory,
    BudgetDbContext db) : Controller
{
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return Redirect(returnUrl ?? "/");
        ViewBag.ReturnUrl = returnUrl;
        return View();
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(string username, string password, string? returnUrl, CancellationToken ct)
    {
        ViewBag.ReturnUrl = returnUrl;

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            ViewBag.Error = "Enter your username and password.";
            return View();
        }

        var kc = config.GetSection("Keycloak");
        var tokenUrl = $"{kc["Authority"]}/protocol/openid-connect/token";

        var formData = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"]    = "password",
            ["client_id"]     = kc["ClientId"]!,
            ["client_secret"] = kc["ClientSecret"]!,
            ["username"]      = username,
            ["password"]      = password,
            ["scope"]         = "openid profile email"
        });

        string body;
        try
        {
            using var client = httpFactory.CreateClient();
            var response = await client.PostAsync(tokenUrl, formData, ct).ConfigureAwait(false);
            body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                ViewBag.Error = ParseKeycloakError(body);
                return View();
            }
        }
        catch (HttpRequestException)
        {
            ViewBag.Error = "Cannot reach the authentication server. Try again later.";
            return View();
        }

        // Parse the access token — trusted source, no signature validation needed
        JwtSecurityToken jwt;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var accessToken = doc.RootElement.GetProperty("access_token").GetString()!;
            jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
        }
        catch
        {
            ViewBag.Error = "Authentication error. Please try again.";
            return View();
        }

        var userId            = jwt.Subject;
        var preferredUsername = jwt.Claims.FirstOrDefault(c => c.Type == "preferred_username")?.Value ?? username;
        var email             = jwt.Claims.FirstOrDefault(c => c.Type == "email")?.Value ?? "";

        // Provision user on first login; first ever user becomes admin
        var budgetUser = await db.BudgetUsers.FindAsync(new object[] { userId }, ct).ConfigureAwait(false);
        if (budgetUser is null)
        {
            var isFirst = !await db.BudgetUsers.AnyAsync(ct).ConfigureAwait(false);
            budgetUser = new BudgetUser
            {
                Id          = userId,
                Email       = email,
                DisplayName = preferredUsername,
                IsApproved  = isFirst,
                IsAdmin     = isFirst,
                CreatedUtc  = DateTime.UtcNow,
                ApprovedUtc = isFirst ? DateTime.UtcNow : null
            };
            db.BudgetUsers.Add(budgetUser);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        // Build cookie identity
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new("preferred_username",      preferredUsername),
            new(ClaimTypes.Email,          email),
            new(ClaimTypes.Name,           preferredUsername),
        };

        // Map Keycloak realm roles
        var realmAccess = jwt.Claims.FirstOrDefault(c => c.Type == "realm_access")?.Value;
        if (realmAccess is not null)
        {
            try
            {
                using var ra = JsonDocument.Parse(realmAccess);
                if (ra.RootElement.TryGetProperty("roles", out var roles))
                    foreach (var role in roles.EnumerateArray())
                        if (role.GetString() is { } r)
                            claims.Add(new Claim(ClaimTypes.Role, r));
            }
            catch { /* ignore malformed realm_access */ }
        }

        if (budgetUser.IsApproved) claims.Add(new Claim("budget_approved", "true"));
        if (budgetUser.IsAdmin)    claims.Add(new Claim("budget_admin",    "true"));

        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddHours(10) })
            .ConfigureAwait(false);

        return budgetUser.IsApproved
            ? Redirect(returnUrl ?? "/")
            : Redirect("/Account/Pending");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme)
            .ConfigureAwait(false);
        return Redirect("/Account/Login");
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Register()
    {
        if (User.Identity?.IsAuthenticated == true)
            return Redirect("/");
        return View();
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(
        string username, string firstName, string lastName,
        string email, string password, string confirmPassword,
        CancellationToken ct)
    {
        if (password != confirmPassword)
        {
            ViewBag.Error = "Passwords do not match.";
            return View();
        }
        if (password.Length < 8)
        {
            ViewBag.Error = "Password must be at least 8 characters.";
            return View();
        }

        var kc       = config.GetSection("Keycloak");
        var authority = kc["Authority"]!;
        var adminUrl = authority.Contains("/realms/", StringComparison.Ordinal)
            ? authority[..authority.IndexOf("/realms/", StringComparison.Ordinal)]
            : authority;
        var realm = authority.Contains("/realms/", StringComparison.Ordinal)
            ? authority[(authority.IndexOf("/realms/", StringComparison.Ordinal) + 8)..].Split('/')[0]
            : "budget";

        var adminToken = await GetAdminTokenAsync(adminUrl, kc["AdminUsername"]!, kc["AdminPassword"]!, ct)
            .ConfigureAwait(false);
        if (adminToken is null)
        {
            ViewBag.Error = "Registration is temporarily unavailable. Try again later.";
            return View();
        }

        var payload = JsonSerializer.Serialize(new
        {
            username,
            firstName,
            lastName,
            email,
            enabled        = true,
            emailVerified  = true,
            credentials    = new[] { new { type = "password", value = password, temporary = false } }
        });

        using var client = httpFactory.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Post, $"{adminUrl}/admin/realms/{realm}/users")
        {
            Headers  = { { "Authorization", $"Bearer {adminToken}" } },
            Content  = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        var response = await client.SendAsync(req, ct).ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            ViewBag.Error = "That username or email is already in use.";
            return View();
        }
        if (!response.IsSuccessStatusCode)
        {
            ViewBag.Error = "Registration failed. Please try again.";
            return View();
        }

        TempData["Notice"] = "Account created. Sign in and wait for administrator approval before you can access the app.";
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    [Authorize]
    public IActionResult Pending() => View();

    [HttpGet]
    [AllowAnonymous]
    public IActionResult AccessDenied() => View();

    private async Task<string?> GetAdminTokenAsync(string adminUrl, string username, string password, CancellationToken ct)
    {
        try
        {
            using var client = httpFactory.CreateClient();
            var response = await client.PostAsync(
                $"{adminUrl}/realms/master/protocol/openid-connect/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "password",
                    ["client_id"]  = "admin-cli",
                    ["username"]   = username,
                    ["password"]   = password
                }), ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            return doc.RootElement.GetProperty("access_token").GetString();
        }
        catch { return null; }
    }

    private static string ParseKeycloakError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error_description", out var desc))
            {
                var raw = desc.GetString() ?? "";
                if (raw.Contains("not fully set up", StringComparison.OrdinalIgnoreCase))
                    return "Your password must be changed — contact your administrator.";
                if (raw.Contains("Account disabled", StringComparison.OrdinalIgnoreCase))
                    return "Your account has been disabled.";
            }
        }
        catch { /* fall through to default */ }
        return "Invalid username or password.";
    }
}
