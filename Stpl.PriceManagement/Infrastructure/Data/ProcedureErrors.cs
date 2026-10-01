using Microsoft.Data.SqlClient;

namespace Stpl.PriceManagement.Infrastructure.Data
{
    /// <summary>
    /// What a stored procedure meant when it raised an error.
    /// </summary>
    /// <remarks>
    /// The procedures THROW numbered errors for conditions the person caused -
    /// an unknown tour, a duplicate hub, a stale page - and the message they
    /// carry is written for a person, so it is safe to show. Anything else is
    /// a fault: it is logged and the person sees a generic sentence.
    ///
    ///   50001-50099  International GIT pricing (tours, fares, prices)
    ///   60001-60099  International GIT change sets
    ///
    /// A new pricing area picks its own block (70001-70099, ...) and adds it to
    /// <see cref="IsBusinessRule"/>.
    /// </remarks>
    public static class ProcedureErrors
    {
        /// <summary>"Somebody else changed this tour while your page was open."</summary>
        public const int StaleWrite = 50030;

        public static bool IsBusinessRule(SqlException ex)
        {
            return ex != null
                && ((ex.Number >= 50001 && ex.Number <= 50099)
                    || (ex.Number >= 60001 && ex.Number <= 60099));
        }

        /// <summary>The errors that mean "no such thing" (a 404 in the old API).</summary>
        public static bool IsNotFound(SqlException ex)
        {
            if (ex == null)
            {
                return false;
            }

            switch (ex.Number)
            {
                case 50001:
                case 50002:
                case 50003:
                case 50006:
                case 60003:
                case 60005:
                    return true;
                default:
                    return false;
            }
        }

        public static bool IsStaleWrite(SqlException ex)
        {
            return ex != null && ex.Number == StaleWrite;
        }
    }
}
