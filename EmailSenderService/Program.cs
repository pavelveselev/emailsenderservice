using System.Security.Claims;
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

if (!builder.Environment.IsDevelopment())
{
    // One JSON object per line is what cluster log collectors expect.
    builder.Logging.AddJsonConsole();
}

var keycloakOptions = builder.Configuration.GetSection(KeycloakOptions.SectionName).Get<KeycloakOptions>()
                      ?? new KeycloakOptions();

// Smtp:UserName and Smtp:Password are expected to arrive as environment variables projected
// from a Kubernetes Secret, so they never sit in appsettings or the ConfigMap.
builder.Services.AddOptions<SmtpOptions>()
    .Bind(builder.Configuration.GetSection(SmtpOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<KeycloakOptions>()
    .Bind(builder.Configuration.GetSection(KeycloakOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();

// Behind an ingress the original scheme and client address arrive in X-Forwarded-*; without
// this every request is logged as plain http from the ingress controller's own IP.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// Bearer tokens only. Callers are other services holding a Keycloak client-credentials token;
// there is no interactive sign-in, so no OIDC handler, cookies, or redirect URIs are involved.
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
            // A service account token names the calling client in `azp`; that, not a person's
            // username, is the identity worth carrying into the logs.
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

    var xmlPath = Path.Combine(AppContext.BaseDirectory, $"{typeof(Program).Assembly.GetName().Name}.xml");
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

// Liveness answers "is the process up"; readiness additionally requires usable SMTP configuration.
app.MapHealthChecks("/healthz/live", new HealthCheckOptions { Predicate = _ => false })
    .AllowAnonymous();
app.MapHealthChecks("/healthz/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") })
    .AllowAnonymous();

app.Run();
