namespace Stpl.PriceManagement.Areas.Core.ViewModels
{
    /// <summary>The page somebody lands on when something has gone wrong.</summary>
    public sealed class ErrorViewModel
    {
        /// <summary>Shown so a report can be matched to a line in the log.</summary>
        public string RequestId { get; set; }

        /// <summary>The heading, in the user's language rather than the system's.</summary>
        public string Title { get; set; }

        /// <summary>What happened and what to do about it. Two sentences at most.</summary>
        public string Explanation { get; set; }

        /// <summary>True where waiting and trying again is the sensible response.</summary>
        public bool IsServiceDown { get; set; }

        /// <summary>Where "try again" points. Null when there is nowhere to send them.</summary>
        public string ReturnPath { get; set; }
    }
}
