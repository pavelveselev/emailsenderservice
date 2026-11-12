using System.Security.Claims;
using System.Text.Encodings.Web;
using EmailSenderService.Options;
using EmailSenderService.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace EmailSenderService.Tests.Infrastructure;

/// <summary>
/// Runs the real pipeline (routing, model binding, validation, message building and the size
/// check) with two seams replaced: authentication accepts a test header instead of a Keycloak
/// token, and the SMTP transport captures the finished message instead of connecting.
/// </summary>
public sealed class EmailApiFactory : WebApplicationFactory<Program>
{
    public CapturingSmtpTransport Transport { get; } = new();

    /// <summary>Client with an authenticated caller holding the send role.</summary>
    public HttpClient CreateAuthorizedClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.CallerHeader, "test-caller");
        return client;
    }

    /// <summary>A factory variant with adjusted limits; it shares nothing with this one.</summary>
    public static EmailApiFactory WithOptions(
        Action<SmtpOptions>? smtp = null,
        Action<AttachmentOptions>? attachments = null)
    {
        var factory = new EmailApiFactory();
        factory.configureOptions = services =>
        {
            if (smtp is not null)
            {
                services.Configure(smtp);
            }

            if (attachments is not null)
            {
                services.Configure(attachments);
            }
        };
        return factory;
    }

    private Action<IServiceCollection>? configureOptions;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development fills the options that ValidateOnStart requires (Smtp:Host, Keycloak:Authority...).
        builder.UseEnvironment("Development");

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<ISmtpTransport>(Transport);

            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultScheme = TestAuthHandler.SchemeName;
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            });

            configureOptions?.Invoke(services);
        });
    }
}

/// <summary>
/// Authenticates a request that carries <see cref="CallerHeader"/> as that client, with the
/// role the send policy requires; anything else is anonymous and gets the usual 401 challenge.
/// </summary>
public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<KeycloakOptions> keycloak)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string CallerHeader = "X-Test-Caller";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(CallerHeader, out var caller))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity(
            [new Claim("azp", caller.ToString()), new Claim(ClaimTypes.Role, keycloak.Value.RequiredRole)],
            SchemeName, nameType: "azp", roleType: ClaimTypes.Role);

        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}

/// <summary>
/// Stands in for the relay. Besides the message it keeps the wire bytes, serialised during the
/// call while the request's upload streams are still open, which proves they were readable
/// after the size measurement had already consumed them once.
/// </summary>
public sealed class CapturingSmtpTransport : ISmtpTransport
{
    private readonly List<(MimeMessage Message, byte[] Wire)> sent = [];

    public IReadOnlyList<(MimeMessage Message, byte[] Wire)> Sent
    {
        get { lock (sent) { return sent.ToList(); } }
    }

    public async Task<string> SendAsync(MimeMessage message, SmtpOptions options, CancellationToken cancellationToken)
    {
        var format = FormatOptions.Default.Clone();
        format.NewLineFormat = NewLineFormat.Dos;

        using var buffer = new MemoryStream();
        await message.WriteToAsync(format, buffer, cancellationToken);

        lock (sent)
        {
            sent.Add((message, buffer.ToArray()));
        }

        return "250 2.0.0 Ok: queued (test)";
    }
}
