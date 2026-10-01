/*
    AREA: IntlGit - table types, the pricing function and the progress view.

    intlgit.fn_PriceMatrix is THE price formula, defined once:
        cost   = LandCostFx x FxRate + PerPersonInr + SharedCost / PaxSlab
               + passenger airfare (only on hubs that carry it)
               + tour manager fare (adult band) / PaxSlab
        price  = ROUND(cost x (1 + MarkupPct/100), 0)          -- to the rupee
        strike = ROUND(COALESCE(published, calculated) x (1 + StrikePct/100), 0)
    Every screen, the preview, the change list and the products list read it.
*/
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/* =========================================================================
   TABLE TYPES (table-valued parameters)
   ========================================================================= */

-- ---------------------------------------------------------------------------
-- intlgit.ResolvedFareList
-- ---------------------------------------------------------------------------
IF TYPE_ID('intlgit.ResolvedFareList') IS NULL
CREATE TYPE intlgit.ResolvedFareList AS TABLE
(
    TourDepartureId INT            NOT NULL,
    FareBandId      TINYINT        NOT NULL,
    Amount          DECIMAL(12, 2) NULL,   -- NULL = not entered yet
    PreviousAmount  DECIMAL(12, 2) NULL,
    PRIMARY KEY (TourDepartureId, FareBandId)
);
GO

-- ---------------------------------------------------------------------------
-- intlgit.OccupancyCostList
-- ---------------------------------------------------------------------------
IF TYPE_ID('intlgit.OccupancyCostList') IS NULL
CREATE TYPE intlgit.OccupancyCostList AS TABLE
(
    OccupancyCode VARCHAR(10)    NOT NULL PRIMARY KEY,

    -- NULL means nobody has entered this cost. It is not zero, and the
    -- pricing function draws no price from it.
    LandCostFx    DECIMAL(12, 2) NULL,
    PerPersonInr  DECIMAL(12, 2) NULL
);
GO

-- ---------------------------------------------------------------------------
-- intlgit.PriceList
-- ---------------------------------------------------------------------------
IF TYPE_ID('intlgit.PriceList') IS NULL
    CREATE TYPE intlgit.PriceList AS TABLE
    (
        HubCode       VARCHAR(10)    NOT NULL,
        DepartureDate DATE           NOT NULL,
        OccupancyCode VARCHAR(10)    NOT NULL,
        Price         DECIMAL(12, 2) NOT NULL,
        PRIMARY KEY (HubCode, DepartureDate, OccupancyCode)
    );
GO

-- ---------------------------------------------------------------------------
-- intlgit.PriceCellList
-- ---------------------------------------------------------------------------
IF TYPE_ID('intlgit.PriceCellList') IS NULL
    CREATE TYPE intlgit.PriceCellList AS TABLE
    (
        HubCode       VARCHAR(10) NOT NULL,
        DepartureDate DATE        NOT NULL,
        OccupancyCode VARCHAR(10) NOT NULL,
        PRIMARY KEY (HubCode, DepartureDate, OccupancyCode)
    );
GO

-- ---------------------------------------------------------------------------
-- intlgit.HubCodeList
-- ---------------------------------------------------------------------------
IF TYPE_ID('intlgit.HubCodeList') IS NULL
    CREATE TYPE intlgit.HubCodeList AS TABLE
    (
        HubCode VARCHAR(10) NOT NULL PRIMARY KEY
    );
GO

-- ---------------------------------------------------------------------------
-- intlgit.FareEditList
-- ---------------------------------------------------------------------------
IF TYPE_ID('intlgit.FareEditList') IS NULL
CREATE TYPE intlgit.FareEditList AS TABLE
(
    HubCode       VARCHAR(10)    NOT NULL,
    DepartureDate DATE           NOT NULL,
    FareBandCode  VARCHAR(10)    NOT NULL,

    -- NULL means "take this fare back", not "leave it alone". A row is only
    -- here because somebody touched that box.
    Amount        DECIMAL(12, 2) NULL,

    PRIMARY KEY (HubCode, DepartureDate, FareBandCode)
);
GO

-- ---------------------------------------------------------------------------
-- intlgit.DepartureList
-- ---------------------------------------------------------------------------
IF TYPE_ID('intlgit.DepartureList') IS NULL
    CREATE TYPE intlgit.DepartureList AS TABLE
    (
        TourDepartureId INT NOT NULL PRIMARY KEY
    );
