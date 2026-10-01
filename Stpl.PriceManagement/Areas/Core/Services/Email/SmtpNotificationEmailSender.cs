using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stpl.PriceManagement.Areas.Core.Models;
using Stpl.PriceManagement.Areas.Core.Repositories;

namespace Stpl.PriceManagement.Areas.Core.Services.Email
{
    /// <summary>
    /// The "MailSettings" section of appsettings.json. Named after, and
    /// behaving like, the appSettings keys ClsCommon.sendmail reads in the
    /// ASP.NET 4.8 applications, so the same values carry across.
    /// </summary>
    public sealed class MailSettings
    {
        public const string SectionName = "MailSettings";

        /// <summary>Master switch. False: in-app notifications only, no email.</summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// ClsCommon's "isLive". When false, every email goes to TestRecipient
        /// instead and CC/BCC are dropped - the same safety net as 4.8.
        /// </summary>
        public bool IsLive { get; set; }

        public string TestRecipient { get; set; }

        /// <summary>ClsCommon's "AuthMail": authenticated SMTP (Office 365).</summary>
        public bool AuthMail { get; set; } = true;

        public string AuthMailSmtpIP { get; set; } = "smtp.office365.com";
        public int AuthMailSmtpPort { get; set; } = 587;

        /// <summary>Plain relay host when AuthMail is false (ClsCommon's "SmtpHost").</summary>
        public string SmtpHost { get; set; }
        public int SmtpPort { get; set; } = 25;

        /// <summary>The logical sender, e.g. etickets@southerntravels.in. Used to find the credentials.</summary>
        public string From { get; set; }
        public string FromName { get; set; } = "Southern Travels Dynamic Pricing";

        public string Cc { get; set; }
        public string Bcc { get; set; }

        /// <summary>
        /// Where the web app is, so the link in the email is clickable, e.g.
        /// http://localhost:5200 or https://pricing.southerntravels.com.
        /// </summary>
        public string AppBaseUrl { get; set; }

        /// <summary>
        /// "{from}_UserName" / "{from}_Password", exactly as in web.config:
        /// etickets@southerntravels.in_UserName and so on.
        /// </summary>
        public Dictionary<string, string> Credentials { get; set; }
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Role -> mailbox(es). Separate several with ; or ,. A role not listed
        /// falls back to the address on the user's own account.
        /// </summary>
        public Dictionary<string, string> RoleRecipients { get; set; }
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Sends the email that goes with each notification, over SMTP, and logs
    /// every attempt to app.EmailLog.
    /// </summary>
    /// <remarks>
    /// A port of ClsCommon.sendmail / AuthMail / SendSmtpMail:
    /// isLive redirect, Office 365 authenticated send on 587 with TLS, the
    /// From being the authenticated mailbox (Office 365 refuses anything
    /// else), HTML body, and a log row whether it went or not.
    ///
    /// One difference, deliberately: a failure is logged AND thrown. The
    /// dispatcher then leaves the notification unmarked and tries again on its
    /// next pass (for up to ten minutes), instead of the email being lost
    /// after one attempt.
    /// </remarks>
    public sealed class SmtpNotificationEmailSender : INotificationEmailSender
    {
        private readonly MailSettings _settings;
        private readonly INotificationEmailRepository _log;
        private readonly ILogger<SmtpNotificationEmailSender> _logger;

        public SmtpNotificationEmailSender(
            IOptions<MailSettings> settings,
            INotificationEmailRepository log,
            ILogger<SmtpNotificationEmailSender> logger)
        {
            _settings = settings.Value;
            _log = log;
            _logger = logger;
        }

        public bool IsEnabled
        {
            get { return _settings.Enabled; }
        }

        public async Task SendAsync(NotificationEmail email, CancellationToken cancellationToken)
        {
            var to = RecipientFor(email);
            var cc = _settings.Cc ?? string.Empty;
            var bcc = _settings.Bcc ?? string.Empty;

            if (!_settings.IsLive)
            {
                to = _settings.TestRecipient;
                cc = string.Empty;
                bcc = string.Empty;
            }

            var subject = SubjectFor(email);
            var body = BodyFor(email);

            string smtpUser = null;
            string smtpPass = null;
            string host;
            int port;
            bool ssl;

            if (_settings.AuthMail)
            {
                host = _settings.AuthMailSmtpIP;
                port = _settings.AuthMailSmtpPort;
                ssl = true;
                smtpUser = Credential((_settings.From ?? string.Empty).ToLowerInvariant() + "_UserName");
                smtpPass = Credential((_settings.From ?? string.Empty).ToLowerInvariant() + "_Password");
            }
            else
            {
                host = _settings.SmtpHost;
                port = _settings.SmtpPort;
                ssl = false;
            }

            // As in SendSmtpMail: with Office 365 the From must be the mailbox
            // that signs in.
            var from = !string.IsNullOrWhiteSpace(smtpUser) ? smtpUser : _settings.From;

            var entry = new EmailLogEntry
            {
                NotificationId = email.NotificationId,
                Event = email.Event,
                RecipientRole = email.Role,
                TourCode = email.TourCode,
                FromAddress = from,
                ToAddress = to,
                CcAddress = cc,
                BccAddress = bcc,
                Subject = subject,
                Body = body
            };

            try
            {
                if (string.IsNullOrWhiteSpace(to))
                {
                    throw new InvalidOperationException(
                        "No recipient: set MailSettings:RoleRecipients for role '" + email.Role + "'"
                        + (_settings.IsLive ? "" : " or MailSettings:TestRecipient") + ".");
                }

                if (string.IsNullOrWhiteSpace(host))
                {
                    throw new InvalidOperationException("No SMTP host configured in MailSettings.");
                }

                using (var message = new MailMessage())
                {
                    message.From = new MailAddress(from, _settings.FromName);

                    foreach (var address in Split(to)) message.To.Add(address);
                    foreach (var address in Split(cc)) message.CC.Add(address);
                    foreach (var address in Split(bcc)) message.Bcc.Add(address);

                    message.Subject = subject;
                    message.Body = body;
                    message.IsBodyHtml = true;
                    message.BodyEncoding = Encoding.UTF8;
                    message.SubjectEncoding = Encoding.UTF8;

                    using (var smtp = new SmtpClient(host, port))
                    {
                        smtp.EnableSsl = ssl;

                        if (!string.IsNullOrWhiteSpace(smtpUser))
                        {
                            smtp.UseDefaultCredentials = false;
                            smtp.Credentials = new NetworkCredential(smtpUser, smtpPass);
                        }
                        else
                        {
                            smtp.UseDefaultCredentials = true;
                        }

                        await smtp.SendMailAsync(message, cancellationToken);
                    }
                }

                entry.Status = "Sent";
                await SafeLogAsync(entry, cancellationToken);

                _logger.LogInformation(
                    "Emailed notification {NotificationId} ({Event}) to {Recipient}.",
                    email.NotificationId, email.Event, to);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                entry.Status = "Failed";
                entry.ErrorMessage = ex.ToString();
                await SafeLogAsync(entry, cancellationToken);
                throw;
            }
        }

