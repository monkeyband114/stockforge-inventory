using System.Net;
using StockForge.API.Controllers;
using StockForge.Application.Identity;
using StockForge.Tests.Integration.Support;

namespace StockForge.Tests.Integration;

public sealed class AuthenticationTests(ApiFixture fixture)
{
    private static readonly Dictionary<string, string> BodyTransport = new() { [AuthController.BodyTransportHeader] = "body" };

    [Fact]
    public async Task Registration_creates_organization_with_admin_and_defaults()
    {
        var tenant = await TestTenant.CreateAsync(fixture, "Registration");

        var me = (await tenant.Admin.GetAsync<CurrentUserDto>("/api/auth/me")).Ok();
        Assert.Equal(tenant.TenantId, me.Tenant!.Id);
        Assert.Contains("OrganizationAdmin", me.Roles);
        Assert.Contains("users.manage", me.Permissions);
        Assert.DoesNotContain(me.Permissions, p => p.StartsWith("platform.", StringComparison.Ordinal));
        Assert.Null(me.WarehouseIds); // Admins see every warehouse.
    }

    [Fact]
    public async Task Wrong_password_is_rejected_without_revealing_account_existence()
    {
        var tenant = await TestTenant.CreateAsync(fixture);
        using var anonymous = fixture.CreateClient();

        var wrongPassword = await anonymous.PostAsync<AuthResponse>("/api/auth/login", new LoginRequest(tenant.AdminEmail, "Wrong-Password-1", null));
        var unknownUser = await anonymous.PostAsync<AuthResponse>("/api/auth/login", new LoginRequest("nobody@tests.local", "Wrong-Password-1", null));

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.Status);
        Assert.Equal("invalid_credentials", wrongPassword.Code);
        Assert.Equal(unknownUser.Code, wrongPassword.Code);
    }

    [Fact]
    public async Task Account_locks_after_five_failures_and_admin_can_unlock()
    {
        var tenant = await TestTenant.CreateAsync(fixture);
        var (_, staffAuth) = await tenant.CreateMemberAsync("WarehouseStaff", tenant.MainWarehouseId);
        using var anonymous = fixture.CreateClient();
        var email = staffAuth.User.Email;

        for (var i = 0; i < 5; i++)
        {
            await anonymous.PostAsync<AuthResponse>("/api/auth/login", new LoginRequest(email, "Wrong-Password-1", null));
        }

        var locked = await anonymous.PostAsync<AuthResponse>("/api/auth/login", new LoginRequest(email, TestTenant.Password, null));
        Assert.Equal("account_locked", locked.Code);

        (await tenant.Admin.PostAsync<object>($"/api/users/{staffAuth.User.Id}/unlock")).Ok();
        (await anonymous.PostAsync<AuthResponse>("/api/auth/login", new LoginRequest(email, TestTenant.Password, null))).Ok();
    }

    [Fact]
    public async Task Refresh_rotates_and_reuse_of_a_rotated_token_revokes_the_session()
    {
        var tenant = await TestTenant.CreateAsync(fixture);
        using var client = fixture.CreateClient();
        var login = (await client.PostAsync<AuthResponse>("/api/auth/login", new LoginRequest(tenant.AdminEmail, TestTenant.Password, null), BodyTransport)).Ok();

        var rotated = (await client.PostAsync<AuthResponse>("/api/auth/refresh", new RefreshRequest(login.RefreshToken), BodyTransport)).Ok();
        Assert.NotEqual(login.RefreshToken, rotated.RefreshToken);

        // Replaying the old token (grace window is 0 in tests) is treated as theft...
        var replay = await client.PostAsync<AuthResponse>("/api/auth/refresh", new RefreshRequest(login.RefreshToken), BodyTransport);
        Assert.Equal("invalid_refresh_token", replay.Code);

        // ...so the legitimate successor and the session's access token stop working too.
        var successor = await client.PostAsync<AuthResponse>("/api/auth/refresh", new RefreshRequest(rotated.RefreshToken), BodyTransport);
        Assert.Equal(HttpStatusCode.Unauthorized, successor.Status);
        using var withAccess = fixture.CreateClient(rotated.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await withAccess.GetAsync<CurrentUserDto>("/api/auth/me")).Status);
    }

    [Fact]
    public async Task Logout_revokes_the_access_token_immediately()
    {
        var tenant = await TestTenant.CreateAsync(fixture);
        using var anonymous = fixture.CreateClient();
        var login = (await anonymous.PostAsync<AuthResponse>("/api/auth/login", new LoginRequest(tenant.AdminEmail, TestTenant.Password, null))).Ok();
        using var client = fixture.CreateClient(login.AccessToken);

        (await client.GetAsync<CurrentUserDto>("/api/auth/me")).Ok();
        (await client.PostAsync<object>("/api/auth/logout")).Ok();

        var after = await client.GetAsync<CurrentUserDto>("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, after.Status);
        Assert.Equal("session_invalid", after.Code);
    }

    [Fact]
    public async Task Sessions_can_be_listed_and_revoked_individually()
    {
        var tenant = await TestTenant.CreateAsync(fixture);
        using var anonymous = fixture.CreateClient();
        var second = (await anonymous.PostAsync<AuthResponse>("/api/auth/login", new LoginRequest(tenant.AdminEmail, TestTenant.Password, null))).Ok();

        var sessions = (await tenant.Admin.GetAsync<List<SessionDto>>("/api/auth/sessions")).Ok();
        Assert.Equal(2, sessions.Count);
        var other = sessions.Single(s => !s.IsCurrent);
        (await tenant.Admin.DeleteAsync<object>($"/api/auth/sessions/{other.Id}")).Ok();

        using var revoked = fixture.CreateClient(second.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await revoked.GetAsync<CurrentUserDto>("/api/auth/me")).Status);
        (await tenant.Admin.GetAsync<CurrentUserDto>("/api/auth/me")).Ok();
    }

    [Fact]
    public async Task Password_reset_flow_issues_email_and_revokes_existing_sessions()
    {
        var tenant = await TestTenant.CreateAsync(fixture);
        using var anonymous = fixture.CreateClient();

        Assert.Equal(HttpStatusCode.Accepted, (await anonymous.PostAsync<object>("/api/auth/forgot-password", new ForgotPasswordRequest(tenant.AdminEmail))).Status);
        Assert.Equal(HttpStatusCode.Accepted, (await anonymous.PostAsync<object>("/api/auth/forgot-password", new ForgotPasswordRequest("ghost@tests.local"))).Status);
        await fixture.ProcessOutboxUntilAsync(() => Task.FromResult(fixture.Emails.LastTo(tenant.AdminEmail) is not null));

        var email = fixture.Emails.LastTo(tenant.AdminEmail);
        Assert.NotNull(email);
        Assert.Null(fixture.Emails.LastTo("ghost@tests.local"));
        var token = Uri.UnescapeDataString(TestTenant.TokenPattern().Match(email.TextBody!).Groups[1].Value);

        var invalid = await anonymous.PostAsync<object>("/api/auth/reset-password", new ResetPasswordRequest(tenant.AdminEmail, "bogus", "New-Password-123"));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.Status);

        (await anonymous.PostAsync<object>("/api/auth/reset-password", new ResetPasswordRequest(tenant.AdminEmail, token, "New-Password-123"))).Ok();
        Assert.Equal(HttpStatusCode.Unauthorized, (await tenant.Admin.GetAsync<CurrentUserDto>("/api/auth/me")).Status);
        (await anonymous.PostAsync<AuthResponse>("/api/auth/login", new LoginRequest(tenant.AdminEmail, "New-Password-123", null))).Ok();
    }

    [Fact]
    public async Task Refresh_token_cookie_is_http_only_strict_and_scoped_to_auth_path()
    {
        var tenant = await TestTenant.CreateAsync(fixture);
        using var anonymous = fixture.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new LoginRequest(tenant.AdminEmail, TestTenant.Password, null)),
        };
        using var response = await anonymous.Http.SendAsync(request);
        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(AuthController.RefreshCookie, StringComparison.Ordinal));

        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/auth", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"refreshToken\":null", body, StringComparison.Ordinal); // Browsers never see it in JSON.
    }
}