GO

-- ---------------------------------------------------------------------------
-- intlgit.FlightDetailList
-- ---------------------------------------------------------------------------
IF TYPE_ID('intlgit.FlightDetailList') IS NULL
CREATE TYPE intlgit.FlightDetailList AS TABLE
(
    HubCode       VARCHAR(10)    NOT NULL,
    DepartureDate DATE           NOT NULL,
    Details       NVARCHAR(1000) NULL
);
GO

-- ---------------------------------------------------------------------------
-- intlgit.ChangeRowList
-- ---------------------------------------------------------------------------
IF TYPE_ID('intlgit.ChangeRowList') IS NULL
CREATE TYPE intlgit.ChangeRowList AS TABLE
(
    RowKey            VARCHAR(80)    NOT NULL PRIMARY KEY,
    Kind              VARCHAR(10)    NOT NULL,
    HubCode           VARCHAR(10)    NOT NULL,
    HubName           NVARCHAR(60)   NOT NULL,
    DepartureDate     DATE           NULL,
    IsNewHub          BIT            NOT NULL,
    RemovalAction     NVARCHAR(40)   NULL,
    RemovalCount      INT            NULL,
    LivePriceAtSubmit DECIMAL(12, 2) NULL,
    SortOrder         INT            NOT NULL,
    HtmlBlock         NVARCHAR(MAX)  NULL
);
GO

-- ---------------------------------------------------------------------------
-- intlgit.ChangeCellList
-- ---------------------------------------------------------------------------
IF TYPE_ID('intlgit.ChangeCellList') IS NULL
CREATE TYPE intlgit.ChangeCellList AS TABLE
(
    RowKey         VARCHAR(80)    NOT NULL,
    OccupancyCode  VARCHAR(10)    NOT NULL,
    OccupancyLabel NVARCHAR(20)   NOT NULL,
    SortOrder      TINYINT        NOT NULL,
    PublishedPrice DECIMAL(12, 2) NOT NULL,

    -- NULL where the tour has no strike-through percentage set, and therefore
    -- no crossed-out price. Not zero, which would be a price.
    StrikeThrough  DECIMAL(12, 2) NULL,

    LivePrice      DECIMAL(12, 2) NULL,
    BaselinePrice  DECIMAL(12, 2) NULL,
    HasChanged     BIT            NOT NULL,
    HasConditions  BIT            NOT NULL,
    PRIMARY KEY (RowKey, OccupancyCode)
);
GO

/* =========================================================================
   THE PRICE FORMULA
   ========================================================================= */

