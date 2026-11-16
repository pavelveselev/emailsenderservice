using System.Security.Claims;
using EmailSenderService.Health;
using EmailSenderService.OpenApi;
using EmailSenderService.Options;
using EmailSenderService.Security;
using EmailSenderService.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

if (!builder.Environment.IsDevelopment())
{
    // Сборщики логов в кластере ожидают один JSON-объект на строку.
    builder.Logging.AddJsonConsole();
}

// Ограничение для JSON-действия, в котором текст письма приходит прямо в теле запроса. Релей
// принимает не больше Smtp:MaxMessageBytes (30 МБ), а кодирование текст только увеличивает,
// поэтому JSON большего размера отправить всё равно не получится - приняв его, мы лишь
// буферизовали бы несколько копий обречённого запроса. У multipart-действия своё, более
// низкое ограничение (AttachmentOptions.MultipartRequestBytes).
var maxBodyMegabytes = builder.Configuration.GetValue("RequestLimits:MaxBodyMegabytes", 30L);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = maxBodyMegabytes * 1024 * 1024);

var keycloakOptions = builder.Configuration.GetSection(KeycloakOptions.SectionName).Get<KeycloakOptions>()
                      ?? new KeycloakOptions();

// Smtp:UserName и Smtp:Password должны приходить как переменные окружения из Kubernetes
// Secret, поэтому их никогда нет ни в appsettings, ни в ConfigMap.
builder.Services.AddOptions<SmtpOptions>()
    .Bind(builder.Configuration.GetSection(SmtpOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<KeycloakOptions>()
    .Bind(builder.Configuration.GetSection(KeycloakOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<AttachmentOptions>()
    .Bind(builder.Configuration.GetSection(AttachmentOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(options => options.MaxFileBytes <= options.MaxTotalBytes,
        "Attachments:MaxFileBytes must not exceed Attachments:MaxTotalBytes.")
    .Validate(options => options.MaxTotalBytes < AttachmentOptions.MultipartRequestBytes,
        $"Attachments:MaxTotalBytes must stay below the multipart request cap of {AttachmentOptions.MultipartRequestBytes} bytes.")
    .ValidateOnStart();

builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddSingleton<ISmtpTransport, SmtpTransport>();
builder.Services.AddSingleton<AttachmentValidator>();

// За ingress исходная схема и адрес клиента приходят в X-Forwarded-*; без этой настройки каждый
// запрос попадает в лог как обычный http с IP самого ingress-контроллера.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// Только bearer-токены. Вызывающие стороны - другие сервисы с токеном Keycloak, полученным
// по client credentials; интерактивного входа нет, поэтому нет ни OIDC-обработчика, ни cookie,
// ни redirect URI.
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = keycloakOptions.Authority;
        options.Audience = keycloakOptions.Audience;
        options.RequireHttpsMetadata = keycloakOptions.RequireHttpsMetadata;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = keycloakOptions.Authority,
            ValidateAudience = keycloakOptions.ValidateAudience,
            ValidAudience = keycloakOptions.Audience,
            ValidateLifetime = true,
            // Токен сервисного аккаунта называет вызывающий клиент в `azp`; именно его, а не имя
            // пользователя, имеет смысл писать в логи как идентификатор вызывающего.
            NameClaimType = "azp",
            RoleClaimType = ClaimTypes.Role
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                if (context.Principal is not null)
                {
                    KeycloakRoleClaims.Map(context.Principal, keycloakOptions.RolesClientId);
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AuthorizationPolicies.SendEmail, policy => policy
        .RequireAuthenticatedUser()
        .RequireRole(keycloakOptions.RequiredRole));

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<EmailDeliveryExceptionHandler>();
builder.Services.AddExceptionHandler<PayloadTooLargeExceptionHandler>();

builder.Services.AddHealthChecks()
    .AddCheck<SmtpConfigurationHealthCheck>("smtp-configuration", tags: ["ready"]);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Email Sender Service",
        Version = "v1",
        Description = "Sends mail through a central relay. SMTP credentials are held by this "
                      + "service alone; callers authenticate with a Keycloak service account token."
    });

    options.AddSecurityDefinition("bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Keycloak access token from the client credentials grant: POST "
                      + "{authority}/protocol/openid-connect/token with grant_type=client_credentials."
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("bearer", document)] = []
    });

    // У POST api/email/send есть JSON- и multipart-действие; документируем их как одну операцию
    // с двумя media type тела запроса.
    options.ResolveConflictingActions(ContentTypeVariantsOperationFilter.Resolve);
    options.OperationFilter<ContentTypeVariantsOperationFilter>();

    var xmlPath =Path.Combine(AppContext.BaseDirectory, $"{typeof(Program).Assembly.GetName().Name}.xml");
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();

if (app.Configuration.GetValue("Swagger:Enabled", true))
{
    app.UseSwagger();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "Email Sender Service v1"));
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Liveness отвечает на вопрос «жив ли процесс»; readiness дополнительно требует пригодной SMTP-конфигурации.
app.MapHealthChecks("/healthz/live", new HealthCheckOptions { Predicate = _ => false })
    .AllowAnonymous();
app.MapHealthChecks("/healthz/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") })
    .AllowAnonymous();

app.Run();
