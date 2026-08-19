using JadeCapital.Admin.Api.Authorization;
using JadeCapital.Admin.Api.Endpoints;
using JadeCapital.Billing.Infrastructure.DependencyInjection;
using JadeCapital.Billing.PublicApi.Endpoints;
using JadeCapital.Admin.Infrastructure.DependencyInjection;
using JadeCapital.Host.Configuration;
using JadeCapital.Identity.Api;
using JadeCapital.Identity.Api.Endpoints;
using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Identity.Application.Features.Auth.Register;
using JadeCapital.Identity.Infrastructure.DependencyInjection;
using JadeCapital.Identity.Infrastructure.Security;
using JadeCapital.Shared.Infrastructure.DependencyInjection;
using JadeCapital.Shared.Infrastructure.Email;
using JadeCapital.Shared.Infrastructure.Storage;
using JadeCapital.Shared.Kernel.Ai;
using JadeCapital.Shared.Kernel.Exceptions;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Api.Endpoints;
using JadeCapital.Trading.Infrastructure.Ai;
using JadeCapital.Trading.Infrastructure.DependencyInjection;
using JadeCapital.Trading.Infrastructure.Realtime;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using Serilog;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// ===== Wave 10 slice 10.2 — Docker Secrets adapter =====
// Mounted secrets (read by docker compose `secrets:` blocks at
// /run/secrets/<name>) become configuration keys under `__Secret:<name>`.
// MUST be added BEFORE Configure<JwtOptions>(...) so the bound JwtOptions
// instance can resolve `JWT__AccessTokenSecret__File` -> secret content -> JwtOptions.AccessTokenSecret.
// Disambiguates against MVC's ApplicationModelConventionExtensions.Add
// which is also in scope via implicit usings.
((IConfigurationBuilder)builder.Configuration).Add(new DockerSecretConfigurationSource());

// ===== Logging =====
builder.Host.UseSerilog((ctx, services, cfg) =>
{
    cfg.ReadFrom.Configuration(ctx.Configuration)
       .ReadFrom.Services(services)
       .Enrich.FromLogContext()
       .Enrich.WithProperty("Application", "JadeCapital.Host")
       .Filter.With(new JadeCapital.Host.PiiLogScrubber())
       .WriteTo.Console();
});

// ===== Options =====
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddOptions<JwtOptions>()
    .Validate(o => !string.IsNullOrWhiteSpace(o.AccessTokenSecret) && o.AccessTokenSecret.Length >= 32,
        "Jwt.AccessTokenSecret must be >= 32 chars.")
    .ValidateOnStart();

// ===== JWT Auth =====
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>, IWebHostEnvironment>((jwtBearerOpts, jwtOptsAccessor, env) =>
    {
        var jwtOpts = jwtOptsAccessor.Value;
        jwtBearerOpts.RequireHttpsMetadata = !env.IsDevelopment();
        jwtBearerOpts.SaveToken = false;
        jwtBearerOpts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOpts.Issuer,
            ValidAudience = jwtOpts.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(
                System.Text.Encoding.UTF8.GetBytes(jwtOpts.AccessTokenSecret)),
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub,
            RoleClaimType = System.Security.Claims.ClaimTypes.Role
        };
    });
builder.Services.AddAuthorization(opts =>
{
    opts.AddRestrictedScopePolicy();
    opts.AddAdminOnly();
});
// Slice 0f — wire the explicit AdminOnly policy handler. The Identity.Api
// skeleton required an authenticated user + Admin role claim; the dedicated
// handler in Admin.Api.Authorization.RequireAdminPolicyHandler makes the
// authorization seam explicit and denies non-Admin / restricted-scope tokens
// BEFORE any handler runs (no subscription lookup or mutation side effect).
builder.Services.AddSingleton<IAuthorizationHandler, RequireAdminPolicyHandler>();

