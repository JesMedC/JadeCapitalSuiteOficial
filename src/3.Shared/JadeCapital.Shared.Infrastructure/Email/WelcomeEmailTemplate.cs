namespace JadeCapital.Shared.Infrastructure.Email;

// ============================================================================
//  WelcomeEmailTemplate — Wave 11 slice 11.4.
//
//  Pure string composition: produces the Spanish / Jade-branded HTML +
//  plain-text body for the post-registration welcome email. NO persistence,
//  NO I/O — the SMTP transport calls these methods and wraps the resulting
//  strings in a MimeMessage.
//
//  <para>
//  <b>Why a static-class template here (shared infrastructure) instead of
//  inside <c>MailKitSmtpEmailSender</c></b>: slice 0c already established the
//  "body never leaves the transport typed-as-a-field" contract for the
//  recovery flow; the template stays at the same layer as the SMTP wrapper
//  so:
//    * it can be referenced by tests without pulling MimeKit into the unit
//      test project;
//    * it documents the canonical copy in a single place (vs. trapped
//      inside an SMTP wrapper);
//    * future MIME composition changes (Markdown, MJML, etc.) stay
//      scoped to one file rather than rippling across every IEmailSender
//      implementation.
//  </para>
//
//  <para>
//  <b>Copy is INTENTIONALLY GENERIC</b>. A production deploy MUST wire
//  the actual Jade-branded copy with sign-off from product + legal. The
//  strings below are placeholder copy sufficient for development + the
//  Mailpit inbox sandbox.
//  </para>
//
//  <para>
//  <b>Failure isolation</b>: callers
//  (<c>RegisterUserHandler</c>) MUST wrap the SMTP send in
//  <c>try { ... } catch { log + continue }</c> so a transient SMTP
//  failure does NOT abort the registration (the user row has already
//  been committed). Idempotency against accidental re-send is enforced
//  via <c>users.welcome_email_sent_at</c> + a 7-day suppression window
//  — see the handler.
/// </para>
// ============================================================================

public static class WelcomeEmailTemplate
{
    /// <summary>
    /// Spanish Jade-branded subject line. Kept short to avoid clipping in
    /// mobile clients; the brand is named once and the rest of the line
    /// states the purpose.
    /// </summary>
    public const string Subject = "Bienvenido a Jade Capital";

    /// <summary>
    /// HTML body for the welcome email. Inline-styled (no external CSS) so
    /// it survives clipping in the major email clients (Outlook/Gmail/Apple).
    /// Placeholder copy — see file-level comment for production sign-off.
    /// </summary>
    public static string RenderHtml(WelcomeEmailMessage m)
    {
        var display = System.Net.WebUtility.HtmlEncode(m.DisplayName);
        return $"""
            <div style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif; max-width:560px; margin:0 auto; padding:24px; color:#0F1B22;">
              <div style="padding:16px 0; border-bottom:1px solid #E7ECF0;">
                <strong style="font-size:18px;">Jade Capital Suite</strong>
              </div>
              <h1 style="margin:24px 0 8px; font-size:24px;">Hola {display},</h1>
              <p style="font-size:16px; line-height:1.55;">
                Gracias por registrarte en <strong>Jade Capital Suite</strong>.
                Empez&aacute; a registrar tus operaciones, controlar saldos
                y analizar tu P&amp;L en menos de 60 segundos.
              </p>
              <p style="font-size:16px; line-height:1.55;">
                Tu prueba gratuita de 14 d&iacute;as ya est&aacute; activa.
                &iquest;Qu&eacute; sigue?
              </p>
              <ul style="font-size:16px; line-height:1.65; padding-left:20px;">
                <li>Registrar&aacute; tu primer trade en la secci&oacute;n <em>Trades</em>.</li>
                <li>Configurar&aacute;s tu perfil de riesgo para activar el patr&oacute;n de alertas.</li>
                <li>Conocer&aacute;s el journal diario y los patrones conductuales.</li>
              </ul>
              <p style="font-size:14px; color:#5A6B75; margin-top:24px;">
                Si no creaste esta cuenta, ignor&aacute; este correo.
              </p>
            </div>
            """;
    }

    /// <summary>
    /// Plain-text body. Same content as the HTML, just text-encoded — keeps
    /// the email usable in clients that disable HTML (or for users who
    /// flipped the toggle).
    /// </summary>
    public static string RenderText(WelcomeEmailMessage m)
    {
        return $"""
            Hola {m.DisplayName},

            Gracias por registrarte en Jade Capital Suite.
            Empezá a registrar tus operaciones, controlar saldos
            y analizar tu P&L en menos de 60 segundos.

            Tu prueba gratuita de 14 días ya está activa. ¿Qué sigue?
              - Registrar tu primer trade en la sección Trades.
              - Configurar tu perfil de riesgo para activar el patrón de alertas.
              - Conocer el journal diario y los patrones conductuales.

            Si no creaste esta cuenta, ignorá este correo.

            --
            Equipo Jade Capital
            """;
    }
}
