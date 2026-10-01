using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Stpl.PriceManagement.Areas.Core.Services.Email
{
    /// <summary>
    /// One email, ready to send. Everything a sender needs and nothing else.
    /// </summary>
    /// <remarks>
    /// Composed when the event happened, not when the email is sent. A message
    /// assembled later would describe figures that have since moved on, and
    /// would quietly rewrite what somebody was told.
    /// </remarks>
    public sealed class NotificationEmail
    {
        public string ToAddress { get; set; }
        public string ToName { get; set; }
        public string Subject { get; set; }
        public string Body { get; set; }

        /// <summary>The path in the application this is about, or null.</summary>
        public string Link { get; set; }

        /// <summary>The notification this email is for (0087).</summary>
        public long NotificationId { get; set; }

        /// <summary>The recipient's role: decides the mailbox (MailSettings:RoleRecipients).</summary>
        public string Role { get; set; }

        /// <summary>What happened: decides the subject line.</summary>
        public string Event { get; set; }

        public string TourCode { get; set; }
    }

    /// <summary>
    /// THE INTEGRATION POINT. One method, and it is the only one.
    /// </summary>
    /// <remarks>
    /// Southern Travels has its own way of sending email, so this application
    /// deliberately does not have one. What it has is the socket: every
    /// notification is already stored with a recipient, a subject and a body,
    /// and this is where a sender collects them.
    ///
    /// TO WIRE IN A REAL SENDER
    ///
    /// Write a class implementing this interface, and change ONE line in
    /// Program.cs - the registration of NoOpNotificationEmailSender - to
    /// register yours instead. Nothing else in the notification code changes,
    /// and none of the four events that raise a notification knows or cares
    /// whether email exists.
    ///
    /// WHAT IS DELIBERATELY NOT HERE
    ///
    /// No retry, no queue, no delivery record. In-app notification is the
    /// channel that is built and it is durable; email is an extra. A sender
    /// that needs to survive a restart should do that inside its own
    /// implementation, where the sending technology's own retry lives, rather
    /// than have this application invent a second outbox beside the one it
    /// already runs.
    /// </remarks>
    public interface INotificationEmailSender
    {
        /// <summary>Whether this sender actually sends anything.</summary>
        /// <remarks>
        /// False on the no-op, and the dispatcher then does not start at all -
        /// no polling, no claimed rows, no work. It is what lets "no sender is
        /// registered" be something the application states plainly at startup
        /// rather than something discovered later.
        ///
        /// A real sender returns true.
        /// </remarks>
        bool IsEnabled { get; }

        Task SendAsync(NotificationEmail email, CancellationToken cancellationToken);
    }

    /// <summary>
    /// The sender fitted by default: it does nothing, and says so once.
    /// </summary>
    /// <remarks>
    /// Genuinely inert. Nothing is attempted, nothing is queued, nothing fails
    /// and nothing appears in the log at a level anybody watches. An email
    /// integration that half-works is worse than one that is plainly absent,
    /// because the second is obvious and the first is discovered by somebody
    /// who did not get an email they were counting on.
    /// </remarks>
    public sealed class NoOpNotificationEmailSender : INotificationEmailSender
    {
        private readonly ILogger<NoOpNotificationEmailSender> _logger;

        public NoOpNotificationEmailSender(ILogger<NoOpNotificationEmailSender> logger)
        {
            _logger = logger;
        }

        public bool IsEnabled
        {
            get { return false; }
        }

        public Task SendAsync(NotificationEmail email, CancellationToken cancellationToken)
        {
            /*
                Debug, not Information. This runs on every notification raised,
                and a line per notification at a level anybody reads would be a
                log full of something that did not happen.

                The recipient is named and the body is not: who was told is
                operationally useful, and the body can carry a fare.
            */
            _logger.LogDebug(
                "No email sender is registered, so nothing was sent to {Recipient}.",
                email == null ? "(nobody)" : email.ToAddress);

            return Task.CompletedTask;
        }
    }
}
