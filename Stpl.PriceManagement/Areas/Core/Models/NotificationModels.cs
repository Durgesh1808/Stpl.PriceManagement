using System;
using System.Collections.Generic;

namespace Stpl.PriceManagement.Areas.Core.Models
{
    /// <summary>One person, for an administration list.</summary>
    /// <summary>Just the number on the bell.</summary>
    public sealed class UnreadCountResponse
    {
        public int Unread { get; set; }
    }

    public sealed class NotificationListResponse
    {
        public int Unread { get; set; }
        public List<NotificationResponse> Items { get; set; }
    }

    /// <summary>One thing that happened, as the person it happened to sees it.</summary>
    public sealed class NotificationResponse
    {
        public long NotificationId { get; set; }

        /// <summary>Which event: fares-received, fare-queried, fares-requested, changeset-submitted, changeset-live.</summary>
        public string Event { get; set; }

        public string TourCode { get; set; }
        public string Reference { get; set; }

        /// <summary>Written when the event happened, not composed on read.</summary>
        public string Subject { get; set; }
        public string Body { get; set; }

        /// <summary>The path this is about. A path, never a URL.</summary>
        public string Link { get; set; }

        public DateTime CreatedUtc { get; set; }
        public bool IsRead { get; set; }
    }
}
