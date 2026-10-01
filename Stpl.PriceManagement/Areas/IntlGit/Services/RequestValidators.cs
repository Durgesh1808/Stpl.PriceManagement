using System;
using System.Collections.Generic;
using Stpl.PriceManagement.Areas.IntlGit.Domain;
using Stpl.PriceManagement.Areas.IntlGit.Models;
using Stpl.PriceManagement.Infrastructure.Validation;

namespace Stpl.PriceManagement.Areas.IntlGit.Services
{
    /*
        What the services will accept.

        The same rules - and the same sentences - the old Pricing and ChangeSets
        APIs applied with FluentValidation. Bounds mirror the database's CHECK
        constraints, so a bad figure is answered with a sentence a person can
        act on rather than a constraint violation from SQL Server.
    */
    public static class RequestValidators
    {
        private const string NoteTooLong = "That note is too long — keep it under 1,000 characters.";
        private static readonly string[] Bands = { "adult", "child", "infant" };

        public static ValidationErrors Validate(SaveCostBuildRequest x)
        {
            var e = new ValidationErrors();

            /* Absent is allowed - a tour nobody has set an FX rate on simply has
               no price yet. A figure that IS there still has to be a sane one. */
            if (x.FxRate.HasValue)
            {
                if (!(x.FxRate.Value > 0)) e.Add("FxRate", "The FX rate must be more than zero.");
                if (!(x.FxRate.Value < 1000)) e.Add("FxRate", "That FX rate looks wrong — check the figure.");
            }

            if (x.StrikePercent.HasValue && (x.StrikePercent.Value < 0 || x.StrikePercent.Value > 99.99m))
                e.Add("StrikePercent", "The strike-through percentage must be between 0 and 99.99.");

            if (!(x.PaxSlab > 0))
                e.Add("PaxSlab", "The launch pax slab must be at least 1.");

            // Absent is allowed; negative is not.
            if (x.SharedCost.HasValue && x.SharedCost.Value < 0)
                e.Add("SharedCost", "The shared cost cannot be negative.");

            e.MaxLength("Note", x.Note, 1000, "That note is too long — keep it under 1,000 characters.");

            // Every occupancy must still be PRESENT - the row is what carries "this one is blank".
            e.NotEmpty("Occupancies", x.Occupancies, "Costs are required for every occupancy.");

            Each(x.Occupancies, "Occupancies", (o, path) =>
            {
                e.NotEmpty(path + ".Occupancy", o.Occupancy);
                if (o.LandCostFx.HasValue && o.LandCostFx.Value < 0)
                    e.Add(path + ".LandCostFx", "A land cost cannot be negative.");
                if (o.PerPersonInr.HasValue && o.PerPersonInr.Value < 0)
                    e.Add(path + ".PerPersonInr", "A per-person cost cannot be negative.");
            });

            return e;
        }

        public static ValidationErrors Validate(SavePricesRequest x)
        {
            var e = new ValidationErrors();
            e.NotEmpty("Prices", x.Prices);

            Each(x.Prices, "Prices", (p, path) =>
            {
                e.NotEmpty(path + ".Hub", p.Hub);
                e.NotEmpty(path + ".Occupancy", p.Occupancy);
                e.NotEmpty(path + ".Date", p.Date);

                /* Absent is an instruction - take this price back. A figure that
                   IS given must be a real price: a nought travels to tech support
                   as a figure to publish, and nobody sells a tour for nothing. */
                if (p.Price.HasValue && !(p.Price.Value > 0))
                    e.Add(path + ".Price", "A price must be more than zero.");
            });

            return e;
        }

        public static ValidationErrors Validate(SaveFaresRequest x)
        {
            var e = new ValidationErrors();
            e.NotEmpty("Fares", x.Fares);

            Each(x.Fares, "Fares", (f, path) =>
            {
                e.NotEmpty(path + ".Hub", f.Hub);
                e.NotEmpty(path + ".Date", f.Date);

                if (!(f.Band != null && Array.IndexOf(Bands, f.Band.ToLowerInvariant()) >= 0))
                    e.Add(path + ".Band", "A fare band must be adult, child or infant.");

                /* Absent takes the fare back. Zero is a real quote (an infant
                   carried free); negative is not. */
                if (f.Amount.HasValue && f.Amount.Value < 0)
                    e.Add(path + ".Amount", "A fare cannot be negative.");
            });

            return e;
        }

        public static ValidationErrors Validate(SaveFlightDetailsRequest x)
        {
            var e = new ValidationErrors();
            e.NotEmpty("Details", x.Details);

            Each(x.Details, "Details", (d, path) =>
            {
                e.NotEmpty(path + ".Hub", d.Hub);
                e.NotEmpty(path + ".Date", d.Date);
                e.MaxLength(path + ".Details", d.Details, 500, "Flight details are limited to 500 characters.");
            });

            return e;
        }

        public static ValidationErrors Validate(AddHubRequest x)
        {
            var e = new ValidationErrors();
            e.NotEmpty("Hub", x.Hub, "Choose a hub.");

            if (x.MarkupPercent.HasValue && (x.MarkupPercent.Value < 0 || x.MarkupPercent.Value > 99.99m))
                e.Add("MarkupPercent", "The markup must be between 0 and 99.99 percent.");

            return e;
        }

