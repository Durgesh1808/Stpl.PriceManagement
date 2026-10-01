using System;
using System.Collections.Generic;
using Stpl.PriceManagement.Areas.Core.Models;

namespace Stpl.PriceManagement.Areas.Core.ViewModels
{
    /// <summary>Everything that has happened to you, newest first.</summary>
    public sealed class NotificationsViewModel
    {
        public List<(string Label, string Href)> Crumbs { get; } = new List<(string, string)>
        {
            ("Notifications", null)
        };

        public IReadOnlyList<NotificationResponse> Items { get; set; } = new List<NotificationResponse>();

        public int Unread { get; set; }

        /// <summary>
        /// How long ago, in the roughest terms that are still useful. Rounded
        /// down: saying 2 hours for 2h50m is better than being ahead of the clock.
        /// </summary>
        public string Ago(DateTime createdUtc)
        {
            var span = DateTime.UtcNow - createdUtc;

            if (span.TotalMinutes < 1)
            {
                return "just now";
            }

            if (span.TotalHours < 1)
            {
                var minutes = (int)span.TotalMinutes;
                return minutes + (minutes == 1 ? " minute ago" : " minutes ago");
            }

            if (span.TotalDays < 1)
            {
                var hours = (int)span.TotalHours;
                return hours + (hours == 1 ? " hour ago" : " hours ago");
            }

            if (span.TotalDays < 7)
            {
                var days = (int)span.TotalDays;
                return days + (days == 1 ? " day ago" : " days ago");
            }

            // Past a week the date is more use than the distance.
            return createdUtc.ToString("d MMM");
        }
    }
}