-- ---------------------------------------------------------------------------
-- intlgit.fn_PriceMatrix
-- ---------------------------------------------------------------------------
CREATE OR ALTER FUNCTION intlgit.fn_PriceMatrix
(
    @TourId INT,
    @Fares  intlgit.ResolvedFareList READONLY
)
RETURNS TABLE
AS
RETURN
(
    SELECT
        th.TourId,
        th.TourHubId,
        h.Code                  AS HubCode,
        h.Name                  AS HubName,
        h.SortOrder             AS HubSortOrder,
        h.HasPassengerAirfare,
        th.MarkupPct,
        th.IsActive             AS HubIsActive,
        td.TourDepartureId,
        td.DepartureDate,
        td.IsActive             AS DepartureIsActive,
        o.OccupancyId,
        o.Code                  AS OccupancyCode,
        o.Label                 AS OccupancyLabel,
        o.SortOrder             AS OccupancySort,

        -- This occupancy's own fare, which is what the fare grid shows. The
        -- tour manager's share is not shown here: it is the same figure on
        -- every row of the departure and belongs to the price, not the band.
        f.Amount                AS Airfare,
        f.PreviousAmount        AS PreviousAirfare,

        /*
            Which fares have to be in before there is a price.

            The tour manager's, always - their seat is shared across every
            price on the departure. The passenger's own, only where they fly
            with us: on a Joining Direct hub the customer meets the tour at the
            destination, so there is no ticket of theirs to wait for.
        */
        CASE WHEN tmf.Amount IS NULL
                  OR (h.HasPassengerAirfare = 1 AND f.Amount IS NULL)
             THEN CAST(0 AS BIT) ELSE CAST(1 AS BIT) END AS HasFare,

        calc.CalculatedPrice,
        pub.PublishedPrice,

        /*
            The struck-through figure, from the published price where there is
            one and the CALCULATED price where there is not.

            It used to be published-only, so an empty box showed no MRP at all -
            a gap in the row with nothing to explain it. There is always a
            figure to strike through: while nobody has agreed a price, it is the
            one the system is offering. The screen marks that reading as
            provisional; the arithmetic does not need to know the difference,
            which is why this is a COALESCE and not a second expression.
        */
        ROUND(COALESCE(pub.PublishedPrice, calc.CalculatedPrice)
              * (1 + cb.StrikePct / 100.0), 0)
                                AS StrikeThrough,

        -- Somebody typed this figure AND it is the one being shown. A stale
        -- override behind a blanked cell is not an override anyone can see.
        CASE WHEN ppo.PublishedPrice IS NULL OR pub.PublishedPrice IS NULL
             THEN CAST(0 AS BIT) ELSE CAST(1 AS BIT) END AS IsOverridden,

        snap.Price              AS LivePrice,
        COALESCE(sub.Price, snap.Price) AS BaselinePrice,
        CASE WHEN sub.TourDepartureId IS NULL AND snap.TourDepartureId IS NULL
             THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END
                                AS IsNewToSite,

        -- Conditions apply. A marker, no text - the wording lives on the site.
        CASE WHEN pc.TourDepartureId IS NULL THEN CAST(0 AS BIT) ELSE CAST(1 AS BIT) END
                                AS HasCondition,

        -- Somebody has agreed to this figure since the last time it moved.
        CASE WHEN conf.TourDepartureId IS NULL THEN CAST(0 AS BIT) ELSE CAST(1 AS BIT) END
                                AS IsConfirmed,
        conf.ConfirmedBy,
        conf.ConfirmedUtc,

        -- This departure is with air-ticketing and cannot be sent.
        CASE WHEN q.TourDepartureId IS NULL THEN CAST(0 AS BIT) ELSE CAST(1 AS BIT) END
                                AS IsQueried,

        /*
            Where this figure stands, in one column, computed once.

              nofare   no airfare yet - this occupancy's, or the manager's
              noprice  there is a fare, but no figure anybody has agreed to
              new      never been on the website
              draft    changed here, not yet sent to tech support
              sent     with tech support, not yet live
              live     what the website shows

            Order matters. "noprice" is tested ahead of "new" because a new
            hub's cell is both, and having no price is the one that decides
            what happens to it. Left further down, a blank cell would fall all
            the way through to 'live': NULL <> BaselinePrice is UNKNOWN, so the
            'draft' test never fires, and an empty box would read as settled.

            A query does NOT appear here. It is not a state the figure is in,
            it is a reason the row cannot travel, and merging the two would
            lose which of the states it was before it was queried.
        */
        CASE
            WHEN tmf.Amount IS NULL
                 OR (h.HasPassengerAirfare = 1 AND f.Amount IS NULL) THEN 'nofare'
            WHEN pub.PublishedPrice IS NULL THEN 'noprice'
            WHEN sub.TourDepartureId IS NULL AND snap.TourDepartureId IS NULL THEN 'new'
            WHEN pub.PublishedPrice <> COALESCE(sub.Price, snap.Price) THEN 'draft'
            WHEN sub.Price IS NOT NULL
                 AND (snap.Price IS NULL OR sub.Price <> snap.Price) THEN 'sent'
            ELSE 'live'
        END                     AS ChangeState
    FROM intlgit.TourHub AS th
        INNER JOIN intlgit.Hub AS h
            ON h.HubId = th.HubId
        INNER JOIN intlgit.TourDeparture AS td
            ON td.TourHubId = th.TourHubId
        INNER JOIN intlgit.CostBuild AS cb
            ON cb.TourId = th.TourId
        CROSS JOIN intlgit.Occupancy AS o
        INNER JOIN intlgit.CostBuildOccupancy AS cbo
            ON cbo.TourId = th.TourId
           AND cbo.OccupancyId = o.OccupancyId
        -- LEFT, not INNER: a departure with no fare yet still appears, so the
        -- product team can see that a price is owed rather than losing the row.
        LEFT JOIN @Fares AS f
            ON f.TourDepartureId = td.TourDepartureId
           AND f.FareBandId = o.FareBandId
        /*
            The tour manager's ticket, on every row of the departure.

            Resolved by CODE, not by the id 1. The seed file is explicit that
            the UI must not hard-code what it reads from reference data, and a
            price formula has less business doing it than a screen does.
        */
        CROSS JOIN (SELECT FareBandId FROM intlgit.FareBand WHERE Code = 'adult') AS tmb
        LEFT JOIN @Fares AS tmf
            ON tmf.TourDepartureId = td.TourDepartureId
           AND tmf.FareBandId = tmb.FareBandId
        CROSS APPLY
        (
            /*
                A NULL fare propagates: no fare, no price. That goes for the
                manager's fare too.

                The passenger's own seat is the one term that is not on every
                hub. On a Joining Direct departure the customer joins the tour
                at the destination and flies with nobody, so there is no ticket
                of theirs in the price - and the ELSE 0 is load-bearing, because
                without it a hub that will never have an own-fare would price at
                NULL for ever.

                Everything else is identical on every hub: the same rounding,
                the same markup, the same share of the tour manager's seat.
                This is one term, not a second formula.
            */
            SELECT CalculatedPrice =
                ROUND(
                    (
                        cbo.LandCostFx * cb.FxRate
                      + cbo.PerPersonInr
                      + cb.SharedCost / cb.PaxSlab
                      + CASE WHEN h.HasPassengerAirfare = 1 THEN f.Amount ELSE 0 END
                      + tmf.Amount / cb.PaxSlab
                    ) * (1 + th.MarkupPct / 100.0), 0)
        ) AS calc
        LEFT JOIN intlgit.PublishedPriceOverride AS ppo
            ON ppo.TourDepartureId = td.TourDepartureId
           AND ppo.OccupancyId = o.OccupancyId
        LEFT JOIN intlgit.LivePriceSnapshot AS snap
            ON snap.TourDepartureId = td.TourDepartureId
           AND snap.OccupancyId = o.OccupancyId
        LEFT JOIN intlgit.SubmittedPriceSnapshot AS sub
            ON sub.TourDepartureId = td.TourDepartureId
           AND sub.OccupancyId = o.OccupancyId
        LEFT JOIN intlgit.PriceCondition AS pc
            ON pc.TourDepartureId = td.TourDepartureId
           AND pc.OccupancyId = o.OccupancyId
        LEFT JOIN intlgit.PriceConfirmation AS conf
            ON conf.TourDepartureId = td.TourDepartureId
           AND conf.OccupancyId = o.OccupancyId
        -- Any open query on the departure, whichever band it names.
        OUTER APPLY
        (
            SELECT TOP 1 fq.TourDepartureId
            FROM intlgit.FareQuery AS fq
            WHERE fq.TourDepartureId = td.TourDepartureId
              AND fq.ResolvedUtc IS NULL
        ) AS q
        /*
            Applied last, because it reads the four things above it: the
            calculated figure, the override, the confirmation and the hub's
            setting. Computing it once here rather than three times inline is
            what stops PublishedPrice, StrikeThrough and ChangeState drifting
            apart - they are three readings of one number.
        */
        CROSS APPLY
        (
            SELECT PublishedPrice = CASE
                -- There is a figure, and nobody has agreed to it since it moved.
                WHEN calc.CalculatedPrice IS NOT NULL
                 AND conf.TourDepartureId IS NULL THEN NULL
                ELSE COALESCE(ppo.PublishedPrice, calc.CalculatedPrice)
            END
        ) AS pub
    WHERE (@TourId IS NULL OR th.TourId = @TourId)
);
GO

/* =========================================================================
   CHANGE-SET PROGRESS
   ========================================================================= */

-- ---------------------------------------------------------------------------
-- intlgit.vw_ChangeSetProgress
-- ---------------------------------------------------------------------------
CREATE OR ALTER VIEW intlgit.vw_ChangeSetProgress
AS
SELECT
    cs.ChangeSetId,
    cs.Reference,
    cs.TotalRows,
    CompletedRows = COALESCE(done.Completed, 0),
    IsComplete    = CASE WHEN COALESCE(done.Completed, 0) >= cs.TotalRows
                         THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END
FROM intlgit.ChangeSet AS cs
    OUTER APPLY
    (
        SELECT Completed = COUNT(*)
        FROM intlgit.ChangeSetRow AS r
        WHERE r.ChangeSetId = cs.ChangeSetId
          AND r.CompletedUtc IS NOT NULL
    ) AS done;
GO
