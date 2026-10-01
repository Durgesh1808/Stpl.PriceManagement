using System;

namespace Stpl.PriceManagement.Areas.Core.Models
{
    public sealed class NotificationRow
    {
        public long NotificationId { get; set; }
        public string Event { get; set; }
        public string TourCode { get; set; }
        public string Reference { get; set; }
        public string Subject { get; set; }
        public string Body { get; set; }
        public string Link { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime? ReadUtc { get; set; }
    }

    /// <summary>What core.usp_LogEmail records.</summary>
    public sealed class EmailLogEntry
    {
        public long? NotificationId { get; set; }
        public string Event { get; set; }
        public string RecipientRole { get; set; }
        public string TourCode { get; set; }
        public string FromAddress { get; set; }
        public string ToAddress { get; set; }
        public string CcAddress { get; set; }
        public string BccAddress { get; set; }
        public string Subject { get; set; }
        public string Body { get; set; }

        /// <summary>"Sent" or "Failed".</summary>
        public string Status { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>Everything a sender needs, so it need look nothing else up.</summary>
    public sealed class PendingEmailRow
    {
        public long NotificationId { get; set; }
        public string ToAddress { get; set; }
        public string ToName { get; set; }

        /// <summary>The recipient's role - decides which mailbox gets it (0087).</summary>
        public string Role { get; set; }

        /// <summary>fares-requested, fares-received, fare-queried, ... (0087).</summary>
        public string Event { get; set; }
        public string TourCode { get; set; }
        public DateTime? CreatedUtc { get; set; }
        public string Subject { get; set; }
        public string Body { get; set; }
        public string Link { get; set; }
    }
}
