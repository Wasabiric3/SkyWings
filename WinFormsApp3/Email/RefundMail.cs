using System.Text;
using System.Text.RegularExpressions;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MimeKit;

namespace SkyWings.Email;

/// <summary>
/// Where SkyWings reads refund requests from. Reuses the sending account by default: <c>imap.host</c> becomes <c>imap.</c> plus the
/// <c>smtp.host</c> name (smtp.gmail.com gives imap.gmail.com), port 993, the same user and password. Set <c>imap.enabled=false</c> to
/// switch reading off, or <c>imap.host</c>, <c>imap.port</c>, <c>imap.user</c>, <c>imap.password</c> to use another mailbox.
/// </summary>
public sealed record ImapSettings(string Host, int Port, string User, string Password, bool Enabled)
{
    public bool IsConfigured => Enabled && Host.Length > 0 && User.Length > 0 && Password.Length > 0;

    public static ImapSettings None { get; } = new("", 993, "", "", false);

    /// <summary>The IMAP name for a mail server name, or "" when it is not in the usual smtp.* form.</summary>
    public static string HostFor(string smtpHost)
        => smtpHost.StartsWith("smtp.", StringComparison.OrdinalIgnoreCase) ? "imap." + smtpHost[5..] : "";
}

/// <summary>One email found in the mailbox: who sent it, its subject, and the start of its plain-text body (never attachments or HTML).</summary>
public sealed record IncomingRefundMail(long Uid, string From, string Subject, DateTime SentAt, string Body);

/// <summary>A batch from the mailbox, oldest first, and the mailbox's UID validity (a new value means old UIDs mean nothing).</summary>
public sealed record RefundMailBatch(long UidValidity, IReadOnlyList<IncomingRefundMail> Mails);

public interface IRefundMailSource
{
    /// <summary>Messages whose subject asks for a refund and whose UID is above <paramref name="afterUid"/>, sent since <paramref name="since"/>, at most <paramref name="max"/>.</summary>
    Task<RefundMailBatch> FetchAsync(long afterUid, DateTime since, int max, CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads the mailbox over IMAP with TLS (port 993). Read-only: nothing is marked as read, moved or deleted, only messages whose
/// subject asks for a refund are looked at, only the plain-text part is downloaded (no HTML, no attachments), and a message that is
/// large is skipped.
/// </summary>
public sealed class ImapRefundMailSource : IRefundMailSource
{
    public const string SubjectMarker = "Refund request SKY-";
    private const int MaxMessageBytes = 256 * 1024;
    private readonly ImapSettings _settings;

    public ImapRefundMailSource(ImapSettings settings) => _settings = settings;

    public async Task<RefundMailBatch> FetchAsync(long afterUid, DateTime since, int max, CancellationToken cancellationToken = default)
    {
        using var client = new ImapClient { Timeout = 20_000 };
        await client.ConnectAsync(_settings.Host, _settings.Port, SecureSocketOptions.SslOnConnect, cancellationToken).ConfigureAwait(false);
        await client.AuthenticateAsync(_settings.User, _settings.Password, cancellationToken).ConfigureAwait(false);
        IMailFolder inbox = client.Inbox;
        await inbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken).ConfigureAwait(false);

        var found = await inbox.SearchAsync(SearchQuery.SubjectContains(SubjectMarker).And(SearchQuery.SentSince(since)), cancellationToken).ConfigureAwait(false);
        List<UniqueId> wanted = found.Where(id => id.Id > afterUid).OrderBy(id => id.Id).Take(Math.Clamp(max, 1, 200)).ToList();
        var mails = new List<IncomingRefundMail>();
        if (wanted.Count > 0)
        {
            var summaries = await inbox.FetchAsync(wanted,
                MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope | MessageSummaryItems.BodyStructure | MessageSummaryItems.InternalDate | MessageSummaryItems.Size,
                cancellationToken).ConfigureAwait(false);
            foreach (IMessageSummary summary in summaries.OrderBy(s => s.UniqueId.Id))
            {
                string body = "";
                if (summary.TextBody is { } part && (summary.Size ?? 0) <= MaxMessageBytes
                    && await inbox.GetBodyPartAsync(summary.UniqueId, part, cancellationToken).ConfigureAwait(false) is TextPart text)
                    body = text.Text ?? "";
                string from = summary.Envelope?.From.Mailboxes.FirstOrDefault()?.Address ?? "";
                mails.Add(new IncomingRefundMail(summary.UniqueId.Id, from, summary.Envelope?.Subject ?? "",
                    summary.Envelope?.Date?.LocalDateTime ?? summary.InternalDate?.LocalDateTime ?? DateTime.Now, body));
            }
        }
        long validity = inbox.UidValidity;
        await client.DisconnectAsync(true, cancellationToken).ConfigureAwait(false);
        return new RefundMailBatch(validity, mails);
    }
}

/// <summary>What one look at the mailbox found.</summary>
public sealed record RefundMailCheck(bool Ok, int Found, int Logged, int Ignored, string Message);

/// <summary>
/// Turns refund emails into refund requests. A request is only ever written down (the booking is never cancelled or refunded here),
/// the booking reference comes from the subject, the sender is compared with the booking's own email address and flagged when it differs,
/// and the message is kept as a short plain-text excerpt.
/// </summary>
public sealed class RefundMailProcessor
{
    // "Refund request SKY-" plus the 12 hex characters of a booking reference.
    private static readonly Regex SubjectPattern = new(@"\bRefund request (SKY-[0-9A-F]{12})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private const int MaxBodyCharacters = 1000;

    private readonly BookingStore _store;

    public RefundMailProcessor(BookingStore store) => _store = store;

    /// <summary>The booking reference named in the subject, or null when the subject is not a refund request.</summary>
    public static string? ReferenceIn(string subject)
    {
        try
        {
            Match match = SubjectPattern.Match(subject ?? "");
            return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
        }
        catch (RegexMatchTimeoutException) { return null; }
    }

    /// <summary>One plain line, no control characters, cut to a short excerpt.</summary>
    public static string Excerpt(string body)
    {
        string flat = Regex.Replace(body ?? "", @"\s+", " ", RegexOptions.None, TimeSpan.FromSeconds(1)).Trim();
        return flat.Length <= MaxBodyCharacters ? flat : flat[..MaxBodyCharacters];
    }

    public (int Logged, int Ignored) Process(IEnumerable<IncomingRefundMail> mails)
    {
        int logged = 0, ignored = 0;
        foreach (IncomingRefundMail mail in mails)
        {
            if (ReferenceIn(mail.Subject) is not { } reference) { ignored++; continue; }
            string? contact = _store.LoadBookingContact(reference);
            bool senderMatches = contact is not null && string.Equals(contact.Trim(), mail.From.Trim(), StringComparison.OrdinalIgnoreCase);
            RefundRequestResult result = _store.LogRefundRequest(reference, "email", null, Excerpt(mail.Body), "EMAIL", senderMatches);
            if (result.Ok)
            {
                logged++;
                _store.WriteAudit("email", "SYSTEM", "REFUND_REQUEST_LOGGED", reference,
                    senderMatches ? "Received by email; sender matches the booking" : "Received by email; sender differs from the booking");
            }
            else
            {
                ignored++;
                _store.WriteAudit("email", "SYSTEM", "REFUND_EMAIL_IGNORED", reference, result.Message);
            }
        }
        return (logged, ignored);
    }
}

/// <summary>
/// Checks the mailbox on a timer (and on demand) and passes what it finds to <see cref="RefundMailProcessor"/>. It remembers how far it
/// has read (the mailbox's UID validity and the last UID) in the app settings, so no message is handled twice and nothing is changed in
/// the mailbox. A failure (no network, wrong password) is logged and reported, and the app carries on.
/// </summary>
public sealed class RefundMailPoller : IDisposable
{
    private const string SettingValidity = "refund_mail_uidvalidity";
    private const string SettingLastUid = "refund_mail_last_uid";
    private const int MaxPerCheck = 50;
    private static readonly TimeSpan FirstLookback = TimeSpan.FromDays(14);

