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

dotnet test                                      # whole suite
dotnet test --project EmailSenderService.Tests --filter-method "*rfc2047*"   # one test (wildcard on the method name)
```

`EmailSenderService.Tests` is xUnit v3 on Microsoft.Testing.Platform; `global.json` opts
`dotnet test` into that runner, which the .NET 10 SDK requires (VSTest mode fails with an MTP
error), and filters use MTP/xUnit syntax (`--filter-method`, `--filter-class`). Tests use `WebApplicationFactory` in the
Development environment with two seams swapped: `TestAuthHandler` authenticates any request
carrying `X-Test-Caller`, and `CapturingSmtpTransport` keeps the built `MimeMessage` and its
wire bytes instead of connecting. Everything between (binding, validation, MIME building, the
size check) is the real code. TestServer does not enforce `RequestSizeLimit`, so the 413 there
comes from the form's `MultipartBodyLengthLimit`.

Local dependencies:

```bash
docker run -d --name mailpit -p 1025:1025 -p 8025:8025 axllent/mailpit
docker run -d -p 8080:8080 -e KC_BOOTSTRAP_ADMIN_USERNAME=admin \
  -e KC_BOOTSTRAP_ADMIN_PASSWORD=admin quay.io/keycloak/keycloak:26.4 start-dev
