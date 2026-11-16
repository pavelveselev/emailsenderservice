# Email Sender Service

Небольшой API на ASP.NET Core, отправляющий почту через корпоративный SMTP-релей.

Смысл не в самой отправке, а в том, что **SMTP-учётные данные хранятся ровно в одном месте**.
Приложения, которым нужно отправлять почту, вызывают этот сервис с токеном Keycloak, вместо того
чтобы каждое держало у себя копию пароля от релея. Сами учётные данные читаются из HashiCorp Vault
во время работы, поэтому их никогда нет ни в образе, ни в ConfigMap, ни в репозитории.

```
вызывающий ──(Keycloak JWT)──> EmailSenderService ──(учётные данные из Vault)──> SMTP-релей
```

## API

`POST /api/email/send` - требует bearer-токен с настроенной ролью.

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

`to` обязателен и должен содержать хотя бы один маршрутизируемый адрес; `cc`, `bcc`, `replyTo` и
`isHtml` необязательны. При `isHtml: false` (по умолчанию) тело отправляется как `text/plain`.

**Адрес отправителя задаётся конфигурацией сервера** и не может быть указан вызывающим - именно
это не даёт сервису стать открытым релеем для поддельной почты.

| Статус | Значение |
| --- | --- |
| 200 | Релей принял письмо; в теле ответа - `Message-Id` |
| 400 | Ошибка валидации (неверный адрес, нет темы или тела) |
| 401 | Токен отсутствует, истёк или недействителен |
| 403 | Токен действителен, но без нужной роли |
| 502 | Релей отклонил письмо или был недоступен |

Другие эндпоинты: `/swagger` (UI с кнопкой Authorize для Keycloak), `/healthz/live`,
`/healthz/ready`. Все три доступны анонимно.

## Локальный запуск

```bash
# SMTP-заглушка с веб-интерфейсом на http://localhost:8025
docker run -d --name mailpit -p 1025:1025 -p 8025:8025 axllent/mailpit

dotnet run --project EmailSenderService
```

`appsettings.Development.json` направляет почту в эту заглушку на порт 1025, отключает Vault и
разрешает Keycloak по обычному HTTP. Если порт 1025 на вашей машине занят, опубликуйте Mailpit на
другом порту и переопределите его через `Smtp__Port=2525`.

Для локального Keycloak:

```bash
docker run -d -p 8080:8080 -e KC_BOOTSTRAP_ADMIN_USERNAME=admin \
  -e KC_BOOTSTRAP_ADMIN_PASSWORD=admin quay.io/keycloak/keycloak:26.4 start-dev
```

В `EmailSenderService.http` есть готовые запросы, включая получение токена.

## Конфигурация

Все настройки привязываются из `appsettings.json`, переменных окружения (в стиле `Smtp__Host`)
или Vault. Приоритет растёт слева направо: appsettings → окружение → **Vault побеждает**.

| Секция | Назначение |
| --- | --- |
| `Smtp` | Хост, порт, режим безопасности, адрес отправителя, таймаут. `UserName`/`Password` обычно приходят из Vault |
| `Keycloak` | Authority, audience, требуемая роль, client id для Swagger |
| `Vault` | Адрес, метод аутентификации, KV mount и путь, интервал перечитывания |
| `Swagger:Enabled` | false убирает UI и JSON-документ |

`Smtp:Security` принимает значения MailKit: `None`, `Auto`, `SslOnConnect`, `StartTls`,
`StartTlsWhenAvailable`.

### Vault

Ключи секрета KV v2 по имени проецируются на секцию `Smtp`, так что секрет с ключами
`username` и `password` заполняет `Smtp:UserName` и `Smtp:Password`:

```bash
vault kv put secret/email-sender/smtp username=relay-user password=relay-secret
```

В кластере под аутентифицируется собственным токеном сервисного аккаунта (метод аутентификации
Kubernetes в Vault) - начальный секрет не нужен:

```bash
vault write auth/kubernetes/role/email-sender \
    bound_service_account_names=email-sender \
    bound_service_account_namespaces=default \
    policies=email-sender ttl=1h
```

Секрет перечитывается раз в `Vault:ReloadInterval` (по умолчанию 5 минут), так что сменённый
пароль подхватывается без перезапуска. При неудачном обновлении остаются последние рабочие значения.

`Vault:AuthMethod=Token` с `Vault:Token` - только для локальных экспериментов.

### Keycloak

Регулярно ломаются две вещи:

- **Audience.** Keycloak не включает клиента в `aud`, если у клиента нет audience mapper.
  Добавьте mapper или установите `Keycloak:ValidateAudience=false`.
- **Роли.** Keycloak вкладывает роли в claims `realm_access` и `resource_access`, которые
  ASP.NET Core сам не понимает. Сервис разворачивает их самостоятельно. Задайте
  `Keycloak:RolesClientId`, если роль - *клиентская*; для роли realm оставьте пустым.

## Развёртывание

```bash
docker build -f EmailSenderService/Dockerfile -t email-sender:1.0.0 .

helm upgrade --install email-sender deploy/helm/email-sender \
  --set image.tag=1.0.0 \
  --set smtp.host=smtp.corp.example.com \
  --set smtp.fromAddress=noreply@corp.example.com \
  --set keycloak.authority=https://keycloak.corp.example.com/realms/corp \
  --set vault.address=http://vault.vault.svc.cluster.local:8200
```

Чарт создаёт ServiceAccount, имя которого должно совпадать с `bound_service_account_names` роли
Vault. Readiness не проходит, пока не пришли настройки из Vault, так что под, который не может
достучаться до Vault, остаётся вне Service, а не принимает почту, которую не сможет отправить.
