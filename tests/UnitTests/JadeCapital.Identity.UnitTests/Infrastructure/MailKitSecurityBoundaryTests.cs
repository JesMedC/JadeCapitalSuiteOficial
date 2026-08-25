using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;

namespace JadeCapital.Identity.UnitTests.Infrastructure;

public class MailKitSecurityBoundaryTests
{
    [Fact]
    public void MailboxAddress_RejectsExactQuotedLocalPartCrlfPayload()
    {
        const string payload = "\"attack\r\nRSET\r\nMAIL FROM:<kc1zs4@poc.send.com>\r\nRCPT TO:<xxx@xxx.xxx.xxx.xxx>\r\nDATA\r\n.\r\nQUIT\r\nhere\"@poc.send.com";

        Action parse = () => _ = new MailboxAddress(string.Empty, payload);

        parse.Should().Throw<ParseException>();
    }

    [Fact]
    public async Task StartTls_DiscardsForgedPreTlsCapabilities()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=localhost",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddMinutes(10));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var exchange = RunStartTlsExchangeAsync(listener, certificate, timeout.Token);

        try
        {
            using var client = new SmtpClient();
            var certificateHash = certificate.GetCertHashString();
            client.ServerCertificateValidationCallback = (_, presented, _, _) =>
                presented?.GetCertHashString() == certificateHash;

            await client.ConnectAsync(IPAddress.Loopback.ToString(), port, SecureSocketOptions.StartTls, timeout.Token);

            client.IsSecure.Should().BeTrue();
            client.AuthenticationMechanisms.Should().BeEquivalentTo(["SCRAM-SHA-256"]);
            await client.DisconnectAsync(false, timeout.Token);

            var commands = await exchange;
            commands.InitialEhlo.Should().StartWith("EHLO ");
            commands.StartTls.Should().Be("STARTTLS\r\n");
            commands.TlsEhlo.Should().StartWith("EHLO ");
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public void RecoveryMessage_PreservesEnvelopeSubjectAndMimeAlternatives()
    {
        var recovery = new RecoveryEmailMessage(
            "ada@example.test",
            "Ada",
            "Temporary-Secret-42",
            new DateTimeOffset(2026, 8, 24, 20, 0, 0, TimeSpan.Zero));
        var sender = new TestableMailKitSmtpEmailSender("no-reply@jadecapital.test");

        using var message = sender.BuildRecovery(recovery);

        message.From.Mailboxes.Should().ContainSingle().Which.Address.Should().Be("no-reply@jadecapital.test");
        message.To.Mailboxes.Should().ContainSingle().Which.Address.Should().Be(recovery.To);
        message.Subject.Should().Be("Recuperación de contraseña — Jade Capital");
        var alternative = message.Body.Should().BeOfType<MultipartAlternative>().Subject;
        alternative.Should().HaveCount(2);
        var text = alternative.OfType<TextPart>().Single(part => part.ContentType.MimeType == "text/plain");
        var html = alternative.OfType<TextPart>().Single(part => part.ContentType.MimeType == "text/html");
        text.Text.Should().Contain(recovery.DisplayName);
        text.Text.Should().Contain(recovery.TemporaryPassword);
        html.Text.Should().Contain(recovery.DisplayName);
        html.Text.Should().Contain(recovery.TemporaryPassword);
    }

    private static async Task<(string InitialEhlo, string StartTls, string TlsEhlo)> RunStartTlsExchangeAsync(
        TcpListener listener,
        X509Certificate2 certificate,
        CancellationToken cancellationToken)
    {
        using var tcp = await listener.AcceptTcpClientAsync(cancellationToken);
        tcp.NoDelay = true;
        await using var network = tcp.GetStream();
        await WriteResponseAsync(network, "220 fake.example ESMTP ready\r\n", cancellationToken);
        var initialEhlo = await ReadLineAsync(network, cancellationToken);
        await WriteResponseAsync(
            network,
            "250-fake.example\r\n250-STARTTLS\r\n250 AUTH SCRAM-SHA-256\r\n",
            cancellationToken);
        var startTls = await ReadLineAsync(network, cancellationToken);

        await WriteResponseAsync(
            network,
            "220 Ready to start TLS\r\n250-forged.example\r\n250 AUTH PLAIN LOGIN\r\n",
            cancellationToken);
        await using var tls = new SslStream(network, leaveInnerStreamOpen: true);
        await tls.AuthenticateAsServerAsync(
            new SslServerAuthenticationOptions { ServerCertificate = certificate },
            cancellationToken);
        var tlsEhlo = await ReadLineAsync(tls, cancellationToken);
        await WriteResponseAsync(
            tls,
            "250-real.example\r\n250 AUTH SCRAM-SHA-256\r\n",
            cancellationToken);

        return (initialEhlo, startTls, tlsEhlo);
    }

    private static async Task<string> ReadLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        var bytes = new List<byte>(128);
        var next = new byte[1];

        while (await stream.ReadAsync(next, cancellationToken) == 1)
        {
            bytes.Add(next[0]);
            if (next[0] == '\n')
                return Encoding.ASCII.GetString(bytes.ToArray());
        }

        throw new EndOfStreamException("SMTP peer closed before completing a command.");
    }

    private static async Task WriteResponseAsync(
        Stream stream,
        string response,
        CancellationToken cancellationToken)
    {
        await stream.WriteAsync(Encoding.ASCII.GetBytes(response), cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private sealed class TestableMailKitSmtpEmailSender : MailKitSmtpEmailSender
    {
        private readonly string _from;

        public TestableMailKitSmtpEmailSender(string from)
            : base(new MailOptions(), NullLogger<MailKitSmtpEmailSender>.Instance)
        {
            _from = from;
        }

        public MimeMessage BuildRecovery(RecoveryEmailMessage message) =>
            BuildRecoveryMime(message, _from);
    }
}
