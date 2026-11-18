namespace SkyWings.Email;

/// <summary>An image shown inside the HTML body, referenced as <c>cid:ContentId</c>.</summary>
public sealed record InlineImage(string ContentId, byte[] Bytes, string MediaType = "image/png");

public sealed record EmailMessage(
    string To, string Subject, string HtmlBody, string TextBody,
    IReadOnlyList<InlineImage> InlineImages, string? UnsubscribeMailbox = null);

/// <summary>What happened to a message: <see cref="Channel"/> is "SMTP", "Outbox", or "Skipped".</summary>
public sealed record EmailSendResult(bool Delivered, string Channel, string Detail);

/// <summary>SMTP settings from <c>db.properties</c>. Without a host and a from-address the app uses the outbox only.</summary>
public sealed record SmtpSettings(string Host, int Port, string User, string Password, string From, bool UseSsl)
{
    public bool IsConfigured => Host.Length > 0 && BookingRules.IsValidEmail(From);

    public static SmtpSettings None { get; } = new("", 587, "", "", "", true);
}
