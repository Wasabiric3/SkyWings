namespace SkyWings.Email;

/// <summary>Something that can deliver an <see cref="EmailMessage"/>. Callers never know which kind they have.</summary>
public interface IEmailSender
{
    string Name { get; }
    Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

/// <summary>Draws a QR code as PNG bytes. Kept behind an interface so the email code does not depend on one library.</summary>
public interface IQrRenderer
{
    byte[] RenderPng(string text, int pixelsPerModule = 8);
}
