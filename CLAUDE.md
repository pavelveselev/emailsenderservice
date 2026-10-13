# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this service is for

An ASP.NET Core API (.NET 10) that relays email over SMTP. The sending itself is trivial; the
reason the service exists is that **SMTP credentials are held in one place**. Callers are other
services: they authenticate with a Keycloak service account token and never see the relay
password, which reaches the process as an environment variable projected from a Kubernetes
Secret. Two design consequences follow, and changes should not undo them:

- The **From address comes from configuration, never from the request**. Letting callers set it
  would turn this into an open relay for spoofed mail.
- **Credentials are never written to disk or into a ConfigMap.** They enter the process only as
  `Smtp__UserName` / `Smtp__Password` environment variables. Filling the Secret behind them is a
  deployment concern (Vault Agent injector, Vault Secrets Operator, external-secrets) and is
  deliberately outside this codebase - do not add a secrets-manager client to the app.

## Commands

Run from the solution root (the folder holding `EmailSenderService.slnx`):

```bash
dotnet build
dotnet run --project EmailSenderService          # http://localhost:5158
dotnet watch --project EmailSenderService

docker build -f EmailSenderService/Dockerfile -t email-sender:dev .   # build context is the solution root
helm lint deploy/helm/email-sender
helm template email-sender deploy/helm/email-sender
```

There is no test project. When one is added, `dotnet test` runs the suite and
`dotnet test --filter FullyQualifiedName~SomeTest` runs a single test.

Local dependencies:

```bash
docker run -d --name mailpit -p 1025:1025 -p 8025:8025 axllent/mailpit
docker run -d -p 8080:8080 -e KC_BOOTSTRAP_ADMIN_USERNAME=admin \
  -e KC_BOOTSTRAP_ADMIN_PASSWORD=admin quay.io/keycloak/keycloak:26.4 start-dev
```

Note: on this machine host port 1025 is occupied by a Windows service, so Docker's proxy accepts
the connection and never speaks SMTP - a 30-second timeout with no error. Publish Mailpit on
another port and set `Smtp__Port` if sends hang.

## Architecture

Three pieces carry the design; the rest is a standard controller API.

**`Security/KeycloakRoleClaims`** flattens Keycloak's nested `realm_access` / `resource_access`
JSON claims into individual role claims from `OnTokenValidated`. Without it `RequireRole` sees no
roles at all and every authenticated caller gets a 403. `Keycloak:RolesClientId` selects a client
role; leave it empty for a realm role. Authentication is **bearer tokens only** - there is no OIDC
handler, no cookie, and no redirect URI, because every caller is a service holding a
client-credentials token. The identity carried into logs is the `azp` claim (the calling client),
which is why `NameClaimType` is `azp` rather than `preferred_username`.

**`Services/SmtpEmailSender`** builds a `MimeMessage` and creates a fresh MailKit `SmtpClient` per
message - MailKit's client is not thread-safe, so a shared instance would have to be locked and
would serialise every request onto one connection. It reads options through an
`IOptionsMonitor<SmtpOptions>` on every send, so a rotated credential takes effect without a
restart as soon as the configuration source behind it reloads. Relay failures are wrapped in
`EmailDeliveryException`, which `EmailDeliveryExceptionHandler` renders as **502**, not 500: the
caller's request was fine, the upstream was not. When widening that catch list, note MailKit
surfaces a connect timeout as a plain `System.TimeoutException`.

**`Health/SmtpConfigurationHealthCheck`** backs `/healthz/ready` and deliberately opens no SMTP
connection - probes run every few seconds and a relay would read that as abuse. It answers the
question that actually matters in a cluster: did the full configuration arrive, or is this pod
running on an incomplete overlay? A user name with no password means the credential Secret never
landed, so the pod stays out of the Service rather than accepting mail it cannot send.

## Gotchas

- **Keycloak audience.** Keycloak omits the client from `aud` unless the calling client has an
  audience mapper. Without that mapper, token validation fails until `Keycloak:ValidateAudience`
  is false.
- `SmtpOptions` and `KeycloakOptions` use `ValidateOnStart`, so a missing `Smtp:Host`,
  `Smtp:FromAddress`, or `Keycloak:Authority` fails the host at boot rather than at first request.
  `appsettings.json` ships those empty on purpose; Development and the Helm ConfigMap fill them.
- **Address validation.** MimeKit parses a bare local part such as `postmaster` as a valid
  mailbox, so `EmailAddressListAttribute` additionally requires a non-empty domain - otherwise a
  bad address becomes a 502 from the relay instead of a 400 from the API.
- Helm renders env vars with ASP.NET Core's `__` section separator (`Smtp__Host`). The deployment
  carries a `checksum/config` annotation so a ConfigMap-only change still rolls the pods. The
  credentials come from `smtp.credentialsSecret` as `env` entries, which outrank the same keys
  arriving via `envFrom`; the chart never creates that Secret.
- **Body size.** `RequestLimits:MaxBodyMegabytes` (default 100) replaces Kestrel's 30 MB request
  cap. It is only the first of three limits: an ingress in front has its own body limit, and the
  relay's SIZE limit turns an oversized message into a 502. The whole body is held in memory
  several times over, so the pod memory limit in `values.yaml` has to grow with it.
- `readOnlyRootFilesystem` is on in the chart, which is why an `emptyDir` is mounted at `/tmp`.
