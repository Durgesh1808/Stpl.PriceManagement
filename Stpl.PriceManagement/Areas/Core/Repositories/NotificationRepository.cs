using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Stpl.PriceManagement.Areas.Core.Models;
using Stpl.PriceManagement.Infrastructure.Data;

namespace Stpl.PriceManagement.Areas.Core.Repositories
{
    /// <summary>
    /// Reading and clearing what somebody has been told. Notifications are
    /// WRITTEN by the stored procedure that raises the event (inside that
    /// event's transaction), never from application code.
    /// </summary>
    public interface INotificationRepository
    {
        Task<IReadOnlyList<NotificationRow>> ListAsync(int userId, int limit, CancellationToken cancellationToken);

        Task<int> CountUnreadAsync(int userId, CancellationToken cancellationToken);

        /// <summary>Marks one read and returns its link; null when it is not this person's.</summary>
        Task<string> MarkReadAsync(long notificationId, int userId, CancellationToken cancellationToken);

        Task MarkAllReadAsync(int userId, CancellationToken cancellationToken);
    }

    public sealed class NotificationRepository : INotificationRepository
    {
        private readonly SqlDatabase _db;

        public NotificationRepository(SqlDatabase db)
        {
            _db = db;
        }

        public async Task<IReadOnlyList<NotificationRow>> ListAsync(
            int userId, int limit, CancellationToken cancellationToken)
        {
            return await _db.QueryAsync<NotificationRow>(
                "core.usp_ListNotifications",
                p => p.Value("UserId", userId).Value("Limit", limit),
                cancellationToken);
        }

        public async Task<int> CountUnreadAsync(int userId, CancellationToken cancellationToken)
        {
            var count = await _db.ScalarAsync<long>(
                "core.usp_CountUnreadNotifications", p => p.Value("UserId", userId), cancellationToken);
            return (int)count;
        }

        public Task<string> MarkReadAsync(long notificationId, int userId, CancellationToken cancellationToken)
        {
            return _db.ScalarAsync<string>(
                "core.usp_MarkNotificationRead",
                p => p.Value("NotificationId", notificationId).Value("UserId", userId),
                cancellationToken);
        }

        public Task MarkAllReadAsync(int userId, CancellationToken cancellationToken)
        {
            return _db.ExecuteAsync(
                "core.usp_MarkAllNotificationsRead", p => p.Value("UserId", userId), cancellationToken);
        }
    }

    /// <summary>What the email sender collects and records.</summary>
    public interface INotificationEmailRepository
    {
        Task<IReadOnlyList<PendingEmailRow>> ClaimUnsentAsync(CancellationToken cancellationToken);

        Task MarkEmailedAsync(long notificationId, CancellationToken cancellationToken);

        Task LogEmailAsync(EmailLogEntry entry, CancellationToken cancellationToken);
    }

    public sealed class NotificationEmailRepository : INotificationEmailRepository
    {
        private readonly SqlDatabase _db;

        public NotificationEmailRepository(SqlDatabase db)
        {
            _db = db;
        }

        public async Task<IReadOnlyList<PendingEmailRow>> ClaimUnsentAsync(CancellationToken cancellationToken)
        {
            return await _db.QueryAsync<PendingEmailRow>("core.usp_ClaimUnsentNotifications", null, cancellationToken);
        }

        public Task MarkEmailedAsync(long notificationId, CancellationToken cancellationToken)
        {
            return _db.ExecuteAsync(
                "core.usp_MarkNotificationEmailed", p => p.Value("NotificationId", notificationId), cancellationToken);
        }

        public Task LogEmailAsync(EmailLogEntry entry, CancellationToken cancellationToken)
        {
            return _db.ExecuteAsync("core.usp_LogEmail", p =>
            {
                p.Value("NotificationId", entry.NotificationId);
                p.Value("Event", entry.Event);
                p.Value("RecipientRole", entry.RecipientRole);
                p.Value("TourCode", entry.TourCode);
                p.Value("FromAddress", entry.FromAddress);
                p.Value("ToAddress", entry.ToAddress);
                p.Value("CcAddress", entry.CcAddress);
                p.Value("BccAddress", entry.BccAddress);
                p.Value("Subject", entry.Subject);
                p.Value("Body", entry.Body);
                p.Value("Status", entry.Status);
                p.Value("ErrorMessage", entry.ErrorMessage);
            }, cancellationToken);
        }
    }
}