// ===== Identity module =====
builder.Services.AddIdentityInfrastructure(builder.Configuration);
builder.Services.AddMailOptions(builder.Configuration);
// Email transport profile: Mailpit (local dev) by default; override in production
// with MailKitSmtpEmailSender + Mail__* env vars.
builder.Services.AddMailpitSmtpEmailSender();

// Uniform-timing gate used by /api/auth/forgot-password.
builder.Services.AddSingleton<IUniformTimingGate, UniformTimingGate>();

// Slice 2a.1 — HTTP context accessor (consumed by HttpHeaderTimezoneAccessor)
// and the timezone accessor itself. Singleton because both are stateless
// thread-safe helpers.
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<JadeCapital.Trading.Application.Abstractions.IUserTimezoneAccessor,
    JadeCapital.Trading.Api.Timezone.HttpHeaderTimezoneAccessor>();

// ===== Trading module =====
builder.Services.AddTradingInfrastructure(builder.Configuration);

// ===== Slice 5b.1 — AI provider interface + Ollama HTTP client =====
// Bind AIProviderOptions from the "Ollama" config section (or env vars
// Ollama__BaseUrl / Ollama__Model / Ollama__Timeout — the project's standard
// env-var convention). Defaults in the options class cover absent config.
builder.Services.AddOptions<AIProviderOptions>()
    .Bind(builder.Configuration.GetSection("Ollama"));

// IHttpClientFactory-managed HttpClient so DNS refresh + socket pooling are
// owned by the host. The factory delegate stamps BaseAddress + Timeout from
// the resolved options BEFORE the first send. AddHttpClient registers
// IAIProvider as transient; the typed-client lifetime is fine because
// OllamaHttpClient is stateless + cheap to construct.
builder.Services.AddHttpClient<IAIProvider, OllamaHttpClient>((sp, client) =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AIProviderOptions>>().Value;
    client.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/') + "/");
    client.Timeout = opts.Timeout;
});

// ===== Billing module =====
// Slice 0f — Admin write-path repositories + UoW + plan/owner lookups. Slice 0e
// created the EF Core DbContext + configurations + migration; slice 0f wires
// the application abstractions so the Admin API can resolve the handlers.
builder.Services.AddBillingInfrastructure(builder.Configuration);

// ===== Admin module (Wave 9 slice 9b.1) =====
// Slice 9b.1 — Admin query surface (IAuditEventQueryStore +
// ListAuditEventsHandler for GET /api/admin/audit/events). Registers
// the IAuditEventQueryStore implementation + the MediatR handler so the
// endpoint can resolve its dependencies. Without this call, the
// endpoint returns 500 in production because IAuditEventQueryStore is
// not in the DI container.
builder.Services.AddAdminInfrastructure();

// ===== Shared infrastructure (IClock + ValidationBehavior) =====
builder.Services.AddSharedInfrastructure();

// ===== MinIO infrastructure (slice 1d.1) =====
// Provee IAttachmentStorage + bootstrapea el bucket al startup via
// MinioInitializerHostedService. La connection string vive en
// ConnectionStrings__Storage (env var) o .env local.
builder.Services.AddMinioInfrastructure(builder.Configuration);

// ===== MediatR (handlers de Identity.Application + Trading.Application) =====
// ValidationBehavior ya queda registrado como IPipelineBehavior<,> via AddSharedInfrastructure.
builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssemblies(
        typeof(JadeCapital.Identity.Application.Features.Auth.Register.RegisterUserHandler).Assembly,
        typeof(JadeCapital.Trading.Application.Features.Trades.OpenTrade.OpenTradeHandler).Assembly,
        // Slice 0f — Billing admin handlers (list/change-tier/cancel/extend-trial).
        typeof(JadeCapital.Billing.Application.Features.Subscriptions.ListSubscriptionsHandler).Assembly,
        // Wave 6a.1 — Stripe handlers (create-or-get-customer + handle-webhook).
        typeof(JadeCapital.Billing.Application.Stripe.CreateOrGetCustomerHandler).Assembly,
        // Wave-1.3 — Billing public catalog query (GetPublicPlansHandler) so
        // MediatR can resolve ISender.Send(new GetPublicPlansQuery()) from the
        // BillingPublicEndpoints minimal-api delegate.
        typeof(JadeCapital.Billing.PublicApi.Services.GetPublicPlansHandler).Assembly,
        // Wave 9 slice 9b.1 — Admin audit query handler (ListAuditEventsHandler).
        typeof(JadeCapital.Admin.Application.Features.Audit.ListAuditEventsHandler).Assembly));

