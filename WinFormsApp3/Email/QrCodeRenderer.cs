using QRCoder;

namespace SkyWings.Email;

/// <summary>Draws a QR code as a PNG with the QRCoder package (MIT licence). PngByteQRCode needs no System.Drawing.</summary>
public sealed class QrCodeRenderer : IQrRenderer
{
    public byte[] RenderPng(string text, int pixelsPerModule = 8)
    {
        using var generator = new QRCodeGenerator();
        using QRCodeData data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        return new PngByteQRCode(data).GetGraphic(pixelsPerModule);
    }
}
