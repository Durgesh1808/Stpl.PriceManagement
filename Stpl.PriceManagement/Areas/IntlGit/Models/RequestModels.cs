using System;
using System.Collections.Generic;

namespace Stpl.PriceManagement.Areas.IntlGit.Models
{
    public sealed class SaveCostBuildRequest
    {
        /// <summary>Null when nobody has entered one. No FX rate, no price.</summary>
        public decimal? FxRate { get; set; }

        /// <summary>Null when nobody has set one. Then no struck-through price is shown.</summary>
        public decimal? StrikePercent { get; set; }
        public int PaxSlab { get; set; }

        /// <summary>
        /// Null when nobody has entered one yet. Not the same as zero, which is
        /// a real answer for a tour that carries no shared cost.
        /// </summary>
        public decimal? SharedCost { get; set; }
        public string Note { get; set; }
        public List<OccupancyCostRequest> Occupancies { get; set; }

        /// <summary>The tour as the page saw it. Null skips the check.</summary>
        public DateTime? ExpectedModifiedUtc { get; set; }
    }

    public sealed class OccupancyCostRequest
    {
        public string Occupancy { get; set; }

        /// <summary>
        /// Null when nobody has entered this cost. A blank box must arrive here
        /// as absent rather than as nought - a nought prices the tour.
        /// </summary>
        public decimal? LandCostFx { get; set; }

        /// <summary>Null when nobody has entered this cost. See LandCostFx.</summary>
        public decimal? PerPersonInr { get; set; }
    }

    public sealed class SavePricesRequest
    {
        public List<PriceRequest> Prices { get; set; }

        /// <summary>The tour as the page saw it. Null skips the check.</summary>
        public DateTime? ExpectedModifiedUtc { get; set; }
    }

    public sealed class PriceRequest
    {
        public string Hub { get; set; }
        public DateTime Date { get; set; }
        public string Occupancy { get; set; }

        /// <summary>
        /// Null means take the price back: the typed figure goes and so does
        /// the agreement, leaving the cell with no agreed price.
        /// </summary>
        /// <remarks>
        /// Not the same as never sending the cell at all. A cell absent from
        /// the request is one nobody touched; a cell present with no price is
        /// one somebody emptied on purpose. The departure is then held out of
        /// the next change request until it is priced again, exactly as a cell
        /// that has never been priced behaves.
        /// </remarks>
        public decimal? Price { get; set; }
    }

    public sealed class SaveFaresRequest
    {
        public List<FareRequest> Fares { get; set; }

        /// <summary>
        /// The tour as the page saw it when it was drawn. The save is refused
        /// if the tour has moved since, so a screen left open for ten minutes
        /// cannot overwrite what somebody else did in the meantime.
        /// Null skips the check.
        /// </summary>
        public DateTime? ExpectedModifiedUtc { get; set; }
    }

    public sealed class FareRequest
    {
        public string Hub { get; set; }
        public DateTime Date { get; set; }

        /// <summary>adult, child or infant.</summary>
        public string Band { get; set; }

        /// <summary>
        /// Null means take the fare back. The departure returns to "awaiting
        /// airfare" and cannot be priced until somebody quotes one — the same
        /// state as a fare nobody has entered, because it is the same state.
        /// </summary>
        public decimal? Amount { get; set; }
    }

    /// <summary>
    /// Air-ticketing's notes about the flights, per departure.
    /// </summary>
    /// <remarks>
    /// Its own request rather than part of SaveFaresRequest: a note can need
    /// correcting on a date whose fares have not moved, and folding the two
    /// together would mean re-saving a fare to fix a typo.
    /// </remarks>
    public sealed class SaveFlightDetailsRequest
    {
        public List<FlightDetailRequest> Details { get; set; }
    }

    public sealed class FlightDetailRequest
    {
        public string Hub { get; set; }
        public DateTime Date { get; set; }

        /// <summary>Free text. Empty or null clears it.</summary>
        public string Details { get; set; }
    }

    /// <summary>
    /// Asks what a proposed fare would do to the prices, without saving it.
    /// Answered by the same SQL function that produces the real prices, so the
    /// preview cannot disagree with what saving would give.
    /// </summary>
    public sealed class PreviewPricesRequest
    {
        public List<FareRequest> Fares { get; set; }
    }

    /// <summary>Air-ticketing hands its fares over, with a note.</summary>
    public sealed class SubmitFaresRequest
    {
        public string Note { get; set; }
    }

    /// <summary>The product team asks air-ticketing for fares, with a note.</summary>
    public sealed class RequestFaresRequest
    {
        public string Note { get; set; }
    }

    public sealed class AddHubRequest
    {
        public string Hub { get; set; }

        /// <summary>Null takes the hub master default.</summary>
        public decimal? MarkupPercent { get; set; }

        /// <summary>
        /// True carries the dates this tour is already selling — live on
        /// another hub and not yet gone. False creates the hub with none, for
        /// a hub added mid-season that runs its own.
        /// </summary>
        /// <remarks>
        /// Defaults true at every layer down to the stored procedure, so a
        /// caller that does not know about the choice behaves as before.
        /// </remarks>
        public bool CopyDepartureDates { get; set; } = true;
    }

    public sealed class UpdateHubRequest
    {
        /// <summary>Null leaves the markup unchanged.</summary>
        public decimal? MarkupPercent { get; set; }

        /// <summary>Null leaves it as it is; false withdraws the hub.</summary>
        public bool? IsActive { get; set; }

    }

    public sealed class AddDepartureRequest
    {
        public DateTime Date { get; set; }
    }

    /// <summary>One price cell, addressed the way the grid names it.</summary>
    public sealed class PriceCellRequest
    {
        public string Hub { get; set; }
        public DateTime Date { get; set; }
        public string Occupancy { get; set; }
    }

    /// <summary>
    /// Records that somebody has looked at these prices and agreed to them.
    /// </summary>
    /// <remarks>
    /// Two shapes in one request because they are one decision at different
    /// scopes. Listing cells confirms exactly those. Leaving the list empty
    /// confirms everything in scope: a hub, one departure on it, or - with both
    /// left out - the whole tour.
    /// </remarks>
    public sealed class ConfirmPricesRequest
    {
        public string Hub { get; set; }
        public DateTime? Date { get; set; }
        public List<PriceCellRequest> Cells { get; set; }
    }

    /// <summary>
    /// Sends one fare back to air-ticketing with a reason.
    /// </summary>
    /// <remarks>
    /// The fare itself is not touched. What this does is hold the departure out
    /// of the change set until air-ticketing answers - and because a change-set
    /// row IS a departure, querying one band holds all six of that date's
    /// prices. There is no half a departure to send.
    /// </remarks>
    public sealed class RaiseFareQueryRequest
    {
        public string Hub { get; set; }
        public DateTime Date { get; set; }

        /// <summary>adult, child or infant. Null queries the whole departure.</summary>
        public string Band { get; set; }

        public string Reason { get; set; }
    }

    /// <summary>
    /// Air-ticketing standing by the figure they gave, which releases the
    /// departure without changing it.
    /// </summary>
    public sealed class ResolveFareQueryRequest
    {
        public int Id { get; set; }
        public string Note { get; set; }
    }

    public sealed class SetConditionsRequest
    {
        public List<string> Hubs { get; set; }
        public List<PriceCellRequest> Cells { get; set; }

        /// <summary>
        /// The tour as the page saw it. This one matters most: a save whose
        /// browser was drawn before somebody else added a star does not carry
        /// that star, and the scope rule would otherwise delete it.
        /// </summary>
        public DateTime? ExpectedModifiedUtc { get; set; }
    }
}