// ===== SignalR (slice 4c — realtime quote broadcast) =====
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    // 32 KB ceiling — the wire shape (QuoteUpdate) is ~120 bytes; generous headroom.
    options.MaximumReceiveMessageSize = 32 * 1024;
});

// ===== FluentValidation: validators desde la assembly de Identity.Application =====
builder.Services.AddAssemblyValidators(typeof(RegisterUserValidator).Assembly);

// ===== Rate limiting =====
// Politica estricta para /api/auth/login y /register (anti brute-force / spam).
// Limites configurables via RateLimit:AuthPermit / RateLimit:ApiPermit
// (defaults: 10/min IP y 100/min IP respectivamente).
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (ctx, ct) =>
    {
        ctx.HttpContext.Response.ContentType = "application/problem+json";
        await ctx.HttpContext.Response.WriteAsync(
            """{"type":"https://jadecapital/errors/rate_limited","title":"Too many requests","status":429,"code":"rate_limited"}""",
            ct);
    };

    var authPermit = builder.Configuration.GetValue<int?>("RateLimit:AuthPermit") ?? 10;
    var apiPermit = builder.Configuration.GetValue<int?>("RateLimit:ApiPermit") ?? 100;
    // Slice 4b — /api/quotes es consumido por watchlist pages; permitimos 3x
    // el general para evitar 429s cuando el FE refresca cada few seconds.
    var apiQuotesPermit = builder.Configuration.GetValue<int?>("RateLimit:ApiQuotesPermit") ?? 300;

    options.AddPolicy("auth-strict", ctx =>
    {
        // 10 requests por IP por minuto (suficiente para uso legitimo, bloquea fuerza bruta).
        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: $"auth-{ip}",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = authPermit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });

    options.AddPolicy("api-general", ctx =>
    {
        // 100 requests por IP por minuto para el resto.
        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: $"api-{ip}",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = apiPermit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });

    options.AddPolicy("api-quotes", ctx =>
    {
        // 300 requests por IP por minuto para /api/quotes — watchlist pages
        // refrescan agresivo y no queremos que el rate limiter corte UX.
        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: $"api-quotes-{ip}",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = apiQuotesPermit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });

    options.AddPolicy("api-billing", ctx =>
    {
        // Wave 6a.1 — Stripe-backed endpoints (customers, checkout, portal).
        // Per the spec: 10 calls/hour/user. We use a 1-hour window keyed
        // by user id when authenticated, by IP otherwise.
        var partitionKey = ctx.User?.Identity?.IsAuthenticated == true
            ? $"billing-{ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "unknown"}"
            : $"billing-{ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: partitionKey,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue<int?>("RateLimit:BillingPermit") ?? 10,
                Window = TimeSpan.FromHours(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });

    // Slice 0c — recovery throttle: 5 requests / IP / hour (override via RateLimit:RecoveryPermit).
    var recoveryPermit = builder.Configuration.GetValue<int?>("RateLimit:RecoveryPermit") ?? 5;
    options.AddRecoveryThrottle(recoveryPermit);
});

// ===== Health checks =====
var pgConn = builder.Configuration.GetConnectionString("Postgres");
var redisConn = builder.Configuration.GetConnectionString("Redis");

var hcBuilder = builder.Services.AddHealthChecks()
    .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("alive"));

