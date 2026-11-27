using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using StockForge.API.Hosting;
using StockForge.Application.Identity;

namespace StockForge.API.Controllers;

/// <summary>Response body for sign-in endpoints. The refresh token travels in an HttpOnly cookie.</summary>
public sealed record AuthResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    CurrentUserDto User,
    string? RefreshToken = null,
    DateTimeOffset? RefreshTokenExpiresAt = null);

[ApiController]
[Route("api/auth")]
public sealed class AuthController(IAuthService auth, IConfiguration configuration) : ControllerBase
{
    public const string RefreshCookie = "sf_refresh";

    /// <summary>Non-browser clients may opt in to receiving the refresh token in the body.</summary>
    public const string BodyTransportHeader = "X-Refresh-Token-Transport";

    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<ActionResult<AuthResponse>> Register(RegisterOrganizationRequest request, CancellationToken ct) =>
        Issue(await auth.RegisterOrganizationAsync(request, Client, ct));

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken ct) =>
        Issue(await auth.LoginAsync(request, Client, ct));

    /// <summary>Rotates the refresh token (cookie, or body for API clients) and returns a new access token.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Refresh)]
    public async Task<ActionResult<AuthResponse>> Refresh([FromBody] RefreshRequest? request, CancellationToken ct)
    {
        var token = request?.RefreshToken is { Length: > 0 } fromBody ? fromBody : Request.Cookies[RefreshCookie];
        if (string.IsNullOrEmpty(token))
        {
            return Problem(statusCode: 401, title: "Authentication failed", detail: "No refresh token was provided.");
        }

        try
        {
            return Issue(await auth.RefreshAsync(token, Client, ct));
        }
        catch (StockForge.Application.Abstractions.AuthenticationFailedException)
        {
            ClearCookie();
            throw;
        }
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await auth.LogoutAsync(ct);
        ClearCookie();
        return NoContent();
    }

    [HttpPost("switch-organization")]
    [Authorize]
    public async Task<ActionResult<AuthResponse>> SwitchOrganization(SwitchTenantRequest request, CancellationToken ct) =>
        Issue(await auth.SwitchTenantAsync(request.TenantId, Client, ct));

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken ct)
    {
        await auth.RequestPasswordResetAsync(request.Email, ct);
        return Accepted(); // Same response whether or not the account exists.
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken ct)
    {
        await auth.ResetPasswordAsync(request, ct);
        return NoContent();
    }

    [HttpPost("accept-invitation")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<ActionResult<AuthResponse>> AcceptInvitation(AcceptInvitationRequest request, CancellationToken ct) =>
        Issue(await auth.AcceptInvitationAsync(request, Client, ct));

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        await auth.ChangePasswordAsync(request, ct);
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public Task<CurrentUserDto> Me(CancellationToken ct) => auth.GetCurrentUserAsync(ct);

    [HttpGet("sessions")]
    [Authorize]
    public Task<IReadOnlyList<SessionDto>> Sessions(CancellationToken ct) => auth.GetSessionsAsync(ct);

    [HttpDelete("sessions/{id:guid}")]
    [Authorize]
    public async Task<IActionResult> RevokeSession(Guid id, CancellationToken ct)
    {
        await auth.RevokeSessionAsync(id, ct);
        return NoContent();
    }

    private ClientInfo Client => new(HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString());

    private ActionResult<AuthResponse> Issue(AuthResult result)
    {
        Response.Cookies.Append(RefreshCookie, result.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = SecureCookie,
            SameSite = SameSiteMode.Strict,
            Path = "/api/auth",
            Expires = result.RefreshTokenExpiresAt,
            IsEssential = true,
        });
        Response.Headers.CacheControl = "no-store";

        var bodyTransport = string.Equals(Request.Headers[BodyTransportHeader], "body", StringComparison.OrdinalIgnoreCase);
        return Ok(new AuthResponse(result.AccessToken, result.AccessTokenExpiresAt, result.User,
            bodyTransport ? result.RefreshToken : null, bodyTransport ? result.RefreshTokenExpiresAt : null));
    }

    private void ClearCookie() =>
        Response.Cookies.Delete(RefreshCookie, new CookieOptions { Path = "/api/auth", Secure = SecureCookie, SameSite = SameSiteMode.Strict, HttpOnly = true });

    /// <summary>
    /// Secure unless the request is plain HTTP and the operator explicitly allows it (`Auth:AllowInsecureRefreshCookie`) —
    /// only for HTTP-only test environments on hosts other than localhost; production must use HTTPS.
    /// </summary>
    private bool SecureCookie => Request.IsHttps || !configuration.GetValue<bool>("Auth:AllowInsecureRefreshCookie");
}
