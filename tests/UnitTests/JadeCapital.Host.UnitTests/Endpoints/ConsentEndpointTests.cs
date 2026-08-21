using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using JadeCapital.Identity.Api.Endpoints;
using JadeCapital.Identity.Application.Features.Auth.Consent;
using JadeCapital.Shared.Kernel.Results;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace JadeCapital.Host.UnitTests.Endpoints;

public sealed class ConsentEndpointTests : IClassFixture<ConsentEndpointTests.MinimalHost>
{
    private static readonly DateTimeOffset AcceptedAt =
        new(2026, 8, 21, 13, 45, 0, TimeSpan.Zero);
    private readonly MinimalHost _host;

    public ConsentEndpointTests(MinimalHost host) => _host = host;

    [Theory]
    [InlineData("all")]
    [InlineData("essential")]
    public async Task ConsentRoute_PreservesSuccessStatusAndResponse(string choice)
    {
        using var request = AuthorizedRequest(choice);

        using var response = await _host.Client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<ConsentResult>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Be(new ConsentResult(choice, AcceptedAt));
    }

    [Fact]
    public async Task ConsentRoute_PreservesAuthorizationRequirement()
    {
        using var response = await _host.Client.PostAsJsonAsync(
            "/api/auth/consent", new ConsentEndpoint.ConsentRequest("all"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ConsentRoute_PreservesInvalidChoiceStatusAndErrorResponse()
    {
        using var request = AuthorizedRequest("tracking-only");

        using var response = await _host.Client.SendAsync(request);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        problem.Should().NotBeNull();
        problem!.Extensions.TryGetValue("code", out var code).Should().BeTrue();
        code!.ToString().Should().Contain("cookie_consent_choice_invalid");
    }

    private static HttpRequestMessage AuthorizedRequest(string choice)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/consent")
        {
            Content = JsonContent.Create(new ConsentEndpoint.ConsentRequest(choice)),
        };
        request.Headers.Add(TestAuthHandler.UserHeader, MinimalHost.UserId.ToString());
        return request;
    }

    public sealed class MinimalHost : IDisposable
    {
        public static readonly Guid UserId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        private readonly WebApplication _app;

        public MinimalHost()
        {
            var sender = Substitute.For<ISender>();
            sender.Send(Arg.Any<ConsentCommand>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var command = call.Arg<ConsentCommand>();
                    return command.Choice is "all" or "essential"
                        ? Result.Success(new ConsentResult(command.Choice, AcceptedAt))
                        : Result.Failure<ConsentResult>(Error.Validation(
                            "auth.cookie_consent_choice_invalid", "Invalid choice."));
                });

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
            builder.Services.AddSingleton(sender);
            builder.Services.AddAuthentication("Test")
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
            builder.Services.AddAuthorization();

            _app = builder.Build();
            _app.UseAuthentication();
            _app.UseAuthorization();
            _app.MapConsentEndpoint();
            _app.Start();
            Client = _app.GetTestServer().CreateClient();
        }

        public HttpClient Client { get; }

        public void Dispose()
        {
            Client.Dispose();
            _app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    public sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string UserHeader = "X-Test-User";

        public TestAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(UserHeader, out var userId))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