    private readonly BookingStore _store;
    private readonly IRefundMailSource _source;
    private readonly RefundMailProcessor _processor;
    private readonly TimeSpan _interval;
    private readonly SemaphoreSlim _busy = new(1, 1);
    private System.Threading.Timer? _timer;

    public RefundMailPoller(BookingStore store, IRefundMailSource source, TimeSpan interval)
    {
        _store = store;
        _source = source;
        _processor = new RefundMailProcessor(store);
        _interval = interval;
    }

    /// <summary>Raised on the thread that did the check whenever new requests were logged.</summary>
    public event Action<RefundMailCheck>? RequestsLogged;

    public void Start() => _timer ??= new System.Threading.Timer(_ => _ = CheckNowAsync(), null, TimeSpan.FromSeconds(5), _interval);

    public async Task<RefundMailCheck> CheckNowAsync(CancellationToken cancellationToken = default)
    {
        if (!await _busy.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return new RefundMailCheck(true, 0, 0, 0, "The mailbox is already being checked.");
        try
        {
            long.TryParse(_store.GetSetting(SettingValidity, "0"), out long validity);
            long.TryParse(_store.GetSetting(SettingLastUid, "0"), out long lastUid);
            DateTime since = DateTime.Now - FirstLookback;
            RefundMailBatch batch = await _source.FetchAsync(lastUid, since, MaxPerCheck, cancellationToken).ConfigureAwait(false);
            if (batch.UidValidity != validity)
            {
                // A different mailbox (or a rebuilt one): the old position means nothing, and the server only returned messages after it.
                // Remember the new validity and read again from the start of the lookback window.
                _store.SetSetting(SettingValidity, batch.UidValidity.ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (lastUid > 0)
                {
                    lastUid = 0;
                    batch = await _source.FetchAsync(0, since, MaxPerCheck, cancellationToken).ConfigureAwait(false);
                }
            }

            var fresh = batch.Mails.Where(m => m.Uid > lastUid).OrderBy(m => m.Uid).ToList();
            (int logged, int ignored) = _processor.Process(fresh);
            if (fresh.Count > 0)
                _store.SetSetting(SettingLastUid, fresh[^1].Uid.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var check = new RefundMailCheck(true, fresh.Count, logged, ignored,
                fresh.Count == 0 ? "No new refund emails." : $"{fresh.Count} refund email(s) read: {logged} request(s) logged, {ignored} ignored.");
            if (logged > 0) RequestsLogged?.Invoke(check);
            return check;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLogger.Error("RefundMail", ex);
            return new RefundMailCheck(false, 0, 0, 0, "Could not read the mailbox: " + ex.Message);
        }
        finally
        {
            _busy.Release();
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _busy.Dispose();
    }
}
