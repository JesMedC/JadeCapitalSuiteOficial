using JadeCapital.Billing.Infrastructure.Stripe;
using JadeCapital.Shared.Kernel.Stripe;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stripe;
using StripeErrors = Stripe.StripeError;

namespace JadeCapital.Billing.UnitTests.Stripe;

/// <summary>
/// Tests for <see cref="StripeGateway"/> (Wave 6, slice 6a.1).
///
/// <para>
/// Uses a fake <see cref="IStripeClient"/> (Stripe's own abstraction over the
/// HTTP transport) to simulate Stripe responses without making real network
/// calls. This is Stripe.NET's documented testing pattern.
/// </para>
/// </summary>
public class StripeGatewayTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static StripeOptions DefaultOptions() => new()
    {
        SecretKey = "sk_test_123",
        ApiVersion = "2025-08-13",
        WebhookSecret = "whsec_test"
    };

    private static StripeGateway BuildSut(FakeStripeClient client, StripeOptions? options = null)
    {
        return new StripeGateway(
            client,
            Options.Create(options ?? DefaultOptions()),
            NullLogger<StripeGateway>.Instance);
    }

    // ========================================================================
    // CreateOrGetCustomerAsync
    // ========================================================================

    [Fact]
    public async Task CreateOrGet_New_Customer_Calls_CreateAsync()
    {
        var fake = new FakeStripeClient();
        fake.CustomerListResponse = new StripeList<Customer>
        {
            Data = new List<Customer>()
        };
        fake.CustomerCreateResponse = new Customer
        {
            Id = "cus_new_1",
            Email = "x@y.com",
            Name = "John Doe",
            Created = DateTime.UtcNow
        };

        var sut = BuildSut(fake);
        var result = await sut.CreateOrGetCustomerAsync(UserId, "x@y.com", "John Doe", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.StripeCustomerId.Should().Be("cus_new_1");
        result.Value.Email.Should().Be("x@y.com");
        fake.CreateCalls.Should().Be(1);
    }

    [Fact]
    public async Task CreateOrGet_Existing_Customer_Returns_From_List_NoCreate()
    {
        var fake = new FakeStripeClient();
        fake.CustomerListResponse = new StripeList<Customer>
        {
            Data = new List<Customer>
            {
                new() { Id = "cus_existing", Email = "x@y.com", Name = "Existing", Created = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc) }
            }
        };

        var sut = BuildSut(fake);
        var result = await sut.CreateOrGetCustomerAsync(UserId, "x@y.com", null, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.StripeCustomerId.Should().Be("cus_existing");
        fake.CreateCalls.Should().Be(0, "existing customer must NOT trigger a Create call");
    }

    [Fact]
    public async Task CreateOrGet_StripeException_Returns_Failure_With_Authentication_Code()
    {
        var fake = new FakeStripeClient();
        fake.CustomerListException = new StripeException
        {
            StripeError = new StripeErrors { Code = "invalid_api_key", Message = "Invalid API Key" },
            HttpStatusCode = System.Net.HttpStatusCode.Unauthorized
        };

        var sut = BuildSut(fake);
        var result = await sut.CreateOrGetCustomerAsync(UserId, "x@y.com", null, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("stripe.authentication_error");
    }

    [Fact]
    public async Task CreateOrGet_HttpTransportError_Returns_Unavailable()
    {
        var fake = new FakeStripeClient();
        fake.CustomerListException = new HttpRequestException("DNS failure");

        var sut = BuildSut(fake);
        var result = await sut.CreateOrGetCustomerAsync(UserId, "x@y.com", null, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("stripe.unavailable");
    }

    [Fact]
    public async Task CreateOrGet_OperationCanceledException_Propagates()
    {
        var fake = new FakeStripeClient();
        fake.CustomerListException = new OperationCanceledException();

        var sut = BuildSut(fake);

        // Per the contract: cancellation throws, does NOT return Result.Failure.
        await FluentActions.Awaiting(() =>
                sut.CreateOrGetCustomerAsync(UserId, "x@y.com", null, CancellationToken.None))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    // ========================================================================
    // VerifyWebhookAsync
    // ========================================================================

    [Fact]
    public async Task VerifyWebhook_Missing_Signature_Returns_SignatureMissing()
    {
        var fake = new FakeStripeClient();
        var sut = BuildSut(fake);

        var result = await sut.VerifyWebhookAsync("{\"id\":\"evt_1\"}", "", CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("stripe.signature_missing");
    }

    [Fact]
    public async Task VerifyWebhook_Missing_WebhookSecret_Returns_SignatureMissing()
    {
        var fake = new FakeStripeClient();
        var opts = DefaultOptions();
        opts.WebhookSecret = null;
        var sut = BuildSut(fake, opts);

        var result = await sut.VerifyWebhookAsync("{}", "t=1,v1=abc", CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("stripe.signature_missing");
    }

    [Fact]
    public void MapStripeException_Unauthorized_Returns_Authentication()
    {
        var ex = new StripeException
        {
            StripeError = new StripeErrors { Code = "other", Message = "x" },
            HttpStatusCode = System.Net.HttpStatusCode.Unauthorized
        };

        var mapped = StripeGateway.MapStripeException(ex);
        mapped.Code.Should().Be("stripe.authentication_error");
    }

    [Fact]
    public void MapStripeException_RateLimit_Returns_RateLimit()
    {
        var ex = new StripeException
        {
            StripeError = new StripeErrors { Code = "other", Message = "x" },
            HttpStatusCode = System.Net.HttpStatusCode.TooManyRequests
        };

        var mapped = StripeGateway.MapStripeException(ex);
        mapped.Code.Should().Be("stripe.rate_limit_error");
    }
}

/// <summary>
/// Fake <see cref="IStripeClient"/> — minimal implementation for testing.
/// Returns hardcoded responses for customer list/create endpoints and lets
/// the test throw on demand.
/// </summary>
internal sealed class FakeStripeClient : IStripeClient
{
    public StripeList<Customer>? CustomerListResponse { get; set; }
    public Customer? CustomerCreateResponse { get; set; }
    public Exception? CustomerListException { get; set; }
    public int CreateCalls { get; private set; }

    public string ApiBase => "https://api.stripe.com";
    public string ApiKey => "sk_test_fake";
    public string? ClientId => null;
    public string ConnectBase => "https://connect.stripe.com";
    public string FilesBase => "https://files.stripe.com";
    public string MeterEventsBase => "https://meter-events.stripe.com";

    Task<T> IStripeClient.RequestAsync<T>(
        HttpMethod method,
        string path,
        BaseOptions options,
        RequestOptions requestOptions,
        CancellationToken cancellationToken)
    {
        // Customers list: GET /v1/customers?email=...
        if (method == HttpMethod.Get && path.Contains("/customers") && !path.Contains("/cus_"))
        {
            if (CustomerListException is not null) throw CustomerListException;
            return Task.FromResult((T)(object)CustomerListResponse!);
        }

        // Customer create: POST /v1/customers
        if (method == HttpMethod.Post && path.Contains("/customers") && !path.Contains("/cus_"))
        {
            CreateCalls++;
            return Task.FromResult((T)(object)CustomerCreateResponse!);
        }

        throw new NotImplementedException($"FakeStripeClient: unhandled {method} {path}");
    }

    Task<Stream> IStripeClient.RequestStreamingAsync(
        HttpMethod method,
        string path,
        BaseOptions options,
        RequestOptions requestOptions,
        CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}
