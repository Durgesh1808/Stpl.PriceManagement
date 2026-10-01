using Stpl.PriceManagement.Areas.IntlGit.Domain;

namespace Stpl.PriceManagement.Areas.IntlGit.ViewModels
{
    /// <summary>
    /// Which chip class a status is drawn with.
    /// </summary>
    /// <remarks>
    /// This is the presentation half of what RevisionStatuses.Style and
    /// ChangeSetStates.Style used to do. It lives here, in the web app,
    /// because a CSS class name is not something a domain assembly should
    /// know - and the hex codes those Style methods return are a layering
    /// mistake this deliberately does not repeat.
    ///
    /// The classes resolve to exactly the colours Style() returns today:
    /// chip--warn is --warn-*, chip--info is --info-*, chip--alert is
    /// --alert-*, chip--ok is --ok-*. The swap is pixel-identical, which is
    /// what makes it safe to do in one pass across four screens.
    ///
    /// Style() itself is left alone. Nothing calls it now, but it is public
    /// API of two shared assemblies and removing it is a separate decision.
    /// </remarks>
    public static class StatusChips
    {
        public static string For(RevisionStatus status)
        {
            switch (status)
            {
                case RevisionStatus.DraftOpen:
                    return "chip--warn";
                case RevisionStatus.FaresReceived:
                    return "chip--info";
                case RevisionStatus.NotStarted:
                    return "chip--alert";
                case RevisionStatus.InProgress:
                    return "chip--info";
                case RevisionStatus.UpdatedOnSite:
                    return "chip--ok";

                /* The only chip drawn in danger red, and the only status that
                   means work is stuck with nobody acting. Not chip--alert:
                   that is "Not started", and two statuses sharing a colour in
                   one list is the confusion the status map calls out -
                   docs/status-map.md. */
                case RevisionStatus.Held:
                    return "chip--danger";

                case RevisionStatus.WithAirTicketing:
                    return "chip--purple";

                default:
                    return "chip--ok";
            }
        }

        public static string For(ChangeSetState state)
        {
            switch (state)
            {
                case ChangeSetState.InProgress:
                    return "chip--info";
                case ChangeSetState.Complete:
                    return "chip--ok";
                case ChangeSetState.Applied:
                    return "chip--ok";
                default:
                    return "chip--alert";
            }
        }
    }
}
