namespace JadeCapital.Api.IntegrationTests;

public class HealthCheckTests : IClassFixture<JadeApiFactory>
{
    private readonly JadeApiFactory _factory;
    private readonly HttpClient _client;

    public HealthCheckTests(JadeApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task HealthLive_ReturnsHealthy()
    {
        var response = await _client.GetAsync("/health/live");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Healthy");
    }

    [Fact]
    public async Task HealthReady_ReturnsHealthy_WhenDependenciesUp()
    {
        var response = await _client.GetAsync("/health/ready");
        // Postgres y Redis containers estan vivos. Health check pasa.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Swagger_Accessible()
    {
        var response = await _client.GetAsync("/swagger/index.html");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Swagger");
    }
}