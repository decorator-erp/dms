using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Identity.Contracts.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// PF-D2, through the whole HTTP pipeline: the three auth rate-limit policies (<c>AuthLogin</c> on /login,
/// <c>AuthRefresh</c> on /refresh and /revoke, <c>AuthSession</c> on /change-password and /logout),
/// <c>Retry-After</c> on a 429 and its CORS exposure, and forwarded headers from trusted proxies only.
///
/// <para>Each test builds a derived host on the shared database and sets <b>every</b> limit it relies on.
/// <see cref="ApiFixture"/> raises all three far out of reach, and a derived host inherits that unless it
/// says otherwise, so "the default" would quietly be 100000. Windows are an hour, so no test straddles
/// two.</para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AuthRateLimitTests(ApiFixture fixture)
{
    private const string WrongPassword = "Wr0ng!Password";
    private const string Origin = "https://app.example.test";

    [Fact]
    public async Task Login_TheEleventhFromOneIp_Is429_WithRetryAfter()
    {
        await using WebApplicationFactory<Program> host = Limited(login: 10);
        HttpClient client = host.CreateClient();
        var body = new LoginRequest($"nobody-{Guid.NewGuid():N}@example.com", WrongPassword);

        List<HttpStatusCode> statuses = [];
        HttpResponseMessage last = null!;
        for (int i = 0; i < 11; i++)
        {
            last = await client.PostAsJsonAsync("/api/auth/login", body);
            statuses.Add(last.StatusCode);
        }

        Assert.Equal([.. Enumerable.Repeat(HttpStatusCode.Unauthorized, 10), HttpStatusCode.TooManyRequests], statuses);
        AssertRetryAfter(last);
    }

    /// <summary>The R9 shape: one login, then fifty refreshes chained on the rotated tokens. Under the old
    /// shared policy the eleventh refresh was a 429 (login's ten a minute); refresh is its own budget now.</summary>
    [Fact]
    public async Task FiftyChainedRefreshes_AreNotThrottledByTheLoginLimit()
    {
        TestSession user = await TestUsers.CreateUserWithPermissionsAsync(fixture);
        await using WebApplicationFactory<Program> host = Limited(login: 10, refresh: 300);
        HttpClient client = host.CreateClient();

        HttpResponseMessage login = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(user.Email, IdentityTestSeeding.Password));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        AuthResponse current = (await login.Content.ReadFromJsonAsync<AuthResponse>())!;

        List<HttpStatusCode> statuses = [];
        for (int i = 0; i < 50; i++)
        {
            HttpResponseMessage refresh = await client.PostAsJsonAsync(
                "/api/auth/refresh", new RefreshTokenRequest(current.Token, current.RefreshToken));
            statuses.Add(refresh.StatusCode);
            if (refresh.StatusCode != HttpStatusCode.OK)
            {
                break;
            }

            current = (await refresh.Content.ReadFromJsonAsync<AuthResponse>())!;
        }

        Assert.DoesNotContain(HttpStatusCode.TooManyRequests, statuses);
        Assert.Equal(Enumerable.Repeat(HttpStatusCode.OK, 50), statuses);
    }

    [Fact]
    public async Task Refresh_BeyondItsOwnLimit_Is429_WithRetryAfter_AndLoginKeepsItsBudget()
    {
        await using WebApplicationFactory<Program> host = Limited(login: 10, refresh: 3);
        HttpClient client = host.CreateClient();

        List<HttpStatusCode> statuses = [];
        HttpResponseMessage last = null!;
        for (int i = 0; i < 4; i++)
        {
            last = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest("x", "y"));
            statuses.Add(last.StatusCode);
        }

        HttpResponseMessage login = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest($"nobody-{Guid.NewGuid():N}@example.com", WrongPassword));

        Assert.Equal(
            [HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests],
            statuses);
        AssertRetryAfter(last);
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task ACrossOrigin429_ExposesRetryAfter()
    {
        await using WebApplicationFactory<Program> host = Limited(
            login: 1, extra: builder => builder.UseSetting("Cors:AllowedOrigins:0", Origin));
        HttpClient client = host.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", Origin);
        var body = new LoginRequest($"nobody-{Guid.NewGuid():N}@example.com", WrongPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", body)).StatusCode);
        HttpResponseMessage limited = await client.PostAsJsonAsync("/api/auth/login", body);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        AssertRetryAfter(limited);
        Assert.Equal([Origin], limited.Headers.GetValues("Access-Control-Allow-Origin"));
        Assert.Contains(
            "Retry-After",
            limited.Headers.GetValues("Access-Control-Expose-Headers").SelectMany(v => v.Split(',', StringSplitOptions.TrimEntries)),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>AuthSession is keyed by the signed-in user, so two colleagues behind one IP do not share
    /// a bucket. This also proves the pipeline order: with the rate limiter before authentication, the
    /// partitioner sees no user, falls back to the IP, and B's first call is the 429.</summary>
    [Fact]
    public async Task ChangePassword_IsLimitedPerUser_NotPerIp()
    {
        TestSession a = await TestUsers.CreateUserWithPermissionsAsync(fixture);
        TestSession b = await TestUsers.CreateUserWithPermissionsAsync(fixture);
        await using WebApplicationFactory<Program> host = Limited(session: 1);

        // A wrong current password leaves the security stamp alone, so A's token stays valid for A's
        // second call.
        HttpResponseMessage aFirst = await ChangePasswordAsync(host, a, WrongPassword);
        HttpResponseMessage bFirst = await ChangePasswordAsync(host, b, IdentityTestSeeding.Password);
        HttpResponseMessage aSecond = await ChangePasswordAsync(host, a, WrongPassword);

        Assert.NotEqual(HttpStatusCode.TooManyRequests, aFirst.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, bFirst.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, aSecond.StatusCode);
        AssertRetryAfter(aSecond);
    }

    [Fact]
    public async Task FromATrustedProxy_EachForwardedClientGetsItsOwnLoginBucket()
    {
        await using WebApplicationFactory<Program> host = Limited(login: 1, extra: builder =>
        {
            builder.UseSetting("ForwardedHeaders:KnownProxies:0", ProxyAddress.ToString());
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter>(new RemoteIpStartupFilter()));
        });

        HttpStatusCode first = await LoginForwardedForAsync(host, "203.0.113.1");
        HttpStatusCode second = await LoginForwardedForAsync(host, "203.0.113.2");
        HttpStatusCode firstAgain = await LoginForwardedForAsync(host, "203.0.113.1");

        Assert.Equal(HttpStatusCode.Unauthorized, first);
        Assert.Equal(HttpStatusCode.Unauthorized, second);
        Assert.Equal(HttpStatusCode.TooManyRequests, firstAgain);
    }

    [Fact]
    public async Task FromAnUntrustedSource_XForwardedForIsIgnored()
    {
        await using WebApplicationFactory<Program> host = Limited(login: 1, extra: builder =>
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter>(new RemoteIpStartupFilter())));

        HttpStatusCode first = await LoginForwardedForAsync(host, "203.0.113.1");
        HttpStatusCode second = await LoginForwardedForAsync(host, "203.0.113.2");

        Assert.Equal(HttpStatusCode.Unauthorized, first);
        Assert.Equal(HttpStatusCode.TooManyRequests, second);
    }

    // --- helpers ---

    private static readonly IPAddress ProxyAddress = IPAddress.Parse("10.0.0.1");

    /// <summary>A derived host with all three auth limits set explicitly and hour-long windows.</summary>
    private WebApplicationFactory<Program> Limited(
        int login = 100000, int refresh = 100000, int session = 100000, Action<IWebHostBuilder>? extra = null) =>
        fixture.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("RateLimiting:AuthLogin:PermitLimit", login.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.UseSetting("RateLimiting:AuthLogin:WindowSeconds", "3600");
            builder.UseSetting("RateLimiting:AuthRefresh:PermitLimit", refresh.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.UseSetting("RateLimiting:AuthRefresh:WindowSeconds", "3600");
            builder.UseSetting("RateLimiting:AuthSession:PermitLimit", session.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.UseSetting("RateLimiting:AuthSession:WindowSeconds", "3600");
            extra?.Invoke(builder);
        });

    private static Task<HttpResponseMessage> ChangePasswordAsync(
        WebApplicationFactory<Program> host, TestSession user, string currentPassword)
    {
        HttpClient client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.AccessToken);
        return client.PostAsJsonAsync(
            "/api/auth/change-password", new { CurrentPassword = currentPassword, NewPassword = "N3w!Passw0rd" });
    }

    private static async Task<HttpStatusCode> LoginForwardedForAsync(WebApplicationFactory<Program> host, string clientIp)
    {
        HttpClient client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", clientIp);
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest($"nobody-{Guid.NewGuid():N}@example.com", WrongPassword));
        return response.StatusCode;
    }

    private static void AssertRetryAfter(HttpResponseMessage response)
    {
        Assert.True(response.Headers.TryGetValues("Retry-After", out IEnumerable<string>? values), "A 429 without Retry-After.");
        int seconds = int.Parse(values!.Single(), System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(seconds, 1, 3600);
    }

    /// <summary>TestServer has no remote address (<c>RemoteIpAddress</c> is null), so these tests give every
    /// request one, as a proxy's connection would have, before UseForwardedHeaders runs.</summary>
    private sealed class RemoteIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((HttpContext context, RequestDelegate nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = ProxyAddress;
                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