```

Note: on this machine host port 1025 is occupied by a Windows service, so Docker's proxy accepts
the connection and never speaks SMTP - a 30-second timeout with no error. Publish Mailpit on
another port and set `Smtp__Port` if sends hang. Pick one outside the Windows-reserved ranges
(`netsh interface ipv4 show excludedportrange protocol=tcp`, e.g. 2500-2599 is reserved here);
`-p 39025:1025 -p 39080:8025` works. Windows curl mangles non-ASCII arguments and file paths,
so test Cyrillic names with a UTF-8 `curl -K` config file and `;filename=...`.

## Architecture

Three pieces carry the design; the rest is a standard controller API.

**`Security/KeycloakRoleClaims`** flattens Keycloak's nested `realm_access` / `resource_access`
JSON claims into individual role claims from `OnTokenValidated`. Without it `RequireRole` sees no
roles at all and every authenticated caller gets a 403. `Keycloak:RolesClientId` selects a client
role; leave it empty for a realm role. Authentication is **bearer tokens only** - there is no OIDC
handler, no cookie, and no redirect URI, because every caller is a service holding a
client-credentials token. The identity carried into logs is the `azp` claim (the calling client),
which is why `NameClaimType` is `azp` rather than `preferred_username`.

**`Services/SmtpEmailSender`** builds the `MimeMessage`, measures it, and hands it to
**`Services/SmtpTransport`** (behind `ISmtpTransport`, the seam tests replace). It reads options
through an `IOptionsMonitor<SmtpOptions>` on every send, so a rotated credential takes effect
without a restart as soon as the configuration source behind it reloads. The transport creates a
fresh MailKit `SmtpClient` per message - MailKit's client is not thread-safe, so a shared instance
would have to be locked and would serialise every request onto one connection. Relay failures are
wrapped in `EmailDeliveryException`, which `EmailDeliveryExceptionHandler` renders as **502**, not
500: the caller's request was fine, the upstream was not. When widening that catch list, note
MailKit surfaces a connect timeout as a plain `System.TimeoutException`.

**`POST api/email/send` has two actions**, chosen by `[Consumes]`: JSON (`SendEmailRequest`) and
multipart/form-data (`SendEmailFormRequest`, which *inherits* the JSON DTO so fields and
validation cannot diverge, plus `Attachments`). Both go through one private `SendAsync` in the
controller. `AttachmentValidator` sanitises file names (last path segment, no control characters,
255 chars) and derives the content type from the extension - the client's is ignored. Attachment
parts wrap the upload stream (`MimeContent`, base64) without copying; the controller owns and
disposes those streams after the send, which is why the sender never disposes the message.
Non-ASCII names use RFC 2047 parameter encoding because Outlook does not decode MimeKit's default
RFC 2231. OpenAPI allows one operation per path and verb, so
`OpenApi/ContentTypeVariantsOperationFilter` folds the multipart action into the JSON operation
as a second request body media type.

**`Health/SmtpConfigurationHealthCheck`** backs `/healthz/ready` and deliberately opens no SMTP
connection - probes run every few seconds and a relay would read that as abuse. It answers the
question that actually matters in a cluster: did the full configuration arrive, or is this pod
running on an incomplete overlay? A user name with no password means the credential Secret never
landed, so the pod stays out of the Service rather than accepting mail it cannot send.

## Gotchas

- **Keycloak audience.** Keycloak omits the client from `aud` unless the calling client has an
  audience mapper. Without that mapper, token validation fails until `Keycloak:ValidateAudience`
  is false.
- `SmtpOptions`, `AttachmentOptions` and `KeycloakOptions` use `ValidateOnStart`, so a missing
  `Smtp:Host`, `Smtp:FromAddress`, or `Keycloak:Authority` fails the host at boot rather than at
  first request. `appsettings.json` ships those empty on purpose; Development and the Helm
  ConfigMap fill them.
- **Form binding and `[]`.** `SendEmailRequest.To` defaults to a `List<string>`, not `[]`: the
  form binder copies into an existing collection and silently skips a read-only one, which left
  `To` empty on the multipart path. ModelState keys are case-insensitive and keep the binder's
  casing, so attachment errors are keyed `Attachments` / `Attachments[i]`.
- **Address validation.** MimeKit parses a bare local part such as `postmaster` as a valid
  mailbox, so `EmailAddressListAttribute` additionally requires a non-empty domain - otherwise a
  bad address becomes a 502 from the relay instead of a 400 from the API.
- Helm renders env vars with ASP.NET Core's `__` section separator (`Smtp__Host`). The deployment
  carries a `checksum/config` annotation so a ConfigMap-only change still rolls the pods. The
  credentials come from `smtp.credentialsSecret` as `env` entries, which outrank the same keys
  arriving via `envFrom`; the chart never creates that Secret.
- **Body size - 413 vs 502.** The production relay rejects messages over 30,000,000 bytes, and
  this API is the only place that limit is enforced before it, so the relay's SIZE should never be
  what refuses a message. Too big is the caller's problem and is a **413** ProblemDetails; **502**
  is reserved for a relay that rejected or could not take a message that was within limits. Four
  layers, outermost first:
  1. Ingress body limit: `ingress.proxyBodySize`, defaulting to `requestLimits.maxBodyMegabytes`
     (ingress-nginx's own default is 1m).
  2. Request body: `RequestLimits:MaxBodyMegabytes` (default **30**) caps the JSON action - text
     only grows when encoded, so a bigger body could never be sent. The multipart action has its
     own `[RequestSizeLimit]` / `MultipartBodyLengthLimit` of `AttachmentOptions.MultipartRequestBytes`
     (25 MiB, a constant because attributes need one). Kestrel's overflow
     (`BadHttpRequestException` 413) goes through `PayloadTooLargeExceptionHandler`;
     `MultipartFormReadFilter` reads the form before binding so a form-limit `InvalidDataException`
     becomes 413 and malformed multipart 400, not 500.
  3. Attachments (400): `Attachments:MaxCount` (10), `MaxFileBytes` (20 MiB), `MaxTotalBytes`
     (20 MiB, must stay below the 25 MiB request cap), `AllowedExtensions` (null = the built-in
     list; a configured list replaces it). Empty files and unusable names are rejected too.
  4. Built message (413): `Smtp:MaxMessageBytes` (default 30,000,000). The sender runs
     `Prepare(SevenBit)` and writes the whole message with CRLF line endings into a
     `MeasuringStream` before connecting, throwing `MessageTooLargeException` if it is over.
     Base64 with line breaks is ~1.37x, so 20 MiB of files is ~28.7 MB, leaving ~1.3 MB for text.
  Memory: JSON is held in memory ~6x (30 MiB -> ~180 MiB per concurrent send), hence the 512Mi
  limit; multipart files over 64 KB are buffered to `/tmp` and streamed, costing little memory.
- `readOnlyRootFilesystem` is on in the chart, which is why an `emptyDir` is mounted at `/tmp`. It
  also holds buffered uploads (up to 25 MiB per request), so it has a `tmpVolume.sizeLimit`
  (256Mi, ~10 concurrent maximum uploads). Exceeding it evicts the pod rather than failing a
  write - size it for peak concurrency.
- Logs carry the caller, attachment count and total bytes, never file names (personal data).
