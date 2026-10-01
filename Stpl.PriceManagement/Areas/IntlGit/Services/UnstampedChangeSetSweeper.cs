using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Stpl.PriceManagement.Areas.IntlGit.Services
{
    /// <summary>
    /// Finishes any change set whose rows are all ticked but which was never
    /// stamped applied (and so whose tour was never promoted).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ticking the last row applies the set in the same request. If that
    /// request dies in between - the connection drops, the app pool recycles -
    /// or the promotion inside usp_MarkChangeSetApplied fails, the rows stay
    /// ticked and nothing is stamped. Something that must eventually happen
    /// needs a loop that will eventually do it, so this runs every few seconds.
    /// </para>
    /// <para>
    /// It replaces the ChangeSets service's OutboxDispatcher, which did this
    /// sweep and then delivered the promotion to the Pricing service over HTTP.
    /// The promotion now happens inside the stamp, so only the sweep is left.
    /// Safe to run as often as it likes: the stamp returns early on a set
    /// already applied.
    /// </para>
    /// </remarks>
    public sealed class UnstampedChangeSetSweeper : BackgroundService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<UnstampedChangeSetSweeper> _logger;

        public UnstampedChangeSetSweeper(IServiceScopeFactory scopes, ILogger<UnstampedChangeSetSweeper> logger)
        {
            _scopes = scopes;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using (var scope = _scopes.CreateScope())
                    {
                        var sets = scope.ServiceProvider.GetRequiredService<ChangeSetService>();
                        var finished = await sets.FinishUnstampedSetsAsync(stoppingToken);

                        if (finished > 0)
                        {
                            _logger.LogWarning(
                                "Finished {Count} change set(s) that were fully ticked but never stamped "
                                + "applied. Something interrupted the request that should have done it.",
                                finished);
                        }
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Never let the loop die: a failure here would strand every
                    // future set silently.
                    _logger.LogError(ex, "Sweep for unstamped change sets failed; will try again.");
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
    }
}
