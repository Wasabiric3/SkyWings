namespace SkyWings.Email;

/// <summary>Tries the primary sender; if it fails for any reason, hands the same message to the fallback.</summary>
public sealed class FallbackEmailSender : IEmailSender
{
    private readonly IEmailSender _primary;
    private readonly IEmailSender _fallback;

    public FallbackEmailSender(IEmailSender primary, IEmailSender fallback)
    {
        _primary = primary;
        _fallback = fallback;
    }

    public string Name => _primary.Name + " + " + _fallback.Name;

    public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _primary.SendAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLogger.Error("Email", ex);   // the log never receives the address or the message body
            EmailSendResult saved = await _fallback.SendAsync(message, cancellationToken).ConfigureAwait(false);
            return saved with { Detail = "Could not reach the mail server, so a copy was saved:\n" + saved.Detail };
        }
    }
}
