# Email Sender Service

A small ASP.NET Core API that sends mail through a corporate SMTP relay.

The point is not the sending - it is that **SMTP credentials live in exactly one place**.
Applications that need to send mail call this service with a Keycloak token instead of each
holding a copy of the relay password. The credentials themselves are read from HashiCorp Vault
at runtime, so they are never in an image, a ConfigMap, or a repository.

```
caller ──(Keycloak JWT)──> EmailSenderService ──(credentials from Vault)──> SMTP relay
```

## API

`POST /api/email/send` - requires a bearer token with the configured role.

```json
{
  "to": ["someone@example.com", "other@example.com"],
  "cc": ["team@example.com"],
  "bcc": ["archive@example.com"],
  "replyTo": "support@example.com",
  "subject": "Weekly report",
  "body": "<h1>Weekly report</h1><p>All systems nominal.</p>",
  "isHtml": true
}
```

`to` is required and must contain at least one routable address; `cc`, `bcc`, `replyTo` and
`isHtml` are optional. With `isHtml: false` (the default) the body is sent as `text/plain`.

The **sender address is fixed by server configuration** and cannot be set by the caller, which
is what stops this service from being an open relay for spoofed mail.

| Status | Meaning |
| --- | --- |
| 200 | Relay accepted the message; body carries the `Message-Id` |
| 400 | Validation failure (bad address, missing subject or body) |
| 401 | Missing, expired, or invalid token |
| 403 | Valid token without the required role |
| 502 | Relay rejected the message or was unreachable |

Other endpoints: `/swagger` (UI with a Keycloak Authorize button), `/healthz/live`,
`/healthz/ready`. All three are anonymous.

## Running locally

```bash
# SMTP sink with a web UI on http://localhost:8025
docker run -d --name mailpit -p 1025:1025 -p 8025:8025 axllent/mailpit

dotnet run --project EmailSenderService
```

`appsettings.Development.json` points at that sink on port 1025, disables Vault, and relaxes
Keycloak to plain HTTP. If port 1025 is taken on your machine, publish Mailpit on another port
and override with `Smtp__Port=2525`.

For a local Keycloak:

```bash
docker run -d -p 8080:8080 -e KC_BOOTSTRAP_ADMIN_USERNAME=admin \
  -e KC_BOOTSTRAP_ADMIN_PASSWORD=admin quay.io/keycloak/keycloak:26.4 start-dev
```

`EmailSenderService.http` has ready-made requests, including fetching a token.

## Configuration

Every setting binds from `appsettings.json`, environment variables (`Smtp__Host` style), or
Vault. Precedence runs left to right: appsettings → environment → **Vault wins**.

| Section | Purpose |
| --- | --- |
| `Smtp` | Host, port, security mode, sender address, timeout. `UserName`/`Password` normally come from Vault |
| `Keycloak` | Authority, audience, required role, Swagger client id |
| `Vault` | Address, auth method, KV mount and path, reload interval |
| `Swagger:Enabled` | Set false to remove the UI and JSON document |

`Smtp:Security` takes MailKit's values: `None`, `Auto`, `SslOnConnect`, `StartTls`,
`StartTlsWhenAvailable`.

### Vault

The KV v2 secret's keys are projected onto the `Smtp` section by name, so a secret holding
`username` and `password` fills `Smtp:UserName` and `Smtp:Password`:

```bash
vault kv put secret/email-sender/smtp username=relay-user password=relay-secret
```

In a cluster the pod authenticates with its own service account token (Vault's Kubernetes auth
method) - no bootstrap secret is needed:

```bash
vault write auth/kubernetes/role/email-sender \
    bound_service_account_names=email-sender \
    bound_service_account_namespaces=default \
    policies=email-sender ttl=1h
```

The secret is re-read on `Vault:ReloadInterval` (default 5 minutes), so a rotated password is
picked up without a restart. A failed refresh keeps the last good values.

Set `Vault:AuthMethod=Token` with `Vault:Token` for local experiments only.

### Keycloak

Two things routinely go wrong:

- **Audience.** Keycloak leaves the client out of `aud` unless the client has an audience
  mapper. Add the mapper, or set `Keycloak:ValidateAudience=false`.
- **Roles.** Keycloak nests roles inside the `realm_access` and `resource_access` claims, which
  ASP.NET Core does not understand natively. The service flattens them itself. Set
  `Keycloak:RolesClientId` when the role is a *client* role; leave it empty for a realm role.

## Deploying

```bash
docker build -f EmailSenderService/Dockerfile -t email-sender:1.0.0 .

helm upgrade --install email-sender deploy/helm/email-sender \
  --set image.tag=1.0.0 \
  --set smtp.host=smtp.corp.example.com \
  --set smtp.fromAddress=noreply@corp.example.com \
  --set keycloak.authority=https://keycloak.corp.example.com/realms/corp \
  --set vault.address=http://vault.vault.svc.cluster.local:8200
```

The chart creates a ServiceAccount whose name must match the `bound_service_account_names` of
the Vault role. Readiness fails when the Vault overlay has not arrived, so a pod that cannot
reach Vault stays out of the Service instead of accepting mail it cannot send.