if (!string.IsNullOrEmpty(pgConn))
    hcBuilder.AddNpgSql(pgConn, name: "postgres", tags: new[] { "ready" });
if (!string.IsNullOrEmpty(redisConn))
    hcBuilder.AddRedis(redisConn, name: "redis", tags: new[] { "ready" });

// ===== API metadata =====
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Jade Capital Suite", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "JWT access token",
        Reference = new()
        {
            Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
            Id = "Bearer"
        }
    });
    c.AddSecurityRequirement(new()
    {
        [new() { Reference = new() { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" } }] = Array.Empty<string>()
    });
});

// ===== CORS =====
builder.Services.AddCors(opts =>
{
    var allowed = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? Array.Empty<string>();
    opts.AddDefaultPolicy(p => p.WithOrigins(allowed).AllowAnyHeader().AllowAnyMethod().AllowCredentials());
});

// ===== Forwarded headers (cuando va detras de Nginx) =====
builder.Services.Configure<ForwardedHeadersOptions>(opts =>
{
    opts.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    opts.KnownIPNetworks.Clear();
    opts.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();

// ===== Pipeline =====
app.UseExceptionHandler(eb => eb.Run(async ctx =>
{
    var feature = ctx.Features.Get<IExceptionHandlerFeature>();
    var ex = feature?.Error;

    ProblemDetails problem;
    int status;

    switch (ex)
    {
        case ValidationException vex:
            status = StatusCodes.Status400BadRequest;
            problem = new() { Title = "Validation failed", Status = status, Instance = ctx.Request.Path };
            problem.Extensions["errors"] = vex.Failures
                .GroupBy(f => f.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).ToArray());
            break;
        case NotFoundDomainException nfe:
            status = StatusCodes.Status404NotFound;
            problem = new() { Title = "Not found", Detail = nfe.Error.Message, Status = status, Instance = ctx.Request.Path };
            problem.Extensions["code"] = nfe.Error.Code;
            break;
        case ConflictDomainException ce:
            status = StatusCodes.Status409Conflict;
            problem = new() { Title = "Conflict", Detail = ce.Error.Message, Status = status, Instance = ctx.Request.Path };
            problem.Extensions["code"] = ce.Error.Code;
            break;
        case UnauthorizedDomainException ue:
            status = StatusCodes.Status401Unauthorized;
            problem = new() { Title = "Unauthorized", Detail = ue.Error.Message, Status = status, Instance = ctx.Request.Path };
            problem.Extensions["code"] = ue.Error.Code;
            break;
        case ForbiddenDomainException fe:
            status = StatusCodes.Status403Forbidden;
            problem = new() { Title = "Forbidden", Detail = fe.Error.Message, Status = status, Instance = ctx.Request.Path };
            problem.Extensions["code"] = fe.Error.Code;
            break;
        case DomainException de:
            status = StatusCodes.Status422UnprocessableEntity;
            problem = new() { Title = "Domain rule violated", Detail = de.Error.Message, Status = status, Instance = ctx.Request.Path };
            problem.Extensions["code"] = de.Error.Code;
            break;
        default:
            status = StatusCodes.Status500InternalServerError;
            problem = new() { Title = "Internal server error", Status = status, Instance = ctx.Request.Path };
            break;
    }

    ctx.Response.StatusCode = status;
    await ctx.Response.WriteAsJsonAsync(problem);
}));

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.MapScalarApiReference();
}

app.UseSerilogRequestLogging();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
// Slice 6c.2 — enforce tenant_id JWT claim on authenticated requests.
// Sits AFTER auth so the principal is populated, but BEFORE endpoint
// resolution so a missing/malformed tenant_id short-circuits with 401
// before any handler runs. Public endpoints (anonymous) pass through.
app.UseMiddleware<JadeCapital.Identity.Infrastructure.MultiTenancy.TenantContextMiddleware>();

// ===== Health =====
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false  // Solo "estoy vivo", no verifico deps
}).AllowAnonymous();

