using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Stpl.PriceManagement.Infrastructure.Data
{
    /// <summary>
    /// /health/ready: can the database be reached? Opening a connection is the
    /// whole test - no SQL text is sent.
    /// </summary>
    public sealed class DatabaseHealthCheck : IHealthCheck
    {
        private readonly SqlDatabase _db;

        public DatabaseHealthCheck(SqlDatabase db)
        {
            _db = db;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context, CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                using (var connection = _db.CreateConnection())
                {
                    await connection.OpenAsync(cancellationToken);
                }

                return HealthCheckResult.Healthy();
            }
            catch (Exception ex)
            {
                // The detail stays in the log; the probe says only that it failed.
                return HealthCheckResult.Unhealthy("The database could not be reached.", ex);
            }
        }
    }
}
