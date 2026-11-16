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
/// Запускает настоящий конвейер (маршрутизация, привязка модели, валидация, сборка письма и
/// проверка размера) с двумя заменёнными швами: аутентификация принимает тестовый заголовок
/// вместо токена Keycloak, а SMTP-транспорт перехватывает готовое письмо вместо подключения.
/// </summary>
public sealed class EmailApiFactory : WebApplicationFactory<Program>
{
    public CapturingSmtpTransport Transport { get; } = new();

    /// <summary>Клиент с аутентифицированным вызывающим, у которого есть роль отправки.</summary>
    public HttpClient CreateAuthorizedClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.CallerHeader, "test-caller");
        return client;
    }

    /// <summary>Вариант фабрики с изменёнными лимитами; ничего не разделяет с текущей.</summary>
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
        // Development заполняет опции, которых требует ValidateOnStart (Smtp:Host, Keycloak:Authority...).
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
/// Аутентифицирует запрос с заголовком <see cref="CallerHeader"/> как указанного в нём клиента
/// с ролью, которой требует политика отправки; всё остальное анонимно и получает обычный 401.
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
/// Заменяет релей. Помимо письма сохраняет его байты в сетевом виде, сериализованные во время
/// вызова, пока потоки загрузки запроса ещё открыты, - это доказывает, что их можно было
/// прочитать после того, как измерение размера уже прочитало их один раз.
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
