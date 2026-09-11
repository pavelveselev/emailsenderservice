using System.Security.Claims;
using EmailSenderService.Configuration;
using EmailSenderService.Health;
using EmailSenderService.Options;
using EmailSenderService.Security;
using EmailSenderService.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// Vault is layered on top of appsettings/environment variables, so SMTP credentials never
// have to exist in a ConfigMap, an image, or a developer's checkout.
builder.Configuration.AddVaultSecrets();

if (!builder.Environment.IsDevelopment())
{
    // One JSON object per line is what cluster log collectors expect.
    builder.Logging.AddJsonConsole();
}

var keycloakOptions = builder.Configuration.GetSection(KeycloakOptions.SectionName).Get<KeycloakOptions>()
                      ?? new KeycloakOptions();

builder.Services.AddOptions<SmtpOptions>()
    .Bind(builder.Configuration.GetSection(SmtpOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<KeycloakOptions>()
    .Bind(builder.Configuration.GetSection(KeycloakOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.Configure<VaultOptions>(builder.Configuration.GetSection(VaultOptions.SectionName));

builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();

// Behind an ingress the original scheme arrives in X-Forwarded-Proto; without this the OAuth
// redirect URIs Swagger builds come out as http and Keycloak rejects them.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

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
            NameClaimType = "preferred_username",
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

builder.Services.AddHealthChecks()
    .AddCheck<SmtpConfigurationHealthCheck>("smtp-configuration", tags: ["ready"]);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Email Sender Service",
        Version = "v1",
        Description = "Sends mail through a central relay. SMTP credentials are held in HashiCorp "
                      + "Vault and never leave this service; callers authenticate with Keycloak."
    });

    options.AddSecurityDefinition("keycloak", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.OAuth2,
        Description = "Keycloak authorization code flow with PKCE.",
        Flows = new OpenApiOAuthFlows
        {
            AuthorizationCode = new OpenApiOAuthFlow
            {
                AuthorizationUrl = new Uri(keycloakOptions.AuthorizationUrl, UriKind.RelativeOrAbsolute),
                TokenUrl = new Uri(keycloakOptions.TokenUrl, UriKind.RelativeOrAbsolute),
                Scopes = new Dictionary<string, string>
                {
                    ["openid"] = "OpenID Connect scope",
                    ["profile"] = "User profile"
                }
            }
        }
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("keycloak", document)] = ["openid", "profile"]
    });

    var xmlPath = Path.Combine(AppContext.BaseDirectory, $"{typeof(Program).Assembly.GetName().Name}.xml");
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }
});

var app = builder.Build();

VaultConfigurationDiagnostics.Drain(app.Services.GetRequiredService<ILogger<Program>>());

app.UseForwardedHeaders();
app.UseExceptionHandler();

if (app.Configuration.GetValue("Swagger:Enabled", true))
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Email Sender Service v1");
        options.OAuthClientId(keycloakOptions.SwaggerClientId ?? keycloakOptions.Audience);
        options.OAuthScopes("openid", "profile");
        options.OAuthUsePkce();
    });
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Liveness answers "is the process up"; readiness additionally requires usable SMTP configuration.
app.MapHealthChecks("/healthz/live", new HealthCheckOptions { Predicate = _ => false })
    .AllowAnonymous();
app.MapHealthChecks("/healthz/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") })
    .AllowAnonymous();

app.Run();