        private string RecipientFor(NotificationEmail email)
        {
            string mapped;
            if (!string.IsNullOrWhiteSpace(email.Role)
                && _settings.RoleRecipients != null
                && _settings.RoleRecipients.TryGetValue(email.Role, out mapped)
                && !string.IsNullOrWhiteSpace(mapped))
            {
                return mapped;
            }

            return email.ToAddress;
        }

        private string Credential(string key)
        {
            string value;
            return _settings.Credentials != null && _settings.Credentials.TryGetValue(key, out value)
                ? value
                : null;
        }

        /// <summary>
        /// Says what was shared and by whom, so the inbox reads as the week's
        /// hand-overs: "Fares requested by the product team | FBK06".
        /// </summary>
        public static string SubjectFor(NotificationEmail email)
        {
            string what;
            switch (email.Event)
            {
                case "fares-requested":
                    what = "Fares requested by the product team";
                    break;
                case "fare-queried":
                    what = "Fare sent back by the product team";
                    break;
                case "fares-received":
                    what = "Fares shared by air-ticketing";
                    break;
                case "changeset-submitted":
                    what = "Change set shared with tech support";
                    break;
                case "changeset-live":
                    what = "Prices updated on the website";
                    break;
                default:
                    what = email.Subject ?? "Dynamic Pricing update";
                    break;
            }

            var tour = string.IsNullOrWhiteSpace(email.TourCode) ? "" : " | " + email.TourCode;
            return what + tour + " | Southern Travels Dynamic Pricing";
        }

        private string BodyFor(NotificationEmail email)
        {
            var url = AbsoluteLink(email.Link);
            var e = (Func<string, string>)WebUtility.HtmlEncode;

            var html = new StringBuilder();
            html.Append("<div style=\"font-family:Segoe UI,Arial,sans-serif;font-size:14px;color:#1F2A37;max-width:600px\">");
            html.Append("<h2 style=\"font-size:18px;margin:0 0 12px;color:#0F2A4A\">").Append(e(email.Subject ?? "")).Append("</h2>");
            html.Append("<p style=\"line-height:1.55;margin:0 0 18px\">").Append(e(email.Body ?? "")).Append("</p>");

            if (!string.IsNullOrWhiteSpace(url))
            {
                html.Append("<p style=\"margin:0 0 18px\"><a href=\"").Append(e(url))
                    .Append("\" style=\"display:inline-block;background:#14528A;color:#ffffff;text-decoration:none;")
                    .Append("padding:10px 18px;border-radius:6px;font-weight:600\">Open in Dynamic Pricing</a></p>");
                html.Append("<p style=\"font-size:12px;color:#5B6B7F;margin:0 0 18px\">Or copy this link: ")
                    .Append("<a href=\"").Append(e(url)).Append("\">").Append(e(url)).Append("</a></p>");
            }

            html.Append("<p style=\"font-size:12px;color:#8391A3;margin:0\">Sent automatically by Southern Travels Dynamic Pricing");
            if (!string.IsNullOrWhiteSpace(email.ToName))
            {
                html.Append(" to the ").Append(e(email.ToName)).Append(" team");
            }
            html.Append(". The same message is in the bell in the application.</p></div>");

            return html.ToString();
        }

        private string AbsoluteLink(string link)
        {
            if (string.IsNullOrWhiteSpace(link))
            {
                return null;
            }

            if (link.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || link.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return link;
            }

            var root = (_settings.AppBaseUrl ?? string.Empty).TrimEnd('/');
            return root + (link.StartsWith("/") ? link : "/" + link);
        }

        private static IEnumerable<string> Split(string addresses)
        {
            return (addresses ?? string.Empty)
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(a => a.Trim())
                .Where(a => a.Length > 0);
        }

        private async Task SafeLogAsync(EmailLogEntry entry, CancellationToken cancellationToken)
        {
            try
            {
                await _log.LogEmailAsync(entry, cancellationToken);
            }
            catch (Exception ex)
            {
                // A missing log table must not stop the email itself.
                _logger.LogWarning(ex, "Could not write to app.EmailLog.");
            }
        }
    }
}
