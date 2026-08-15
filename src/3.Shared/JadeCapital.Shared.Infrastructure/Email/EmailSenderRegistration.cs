using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace JadeCapital.Shared.Infrastructure.Email;

/// <summary>
/// DI registration helpers for the email transport. The profile is selected
/// per-environment via <see cref="MailTransportProfile"/>:
///   - Production / Staging → <see cref="MailKitSmtpEmailSender"/> (Mail__* config).
///   - Local dev → <see cref="MailpitSmtpEmailSender"/> (localhost:1025 default).
///   - Tests → <see cref="InMemoryCapturingEmailSender"/> (override at fixture level).
/// </summary>
public static class EmailSenderRegistration
{
    public static IServiceCollection AddMailOptions(this IServiceCollection services, Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        services.Configure<MailOptions>(configuration.GetSection(MailOptions.SectionName));
        return services;
    }

    /// <summary>Adds the configured production SMTP sender (MailKit). Safe to call multiple times.</summary>
    public static IServiceCollection AddMailKitSmtpEmailSender(this IServiceCollection services)
    {
        services.TryAddScoped<IEmailSender, MailKitSmtpEmailSender>();
        return services;
    }

    /// <summary>Adds the local-development Mailpit sender (MailKit to localhost:1025).</summary>
    public static IServiceCollection AddMailpitSmtpEmailSender(this IServiceCollection services)
    {
        services.TryAddSingleton<IEmailSender, MailpitSmtpEmailSender>();
        return services;
    }

    /// <summary>Adds the in-memory capturing sender. Used by integration tests.</summary>
    public static IServiceCollection AddInMemoryCapturingEmailSender(this IServiceCollection services)
    {
        services.TryAddSingleton<IEmailSender, InMemoryCapturingEmailSender>();
        return services;
    }
}