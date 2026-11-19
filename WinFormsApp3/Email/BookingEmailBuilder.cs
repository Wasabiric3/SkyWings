using System.Net;
using System.Text;

namespace SkyWings.Email;

/// <summary>Turns a booking into the receipt email: an HTML body with the QR code inline, a plain-text twin, and an unsubscribe footer.</summary>
public static class BookingEmailBuilder
{
    public const string QrContentId = "booking-qr";

    public const string UnsubscribeText =
        "You are receiving this because an email address was given for this booking. To stop these emails, " +
        "contact SkyWings staff and ask for the address to be removed from receipt emails.";

    public static EmailMessage Build(BookingRecord booking, string toEmail, byte[] qrPng, string? unsubscribeMailbox = null,
        IReadOnlyList<EmailAttachment>? attachments = null)
    {
        var rows = BookingSummary.Rows(booking).Where(r => r.Label != "Refund" || booking.RefundAmount > 0).ToList();
        string subject = $"SkyWings booking {booking.Reference}: {booking.Origin} to {booking.Destination}";

        var html = new StringBuilder();
        html.Append("<!DOCTYPE html><html><body style=\"margin:0;padding:24px;background:#f3f7f4;font-family:Segoe UI,Arial,sans-serif;color:#1e2726\">");
        html.Append("<div style=\"max-width:560px;margin:0 auto;background:#ffffff;border-radius:14px;padding:28px;border:1px solid #e1eae4\">");
        html.Append("<div style=\"font-size:22px;font-weight:700;color:#3f6556\">SkyWings</div>");
        html.Append("<div style=\"color:#687e74;margin-bottom:18px\">Your booking is confirmed" + (attachments is { Count: > 0 } ? ". Attached (PDF): " + AttachmentNames(attachments) + "." : "") + "</div>");
        html.Append($"<div style=\"font-size:13px;color:#687e74\">BOOKING REFERENCE</div><div style=\"font-size:24px;font-weight:700;letter-spacing:1px\">{Enc(booking.Reference)}</div>");
        html.Append("<table style=\"width:100%;border-collapse:collapse;margin:18px 0\">");
        foreach ((string label, string value) in rows)
            html.Append($"<tr><td style=\"padding:6px 0;color:#687e74;width:34%;vertical-align:top\">{Enc(label)}</td><td style=\"padding:6px 0\">{Enc(value)}</td></tr>");
        html.Append("</table>");
        html.Append($"<div style=\"text-align:center;margin:20px 0\"><img src=\"cid:{QrContentId}\" width=\"180\" height=\"180\" alt=\"QR code for booking {Enc(booking.Reference)}\"/>");
        html.Append("<div style=\"font-size:12px;color:#687e74;margin-top:6px\">Show this code at check-in. It contains your booking reference.</div></div>");
        html.Append($"<hr style=\"border:none;border-top:1px solid #e1eae4;margin:20px 0\"/><div style=\"font-size:12px;color:#687e74\">{Enc(UnsubscribeText)}</div>");
        html.Append(UnsubscribeButtonHtml(unsubscribeMailbox, booking.Reference));
        html.Append("</div></body></html>");

        var text = new StringBuilder();
        text.AppendLine("SkyWings - your booking is confirmed").AppendLine();
        if (attachments is { Count: > 0 }) text.AppendLine("Attached (PDF): " + AttachmentNames(attachments) + ".").AppendLine();
        text.AppendLine("Booking reference: " + booking.Reference);
        foreach ((string label, string value) in rows) text.AppendLine($"{label}: {value}");
        text.AppendLine().AppendLine("Show your booking reference (the QR code in the HTML version) at check-in.");
        text.AppendLine().AppendLine(UnsubscribeText);
        text.Append(UnsubscribeTextLine(unsubscribeMailbox, booking.Reference));

        return new EmailMessage(toEmail, subject, html.ToString(), text.ToString(),
            new[] { new InlineImage(QrContentId, qrPng) }, unsubscribeMailbox,
            attachments is { Count: > 0 } ? attachments : null);
    }

    /// <summary>The boarding pass email sent after check-in. Carries the pass PDF(s) and the booking QR code.</summary>
    public static EmailMessage BuildBoardingPass(BookingRecord booking, string toEmail, byte[] qrPng, string? unsubscribeMailbox,
        IReadOnlyList<EmailAttachment> attachments)
    {
        string subject = $"SkyWings boarding pass {booking.Reference}: {booking.Origin} to {booking.Destination}";
        string attached = AttachmentNames(attachments);

        var html = new StringBuilder();
        html.Append("<!DOCTYPE html><html><body style=\"margin:0;padding:24px;background:#f3f7f4;font-family:Segoe UI,Arial,sans-serif;color:#1e2726\">");
        html.Append("<div style=\"max-width:560px;margin:0 auto;background:#ffffff;border-radius:14px;padding:28px;border:1px solid #e1eae4\">");
        html.Append("<div style=\"font-size:22px;font-weight:700;color:#3f6556\">SkyWings</div>");
        html.Append($"<div style=\"color:#687e74;margin-bottom:18px\">You are checked in. Attached (PDF): {Enc(attached)}.</div>");
        html.Append($"<div style=\"font-size:13px;color:#687e74\">BOOKING REFERENCE</div><div style=\"font-size:24px;font-weight:700;letter-spacing:1px\">{Enc(booking.Reference)}</div>");
        html.Append($"<div style=\"text-align:center;margin:20px 0\"><img src=\"cid:{QrContentId}\" width=\"180\" height=\"180\" alt=\"QR code for booking {Enc(booking.Reference)}\"/>");
        html.Append("<div style=\"font-size:12px;color:#687e74;margin-top:6px\">Show this code or the attached boarding pass at the airport.</div></div>");
        html.Append($"<hr style=\"border:none;border-top:1px solid #e1eae4;margin:20px 0\"/><div style=\"font-size:12px;color:#687e74\">{Enc(UnsubscribeText)}</div>");
        html.Append(UnsubscribeButtonHtml(unsubscribeMailbox, booking.Reference));
        html.Append("</div></body></html>");

        var text = new StringBuilder();
        text.AppendLine("SkyWings - you are checked in").AppendLine();
        text.AppendLine("Attached (PDF): " + attached + ".").AppendLine();
        text.AppendLine("Booking reference: " + booking.Reference);
        text.AppendLine().AppendLine(UnsubscribeText);
        text.Append(UnsubscribeTextLine(unsubscribeMailbox, booking.Reference));

        return new EmailMessage(toEmail, subject, html.ToString(), text.ToString(),
            new[] { new InlineImage(QrContentId, qrPng) }, unsubscribeMailbox, attachments);
    }

