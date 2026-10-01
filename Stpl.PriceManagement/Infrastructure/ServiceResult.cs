namespace Stpl.PriceManagement.Infrastructure
{
    /// <summary>Outcome of a write, with a message fit to show the person.</summary>
    public sealed class ServiceResult
    {
        /// <summary>Shown when something unexpected failed. The detail goes to the log only.</summary>
        public const string UnexpectedMessage =
            "Something went wrong handling that request. Quote the trace id if you report it.";

        /// <summary>Shown when the role may not do this.</summary>
        public const string ForbiddenMessage = "Your role does not have access to this.";

        private ServiceResult(bool succeeded, string message)
        {
            Succeeded = succeeded;
            Message = message;
        }

        public bool Succeeded { get; }
        public string Message { get; }

        /// <summary>
        /// The tour moved while this page was open. Distinct from every other
        /// failure because the page answers it by showing what changed and
        /// keeping what was typed, not by showing a message and starting over.
        /// </summary>
        public bool IsStale { get; private set; }

        public static ServiceResult Ok()
        {
            return new ServiceResult(true, null);
        }

        public static ServiceResult Stale(string message)
        {
            return new ServiceResult(false, message) { IsStale = true };
        }

        public static ServiceResult Failed(string message)
        {
            return new ServiceResult(false, message);
        }
    }
}
