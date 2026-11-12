Add file attachments to the email relay using multipart/form-data.

Read CLAUDE.md first. Its invariants still hold: From comes from config only, credentials only
via env, relay failures stay 502. Start in plan mode: inspect the current controller, request DTO,
SmtpEmailSender, exception handlers, RequestLimits wiring and the Helm chart, then show me a plan
before writing code.

## Contract
- Keep the existing JSON action unchanged so current callers don't break. Add a second action on
  the same route constrained with [Consumes("multipart/form-data")]; JSON stays on
  [Consumes("application/json")]. Same auth policy/role on both.
- The multipart form carries the same fields as the JSON DTO (bound with [FromForm], lists as
  repeated keys), with the same validation — EmailAddressListAttribute must run on the form path
  too — plus `attachments` as zero or more files. Share the mapping to the sender so the two
  actions can't drift apart.
- The sender abstraction gets an optional attachments collection (file name, content type,
  stream). The JSON path passes none.

## Size limits — the hard constraint
The production relay rejects messages over 30_000_000 bytes. You cannot reach it from this
environment: don't try to connect to it, probe it, or read its EHLO/SIZE. Treat
`Smtp:MaxMessageBytes` as the source of truth, and treat this API as the only place that
enforces the limit before the relay does. Attachments are base64-encoded with line breaks in the
message, about ×1.37, so raw attachment bytes must stay around 21 MB at most.

- New options section `Attachments` with ValidateOnStart: MaxCount (default 10),
  MaxTotalBytes (default 20 MiB), MaxFileBytes (default 20 MiB), AllowedExtensions
  (default: .pdf .doc .docx .xls .xlsx .odt .ods .txt .csv .png .jpg .jpeg .zip).
- `Smtp:MaxMessageBytes` (default 30_000_000). Before connecting to the relay, measure the
  fully built MimeMessage (MimeKit's MeasuringStream, or WriteTo into a counting stream). If it
  exceeds the limit, throw a dedicated exception rendered as 413 ProblemDetails, not 502: the
  caller's input is too big, the relay is fine.
- Align the request limit for the multipart action with this (around 25 MB: attachments,
  multipart overhead and text). Set RequestSizeLimit and RequestFormLimits.MultipartBodyLengthLimit
  per action rather than raising the global cap. Reconsider whether the global
  RequestLimits:MaxBodyMegabytes default of 100 still makes sense, and tell me what you chose.
- Exceeding the Kestrel or form limits must return 413 ProblemDetails. By default the form-reading
  path throws InvalidDataException / BadHttpRequestException and becomes a 500; handle that.

## Validation (400 ProblemDetails, one clear message per problem)
- Count, per-file and total size, empty files rejected.
- File name: Path.GetFileName, strip control characters, cap the length, reject if empty after
  that. Extension must be on the allow-list (case-insensitive).
- Don't trust the client Content-Type. Derive it from the extension
  (FileExtensionContentTypeProvider), falling back to application/octet-stream.

## MIME building
- Don't copy files into MemoryStream. Build each MimePart over the IFormFile stream
  (MimeContent, ContentEncoding.Base64). Sending happens inside the request, so the stream stays
  valid. Mind disposal order.
- Non-ASCII (Cyrillic) file names must display correctly in Outlook: use RFC 2047 encoding for
  the Content-Disposition/Content-Type name parameters
  (ParameterEncodingMethod.Rfc2047), not MimeKit's default RFC 2231.
- If the message has attachments, the body becomes multipart/mixed (BodyBuilder is fine as long
  as it doesn't force an extra copy).

## Infra
- ASP.NET buffers form files over 64 KB to temp files, i.e. /tmp, which is the emptyDir under
  readOnlyRootFilesystem. Give that emptyDir a sizeLimit that fits several concurrent requests,
  and revisit the pod memory limit in values.yaml given the new limits. Explain the numbers.
- If the chart has an Ingress, add/align the body-size annotation (ingress-nginx defaults to
  1 MB) and make it configurable via values.
- `helm lint` and `helm template` must pass.

## Logging
Log attachment count and total bytes with the caller (azp). Don't log file names or content.

## OpenAPI
The multipart action must show up with the file upload field in the API docs that this project
already uses.

## Tests
There's no test project yet. Add one (xUnit, WebApplicationFactory, auth stubbed, a fake
sender that captures the MimeMessage, so no SMTP needed). Cover: JSON path unchanged,
multipart happy path with a Cyrillic file name, disallowed extension → 400, too many files → 400,
over-limit body → 413 (not 500), built message over MaxMessageBytes → 413, missing auth → 401.

## Done when
- dotnet build and dotnet test are green, helm lint passes.
- A manual send through a local Mailpit works (note the port 1025 gotcha in CLAUDE.md). Mailpit
  does not enforce the production size limit, so the 413 pre-send check is proven by the tests
  (run them with a small MaxMessageBytes), not by Mailpit. Give me a curl example with two files,
  one of them with a Cyrillic name, that I can run against the real relay myself.
- CLAUDE.md is updated: the Body size section now describes the attachment limits and the 413
  vs 502 split, and the new options are documented.