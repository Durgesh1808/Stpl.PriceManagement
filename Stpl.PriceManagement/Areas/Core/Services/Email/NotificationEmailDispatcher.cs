using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Stpl.PriceManagement.Areas.Core.Models;
using Stpl.PriceManagement.Areas.Core.Repositories;

namespace Stpl.PriceManagement.Areas.Core.Services.Email
{
    /// <summary>
    /// Hands recent notifications to whatever sender is registered.
    /// </summary>
    /// <remarks>
    /// The same shape as the outbox the ChangeSets service already runs, and
    /// for the same reason: a notification is written inside the transaction of
    /// the thing that caused it, and sending an email cannot be. So the rows
    /// sit in the table and this collects them afterwards.
    ///
    /// WITH NO SENDER REGISTERED IT DOES NOTHING AT ALL.
    ///
    /// Not "polls and finds nothing" - it stops before its first pass, says so
    /// once, and never touches the database again. That is the difference
    /// between an integration point that is plainly absent and one that
    /// half-works, and the second is the kind somebody discovers by not getting
    /// an email they were counting on.
    ///
    /// A failure to send is logged and the row is left unmarked, so the next
    /// pass tries again until it falls outside the ten-minute window in
    /// usp_ClaimUnsentNotifications. Nothing is retried forever, and nothing
    /// throws out of here - an email that will not send must not be able to
    /// take the application down with it.
    /// </remarks>
    public sealed class NotificationEmailDispatcher : BackgroundService
    {
        // Five seconds, so the email lands at practically the same moment as the
        // bell. The notification itself is written inside the transaction of
        // the event; SMTP cannot be, so this is as close as "same time" gets.
        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<NotificationEmailDispatcher> _logger;

        public NotificationEmailDispatcher(
            IServiceScopeFactory scopes, ILogger<NotificationEmailDispatcher> logger)
        {
            _scopes = scopes;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using (var scope = _scopes.CreateScope())
            {
                var sender = scope.ServiceProvider.GetRequiredService<INotificationEmailSender>();

                if (!sender.IsEnabled)
                {
                    /*
                        Information, not Warning. Running without an email
                        sender is the expected state of this application today,
                        not a fault - notifications are delivered in-app and
                        that half is complete. This line exists so nobody has
                        to read code to find out why no email arrived.
                    */
                    _logger.LogInformation(
                        "No email sender is registered, so notifications are in-app only. "
                        + "To send email, set MailSettings:Enabled to true in appsettings.json.");
                    return;
                }
            }

            _logger.LogInformation("Notification email dispatcher started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await DispatchBatchAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Never let a bad batch stop the loop. The next pass finds
                    // the same rows, because nothing was marked sent.
                    _logger.LogError(ex, "A notification email batch failed.");
                }

                try
                {
                    await Task.Delay(Interval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task DispatchBatchAsync(CancellationToken cancellationToken)
        {
            using (var scope = _scopes.CreateScope())
            {
                var repository = scope.ServiceProvider
                    .GetRequiredService<INotificationEmailRepository>();
                var sender = scope.ServiceProvider
                    .GetRequiredService<INotificationEmailSender>();

                var pending = await repository.ClaimUnsentAsync(cancellationToken);

                /*
                    One email per ROLE per event, not per person. Mail goes to
                    the role's mailbox (MailSettings:RoleRecipients), and a role
                    with two accounts would otherwise send that mailbox the same
                    email twice. Every notification in the group is marked
                    emailed once the one email has gone.
                */
                var groups = pending
                    .GroupBy(r => (r.Role ?? string.Empty) + "\u0001" + r.Event + "\u0001"
                                  + r.Subject + "\u0001" + r.Body + "\u0001" + r.Link)
                    .ToList();

                foreach (var group in groups)
                {
                    var row = group.First();

                    try
                    {
                        await sender.SendAsync(
                            new NotificationEmail
                            {
                                ToAddress = row.ToAddress,
                                ToName = row.ToName,
                                Subject = row.Subject,
                                Body = row.Body,
                                Link = row.Link,
                                NotificationId = row.NotificationId,
                                Role = row.Role,
                                Event = row.Event,
                                TourCode = row.TourCode
                            },
                            cancellationToken);

                        // Only after the sender returns without throwing. A row
                        // marked sent for an email that failed is one nobody
                        // will ever know was missed.
                        foreach (var sibling in group)
                        {
                            await repository.MarkEmailedAsync(sibling.NotificationId, cancellationToken);
                        }
                    }
                    catch (Exception ex)
                    {
                        // The recipient, not the body: who was not reached is
                        // worth knowing, and the body can carry a fare.
                        _logger.LogWarning(
                            ex,
                            "Could not email notification {NotificationId} to {Recipient}.",
                            row.NotificationId,
                            row.ToAddress);
                    }
                }
            }
        }
    }
}
