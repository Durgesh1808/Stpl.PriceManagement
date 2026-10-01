using Stpl.PriceManagement.Areas.Core.Domain;

namespace Stpl.PriceManagement.Areas.IntlGit.Domain
{
    /// <summary>
    /// What the current role may change in the price revision workspace.
    /// </summary>
    /// <remarks>
    /// The split that matters: air-ticketing enters airfare and nothing else.
    /// They do not see or set the cost build, the markup, or the published
    /// selling price, because those carry the margin. Tech support does not edit
    /// the workspace at all - they work from the change set.
    ///
    /// This type is the single statement of that rule. The API authorises
    /// against it and the UI hides against it, so the two cannot drift apart.
    /// </remarks>
    public sealed class RevisionPermissions
    {
        private RevisionPermissions(UserRole role, bool isEditing)
        {
            Role = role;
            IsEditing = isEditing;
        }

        public static RevisionPermissions For(UserRole role, bool isEditingRequested)
        {
            // Tech support has no edit mode, whatever the UI last remembered.
            var editing = role != UserRole.TechSupport && isEditingRequested;
            return new RevisionPermissions(role, editing);
        }

        /// <summary>Permissions for a role that is only looking.</summary>
        public static RevisionPermissions ReadOnly(UserRole role)
        {
            return new RevisionPermissions(role, false);
        }

        public UserRole Role { get; }

        public bool IsEditing { get; }

        public bool IsReadOnly
        {
            get { return !IsEditing; }
        }

        private bool IsAirTicketing
        {
            get { return Role == UserRole.AirTicketingExecutive; }
        }

        /// <summary>FX rate, strike %, pax slab, shared cost, land and per-person costs.</summary>
        public bool CanEditCostBuild
        {
            get { return IsEditing && !IsAirTicketing; }
        }

        /// <summary>Airfare per hub and departure. Both product and air-ticketing.</summary>
        public bool CanEditFares
        {
            get { return IsEditing; }
        }

        /// <summary>The published selling price, overriding the calculated one.</summary>
        public bool CanEditPublishedPrice
        {
            get { return IsEditing && !IsAirTicketing; }
        }

        /// <summary>
        /// Adding or withdrawing hubs and departure dates, and hub markup.
        /// </summary>
        /// <remarks>
        /// Product team only, by decision. Air-ticketing enters airfare and
        /// nothing else: which hubs a tour sells from, on which dates, and at
        /// what markup are commercial decisions rather than airfare ones.
        /// </remarks>
        public bool CanChangeStructure
        {
            get { return IsEditing && !IsAirTicketing; }
        }

        /// <summary>
        /// Whether this role works in cost and margin. The reason airfare is
        /// destined to become a separate service.
        /// </summary>
        /// <remarks>
        /// Not a blindfold, by decision in round G. Air-ticketing can read the
        /// cost build and the prices on the revision screen and the service
        /// still sends them - they work from those figures. What this gates is
        /// the acts: the margin preview, and through the sibling properties
        /// every write that moves a cost, a markup or a price.
        /// </remarks>
        public bool CanSeeCostAndMargin
        {
            get { return Role == UserRole.ProductExecutive; }
        }

        /// <summary>Only the product team submits a change set to tech support.</summary>
        public bool CanSubmitChangeSet
        {
            get { return Role == UserRole.ProductExecutive; }
        }

        /*
            The four acts of the fare hand-over.

            Not edits, so not gated on IsEditing - they are things a role either
            performs or does not, like submitting a change set. Asking for fares
            is not a change to the tour; it is a message to another team.

            These existed only as Forbid() calls on the four page handlers until
            23 September 2026. The services took them from anybody: three of the
            endpoints had no role check at all, and fares/submit had the wrong
            one - EditFares, which both the product team and air-ticketing hold.
            So the rule was real on the screens and absent behind them, which is
            the same gap docs/architecture.md wrongly claimed was closed.
        */

        /// <summary>Asking air-ticketing for fares on this tour.</summary>
        public bool CanRequestFares
        {
            get { return Role == UserRole.ProductExecutive; }
        }

        /// <summary>
        /// Sending a fare back for a re-quote, which holds its departure out of
        /// the change set.
        /// </summary>
        /// <remarks>
        /// The product team's act: it is a commercial objection to a figure,
        /// not a correction of one. Air-ticketing answers it, and answering is
        /// <see cref="CanResolveFareQuery"/>.
        /// </remarks>
        public bool CanRaiseFareQuery
        {
            get { return Role == UserRole.ProductExecutive; }
        }

        /// <summary>
        /// Standing by a queried fare, releasing the departure without changing
        /// the figure.
        /// </summary>
        /// <remarks>
        /// Air-ticketing's alone, and the one place the pair is asymmetric: the
        /// team that raised the objection must not be the team that dismisses
        /// it.
        /// </remarks>
        public bool CanResolveFareQuery
        {
            get { return Role == UserRole.AirTicketingExecutive; }
        }

        /// <summary>Handing a batch of fares over to the product team.</summary>
        /// <remarks>
        /// Air-ticketing's, not merely "anybody who may edit a fare". The
        /// product team can correct a fare - <see cref="CanEditFares"/> - but
        /// the hand-over is a message from one team to the other, and it
        /// notifies the product team that fares have arrived. Submitting your
        /// own would notify you about yourself.
        /// </remarks>
        public bool CanSubmitFares
        {
            get { return Role == UserRole.AirTicketingExecutive; }
        }
    }
}
