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
using OtpNet;

namespace BudgetApp.Controllers;

public sealed class AccountController(
    IConfiguration config,
    IHttpClientFactory httpFactory,
    BudgetDbContext db) : Controller
{
    // ── Login ─────────────────────────────────────────────────────────────────

    [HttpGet, AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return Redirect(returnUrl ?? "/");
        ViewBag.ReturnUrl = returnUrl;
        return View();
    }

    [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
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

        // 2FA verification
        if (budgetUser.TotpEnabled && !string.IsNullOrEmpty(budgetUser.TotpSecret))
        {
            var otp = Request.Form["otp"].ToString().Trim();
            if (string.IsNullOrEmpty(otp))
            {
                ViewBag.Error   = "Enter your authenticator code.";
                ViewBag.Show2FA = true;
                return View();
            }
            try
            {
                var totp = new Totp(Base32Encoding.ToBytes(budgetUser.TotpSecret));
                if (!totp.VerifyTotp(otp, out _, new VerificationWindow(2, 2)))
                {
                    ViewBag.Error   = "Invalid authenticator code.";
                    ViewBag.Show2FA = true;
                    return View();
                }
            }
            catch
            {
                ViewBag.Error   = "2FA verification failed. Contact your administrator.";
                ViewBag.Show2FA = true;
                return View();
            }
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new("preferred_username",      preferredUsername),
            new(ClaimTypes.Email,          email),
            new(ClaimTypes.Name,           preferredUsername),
        };

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
            catch { }
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

    // ── Logout ────────────────────────────────────────────────────────────────

    [HttpPost, ValidateAntiForgeryToken, Authorize]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme).ConfigureAwait(false);
        return Redirect("/Account/Login");
    }

    // ── Register ──────────────────────────────────────────────────────────────

    [HttpGet, AllowAnonymous]
    public IActionResult Register()
    {
        if (User.Identity?.IsAuthenticated == true) return Redirect("/");
        return View();
    }

    [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(
        string username, string firstName, string lastName,
        string email, string password, string confirmPassword,
        CancellationToken ct)
    {
        if (password != confirmPassword) { ViewBag.Error = "Passwords do not match."; return View(); }
        if (password.Length < 8)        { ViewBag.Error = "Password must be at least 8 characters."; return View(); }

        var kc        = config.GetSection("Keycloak");
        var authority = kc["Authority"]!;
        var (adminUrl, realm) = SplitAuthority(authority);

        var adminToken = await GetAdminTokenAsync(adminUrl, kc["AdminUsername"]!, kc["AdminPassword"]!, ct).ConfigureAwait(false);
        if (adminToken is null) { ViewBag.Error = "Registration is temporarily unavailable."; return View(); }

        var payload = JsonSerializer.Serialize(new
        {
            username, firstName, lastName, email,
            enabled = true, emailVerified = true,
            credentials = new[] { new { type = "password", value = password, temporary = false } }
        });

        using var client = httpFactory.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Post, $"{adminUrl}/admin/realms/{realm}/users")
        {
            Headers = { { "Authorization", $"Bearer {adminToken}" } },
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        var response = await client.SendAsync(req, ct).ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        { ViewBag.Error = "That username or email is already in use."; return View(); }
        if (!response.IsSuccessStatusCode)
        { ViewBag.Error = "Registration failed. Please try again."; return View(); }

        TempData["Notice"] = "Account created. Sign in and wait for administrator approval.";
        return RedirectToAction(nameof(Login));
    }

    // ── Forgot Password ───────────────────────────────────────────────────────

    [HttpGet, AllowAnonymous]
    public IActionResult ForgotPassword() => View();

    [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(string username, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(username))
        {
            var kc = config.GetSection("Keycloak");
            var (adminUrl, realm) = SplitAuthority(kc["Authority"]!);
            var adminToken = await GetAdminTokenAsync(adminUrl, kc["AdminUsername"]!, kc["AdminPassword"]!, ct).ConfigureAwait(false);
            if (adminToken is not null)
            {
                try
                {
                    using var client = httpFactory.CreateClient();
                    var search = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get,
                        $"{adminUrl}/admin/realms/{realm}/users?username={Uri.EscapeDataString(username)}&exact=true")
                    { Headers = { { "Authorization", $"Bearer {adminToken}" } } }, ct).ConfigureAwait(false);

                    if (search.IsSuccessStatusCode)
                    {
                        using var doc = JsonDocument.Parse(await search.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
                        var users = doc.RootElement.EnumerateArray().ToList();
                        if (users.Count > 0 && users[0].TryGetProperty("id", out var idEl))
                        {
                            await client.SendAsync(new HttpRequestMessage(HttpMethod.Put,
                                $"{adminUrl}/admin/realms/{realm}/users/{idEl.GetString()}/execute-actions-email")
                            {
                                Headers = { { "Authorization", $"Bearer {adminToken}" } },
                                Content = new StringContent("[\"UPDATE_PASSWORD\"]", Encoding.UTF8, "application/json")
                            }, ct).ConfigureAwait(false);
                        }
                    }
                }
                catch { /* always show generic message */ }
            }
        }

        TempData["Notice"] = "If that account exists and email is configured, a reset link has been sent. Otherwise, contact your administrator.";
        return RedirectToAction(nameof(Login));
    }

    // ── Profile ───────────────────────────────────────────────────────────────

    [HttpGet, Authorize]
    public async Task<IActionResult> Profile(CancellationToken ct)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var user   = await db.BudgetUsers.FindAsync(new object[] { userId! }, ct).ConfigureAwait(false);
        ViewBag.TotpEnabled = user?.TotpEnabled ?? false;
        return View();
    }

    [HttpPost, Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(
        string currentPassword, string newPassword, string confirmPassword, CancellationToken ct)
    {
        if (newPassword != confirmPassword)
        { TempData["PwError"] = "New passwords do not match."; return RedirectToAction(nameof(Profile)); }
        if (newPassword.Length < 8)
        { TempData["PwError"] = "Password must be at least 8 characters."; return RedirectToAction(nameof(Profile)); }

        var kc = config.GetSection("Keycloak");
        var (adminUrl, realm) = SplitAuthority(kc["Authority"]!);

        // Verify current password
        using var client = httpFactory.CreateClient();
        var verify = await client.PostAsync(
            $"{kc["Authority"]}/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password", ["client_id"] = kc["ClientId"]!,
                ["client_secret"] = kc["ClientSecret"]!,
                ["username"] = User.FindFirst("preferred_username")?.Value ?? "",
                ["password"] = currentPassword, ["scope"] = "openid"
            }), ct).ConfigureAwait(false);

        if (!verify.IsSuccessStatusCode)
        { TempData["PwError"] = "Current password is incorrect."; return RedirectToAction(nameof(Profile)); }

        var adminToken = await GetAdminTokenAsync(adminUrl, kc["AdminUsername"]!, kc["AdminPassword"]!, ct).ConfigureAwait(false);
        if (adminToken is null)
        { TempData["PwError"] = "Password change temporarily unavailable."; return RedirectToAction(nameof(Profile)); }

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var reset  = await client.SendAsync(new HttpRequestMessage(HttpMethod.Put,
            $"{adminUrl}/admin/realms/{realm}/users/{userId}/reset-password")
        {
            Headers = { { "Authorization", $"Bearer {adminToken}" } },
            Content = new StringContent(
                JsonSerializer.Serialize(new { type = "password", value = newPassword, temporary = false }),
                Encoding.UTF8, "application/json")
        }, ct).ConfigureAwait(false);

        TempData[reset.IsSuccessStatusCode ? "PwSuccess" : "PwError"] =
            reset.IsSuccessStatusCode ? "Password updated successfully." : "Failed to update password. Try again.";
        return RedirectToAction(nameof(Profile));
    }

    // ── 2FA Setup ─────────────────────────────────────────────────────────────

    [HttpGet, Authorize]
    public IActionResult Setup2FA()
    {
        var secret   = Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20));
        var username = User.FindFirst("preferred_username")?.Value ?? "user";
        ViewBag.Secret  = secret;
        ViewBag.OtpAuth = $"otpauth://totp/Budget:{Uri.EscapeDataString(username)}?secret={secret}&issuer=Budget&algorithm=SHA1&digits=6&period=30";
        return View();
    }

    [HttpPost, Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> Setup2FA(string secret, string code, CancellationToken ct)
    {
        bool valid = false;
        try
        {
            var totp = new Totp(Base32Encoding.ToBytes(secret));
            valid = totp.VerifyTotp(code?.Trim() ?? "", out _, new VerificationWindow(2, 2));
        }
        catch { }

        if (!valid)
        {
            var username = User.FindFirst("preferred_username")?.Value ?? "user";
            ViewBag.Error   = "Invalid code — check your authenticator app and try again.";
            ViewBag.Secret  = secret;
            ViewBag.OtpAuth = $"otpauth://totp/Budget:{Uri.EscapeDataString(username)}?secret={secret}&issuer=Budget&algorithm=SHA1&digits=6&period=30";
            return View();
        }

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var user   = await db.BudgetUsers.FindAsync(new object[] { userId! }, ct).ConfigureAwait(false);
        if (user is not null)
        {
            user.TotpSecret  = secret;
            user.TotpEnabled = true;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        TempData["TotpSuccess"] = "Two-factor authentication enabled.";
        return RedirectToAction(nameof(Profile));
    }

    [HttpPost, Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> Disable2FA(CancellationToken ct)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var user   = await db.BudgetUsers.FindAsync(new object[] { userId! }, ct).ConfigureAwait(false);
        if (user is not null)
        {
            user.TotpSecret  = null;
            user.TotpEnabled = false;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        TempData["TotpSuccess"] = "Two-factor authentication disabled.";
        return RedirectToAction(nameof(Profile));
    }

    // ── Status pages ──────────────────────────────────────────────────────────

    [HttpGet, Authorize]
    public IActionResult Pending() => View();

    [HttpGet, AllowAnonymous]
    public IActionResult AccessDenied() => View();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (string adminUrl, string realm) SplitAuthority(string authority)
    {
        var idx    = authority.IndexOf("/realms/", StringComparison.Ordinal);
        var admin  = idx >= 0 ? authority[..idx] : authority;
        var realm  = idx >= 0 ? authority[(idx + 8)..].Split('/')[0] : "budget";
        return (admin, realm);
    }

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
        catch { }
        return "Invalid username or password.";
    }
}
