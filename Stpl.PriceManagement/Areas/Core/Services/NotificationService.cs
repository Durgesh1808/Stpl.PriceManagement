using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Stpl.PriceManagement.Areas.Core.Models;
using Stpl.PriceManagement.Areas.Core.Repositories;

namespace Stpl.PriceManagement.Areas.Core.Services
{
    /// <summary>
    /// Reading and clearing what somebody has been told.
    /// </summary>
    /// <remarks>
    /// Writing is not here on purpose. A notification is written by the stored
    /// procedure that raises it (core.usp_NotifyRole / core.usp_NotifyPerson),
    /// inside the transaction of the thing that caused it, and there is no way
    /// to create one from application code. Every pricing area raises its own
    /// events that way and they all arrive in the same bell.
    ///
    /// Every method takes the reader's own user id and the procedures match on
    /// it, so the id alone is never enough to read a colleague's notifications.
    /// </remarks>
    public sealed class NotificationService
    {
        /// <summary>
        /// How far back the list goes. Not paged: a few thousand rows a year is
        /// not a list anybody scrolls.
        /// </summary>
        private const int ListLimit = 100;

        private readonly INotificationRepository _notifications;
        private readonly ILogger<NotificationService> _logger;

        public NotificationService(INotificationRepository notifications, ILogger<NotificationService> logger)
        {
            _notifications = notifications;
            _logger = logger;
        }

        public async Task<NotificationListResponse> ListAsync(int userId, CancellationToken cancellationToken)
        {
            var rows = await _notifications.ListAsync(userId, ListLimit, cancellationToken);

            return new NotificationListResponse
            {
                Unread = rows.Count(r => r.ReadUtc == null),
                Items = rows.Select(Describe).ToList()
            };
        }

        /// <summary>
        /// The number on the bell. Zero when it cannot be counted.
        /// </summary>
        /// <remarks>
        /// This runs on every page render, so it is the one call that must
        /// never be able to break a page: a database hiccup costs the badge,
        /// never the products list.
        /// </remarks>
        public async Task<int> CountUnreadAsync(int userId, CancellationToken cancellationToken)
        {
            try
            {
                return await _notifications.CountUnreadAsync(userId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "The unread notification count could not be fetched.");
                return 0;
            }
        }

        /// <summary>
        /// Marks one read and returns where it points: an empty string when it
        /// points nowhere, null when there is no such notification for this
        /// person (or it could not be marked).
        /// </summary>
        public async Task<string> MarkReadAsync(long notificationId, int userId, CancellationToken cancellationToken)
        {
            try
            {
                var link = await _notifications.MarkReadAsync(notificationId, userId, cancellationToken);
                return link == null ? null : link;
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _logger.LogWarning(ex, "A notification could not be marked read.");
                return null;
            }
        }

        public Task MarkAllReadAsync(int userId, CancellationToken cancellationToken)
        {
            return _notifications.MarkAllReadAsync(userId, cancellationToken);
        }

        private static NotificationResponse Describe(NotificationRow row)
        {
            return new NotificationResponse
            {
                NotificationId = row.NotificationId,
                Event = row.Event,
                TourCode = row.TourCode,
                Reference = row.Reference,
                Subject = row.Subject,
                Body = row.Body,
                Link = row.Link,
                CreatedUtc = row.CreatedUtc,
                IsRead = row.ReadUtc != null
            };
        }
    }
}