app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
}).AllowAnonymous();

// ===== Modules =====
app.MapIdentityApi();
app.MapAccountEndpoints();
app.MapInstrumentEndpoints();
app.MapTradeEndpoints();
// Slice 1f — server-side trading metrics (replaces analytics.page.ts mocks).
app.MapTraderMetricsEndpoints();
// Slice 1b — read-only position-size calculator (uses IIdentityUserRiskProfileReader).
app.MapPositionSizeEndpoints();
// Slice 1d.1 — post-trade review + MinIO attachment endpoints.
app.MapTradeReviewEndpoints();
// Slice 2a.1 — daily journal endpoints (GET today, GET range, POST upsert, DELETE).
app.MapJournalEndpoints();
// Slice 2b.1 — behavioral analytics (5 detection rules + emotionality buckets).
app.MapBehavioralEndpoints();
// Slice 2c — per-trade MFE/MAE approximation + user-aggregate histograms.
app.MapTradeMfeMaeEndpoints();
// Slice 2d — rule-based coaching prompts (5 rules registered; aggregates
// over trades + journals + behavioral events in the requested window).
app.MapCoachingPromptsEndpoint();
// Slice 3a — trader strategies (CRUD + analytics) + tag/untag trade.
 app.MapStrategyEndpoints();
// Slice 3b — alerts (list, get-by-id, ack) + BackgroundService evaluation.
app.MapAlertEndpoints();
// Slice 3c — planner sessions (create, update, list-by-week, status change).
app.MapPlannerEndpoints();
// Slice 4a — scanner filters (CRUD + run against instrument universe).
app.MapScannerEndpoints();
// Slice 5a.1 — CSV importer (upload + status).
app.MapImportEndpoints();
// Slice 5b.1 — AI provider health probe. Future AI endpoints (5b.2 coaching,
// 5c.1 risk-advice) extend the same AiEndpoints class; no extra MapXxx call.
app.MapAiEndpoints();
// Slice 4b — market data quotes (single + bulk, cache-backed).
// NOTE: QuoteEndpoints handles the `api-quotes` rate limit internally via
// RequireRateLimiting on the endpoints group (added in QuoteEndpoints.cs).
// The endpoint group uses the higher `api-quotes` policy instead of
// `api-general` because watchlist pages refresh aggressively.
app.MapQuoteEndpoints();
// Slice 4c — SignalR /hubs/quotes endpoint. Auth via [Authorize] on the hub
// class (rejects unauthenticated handshakes with 401 before WebSocket upgrade).
// The broadcast loop runs in QuoteBroadcastService (BackgroundService, 5s tick).
app.MapHub<QuoteHub>("/hubs/quotes");
// Slice 0f — Admin API endpoints (subscriptions only). Deny-by-default via
// the AdminOnly policy + RequireAdminPolicyHandler: no subscription existence,
// owner, plan, or history information leaks to non-Admins.
app.MapAdminSubscriptionEndpoints();
// Wave 9 slice 9b.1 — admin audit query endpoint (GET /api/admin/audit/events).
app.MapAdminAuditEndpoints();
// Wave-1.3 — Public Billing catalog endpoints (AllowAnonymous; pricing page
// must load the plan list before the visitor authenticates).
app.MapBillingPublicEndpoints();
// Wave 6a.1 — Stripe-backed Billing endpoints (Customer create/fetch +
// webhook receiver). 4 endpoints total; 6a.1 ships customers + webhooks,
// 6a.2 adds checkout + portal.
app.MapBillingStripeEndpoints();
// Wave 6b.1 — Self-service billing portal read endpoints (subscription +
// payment-methods + invoices). All require the JWT-derived userId; cross-user
// access returns 404. The portal read DTOs are in Billing.Contracts/Portal.
app.MapBillingPortalEndpoints();

app.Run();

namespace JadeCapital.Host
{
    public partial class Program
    {
    }
}