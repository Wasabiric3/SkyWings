using System.Text;

namespace SkyWings.Email;

/// <summary>
/// Saves the email as an .html file (QR code embedded) in the data folder instead of sending it. Used when no mail server
/// is configured, and as the fallback when sending fails, so a booking always leaves a readable copy.
/// </summary>
public sealed class FileOutboxEmailSender : IEmailSender
{
    private readonly string _directory;

    public FileOutboxEmailSender(string? directory = null)
        => _directory = directory ?? Path.Combine(AppPaths.DataDirectory, "outbox");

    public string Name => "Outbox";

    public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_directory);

        // A saved file has no mail client to resolve cid: images, so embed them as data: URIs.
        string html = message.HtmlBody;
        foreach (InlineImage image in message.InlineImages)
            html = html.Replace("cid:" + image.ContentId, $"data:{image.MediaType};base64,{Convert.ToBase64String(image.Bytes)}", StringComparison.Ordinal);

        string header = $"<!-- To: {System.Net.WebUtility.HtmlEncode(message.To)} | Subject: {System.Net.WebUtility.HtmlEncode(message.Subject)} -->\r\n";
        string path = Path.Combine(_directory, $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}.html");
        await File.WriteAllTextAsync(path, header + html, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        // Attachments are saved beside the .html with the same name, so the outbox shows exactly what the customer would get.
        foreach (EmailAttachment file in message.Attachments ?? Array.Empty<EmailAttachment>())
            await File.WriteAllBytesAsync(Path.ChangeExtension(path, null) + "-" + Path.GetFileName(file.FileName), file.Bytes, cancellationToken).ConfigureAwait(false);
        return new EmailSendResult(true, Name, path);
    }
}
