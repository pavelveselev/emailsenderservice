using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EmailSenderService.Models;
using EmailSenderService.Options;
using EmailSenderService.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MimeKit;

namespace EmailSenderService.Tests;

public sealed class SendEmailTests : IDisposable
{
    private const string SendPath = "/api/email/send";
    private const string CyrillicFileName = "Отчёт за квартал.pdf";

    private readonly EmailApiFactory factory = new();

    public void Dispose() => factory.Dispose();

    [Fact]
    public async Task Json_request_sends_a_plain_message_as_before()
    {
        using var client = factory.CreateAuthorizedClient();

        var response = await client.PostAsJsonAsync(SendPath, new
        {
            to = new[] { "someone@example.com" },
            cc = new[] { "team@example.com" },
            subject = "Deployment finished",
            body = "Release 1.4.2 is live.",
            isHtml = false
        }, TestContext.Current.CancellationToken);

        await AssertStatus(response, HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<SendEmailResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal(2, result.RecipientCount);

        var (message, _) = Assert.Single(factory.Transport.Sent);
        var smtp = factory.Services.GetRequiredService<IOptions<SmtpOptions>>().Value;
        Assert.Equal(smtp.FromAddress, Assert.IsType<MailboxAddress>(Assert.Single(message.From)).Address);
        Assert.Equal(result.MessageId, message.MessageId);
        var text = Assert.IsType<TextPart>(message.Body);
        Assert.Equal("plain", text.ContentType.MediaSubtype);
        Assert.Empty(message.Attachments);
    }

    [Fact]
    public async Task Multipart_request_attaches_files_with_rfc2047_names_and_derived_content_types()
    {
        using var client = factory.CreateAuthorizedClient();
        var pdfBytes = RandomBytes(150_000, seed: 1); // больше порога 64 КБ, поэтому буферизуется на диск
        var csvBytes = Encoding.UTF8.GetBytes("a;b\n1;2\n");

        using var form = Form(("to", "someone@example.com"), ("to", "other@example.com"),
            ("subject", "Квартальный отчёт"), ("body", "<p>См. вложение</p>"), ("isHtml", "true"));
        // Клиент заявляет text/plain; сервис должен вывести application/pdf из расширения.
        form.Add(File(pdfBytes, "text/plain"), "attachments", CyrillicFileName);
        form.Add(File(csvBytes, "application/x-whatever"), "attachments", @"C:\Users\someone\data.csv");

        var response = await client.PostAsync(SendPath, form, TestContext.Current.CancellationToken);

        await AssertStatus(response, HttpStatusCode.OK);
        var (message, wire) = Assert.Single(factory.Transport.Sent);

        var mixed = Assert.IsType<Multipart>(message.Body);
        Assert.Equal("mixed", mixed.ContentType.MediaSubtype);

        var attachments = message.Attachments.Cast<MimePart>().ToList();
        Assert.Equal(2, attachments.Count);
        Assert.Equal(CyrillicFileName, attachments[0].FileName);
        Assert.Equal("application/pdf", attachments[0].ContentType.MimeType);
        Assert.Equal(ContentEncoding.Base64, attachments[0].ContentTransferEncoding);
        Assert.Equal("data.csv", attachments[1].FileName);
        Assert.Equal("text/csv", attachments[1].ContentType.MimeType);

        // Outlook нужны encoded-words RFC 2047, а не RFC 2231 (filename*=utf-8''...).
        var headers = Encoding.ASCII.GetString(wire);
        Assert.Contains("filename=\"=?utf-8?b?", headers, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("name=\"=?utf-8?b?", headers, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("filename*", headers, StringComparison.Ordinal);
        Assert.DoesNotContain("name*", headers, StringComparison.Ordinal);

        // Разбираем байты обратно: содержимое пережило первое чтение при проверке размера.
        var reparsed = MimeMessage.Load(new MemoryStream(wire), TestContext.Current.CancellationToken);
        var reparsedParts = reparsed.Attachments.Cast<MimePart>().ToList();
        Assert.Equal(CyrillicFileName, reparsedParts[0].FileName);
        Assert.Equal(pdfBytes, Decode(reparsedParts[0]));
        Assert.Equal(csvBytes, Decode(reparsedParts[1]));
    }

    [Fact]
    public async Task Multipart_request_applies_the_same_address_validation_as_json()
    {
        using var client = factory.CreateAuthorizedClient();
        using var form = Form(("to", "postmaster"), ("subject", "s"), ("body", "b"));

        var response = await client.PostAsync(SendPath, form, TestContext.Current.CancellationToken);

        var errors = await AssertValidationProblem(response);
        Assert.True(errors.TryGetProperty("To", out _));
        Assert.Empty(factory.Transport.Sent);
    }

    [Fact]
    public async Task Disallowed_extension_is_rejected_with_400()
    {
        using var client = factory.CreateAuthorizedClient();
        using var form = Form(("to", "someone@example.com"), ("subject", "s"), ("body", "b"));
        form.Add(File([1, 2, 3], "application/pdf"), "attachments", "invoice.pdf.EXE");

        var response = await client.PostAsync(SendPath, form, TestContext.Current.CancellationToken);

        var errors = await AssertValidationProblem(response);
        var message = errors.GetProperty("Attachments[0]")[0].GetString();
        Assert.Contains(".EXE", message);
        Assert.Contains("not allowed", message);
        Assert.Empty(factory.Transport.Sent);
    }

    [Fact]
    public async Task More_files_than_allowed_are_rejected_with_400()
    {
        using var client = factory.CreateAuthorizedClient();
        using var form = Form(("to", "someone@example.com"), ("subject", "s"), ("body", "b"));
        for (var i = 0; i < 11; i++)
        {
            form.Add(File([1], "text/plain"), "attachments", $"note{i}.txt");
        }

        var response = await client.PostAsync(SendPath, form, TestContext.Current.CancellationToken);

        var errors = await AssertValidationProblem(response);
        Assert.Contains("At most 10 attachments", errors.GetProperty("Attachments")[0].GetString());
        Assert.Empty(factory.Transport.Sent);
    }

    [Fact]
    public async Task Empty_file_is_rejected_with_400()
    {
        using var client = factory.CreateAuthorizedClient();
        using var form = Form(("to", "someone@example.com"), ("subject", "s"), ("body", "b"));
        form.Add(File([], "text/plain"), "attachments", "empty.txt");

        var response = await client.PostAsync(SendPath, form, TestContext.Current.CancellationToken);

        var errors = await AssertValidationProblem(response);
        Assert.Contains("is empty", errors.GetProperty("Attachments[0]")[0].GetString());
    }

    [Fact]
    public async Task Body_over_the_multipart_request_limit_is_413_not_500()
    {
        using var client = factory.CreateAuthorizedClient();
        using var form = Form(("to", "someone@example.com"), ("subject", "s"), ("body", "b"));
        form.Add(File(new byte[AttachmentOptions.MultipartRequestBytes + 1024 * 1024], "application/zip"),
            "attachments", "huge.zip");

        var response = await client.PostAsync(SendPath, form, TestContext.Current.CancellationToken);

        await AssertPayloadTooLarge(response);
        Assert.Empty(factory.Transport.Sent);
    }

    [Fact]
    public async Task Built_message_over_MaxMessageBytes_is_413_and_never_reaches_the_relay()
    {
        using var smallRelay = EmailApiFactory.WithOptions(smtp: options => options.MaxMessageBytes = 20_000);
        using var client = smallRelay.CreateAuthorizedClient();
        using var form = Form(("to", "someone@example.com"), ("subject", "s"), ("body", "b"));
        // 16 КБ исходных данных - намного меньше любого лимита вложений, но около 22 КБ в base64.
        form.Add(File(RandomBytes(16_000, seed: 2), "application/pdf"), "attachments", "scan.pdf");

        var response = await client.PostAsync(SendPath, form, TestContext.Current.CancellationToken);

        var problem = await AssertPayloadTooLarge(response);
        Assert.Contains("20,000", problem.GetProperty("detail").GetString());
        Assert.Empty(smallRelay.Transport.Sent);
    }

    [Fact]
    public async Task Json_message_over_MaxMessageBytes_is_413_as_well()
    {
        using var smallRelay = EmailApiFactory.WithOptions(smtp: options => options.MaxMessageBytes = 1_000);
        using var client = smallRelay.CreateAuthorizedClient();

        var response = await client.PostAsJsonAsync(SendPath, new
        {
            to = new[] { "someone@example.com" },
            subject = "s",
            body = new string('x', 5_000)
        }, TestContext.Current.CancellationToken);

        await AssertPayloadTooLarge(response);
        Assert.Empty(smallRelay.Transport.Sent);
    }

    [Theory]
    [InlineData("json")]
    [InlineData("multipart")]
    public async Task Missing_authentication_is_401(string kind)
    {
        using var client = factory.CreateClient();
        HttpContent content = kind == "json"
            ? JsonContent.Create(new { to = new[] { "someone@example.com" }, subject = "s", body = "b" })
            : Form(("to", "someone@example.com"), ("subject", "s"), ("body", "b"));

        var response = await client.PostAsync(SendPath, content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(factory.Transport.Sent);
    }

    [Fact]
    public async Task OpenApi_document_describes_the_multipart_upload()
    {
        using var client = factory.CreateClient();

        var json = await client.GetStringAsync("/swagger/v1/swagger.json", TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(json);

        var content = document.RootElement.GetProperty("paths").GetProperty("/api/Email/send")
            .GetProperty("post").GetProperty("requestBody").GetProperty("content");
        Assert.True(content.TryGetProperty("application/json", out _));
        var attachments = content.GetProperty("multipart/form-data").GetProperty("schema")
            .GetProperty("properties").GetProperty("Attachments");
        Assert.Equal("array", attachments.GetProperty("type").GetString());
        Assert.Equal("binary", attachments.GetProperty("items").GetProperty("format").GetString());
    }

    private static MultipartFormDataContent Form(params (string Name, string Value)[] fields)
    {
        var form = new MultipartFormDataContent();
        foreach (var (name, value) in fields)
        {
            form.Add(new StringContent(value, Encoding.UTF8), name);
        }

        return form;
    }

    private static ByteArrayContent File(byte[] bytes, string contentType)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return content;
    }

    private static byte[] RandomBytes(int length, int seed)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    private static byte[] Decode(MimePart part)
    {
        using var decoded = new MemoryStream();
        Assert.NotNull(part.Content);
        part.Content.DecodeTo(decoded);
        return decoded.ToArray();
    }

    private static async Task<JsonElement> AssertValidationProblem(HttpResponseMessage response)
    {
        await AssertStatus(response, HttpStatusCode.BadRequest);
        // [Produces("application/json")] контроллера действует и на ошибки валидации;
        // тело при этом всё равно ValidationProblemDetails.
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(400, problem.GetProperty("status").GetInt32());
        return problem.GetProperty("errors");
    }

    private static async Task AssertStatus(HttpResponseMessage response, HttpStatusCode expected)
    {
        if (response.StatusCode != expected)
        {
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.Fail($"Expected {(int)expected} but got {(int)response.StatusCode}: {body}");
        }
    }

    private static async Task<JsonElement> AssertPayloadTooLarge(HttpResponseMessage response)
    {
        await AssertStatus(response, HttpStatusCode.RequestEntityTooLarge);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(413, problem.GetProperty("status").GetInt32());
        return problem;
    }
}
