namespace SkyWings.Email;

/// <summary>
/// PLACEHOLDER. The real renderer needs the QRCoder package (planned, MIT licence); until it is added to the project this
/// throws, and <see cref="BookingEmailService"/> reports "could not be sent" instead of emailing a broken image.
/// </summary>
public sealed class QrCodeRenderer : IQrRenderer
{
    public byte[] RenderPng(string text, int pixelsPerModule = 8)
        => throw new NotSupportedException("The QRCoder package has not been added to the project yet.");
}