        public static ValidationErrors Validate(UpdateHubRequest x)
        {
            var e = new ValidationErrors();

            if (x.MarkupPercent.HasValue && (x.MarkupPercent.Value < 0 || x.MarkupPercent.Value > 99.99m))
                e.Add("MarkupPercent", "The markup must be between 0 and 99.99 percent.");

            if (!(x.MarkupPercent.HasValue || x.IsActive.HasValue))
                e.Add("", "Nothing to change — supply a markup or an active flag.");

            return e;
        }

        public static ValidationErrors Validate(AddDepartureRequest x)
        {
            var e = new ValidationErrors();
            e.NotEmpty("Date", x.Date, "Enter a departure date with the year — 08 Dec 26 or 08 Dec 2026.");
            return e;
        }

        public static ValidationErrors Validate(SubmitFaresRequest x)
        {
            var e = new ValidationErrors();
            e.MaxLength("Note", x.Note, 1000, NoteTooLong);
            return e;
        }

        public static ValidationErrors Validate(RequestFaresRequest x)
        {
            var e = new ValidationErrors();
            e.MaxLength("Note", x.Note, 1000, NoteTooLong);
            return e;
        }

        public static ValidationErrors Validate(RaiseFareQueryRequest x)
        {
            var e = new ValidationErrors();
            e.NotEmpty("Hub", x.Hub);
            e.NotEmpty("Date", x.Date);

            /* The reason IS required: sending a fare back holds a whole
               departure, and air-ticketing cannot act on "please look again". */
            e.NotEmpty("Reason", x.Reason, "Say what looks wrong, so air-ticketing can check it.");
            e.MaxLength("Reason", x.Reason, 1000, "That reason is too long — keep it under 1,000 characters.");
            return e;
        }

        public static ValidationErrors Validate(ResolveFareQueryRequest x)
        {
            var e = new ValidationErrors();
            e.MaxLength("Note", x.Note, 1000, NoteTooLong);
            return e;
        }

        /// <summary>
        /// A change set is immutable once created, so everything that can be
        /// checked is checked here - there is no correcting it afterwards.
        /// </summary>
        public static ValidationErrors Validate(CreateChangeSetRequest x)
        {
            var e = new ValidationErrors();
            e.NotEmpty("TourCode", x.TourCode); e.MaxLength("TourCode", x.TourCode, 10);
            e.NotEmpty("TourName", x.TourName); e.MaxLength("TourName", x.TourName, 200);
            e.NotEmpty("Region", x.Region); e.MaxLength("Region", x.Region, 60);
            e.NotEmpty("VersionBefore", x.VersionBefore); e.MaxLength("VersionBefore", x.VersionBefore, 10);
            e.NotEmpty("SubmittedBy", x.SubmittedBy); e.MaxLength("SubmittedBy", x.SubmittedBy, 100);
            e.MaxLength("Note", x.Note, 1000);

            e.NotEmpty("Rows", x.Rows, "A change set must contain at least one row.");

            Each(x.Rows, "Rows", (r, path) =>
            {
                e.NotEmpty(path + ".RowKey", r.RowKey); e.MaxLength(path + ".RowKey", r.RowKey, 80);
                e.NotEmpty(path + ".Hub", r.Hub); e.MaxLength(path + ".Hub", r.Hub, 10);
                e.NotEmpty(path + ".HubName", r.HubName); e.MaxLength(path + ".HubName", r.HubName, 60);

                if (!(r.Kind == RowKeys.PriceKind || r.Kind == RowKeys.RemovalKind))
                    e.Add(path + ".Kind", "A row must be 'price' or 'removal'.");

                // A price row always names a departure; the cells belong to it.
                if (r.Kind == RowKeys.PriceKind && r.Date == null)
                    e.Add(path + ".Date", "A price row must name a departure date.");

                if (r.Kind == RowKeys.PriceKind && (r.Cells == null || r.Cells.Count == 0))
                    e.Add(path + ".Cells", "A price row must carry its prices.");

                Each(r.Cells, path + ".Cells", (c, cellPath) =>
                {
                    e.NotEmpty(cellPath + ".Occupancy", c.Occupancy); e.MaxLength(cellPath + ".Occupancy", c.Occupancy, 10);
                    e.NotEmpty(cellPath + ".Label", c.Label); e.MaxLength(cellPath + ".Label", c.Label, 20);

                    /* A nought reaches a customer-facing page as a figure. */
                    if (!(c.Published > 0))
                        e.Add(cellPath + ".Published", "A published price must be more than zero.");

                    /* Null means there is no crossed-out price; a figure must be a real one. */
                    if (c.StrikeThrough.HasValue && !(c.StrikeThrough.Value > 0))
                        e.Add(cellPath + ".StrikeThrough", "A struck-through price must be more than zero.");
                });
            });

            // A duplicate key would silently collapse two rows of work into one.
            if (x.Rows != null)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var row in x.Rows)
                {
                    if (row.RowKey != null && !seen.Add(row.RowKey))
                    {
                        e.Add("Rows", "Two rows share the same key — each row must be uniquely identified.");
                        break;
                    }
                }
            }

            return e;
        }

        public static ValidationErrors Validate(CompleteRowRequest x)
        {
            var e = new ValidationErrors();
            e.MaxLength("CompletedBy", x.CompletedBy, 100);
            return e;
        }

        private static void Each<T>(IList<T> items, string path, Action<T, string> rule)
        {
            if (items == null)
            {
                return;
            }

            for (var i = 0; i < items.Count; i++)
            {
                if (items[i] != null)
                {
                    rule(items[i], path + "[" + i + "]");
                }
            }
        }
    }
}
