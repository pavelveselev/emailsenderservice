namespace EmailSenderService.Services;

/// <summary>
/// A file to attach. <paramref name="Content"/> is owned by the caller: it is read while the
/// message is measured and again while it is sent, so it must stay open and seekable until
/// <see cref="IEmailSender.SendAsync"/> completes, and the caller disposes it afterwards.
/// </summary>
/// <param name="FileName">Already sanitised; used as-is for the MIME name parameters.</param>
/// <param name="ContentType">Derived by this service, never taken from the client.</param>
/// <param name="Content">The file's bytes.</param>
public sealed record EmailAttachment(string FileName, string ContentType, Stream Content);
