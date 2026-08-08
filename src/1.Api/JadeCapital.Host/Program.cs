using JadeCapital.Identity.Api.Endpoints;
using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Identity.Application.Features.Auth.Register;
using JadeCapital.Identity.Infrastructure.DependencyInjection;
using JadeCapital.Identity.Infrastructure.Security;
using JadeCapital.Shared.Infrastructure.DependencyInjection;
using JadeCapital.Shared.Kernel.Exceptions;
using JadeCapital.Shared.Kernel.Results;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using Serilog;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// ===== Logging =====
builder.Host.UseSerilog((ctx, services, cfg) =>
{
    cfg.ReadFrom.Configuration(ctx.Configuration)
       .ReadFrom.Services(services)
       .Enrich.FromLogContext()
       .Enrich.WithProperty("Application", "JadeCapital.Host")
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
builder.Services.AddAuthorization();

// ===== Identity module =====
builder.Services.AddIdentityInfrastructure(builder.Configuration);

// ===== Shared infrastructure (IClock + ValidationBehavior) =====
builder.Services.AddSharedInfrastructure();

// ===== MediatR (handlers de Identity.Application) =====
// ValidationBehavior ya queda registrado como IPipelineBehavior<,> via AddSharedInfrastructure.
builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssemblies(
        typeof(JadeCapital.Identity.Application.Features.Auth.Register.RegisterUserHandler).Assembly));

// ===== FluentValidation: validators desde la assembly de Identity.Application =====
builder.Services.AddAssemblyValidators(typeof(RegisterUserValidator).Assembly);

// ===== Rate limiting =====
// Politica estricta para /api/auth/login y /register (anti brute-force / spam).
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

    options.AddPolicy("auth-strict", ctx =>
    {
        // 10 requests por IP por minuto (suficiente para uso legitimo, bloquea fuerza bruta).
        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: $"auth-{ip}",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
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
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });
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
app.MapAuthEndpoints();

app.Run();

namespace JadeCapital.Host
{
    public partial class Program
    {
    }
}