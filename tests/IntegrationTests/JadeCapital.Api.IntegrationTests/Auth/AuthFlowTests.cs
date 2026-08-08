namespace JadeCapital.Api.IntegrationTests.Auth;

public record RegisterRequest(string Email, string DisplayName, string Password);
public record LoginRequest(string Email, string Password);
public record RefreshRequest(string RefreshToken);

public record TokenResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    Guid UserId,
    string Email,
    string DisplayName,
    string Role,
    string Tier);

public class AuthFlowTests : IClassFixture<JadeApiFactory>
{
    private readonly HttpClient _client;

    public AuthFlowTests(JadeApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Register_NewUser_Returns201WithTokens()
    {
        var uniqueEmail = "user" + Guid.NewGuid().ToString("N") + "@" + "test.com";
        var req = new RegisterRequest(uniqueEmail, "Test User", "Passw0rd!Str0ng");

        var response = await _client.PostAsJsonAsync("/api/auth/register", req);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<TokenResponse>();
        body.Should().NotBeNull();
        body!.Email.Should().Be(uniqueEmail);
        body.AccessToken.Should().NotBeNullOrEmpty();
        body.RefreshToken.Should().NotBeNullOrEmpty();
        body.Role.Should().Be("Trader");
    }

    [Fact]
    public async Task Register_DuplicateEmail_Returns409()
    {
        var uniqueEmail = "dup" + Guid.NewGuid().ToString("N") + "@" + "test.com";
        var req = new RegisterRequest(uniqueEmail, "Dup", "Passw0rd!Str0ng");

        var first = await _client.PostAsJsonAsync("/api/auth/register", req);
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await _client.PostAsJsonAsync("/api/auth/register", req);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Register_WeakPassword_Returns400()
    {
        var req = new RegisterRequest("weak" + Guid.NewGuid().ToString("N") + "@" + "test.com", "User", "weak");

        var response = await _client.PostAsJsonAsync("/api/auth/register", req);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Login_AfterRegister_ReturnsTokens()
    {
        var email = "login" + Guid.NewGuid().ToString("N") + "@" + "test.com";
        await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, "User", "Passw0rd!Str0ng"));

        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "Passw0rd!Str0ng"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TokenResponse>();
        body.Should().NotBeNull();
        body!.Email.Should().Be(email);
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401()
    {
        var email = "wrong" + Guid.NewGuid().ToString("N") + "@" + "test.com";
        await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, "User", "Passw0rd!Str0ng"));

        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "WRONG_PASSWORD!"));
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_ValidToken_ReturnsNewTokens()
    {
        var email = "refresh" + Guid.NewGuid().ToString("N") + "@" + "test.com";
        var regResp = await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, "User", "Passw0rd!Str0ng"));
        var regBody = await regResp.Content.ReadFromJsonAsync<TokenResponse>();

        var response = await _client.PostAsJsonAsync("/api/auth/refresh",
            new RefreshRequest(regBody!.RefreshToken));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TokenResponse>();
        body!.RefreshToken.Should().NotBe(regBody.RefreshToken); // rotacion
    }

    [Fact]
    public async Task Refresh_ReuseRevokedToken_Returns401AndRevokesAll()
    {
        var email = "reuse" + Guid.NewGuid().ToString("N") + "@" + "test.com";
        var regResp = await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, "User", "Passw0rd!Str0ng"));
        var regBody = await regResp.Content.ReadFromJsonAsync<TokenResponse>();

        // 1. Rotar
        var refreshResp = await _client.PostAsJsonAsync("/api/auth/refresh",
            new RefreshRequest(regBody!.RefreshToken));
        var newBody = await refreshResp.Content.ReadFromJsonAsync<TokenResponse>();

        // 2. Intentar reusar el viejo (revocado)
        var reuseResp = await _client.PostAsJsonAsync("/api/auth/refresh",
            new RefreshRequest(regBody.RefreshToken));
        reuseResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // 3. El nuevo token TAMPOCO debe servir (todos revocados)
        var newReuseResp = await _client.PostAsJsonAsync("/api/auth/refresh",
            new RefreshRequest(newBody!.RefreshToken));
        newReuseResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RateLimit_Login_BlocksAfter10Attempts()
    {
        // Disparar 15 requests de login (algunos seran 401 invalidos).
        // Despues del 10mo, debe retornar 429.
        var tasks = Enumerable.Range(0, 15)
            .Select(_ => _client.PostAsJsonAsync("/api/auth/login",
                new LoginRequest("attacker" + "@" + "test.com", "wrongpass")))
            .ToArray();

        var responses = await Task.WhenAll(tasks);
        var rateLimited = responses.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests);
        rateLimited.Should().BeGreaterThan(0, "rate limiter should reject some requests after the 10th");
    }
}