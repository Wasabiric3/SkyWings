using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;

namespace SkyWings.Email;

/// <summary>Sends through a real mail server (for example Gmail with an app password).</summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly SmtpSettings _settings;

    public SmtpEmailSender(SmtpSettings settings) => _settings = settings;

    public string Name => "SMTP";

    public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (!_settings.IsConfigured) throw new InvalidOperationException("SMTP is not configured.");

        using var mail = new MailMessage { From = new MailAddress(_settings.From, "SkyWings"), Subject = message.Subject };
        mail.To.Add(new MailAddress(message.To));
        mail.SubjectEncoding = Encoding.UTF8;
        mail.BodyEncoding = Encoding.UTF8;
        if (message.UnsubscribeMailbox is { Length: > 0 } mailbox)
            mail.Headers.Add("List-Unsubscribe", $"<mailto:{mailbox}?subject=unsubscribe>");

        mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(message.TextBody, Encoding.UTF8, MediaTypeNames.Text.Plain));
        AlternateView html = AlternateView.CreateAlternateViewFromString(message.HtmlBody, Encoding.UTF8, MediaTypeNames.Text.Html);
        foreach (InlineImage image in message.InlineImages)
            html.LinkedResources.Add(new LinkedResource(new MemoryStream(image.Bytes), image.MediaType) { ContentId = image.ContentId });
        mail.AlternateViews.Add(html);
        foreach (EmailAttachment file in message.Attachments ?? Array.Empty<EmailAttachment>())
            mail.Attachments.Add(new Attachment(new MemoryStream(file.Bytes), file.FileName, file.MediaType));

        using var client = new SmtpClient(_settings.Host, _settings.Port) { EnableSsl = _settings.UseSsl, Timeout = 15000 };
        if (_settings.User.Length > 0) client.Credentials = new NetworkCredential(_settings.User, _settings.Password);
        await client.SendMailAsync(mail, cancellationToken).ConfigureAwait(false);
        return new EmailSendResult(true, Name, "Sent to the customer's mailbox.");
    }
}