    private static string AttachmentNames(IReadOnlyList<EmailAttachment> attachments)
        => string.Join(", ", attachments.Select(a => a.Label ?? a.FileName));

    /// <summary>The "a seat is available" email sent to the first waitlisted traveller. Carries the code needed to book.</summary>
    public static EmailMessage BuildWaitlistOffer(WaitlistEntry entry, string? unsubscribeMailbox = null)
    {
        string flight = $"{entry.FlightNo} on {entry.FlightDate:dddd, d MMMM yyyy}";
        string seats = entry.SeatsWanted == 1 ? "1 seat" : $"{entry.SeatsWanted} seats";
        string until = entry.OfferExpiresAt?.ToString("dddd, d MMMM yyyy 'at' h:mm tt") ?? "soon";
        string subject = $"SkyWings: seats are now available on {entry.FlightNo}";
        string how = "In SkyWings, search this flight and cabin, choose Book, and enter your waitlist code when asked. " +
                     "After the deadline the seats go to the next traveller in line.";

        var html = new StringBuilder();
        html.Append("<!DOCTYPE html><html><body style=\"margin:0;padding:24px;background:#f3f7f4;font-family:Segoe UI,Arial,sans-serif;color:#1e2726\">");
        html.Append("<div style=\"max-width:560px;margin:0 auto;background:#ffffff;border-radius:14px;padding:28px;border:1px solid #e1eae4\">");
        html.Append("<div style=\"font-size:22px;font-weight:700;color:#3f6556\">SkyWings</div>");
        html.Append("<div style=\"color:#687e74;margin-bottom:18px\">Good news: your waitlist spot has come up</div>");
        html.Append($"<p>Hello {Enc(entry.PassengerName)}, {Enc(seats)} in {Enc(entry.ClassName)} on <b>{Enc(flight)}</b> are being held for you until <b>{Enc(until)}</b>.</p>");
        html.Append($"<div style=\"font-size:13px;color:#687e74\">YOUR WAITLIST CODE</div><div style=\"font-size:24px;font-weight:700;letter-spacing:1px\">{Enc(entry.BookingReference)}</div>");
        html.Append($"<p>{Enc(how)}</p>");
        if (unsubscribeMailbox is not null) html.Append($"<div style=\"font-size:12px;color:#687e74\">Questions: {Enc(unsubscribeMailbox)}</div>");
        html.Append(UnsubscribeButtonHtml(unsubscribeMailbox, entry.BookingReference));
        html.Append("</div></body></html>");

        var text = new StringBuilder();
        text.AppendLine("SkyWings - your waitlist spot has come up").AppendLine();
        text.AppendLine($"Hello {entry.PassengerName}, {seats} in {entry.ClassName} on {flight} are held for you until {until}.");
        text.AppendLine("Waitlist code: " + entry.BookingReference).AppendLine().AppendLine(how);
        text.Append(UnsubscribeTextLine(unsubscribeMailbox, entry.BookingReference));

        return new EmailMessage(entry.Email, subject, html.ToString(), text.ToString(), Array.Empty<InlineImage>(), unsubscribeMailbox);
    }


    /// <summary>
    /// The "Unsubscribe" button. SkyWings has no website, so the button is a mailto: link that opens a ready-written email to the SkyWings
    /// address; staff then stop the emails from Manage booking. Empty when no sender address is configured (outbox only).
    /// </summary>
    private static string UnsubscribeButtonHtml(string? mailbox, string reference)
    {
        if (string.IsNullOrWhiteSpace(mailbox)) return "";
        string link = UnsubscribeLink(mailbox, reference);
        return "<div style=\"text-align:center;margin:14px 0 0\"><a href=\"" + Enc(link) + "\" style=\"display:inline-block;padding:9px 22px;" +
               "border:1px solid #3f6556;border-radius:8px;color:#3f6556;font-size:13px;font-weight:600;text-decoration:none\">Unsubscribe</a></div>";
    }

    private static string UnsubscribeTextLine(string? mailbox, string reference)
        => string.IsNullOrWhiteSpace(mailbox) ? "" : $"{Environment.NewLine}To unsubscribe, email {mailbox} with the subject \"Unsubscribe {reference}\".{Environment.NewLine}";

    internal static string UnsubscribeLink(string mailbox, string reference)
        => $"mailto:{mailbox}?subject={Uri.EscapeDataString("Unsubscribe " + reference)}" +
           $"&body={Uri.EscapeDataString("Please stop sending SkyWings emails to this address. Booking reference: " + reference)}";

    private static string Enc(string value) => WebUtility.HtmlEncode(value);
}
