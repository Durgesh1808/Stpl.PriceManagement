/*
    AREA: IntlGit - stored procedures.
    All business rules that live in SQL are here (pricing formula, saves,
    submit, change sets, version promotion).
*/
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/* =========================================================================
   READS - products list, revision workspace, hub master, stamp
   ========================================================================= */

-- ---------------------------------------------------------------------------
-- intlgit.usp_GetTourList
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE intlgit.usp_GetTourList
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Fares intlgit.ResolvedFareList;
    INSERT INTO @Fares (TourDepartureId, FareBandId, Amount, PreviousAmount)
    EXEC intlgit.usp_GetAllFares;

    DECLARE @Changes TABLE (TourId INT NOT NULL PRIMARY KEY, ChangedRows INT NOT NULL);

    INSERT INTO @Changes (TourId, ChangedRows)
    SELECT changed.TourId, COUNT(*)
    FROM
    (
        SELECT DISTINCT r.TourId, r.TourDepartureId
        FROM
        (
            SELECT
                m.TourId, m.TourDepartureId, m.HasFare, m.IsNewToSite,
                m.PublishedPrice, m.BaselinePrice,
                BlanksOnDeparture = COUNT(CASE WHEN m.PublishedPrice IS NULL THEN 1 END)
                    OVER (PARTITION BY m.TourDepartureId)
            FROM intlgit.fn_PriceMatrix(NULL, @Fares) AS m
            WHERE m.HubIsActive = 1 AND m.DepartureIsActive = 1
              AND m.IsQueried = 0
        ) AS r
        WHERE r.BlanksOnDeparture = 0
          AND r.HasFare = 1
          AND (r.IsNewToSite = 1 OR r.PublishedPrice <> r.BaselinePrice)
    ) AS changed
    GROUP BY changed.TourId;

    /*
        Held departures, both reasons together.

        The products list has one number for "not going anywhere yet" and it
        would be a lie if it counted only the disputed fares. Which reason it
        is belongs to the review screen, which has room to say so.
    */
    DECLARE @Held TABLE (TourId INT NOT NULL PRIMARY KEY, Held INT NOT NULL);

    INSERT INTO @Held (TourId, Held)
    SELECT held.TourId, COUNT(DISTINCT held.TourDepartureId)
    FROM
    (
        SELECT th.TourId, fq.TourDepartureId
        FROM intlgit.FareQuery AS fq
            INNER JOIN intlgit.TourDeparture AS td ON td.TourDepartureId = fq.TourDepartureId
            INNER JOIN intlgit.TourHub AS th ON th.TourHubId = td.TourHubId
        WHERE fq.ResolvedUtc IS NULL
          AND th.IsActive = 1
          AND td.IsActive = 1

        UNION

        SELECT m.TourId, m.TourDepartureId
        FROM intlgit.fn_PriceMatrix(NULL, @Fares) AS m
        WHERE m.HubIsActive = 1
          AND m.DepartureIsActive = 1
          AND m.IsQueried = 0
          AND m.HasFare = 1
          AND m.PublishedPrice IS NULL
    ) AS held
    GROUP BY held.TourId;

    DECLARE @Removals TABLE (TourId INT NOT NULL PRIMARY KEY, Removals INT NOT NULL);

    INSERT INTO @Removals (TourId, Removals)
    SELECT th.TourId,
        COUNT(DISTINCT CASE
            WHEN th.IsActive = 0 THEN CAST(th.TourHubId AS VARCHAR(20))
            ELSE 'd' + CAST(td.TourDepartureId AS VARCHAR(20))
        END)
    FROM intlgit.TourHub AS th
        INNER JOIN intlgit.TourDeparture AS td ON td.TourHubId = th.TourHubId
    WHERE (th.IsActive = 0 OR td.IsActive = 0)
      AND EXISTS (SELECT 1 FROM intlgit.LivePriceSnapshot AS s WHERE s.TourDepartureId = td.TourDepartureId)
      AND NOT EXISTS (SELECT 1 FROM intlgit.SubmittedRemoval AS sr
                      WHERE sr.TourDepartureId = td.TourDepartureId)
    GROUP BY th.TourId;

    SELECT
        t.TourId, t.Code, t.Name, t.Region, t.Duration, t.CurrentVersion, t.ModifiedUtc,
        cb.SavedUtc,
        HubCount = (SELECT COUNT(*) FROM intlgit.TourHub AS th WHERE th.TourId = t.TourId),
        DepartureCount = (
            SELECT COUNT(DISTINCT td.DepartureDate)
            FROM intlgit.TourDeparture AS td
                INNER JOIN intlgit.TourHub AS th ON th.TourHubId = td.TourHubId
            WHERE th.TourId = t.TourId
        ),
        PendingPriceRows = COALESCE(ch.ChangedRows, 0),
        PendingRemovals  = COALESCE(rm.Removals, 0),
        HeldDepartures   = COALESCE(hd.Held, 0),
        AwaitingFares = (
            SELECT COUNT(DISTINCT df.TourDepartureId)
            FROM intlgit.DepartureFare AS df
            WHERE df.TourId = t.TourId AND df.Amount IS NULL
        ),
        FareSubmittedUtc = fs.SubmittedUtc,
        /*
            0086: the hand-over to air-ticketing, so the products list can say
            "With air-ticketing" the moment the product team sends a tour
            there. An open request is closed by usp_SubmitFares.
        */
        FareRequestedUtc = (
            SELECT MAX(fr.RequestedUtc)
            FROM intlgit.FareRequest AS fr
            WHERE fr.TourId = t.TourId AND fr.ClosedUtc IS NULL
        ),
        QueriedDepartures = (
            SELECT COUNT(DISTINCT fq.TourDepartureId)
            FROM intlgit.FareQuery AS fq
                INNER JOIN intlgit.TourDeparture AS td ON td.TourDepartureId = fq.TourDepartureId
                INNER JOIN intlgit.TourHub AS th ON th.TourHubId = td.TourHubId
            WHERE th.TourId = t.TourId
              AND fq.ResolvedUtc IS NULL
              AND th.IsActive = 1
              AND td.IsActive = 1
        )
    FROM intlgit.Tour AS t
        INNER JOIN intlgit.CostBuild AS cb ON cb.TourId = t.TourId
        LEFT JOIN @Changes AS ch ON ch.TourId = t.TourId
        LEFT JOIN @Removals AS rm ON rm.TourId = t.TourId
        LEFT JOIN @Held AS hd ON hd.TourId = t.TourId
        LEFT JOIN
        (
            SELECT s.TourId, MAX(s.SubmittedUtc) AS SubmittedUtc
            FROM intlgit.FareSubmission AS s GROUP BY s.TourId
        ) AS fs ON fs.TourId = t.TourId
    ORDER BY t.TourId;
END
GO

-- ---------------------------------------------------------------------------
-- intlgit.usp_GetTourRevision
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE intlgit.usp_GetTourRevision
    @TourCode VARCHAR(10)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @TourId INT;
    SELECT @TourId = t.TourId FROM intlgit.Tour AS t WHERE t.Code = @TourCode;

    IF @TourId IS NULL
        THROW 50001, 'Unknown tour code.', 1;

    DECLARE @Fares intlgit.ResolvedFareList;
    INSERT INTO @Fares (TourDepartureId, FareBandId, Amount, PreviousAmount)
    EXEC intlgit.usp_GetFares @TourId = @TourId;

    -- 1. Tour and cost build, plus the latest fare submission.
    SELECT
        t.TourId, t.Code, t.Name, t.Region, t.Duration, t.CurrentVersion, t.ModifiedUtc,
        cb.FxRate, cb.PreviousFxRate, cb.StrikePct, cb.PaxSlab, cb.SharedCost,
        cb.Note, cb.SavedUtc,
        fs.SubmittedUtc AS FareSubmittedUtc,
        fs.SubmittedBy  AS FareSubmittedBy,
        fs.Note         AS FareNote
    FROM intlgit.Tour AS t
        INNER JOIN intlgit.CostBuild AS cb ON cb.TourId = t.TourId
        OUTER APPLY
        (
            SELECT TOP 1 s.SubmittedUtc, s.SubmittedBy, s.Note
            FROM intlgit.FareSubmission AS s
            WHERE s.TourId = t.TourId
            ORDER BY s.FareSubmissionId DESC
        ) AS fs
    WHERE t.TourId = @TourId;

    -- 2. Cost build by occupancy.
    SELECT o.Code AS OccupancyCode, o.Label AS OccupancyLabel, o.SortOrder,
           cbo.LandCostFx, cbo.PerPersonInr
    FROM intlgit.CostBuildOccupancy AS cbo
        INNER JOIN intlgit.Occupancy AS o ON o.OccupancyId = cbo.OccupancyId
    WHERE cbo.TourId = @TourId
    ORDER BY o.SortOrder;

    -- 3. Hubs.
    SELECT
        th.TourHubId, h.Code AS HubCode, h.Name AS HubName, th.MarkupPct, th.IsActive,
        h.HasPassengerAirfare,
        DepartureCount = (
            SELECT COUNT(*) FROM intlgit.TourDeparture AS td
            WHERE td.TourHubId = th.TourHubId AND td.IsActive = 1
        ),
        IsNewHub = CASE WHEN NOT EXISTS
        (
            SELECT 1 FROM intlgit.TourDeparture AS td
                INNER JOIN intlgit.LivePriceSnapshot AS s ON s.TourDepartureId = td.TourDepartureId
            WHERE td.TourHubId = th.TourHubId
        ) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END
    FROM intlgit.TourHub AS th
        INNER JOIN intlgit.Hub AS h ON h.HubId = th.HubId
    WHERE th.TourId = @TourId
    -- By the master's sort order, so a hub can be placed rather than just
    -- landing wherever it was added. Was th.TourHubId - insertion order - which
    -- is why a hub added later could only ever appear last.
    ORDER BY h.SortOrder, h.Name;

    -- 4. Departures and their fares, with what each fare was before, and
    --    whether this particular band is the one under query.
    SELECT
        td.TourDepartureId, h.Code AS HubCode, td.DepartureDate, td.IsActive,
        fb.Code AS FareBandCode, f.Amount AS Airfare, f.PreviousAmount AS PreviousAirfare,
        IsQueried = CASE WHEN EXISTS
        (
            SELECT 1 FROM intlgit.FareQuery AS fq
            WHERE fq.TourDepartureId = td.TourDepartureId
              AND fq.ResolvedUtc IS NULL
              AND (fq.FareBandId IS NULL OR fq.FareBandId = fb.FareBandId)
        ) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END,

        -- Free text from air-ticketing, per departure rather than per band,
        -- so it repeats across the three rows each departure produces here.
        fl.Details AS FlightDetails
    FROM intlgit.TourDeparture AS td
        INNER JOIN intlgit.TourHub AS th ON th.TourHubId = td.TourHubId
        INNER JOIN intlgit.Hub AS h ON h.HubId = th.HubId
        CROSS JOIN intlgit.FareBand AS fb
        LEFT JOIN intlgit.DepartureFlight AS fl
            ON fl.TourDepartureId = td.TourDepartureId
        LEFT JOIN @Fares AS f
            ON f.TourDepartureId = td.TourDepartureId AND f.FareBandId = fb.FareBandId
    WHERE th.TourId = @TourId
    ORDER BY h.SortOrder, h.Name, td.DepartureDate, fb.FareBandId;

    -- 5. Price matrix, with where each figure stands and whether it is agreed.
    SELECT
        pm.HubCode, pm.DepartureDate, pm.OccupancyCode, pm.OccupancySort,
        pm.CalculatedPrice, pm.PublishedPrice, pm.StrikeThrough, pm.IsOverridden,
        pm.LivePrice, pm.BaselinePrice, pm.IsNewToSite, pm.HasFare,
        pm.ChangeState, pm.HasCondition, pm.IsConfirmed, pm.ConfirmedBy, pm.ConfirmedUtc,
        pm.IsQueried
    FROM intlgit.fn_PriceMatrix(@TourId, @Fares) AS pm
    ORDER BY pm.HubSortOrder, pm.HubName, pm.DepartureDate, pm.OccupancySort;
END
GO

-- ---------------------------------------------------------------------------
-- intlgit.usp_GetPendingChanges
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE intlgit.usp_GetPendingChanges
    @TourCode VARCHAR(10)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @TourId INT;
    SELECT @TourId = t.TourId FROM intlgit.Tour AS t WHERE t.Code = @TourCode;

    IF @TourId IS NULL
        THROW 50001, 'Unknown tour code.', 1;

    DECLARE @Fares intlgit.ResolvedFareList;
    INSERT INTO @Fares (TourDepartureId, FareBandId, Amount, PreviousAmount)
    EXEC intlgit.usp_GetFares @TourId = @TourId;

    /*
        Counted over the whole departure, not the cell, because a departure is
        the unit tech support ticks off and the website table prices one date
        at once. One blank occupancy holds all six - there is no half a
        departure to send, the same consequence a fare query carries.

        A window function rather than a second call to the matrix: the function
        is the most expensive thing in this procedure and reading it twice to
        ask one question about it is how a page starts timing out.
    */
    WITH raw AS
    (
        SELECT
            m.*,
            /*
                0085: a cell with no airfare counts as blank too. Missing
                fares no longer block the whole change set, so this is what
                keeps a departure that is only partly fared (say, child fare
                not in yet, with a stale typed figure behind it) out of it.
            */
            BlanksOnDeparture = COUNT(CASE WHEN m.PublishedPrice IS NULL OR m.HasFare = 0 THEN 1 END)
                OVER (PARTITION BY m.TourDepartureId)
        FROM intlgit.fn_PriceMatrix(@TourId, @Fares) AS m
        WHERE m.HubIsActive = 1
          AND m.DepartureIsActive = 1
          -- Held back. The rest of the tour can still go.
          AND m.IsQueried = 0
    ),
    matrix AS
    (
        SELECT
            r.*,
            HasChanged = CASE
                WHEN r.HasFare = 0 THEN CAST(0 AS BIT)
                WHEN r.IsNewToSite = 1 OR r.PublishedPrice <> r.BaselinePrice
                THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END
        FROM raw AS r
        /*
            Not only for the screen's sake. A new hub has no snapshot, so
            IsNewToSite = 1 makes HasChanged = 1 whatever the price is - the
            <> comparison never runs - and the departure would leave here with
            a NULL for ChangeSetCell.PublishedPrice, which is NOT NULL, and
            take the whole change set down with it.
        */
        WHERE r.BlanksOnDeparture = 0
    )
    SELECT
        pm.HubCode, pm.HubName, pm.DepartureDate, pm.OccupancyCode, pm.OccupancySort,
        pm.PublishedPrice, pm.StrikeThrough, pm.LivePrice, pm.BaselinePrice,
        pm.IsNewToSite, pm.HasChanged, pm.HasCondition,
        IsNewHub = CASE WHEN NOT EXISTS
        (
            SELECT 1 FROM intlgit.TourDeparture AS td
                INNER JOIN intlgit.LivePriceSnapshot AS s ON s.TourDepartureId = td.TourDepartureId
            WHERE td.TourHubId = pm.TourHubId
        ) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END
    FROM matrix AS pm
    WHERE EXISTS
    (
        SELECT 1 FROM matrix AS sibling
        WHERE sibling.TourDepartureId = pm.TourDepartureId AND sibling.HasChanged = 1
    )
    ORDER BY pm.TourHubId, pm.DepartureDate, pm.OccupancySort;

    -- Removals still outstanding: on the website, withdrawn, and not already sent.
    SELECT
        Kind = 'hub', HubCode = h.Code, HubName = h.Name,
        DepartureDate = CAST(NULL AS DATE),
        DepartureCount = (SELECT COUNT(*) FROM intlgit.TourDeparture AS td WHERE td.TourHubId = th.TourHubId),
        LivePrice = (
            SELECT MIN(s.Price)
            FROM intlgit.TourDeparture AS td
                INNER JOIN intlgit.LivePriceSnapshot AS s ON s.TourDepartureId = td.TourDepartureId
                INNER JOIN intlgit.Occupancy AS o ON o.OccupancyId = s.OccupancyId AND o.Code = 'twin'
            WHERE td.TourHubId = th.TourHubId
        )
    FROM intlgit.TourHub AS th
        INNER JOIN intlgit.Hub AS h ON h.HubId = th.HubId
    WHERE th.TourId = @TourId
      AND th.IsActive = 0
      AND EXISTS
      (
          SELECT 1 FROM intlgit.TourDeparture AS td
              INNER JOIN intlgit.LivePriceSnapshot AS s ON s.TourDepartureId = td.TourDepartureId
          WHERE td.TourHubId = th.TourHubId
            AND NOT EXISTS (SELECT 1 FROM intlgit.SubmittedRemoval AS sr
                            WHERE sr.TourDepartureId = td.TourDepartureId)
      )

    UNION ALL

    SELECT
        Kind = 'departure', HubCode = h.Code, HubName = h.Name,
        DepartureDate = td.DepartureDate, DepartureCount = 1,
        LivePrice = (
            SELECT s.Price FROM intlgit.LivePriceSnapshot AS s
                INNER JOIN intlgit.Occupancy AS o ON o.OccupancyId = s.OccupancyId AND o.Code = 'twin'
            WHERE s.TourDepartureId = td.TourDepartureId
        )
    FROM intlgit.TourDeparture AS td
        INNER JOIN intlgit.TourHub AS th ON th.TourHubId = td.TourHubId
        INNER JOIN intlgit.Hub AS h ON h.HubId = th.HubId
    WHERE th.TourId = @TourId
      AND th.IsActive = 1
      AND td.IsActive = 0
      AND EXISTS (SELECT 1 FROM intlgit.LivePriceSnapshot AS s WHERE s.TourDepartureId = td.TourDepartureId)
      AND NOT EXISTS (SELECT 1 FROM intlgit.SubmittedRemoval AS sr
                      WHERE sr.TourDepartureId = td.TourDepartureId)
    ORDER BY Kind, HubCode, DepartureDate;

    /*
        Third result set: what is being held back with air-ticketing.

        The exclusion above is silent by itself, and a row quietly missing from
        what you are about to submit is the kind of thing people find out about
        a week later. Every screen that shows the change list has to be able to
        say what is not in it.
    */
    SELECT
        h.Code   AS HubCode,
        h.Name   AS HubName,
        td.DepartureDate,
        fb.Code  AS FareBandCode,
        fb.Label AS FareBandLabel,
        fq.RaisedUtc,
        fq.RaisedBy,
        fq.Reason
    FROM intlgit.FareQuery AS fq
        INNER JOIN intlgit.TourDeparture AS td ON td.TourDepartureId = fq.TourDepartureId
        INNER JOIN intlgit.TourHub AS th ON th.TourHubId = td.TourHubId
        INNER JOIN intlgit.Hub AS h ON h.HubId = th.HubId
        LEFT JOIN intlgit.FareBand AS fb ON fb.FareBandId = fq.FareBandId
    WHERE th.TourId = @TourId
      AND fq.ResolvedUtc IS NULL
      AND th.IsActive = 1
      AND td.IsActive = 1
    ORDER BY h.Code, td.DepartureDate, fb.FareBandId;

    /*
        Fourth result set: held back because nobody has priced it.

        Kept apart from the queried ones rather than folded in behind a reason
        column. They read alike and are not alike: one is waiting on somebody
        else and the remedy is a phone call, the other is waiting on the person
        reading this screen and the remedy is two minutes on the grid. A single
        list would send people to chase fares that are already in.
    */
    SELECT
        h.Code AS HubCode,
        h.Name AS HubName,
        td.DepartureDate,
        BlankCount = COUNT(*),
        CalculatedTotal = SUM(m.CalculatedPrice)
    FROM intlgit.fn_PriceMatrix(@TourId, @Fares) AS m
        INNER JOIN intlgit.TourDeparture AS td ON td.TourDepartureId = m.TourDepartureId
        INNER JOIN intlgit.TourHub AS th ON th.TourHubId = td.TourHubId
        INNER JOIN intlgit.Hub AS h ON h.HubId = th.HubId
    WHERE th.TourId = @TourId
      AND m.HubIsActive = 1
      AND m.DepartureIsActive = 1
      AND m.IsQueried = 0
      AND m.HasFare = 1
      AND m.PublishedPrice IS NULL
    GROUP BY h.Code, h.Name, td.DepartureDate
    ORDER BY h.Code, td.DepartureDate;
END
GO

-- ---------------------------------------------------------------------------
-- intlgit.usp_GetHubMaster
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE intlgit.usp_GetHubMaster
AS
BEGIN
    SET NOCOUNT ON;

    SELECT h.Code, h.Name, h.DefaultMarkupPct, h.HasPassengerAirfare
    FROM intlgit.Hub AS h
    WHERE h.IsActive = 1
    ORDER BY h.SortOrder, h.Name;
END
GO

-- ---------------------------------------------------------------------------
-- intlgit.usp_GetTourStamp
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE intlgit.usp_GetTourStamp
    @TourCode VARCHAR(10)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT t.ModifiedUtc
    FROM intlgit.Tour AS t
    WHERE t.Code = @TourCode;
END
GO

-- ---------------------------------------------------------------------------
-- intlgit.usp_GetChangesSince
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE intlgit.usp_GetChangesSince
    @TourCode VARCHAR(10),
    @Since    DATETIME2(0)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @TourId INT;
    SELECT @TourId = TourId FROM intlgit.Tour WHERE Code = @TourCode;

    IF @TourId IS NULL
        THROW 50001, 'Unknown tour code.', 1;

    SELECT TOP (20)
        a.OccurredUtc,
        a.ActorName,
        a.ActorRole,
        a.Action,
        a.HubCode,
        a.DepartureDate,
        a.Field,
        a.OldValue,
        a.NewValue
    FROM intlgit.Activity AS a
    WHERE a.TourId = @TourId
      AND a.OccurredUtc > @Since
    ORDER BY a.OccurredUtc DESC, a.ActivityId DESC;
END
GO

-- ---------------------------------------------------------------------------
-- intlgit.usp_AssertUnchanged
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE intlgit.usp_AssertUnchanged
    @TourId              INT,
    @ExpectedModifiedUtc DATETIME2(0) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    -- No stamp means the caller is not using the guard. Every save keeps
    -- working exactly as before until its caller starts sending one.
    IF @ExpectedModifiedUtc IS NULL
        RETURN;

    DECLARE @Actual DATETIME2(0);

    /*
        UPDLOCK, and inside the caller's transaction. Two saves arriving
        together serialise here: the second waits for the first to commit,
        then reads the stamp the first wrote and refuses. Without the lock
        both would read the old stamp and both would pass.
    */
    SELECT @Actual = ModifiedUtc
    FROM intlgit.Tour WITH (UPDLOCK)
    WHERE TourId = @TourId;

    IF @Actual IS NULL
        THROW 50001, 'Unknown tour code.', 1;

    IF @Actual <> @ExpectedModifiedUtc
        THROW 50030, 'This tour was changed by somebody else while you were editing it.', 1;
END
GO

-- ---------------------------------------------------------------------------
-- intlgit.usp_PreviewPrices
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE intlgit.usp_PreviewPrices
    @TourCode VARCHAR(10),
    @Fares    intlgit.FareEditList READONLY
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @TourId INT;
    SELECT @TourId = TourId FROM intlgit.Tour WHERE Code = @TourCode;

    IF @TourId IS NULL
        THROW 50001, 'Unknown tour code.', 1;

    /*
        Start from the fares as they stand, then lay the proposed ones over the
        top. Both are needed: a departure is priced from three bands, and the
        caller may only be changing one of them.
    */
    DECLARE @Resolved intlgit.ResolvedFareList;

    INSERT INTO @Resolved (TourDepartureId, FareBandId, Amount, PreviousAmount)
    EXEC intlgit.usp_GetFares @TourId = @TourId;

    DECLARE @Proposed TABLE
    (
        TourDepartureId INT            NOT NULL,
        FareBandId      TINYINT        NOT NULL,
        Amount          DECIMAL(12, 2) NULL,
        PRIMARY KEY (TourDepartureId, FareBandId)
    );

    INSERT INTO @Proposed (TourDepartureId, FareBandId, Amount)
    SELECT td.TourDepartureId, fb.FareBandId, f.Amount
    FROM @Fares AS f
        INNER JOIN intlgit.Hub AS h ON h.Code = f.HubCode
        INNER JOIN intlgit.TourHub AS th ON th.HubId = h.HubId AND th.TourId = @TourId
        INNER JOIN intlgit.TourDeparture AS td
            ON td.TourHubId = th.TourHubId AND td.DepartureDate = f.DepartureDate
        INNER JOIN intlgit.FareBand AS fb ON fb.Code = f.FareBandCode;

    UPDATE r
    SET r.Amount = p.Amount
    FROM @Resolved AS r
        INNER JOIN @Proposed AS p
            ON p.TourDepartureId = r.TourDepartureId
           AND p.FareBandId = r.FareBandId;

    -- A departure that had no fare row at all still needs one to be priced.
    INSERT INTO @Resolved (TourDepartureId, FareBandId, Amount)
    SELECT p.TourDepartureId, p.FareBandId, p.Amount
    FROM @Proposed AS p
    WHERE NOT EXISTS
    (
        SELECT 1 FROM @Resolved AS r
        WHERE r.TourDepartureId = p.TourDepartureId AND r.FareBandId = p.FareBandId
    );

    -- Only the departures the caller asked about.
    SELECT
        m.HubCode,
        m.DepartureDate,
        m.OccupancyCode,
        m.OccupancySort,
        m.CalculatedPrice,
        m.PublishedPrice,
        m.StrikeThrough,
        m.IsOverridden,
        m.HasFare,
        m.LivePrice
    FROM intlgit.fn_PriceMatrix(@TourId, @Resolved) AS m
    WHERE EXISTS
    (
        SELECT 1 FROM @Proposed AS p WHERE p.TourDepartureId = m.TourDepartureId
    )
    ORDER BY m.DepartureDate, m.OccupancySort;
END
GO

/* =========================================================================
   COST BUILD
   ========================================================================= */

-- ---------------------------------------------------------------------------
-- intlgit.usp_SaveCostBuild
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE intlgit.usp_SaveCostBuild
    @TourCode      VARCHAR(10),
    @FxRate        DECIMAL(10, 4),
    @StrikePct     DECIMAL(5, 2),
    @PaxSlab       INT,
    @SharedCost    DECIMAL(12, 2),
    @Note          NVARCHAR(1000),
    @Costs         intlgit.OccupancyCostList READONLY,
    @ChangedBy     NVARCHAR(100) = NULL,
    @ActorRole     VARCHAR(20)   = 'product',

    -- Round-tripped from the page: the tour as the person saw it. NULL
    -- means the caller is not using the guard.
    @ExpectedModifiedUtc DATETIME2(0) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @TourId INT;
    SELECT @TourId = TourId FROM intlgit.Tour WHERE Code = @TourCode;

    IF @TourId IS NULL
    BEGIN
        THROW 50001, 'Unknown tour code.', 1;
    END

    BEGIN TRANSACTION;

    -- Inside the transaction, not before it: checking first and writing
    -- second leaves a gap in which two saves both pass the check.
    EXEC intlgit.usp_AssertUnchanged @TourId, @ExpectedModifiedUtc;

    /* The four header figures, before and after, captured by the UPDATE itself
       so the before-value is the one this statement actually replaced. */
    DECLARE @Header TABLE
    (
        OldFx     DECIMAL(10, 4) NULL, NewFx     DECIMAL(10, 4) NULL,
        OldStrike DECIMAL(5, 2)  NULL, NewStrike DECIMAL(5, 2)  NULL,
        OldSlab   INT            NULL, NewSlab   INT            NULL,
        OldShared DECIMAL(12, 2) NULL, NewShared DECIMAL(12, 2) NULL
    );

    UPDATE intlgit.CostBuild
    SET FxRate        = @FxRate,
        StrikePct     = @StrikePct,
        PaxSlab       = @PaxSlab,
        SharedCost    = @SharedCost,
        Note          = @Note,
        SavedUtc      = SYSUTCDATETIME()
    OUTPUT
        deleted.FxRate,     inserted.FxRate,
        deleted.StrikePct,  inserted.StrikePct,
        deleted.PaxSlab,    inserted.PaxSlab,
        deleted.SharedCost, inserted.SharedCost
        INTO @Header
    WHERE TourId = @TourId;

    DECLARE @OccupancyChanges TABLE
    (
        OccupancyId  TINYINT        NOT NULL,
        OldLand      DECIMAL(12, 2) NULL, NewLand      DECIMAL(12, 2) NULL,
        OldPerPerson DECIMAL(12, 2) NULL, NewPerPerson DECIMAL(12, 2) NULL
    );

    UPDATE cbo
    SET cbo.LandCostFx   = c.LandCostFx,
        cbo.PerPersonInr = c.PerPersonInr
    OUTPUT
        inserted.OccupancyId,
        deleted.LandCostFx,   inserted.LandCostFx,
        deleted.PerPersonInr, inserted.PerPersonInr
        INTO @OccupancyChanges (OccupancyId, OldLand, NewLand, OldPerPerson, NewPerPerson)
    FROM intlgit.CostBuildOccupancy AS cbo
        INNER JOIN intlgit.Occupancy AS o
            ON o.OccupancyId = cbo.OccupancyId
        INNER JOIN @Costs AS c
            ON c.OccupancyCode = o.Code
    WHERE cbo.TourId = @TourId;

    /* Deliberately outside the @ChangedBy test below: whether the gate re-opens
       cannot depend on whether the caller told us who was acting.

       IS DISTINCT FROM, not <>: a figure entered for the first time moves from
       NULL to a number, and <> would call that no change and leave every
       confirmation standing against a price that has just moved. */
    IF EXISTS (SELECT 1 FROM @Header
               WHERE OldFx     IS DISTINCT FROM NewFx
                  OR OldStrike IS DISTINCT FROM NewStrike
                  OR OldSlab   IS DISTINCT FROM NewSlab
                  OR OldShared IS DISTINCT FROM NewShared)
    BEGIN
        DELETE conf
        FROM intlgit.PriceConfirmation AS conf
            INNER JOIN intlgit.TourDeparture AS td ON td.TourDepartureId = conf.TourDepartureId
            INNER JOIN intlgit.TourHub AS th ON th.TourHubId = td.TourHubId
        WHERE th.TourId = @TourId;
    END
    ELSE
    BEGIN
        -- A land or per-person cost moves only its own occupancy's prices.
        DELETE conf
        FROM intlgit.PriceConfirmation AS conf
            INNER JOIN intlgit.TourDeparture AS td ON td.TourDepartureId = conf.TourDepartureId
            INNER JOIN intlgit.TourHub AS th ON th.TourHubId = td.TourHubId
            INNER JOIN @OccupancyChanges AS oc ON oc.OccupancyId = conf.OccupancyId
        WHERE th.TourId = @TourId
          AND (oc.OldLand      IS DISTINCT FROM oc.NewLand
            OR oc.OldPerPerson IS DISTINCT FROM oc.NewPerPerson);
    END

    IF @ChangedBy IS NOT NULL
    BEGIN
        /* One row per figure that actually moved. A save that changed nothing
           writes nothing - an audit trail full of "saved, no change" is an
           audit trail nobody reads.

           The first entry of a cost is a change, and the most interesting one
           on the tour. <> called it nothing at all. */
        INSERT INTO intlgit.Activity
            (TourId, ActorName, ActorRole, Action, Field, OldValue, NewValue)
        SELECT @TourId, @ChangedBy, @ActorRole, 'costbuild', v.Field, v.OldValue, v.NewValue
        FROM @Header AS h
        CROSS APPLY
        (
            VALUES
                ('fx',     CONVERT(DECIMAL(12, 2), h.OldFx),     CONVERT(DECIMAL(12, 2), h.NewFx)),
                ('strike', CONVERT(DECIMAL(12, 2), h.OldStrike), CONVERT(DECIMAL(12, 2), h.NewStrike)),
                ('slab',   CONVERT(DECIMAL(12, 2), h.OldSlab),   CONVERT(DECIMAL(12, 2), h.NewSlab)),
                ('shared', h.OldShared,                          h.NewShared)
        ) AS v (Field, OldValue, NewValue)
        WHERE v.OldValue IS DISTINCT FROM v.NewValue;

        INSERT INTO intlgit.Activity
            (TourId, ActorName, ActorRole, Action, Field, OldValue, NewValue)
        SELECT @TourId, @ChangedBy, @ActorRole, 'costbuild', v.Field, v.OldValue, v.NewValue
        FROM @OccupancyChanges AS oc
            INNER JOIN intlgit.Occupancy AS o ON o.OccupancyId = oc.OccupancyId
        CROSS APPLY
        (
            VALUES
                ('land:' + o.Code,      oc.OldLand,      oc.NewLand),
                ('perperson:' + o.Code, oc.OldPerPerson, oc.NewPerPerson)
        ) AS v (Field, OldValue, NewValue)
        WHERE v.OldValue IS DISTINCT FROM v.NewValue;
    END

    UPDATE intlgit.Tour
    SET ModifiedUtc = SYSUTCDATETIME()
    WHERE TourId = @TourId;

    COMMIT TRANSACTION;
END
GO

/* =========================================================================
   PUBLISHED PRICES, CONFIRMATION, CONDITIONS
   ========================================================================= */

-- ---------------------------------------------------------------------------
-- intlgit.usp_SavePublishedPrices
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE intlgit.usp_SavePublishedPrices
    @TourCode  VARCHAR(10),
    @Prices    intlgit.PriceList READONLY,
    @ChangedBy NVARCHAR(100) = NULL,
    @ActorRole VARCHAR(20)   = 'product',

    -- Round-tripped from the page: the tour as the person saw it. NULL
    -- means the caller is not using the guard.
    @ExpectedModifiedUtc DATETIME2(0) = NULL,

    -- Cells the person emptied. Omitted by a caller that has none, which SQL
    -- Server reads as an empty table.
    @Cleared   intlgit.PriceCellList READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @TourId INT;
    SELECT @TourId = TourId FROM intlgit.Tour WHERE Code = @TourCode;

    IF @TourId IS NULL
    BEGIN
        THROW 50001, 'Unknown tour code.', 1;
    END

    BEGIN TRANSACTION;

    -- Inside the transaction, not before it: checking first and writing
    -- second leaves a gap in which two saves both pass the check.
    EXEC intlgit.usp_AssertUnchanged @TourId, @ExpectedModifiedUtc;

    DECLARE @Posted TABLE
    (
        TourDepartureId INT            NOT NULL,
        OccupancyId     TINYINT        NOT NULL,
        Price           DECIMAL(12, 2) NOT NULL,
        PRIMARY KEY (TourDepartureId, OccupancyId)
    );

    /*
        Rounded to the rupee on the way in, and rounded HERE rather than at the
        MERGE below so that the comparison and the write see the same figure.

        The column is DECIMAL(12,2) and a typed price used to go in exactly as
        given, so 242256.78 was stored while the box redrew as 2,42,257 - the
        screen and the database disagreeing about the same cell. Saving again
        then compared the stored 242256.78 against the 242257 posted back, found
        a difference, and raised a change-set row for a price nobody had moved.

        NULL is untouched: absent is an instruction - take this price back - and
        ROUND(NULL) is NULL, which is what should reach the delete below.
    */
    INSERT INTO @Posted (TourDepartureId, OccupancyId, Price)
    SELECT td.TourDepartureId, o.OccupancyId, ROUND(p.Price, 0)
    FROM @Prices AS p
        INNER JOIN intlgit.Hub AS h
            ON h.Code = p.HubCode
        INNER JOIN intlgit.TourHub AS th
            ON th.HubId = h.HubId AND th.TourId = @TourId
        INNER JOIN intlgit.TourDeparture AS td
            ON td.TourHubId = th.TourHubId AND td.DepartureDate = p.DepartureDate
        INNER JOIN intlgit.Occupancy AS o
            ON o.Code = p.OccupancyCode;

    /*
        And the cells being emptied, resolved the same way.

        A cell cannot be both: the form posts one box per cell, so a figure and
        an instruction to remove it cannot both come from it. Excluded anyway,
        because a procedure that would do two contradictory things to one row if
        somebody built the request by hand is a procedure waiting to be asked.
    */
    DECLARE @ToClear TABLE
    (
        TourDepartureId INT     NOT NULL,
        OccupancyId     TINYINT NOT NULL,
        PRIMARY KEY (TourDepartureId, OccupancyId)
    );

    INSERT INTO @ToClear (TourDepartureId, OccupancyId)
    SELECT td.TourDepartureId, o.OccupancyId
    FROM @Cleared AS c
        INNER JOIN intlgit.Hub AS h
            ON h.Code = c.HubCode
        INNER JOIN intlgit.TourHub AS th
            ON th.HubId = h.HubId AND th.TourId = @TourId
        INNER JOIN intlgit.TourDeparture AS td
            ON td.TourHubId = th.TourHubId AND td.DepartureDate = c.DepartureDate
        INNER JOIN intlgit.Occupancy AS o
            ON o.Code = c.OccupancyCode
    WHERE NOT EXISTS
    (
        SELECT 1 FROM @Posted AS p
        WHERE p.TourDepartureId = td.TourDepartureId
          AND p.OccupancyId = o.OccupancyId
    );

    DECLARE @Touched TABLE
    (
        TourDepartureId INT            NOT NULL,
        OccupancyId     TINYINT        NOT NULL,
        OldPrice        DECIMAL(12, 2) NULL,
        NewPrice        DECIMAL(12, 2) NULL
    );

    MERGE intlgit.PublishedPriceOverride AS target
    USING @Posted AS source
        ON source.TourDepartureId = target.TourDepartureId
       AND source.OccupancyId = target.OccupancyId
    WHEN MATCHED AND target.PublishedPrice <> source.Price THEN
        UPDATE SET PublishedPrice = source.Price,
                   SetUtc = SYSUTCDATETIME()
    WHEN NOT MATCHED BY TARGET THEN
        INSERT (TourDepartureId, OccupancyId, PublishedPrice, SetUtc)
        VALUES (source.TourDepartureId, source.OccupancyId, source.Price, SYSUTCDATETIME())
    OUTPUT
        inserted.TourDepartureId, inserted.OccupancyId,
        deleted.PublishedPrice, inserted.PublishedPrice
        INTO @Touched (TourDepartureId, OccupancyId, OldPrice, NewPrice);

    /*
        Taking a price back: the typed figure goes, and so does the agreement.

        Both, and the agreement is the one that matters. Deleting the override
        alone would leave the cell agreed at whatever the formula now says,
        which is the opposite of what emptying a box asks for. Deleting the
        confirmation is what makes the cell read as blank, and blank is what
        holds the departure out of the change request.

        OUTPUT into @Touched so the activity log below records the removal in
        the same breath as the changes - a price disappearing is worth as much
        in that trail as a price moving.
    */
    DELETE ppo
    OUTPUT deleted.TourDepartureId, deleted.OccupancyId, deleted.PublishedPrice, NULL
        INTO @Touched (TourDepartureId, OccupancyId, OldPrice, NewPrice)
    FROM intlgit.PublishedPriceOverride AS ppo
        INNER JOIN @ToClear AS c
            ON c.TourDepartureId = ppo.TourDepartureId
           AND c.OccupancyId = ppo.OccupancyId;

    DELETE conf
    FROM intlgit.PriceConfirmation AS conf
        INNER JOIN @ToClear AS c
            ON c.TourDepartureId = conf.TourDepartureId
           AND c.OccupancyId = conf.OccupancyId;

    IF @ChangedBy IS NOT NULL
    BEGIN
        MERGE intlgit.PriceConfirmation AS target
        USING @Posted AS source
            ON source.TourDepartureId = target.TourDepartureId
           AND source.OccupancyId = target.OccupancyId
        WHEN MATCHED THEN
            UPDATE SET ConfirmedUtc = SYSUTCDATETIME(), ConfirmedBy = @ChangedBy
        WHEN NOT MATCHED BY TARGET THEN
            INSERT (TourDepartureId, OccupancyId, ConfirmedUtc, ConfirmedBy)
            VALUES (source.TourDepartureId, source.OccupancyId, SYSUTCDATETIME(), @ChangedBy);

        INSERT INTO intlgit.Activity
            (TourId, ActorName, ActorRole, Action, HubCode, DepartureDate, Field, OldValue, NewValue)
        SELECT
            @TourId, @ChangedBy, @ActorRole, 'price',
            h.Code, td.DepartureDate, o.Code, t.OldPrice, t.NewPrice
        FROM @Touched AS t
            INNER JOIN intlgit.TourDeparture AS td ON td.TourDepartureId = t.TourDepartureId
            INNER JOIN intlgit.TourHub AS th ON th.TourHubId = td.TourHubId
            INNER JOIN intlgit.Hub AS h ON h.HubId = th.HubId
            INNER JOIN intlgit.Occupancy AS o ON o.OccupancyId = t.OccupancyId
        ORDER BY h.Code, td.DepartureDate, o.SortOrder;
    END

    UPDATE intlgit.Tour SET ModifiedUtc = SYSUTCDATETIME() WHERE TourId = @TourId;

    COMMIT TRANSACTION;
END
GO

-- ---------------------------------------------------------------------------
-- intlgit.usp_AdoptCalculatedPrices
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE intlgit.usp_AdoptCalculatedPrices
    @TourCode   VARCHAR(10),
    @By         NVARCHAR(100) = N'Tour manager correction',
    @WhatIf     BIT = 1,
    @KeepTyped  BIT = 1,        -- a figure somebody chose is not ours to discard
    @HubCode    VARCHAR(10) = NULL,  -- null: the whole tour
    @BlanksOnly BIT = 0         -- 1: only cells with no agreed figure
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @TourId INT;
    SELECT @TourId = TourId FROM intlgit.Tour WHERE Code = @TourCode;

    IF @TourId IS NULL
        THROW 50001, 'Unknown tour code.', 1;

    DECLARE @Fares intlgit.ResolvedFareList;
    INSERT INTO @Fares (TourDepartureId, FareBandId, Amount, PreviousAmount)
    EXEC intlgit.usp_GetFares @TourId = @TourId;

    /*
        Everything this would touch, worked out once, before anything moves.

        A queried departure is left alone. Being under dispute with
        air-ticketing is not something a correction gets to overrule, and
        pushing its price through is exactly what raising the query was meant
        to prevent.
    */
    DECLARE @Scope TABLE
    (
        TourDepartureId INT     NOT NULL,
        OccupancyId     TINYINT NOT NULL,
        OldPublished    DECIMAL(12, 2) NULL,   -- null where nothing was typed
        NewPublished    DECIMAL(12, 2) NOT NULL,
        /*
            Whether the typed figure gets removed. Every cell in scope is still
            CONFIRMED either way - otherwise a kept price would sit blank and
            hold its departure out of the change set, which is the opposite of
            leaving it alone.
        */
        ReplaceTyped    BIT NOT NULL,
        PRIMARY KEY (TourDepartureId, OccupancyId)
    );

    INSERT INTO @Scope (TourDepartureId, OccupancyId, OldPublished, NewPublished, ReplaceTyped)
    SELECT m.TourDepartureId, m.OccupancyId, ppo.PublishedPrice, m.CalculatedPrice,
           /*
               @BlanksOnly overrides @KeepTyped, and must.

               A blank cell can still carry an override row - one typed months
               ago, hidden ever since because nobody has agreed to the figure.
               "Keep typed" would spare it, and the cell would then fall back to
               that stale number instead of the calculated one. The person
               ticking "same as calculated SP" would get neither.

               There is nothing on screen to protect here: the cell is empty.
           */
           CASE WHEN ppo.PublishedPrice IS NOT NULL
                 AND @KeepTyped = 1 AND @BlanksOnly = 0
                THEN CAST(0 AS BIT) ELSE CAST(1 AS BIT) END
    FROM intlgit.fn_PriceMatrix(@TourId, @Fares) AS m
        LEFT JOIN intlgit.PublishedPriceOverride AS ppo
            ON ppo.TourDepartureId = m.TourDepartureId
           AND ppo.OccupancyId = m.OccupancyId
    WHERE m.HubIsActive = 1
      AND m.DepartureIsActive = 1
      AND m.HasFare = 1
      AND m.IsQueried = 0
      AND (@HubCode IS NULL OR m.HubCode = @HubCode)
      AND (@BlanksOnly = 0 OR m.PublishedPrice IS NULL)
      -- Nothing to adopt where no price can be worked out. Without this the
      -- NOT NULL column above takes a NULL and the whole action throws.
      AND m.CalculatedPrice IS NOT NULL;

    DECLARE @TypedReplaced INT =
        (SELECT COUNT(*) FROM @Scope WHERE ReplaceTyped = 1
                                       AND OldPublished IS NOT NULL
                                       AND OldPublished <> NewPublished);

    DECLARE @TypedKept INT =
        (SELECT COUNT(*) FROM @Scope WHERE ReplaceTyped = 0);

    IF @WhatIf = 1
    BEGIN
        SELECT
            TourCode        = @TourCode,
            WhatIf          = CAST(1 AS BIT),
            CellsInScope    = (SELECT COUNT(*) FROM @Scope),
            TypedReplaced   = @TypedReplaced,
            TypedKept       = @TypedKept,
            StillUnagreed   = (SELECT COUNT(*) FROM intlgit.fn_PriceMatrix(@TourId, @Fares) AS m
                               WHERE m.HubIsActive = 1 AND m.DepartureIsActive = 1
                                 AND m.HasFare = 1 AND m.IsQueried = 0
                                 AND m.IsConfirmed = 0);
        RETURN;
    END

    BEGIN TRANSACTION;

    -- Logged BEFORE the delete, and only where the figure actually moves, so
    -- the trail carries what somebody chose and what replaced it.
    INSERT INTO intlgit.Activity
        (TourId, ActorName, ActorRole, Action, HubCode, DepartureDate, Field, OldValue, NewValue)
    SELECT
        @TourId, @By, 'system', 'price',
        h.Code, td.DepartureDate, o.Code, s.OldPublished, s.NewPublished
    FROM @Scope AS s
        INNER JOIN intlgit.TourDeparture AS td ON td.TourDepartureId = s.TourDepartureId
        INNER JOIN intlgit.TourHub AS th ON th.TourHubId = td.TourHubId
        INNER JOIN intlgit.Hub AS h ON h.HubId = th.HubId
        INNER JOIN intlgit.Occupancy AS o ON o.OccupancyId = s.OccupancyId
    WHERE s.ReplaceTyped = 1
      AND s.OldPublished IS NOT NULL
      AND s.OldPublished <> s.NewPublished
    ORDER BY h.Code, td.DepartureDate, o.SortOrder;

    DELETE ppo
    FROM intlgit.PublishedPriceOverride AS ppo
        INNER JOIN @Scope AS s
            ON s.TourDepartureId = ppo.TourDepartureId
           AND s.OccupancyId = ppo.OccupancyId
    WHERE s.ReplaceTyped = 1;

    -- And agreed, because that is what "no review step" means: somebody has to
    -- be recorded as having accepted these, and it is this correction.
    MERGE intlgit.PriceConfirmation AS target
    USING @Scope AS source
        ON source.TourDepartureId = target.TourDepartureId
       AND source.OccupancyId = target.OccupancyId
    WHEN MATCHED THEN
        UPDATE SET ConfirmedUtc = SYSUTCDATETIME(), ConfirmedBy = @By
    WHEN NOT MATCHED BY TARGET THEN
        INSERT (TourDepartureId, OccupancyId, ConfirmedUtc, ConfirmedBy)
        VALUES (source.TourDepartureId, source.OccupancyId, SYSUTCDATETIME(), @By);

    UPDATE intlgit.Tour SET ModifiedUtc = SYSUTCDATETIME() WHERE TourId = @TourId;

    COMMIT TRANSACTION;

    SELECT
        TourCode      = @TourCode,
        WhatIf        = CAST(0 AS BIT),
        CellsInScope  = (SELECT COUNT(*) FROM @Scope),
        TypedReplaced = @TypedReplaced,
        TypedKept     = @TypedKept,
        StillUnagreed = 0;
END
GO

-- ---------------------------------------------------------------------------
-- intlgit.usp_ConfirmPriceCells
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE intlgit.usp_ConfirmPriceCells
    @TourCode    VARCHAR(10),
    @Cells       intlgit.PriceCellList READONLY,
    @ConfirmedBy NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @TourId INT;
    SELECT @TourId = TourId FROM intlgit.Tour WHERE Code = @TourCode;

    IF @TourId IS NULL
        THROW 50001, 'Unknown tour code.', 1;

    DECLARE @Fares intlgit.ResolvedFareList;
    INSERT INTO @Fares (TourDepartureId, FareBandId, Amount, PreviousAmount)
    EXEC intlgit.usp_GetFares @TourId = @TourId;

    DECLARE @Target TABLE (TourDepartureId INT NOT NULL, OccupancyId TINYINT NOT NULL,
                           PRIMARY KEY (TourDepartureId, OccupancyId));

    INSERT INTO @Target (TourDepartureId, OccupancyId)
    SELECT td.TourDepartureId, o.OccupancyId
    FROM @Cells AS c
        INNER JOIN intlgit.Hub AS h ON h.Code = c.HubCode
        INNER JOIN intlgit.TourHub AS th ON th.HubId = h.HubId AND th.TourId = @TourId
        INNER JOIN intlgit.TourDeparture AS td
            ON td.TourHubId = th.TourHubId AND td.DepartureDate = c.DepartureDate
        INNER JOIN intlgit.Occupancy AS o ON o.Code = c.OccupancyCode
        INNER JOIN intlgit.fn_PriceMatrix(@TourId, @Fares) AS m
            ON m.TourDepartureId = td.TourDepartureId AND m.OccupancyId = o.OccupancyId
    -- Same refusal as the bulk confirm, for the same reason: typing a
    -- figure into a box does not settle a dispute about the fare under it.
    WHERE m.IsQueried = 0
      AND m.HasFare = 1
      AND m.PublishedPrice IS NOT NULL;

    BEGIN TRANSACTION;

    MERGE intlgit.PriceConfirmation AS target
    USING @Target AS source
        ON source.TourDepartureId = target.TourDepartureId
       AND source.OccupancyId = target.OccupancyId
    WHEN MATCHED THEN
        UPDATE SET ConfirmedUtc = SYSUTCDATETIME(), ConfirmedBy = @ConfirmedBy
    WHEN NOT MATCHED BY TARGET THEN
        INSERT (TourDepartureId, OccupancyId, ConfirmedUtc, ConfirmedBy)
        VALUES (source.TourDepartureId, source.OccupancyId, SYSUTCDATETIME(), @ConfirmedBy);

    COMMIT TRANSACTION;
END
GO

-- ---------------------------------------------------------------------------
-- intlgit.usp_SetConditions
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE intlgit.usp_SetConditions
    @TourCode  VARCHAR(10),
    @Hubs      intlgit.HubCodeList   READONLY,  -- the grids that were on screen
    @Cells     intlgit.PriceCellList READONLY,  -- the ones ticked, within those
    @ChangedBy NVARCHAR(100) = NULL,

    -- Round-tripped from the page: the tour as the person saw it. NULL
    -- means the caller is not using the guard.
    @ExpectedModifiedUtc DATETIME2(0) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @TourId INT;
    SELECT @TourId = TourId FROM intlgit.Tour WHERE Code = @TourCode;

    IF @TourId IS NULL
        THROW 50001, 'Unknown tour code.', 1;

    -- Nothing on screen, nothing to say. Not an error: a save from a page with
    -- every hub collapsed posts no grid at all.
    IF NOT EXISTS (SELECT 1 FROM @Hubs)
        RETURN;

    BEGIN TRANSACTION;

    -- Inside the transaction, not before it: checking first and writing
    -- second leaves a gap in which two saves both pass the check.
    EXEC intlgit.usp_AssertUnchanged @TourId, @ExpectedModifiedUtc;

    -- Every departure the caller is speaking for.
    DECLARE @Scope TABLE (TourDepartureId INT NOT NULL PRIMARY KEY);

    INSERT INTO @Scope (TourDepartureId)
    SELECT td.TourDepartureId
    FROM intlgit.TourDeparture AS td
        INNER JOIN intlgit.TourHub AS th ON th.TourHubId = td.TourHubId
        INNER JOIN intlgit.Hub AS h ON h.HubId = th.HubId
        INNER JOIN @Hubs AS s ON s.HubCode = h.Code
    WHERE th.TourId = @TourId;

    DECLARE @Wanted TABLE (TourDepartureId INT NOT NULL, OccupancyId TINYINT NOT NULL,
                           PRIMARY KEY (TourDepartureId, OccupancyId));

    INSERT INTO @Wanted (TourDepartureId, OccupancyId)
    SELECT td.TourDepartureId, o.OccupancyId
    FROM @Cells AS c
        INNER JOIN intlgit.Hub AS h ON h.Code = c.HubCode
        INNER JOIN intlgit.TourHub AS th ON th.HubId = h.HubId AND th.TourId = @TourId
        INNER JOIN intlgit.TourDeparture AS td
            ON td.TourHubId = th.TourHubId AND td.DepartureDate = c.DepartureDate
        INNER JOIN intlgit.Occupancy AS o ON o.Code = c.OccupancyCode
        -- A cell outside the stated scope is ignored rather than trusted. The
        -- caller is only entitled to speak for the grids it showed.
        INNER JOIN @Scope AS s ON s.TourDepartureId = td.TourDepartureId;

    /*
        Both halves are captured, because a star is a change to what reaches
        the website and belongs in the log like any other. Until now it was
        the one edit on this screen that left no trace: the history could not
        say who starred a price, and a save refused BECAUSE of a star could
        not say what the star was.
    */
    DECLARE @Cleared TABLE (TourDepartureId INT, OccupancyId TINYINT);
    DECLARE @Added   TABLE (TourDepartureId INT, OccupancyId TINYINT);

    DELETE pc
    OUTPUT deleted.TourDepartureId, deleted.OccupancyId INTO @Cleared
    FROM intlgit.PriceCondition AS pc
        INNER JOIN @Scope AS s ON s.TourDepartureId = pc.TourDepartureId
    WHERE NOT EXISTS (SELECT 1 FROM @Wanted AS w
                      WHERE w.TourDepartureId = pc.TourDepartureId
                        AND w.OccupancyId = pc.OccupancyId);

    INSERT INTO intlgit.PriceCondition (TourDepartureId, OccupancyId, SetBy)
    OUTPUT inserted.TourDepartureId, inserted.OccupancyId INTO @Added
    SELECT w.TourDepartureId, w.OccupancyId, @ChangedBy
    FROM @Wanted AS w
    WHERE NOT EXISTS (SELECT 1 FROM intlgit.PriceCondition AS pc
                      WHERE pc.TourDepartureId = w.TourDepartureId
                        AND pc.OccupancyId = w.OccupancyId);

    IF @ChangedBy IS NOT NULL
        INSERT INTO intlgit.Activity
            (TourId, ActorName, ActorRole, Action, HubCode, DepartureDate, Field, Note)
        SELECT
            @TourId, @ChangedBy, 'product', 'conditions',
            h.Code, td.DepartureDate, o.Code,
            CASE WHEN c.applied = 1 THEN N'Conditions apply.'
                 ELSE N'Conditions no longer apply.' END
        FROM (
            SELECT TourDepartureId, OccupancyId, 1 AS applied FROM @Added
            UNION ALL
            SELECT TourDepartureId, OccupancyId, 0 FROM @Cleared
        ) AS c
            INNER JOIN intlgit.TourDeparture AS td ON td.TourDepartureId = c.TourDepartureId
            INNER JOIN intlgit.TourHub AS th ON th.TourHubId = td.TourHubId
            INNER JOIN intlgit.Hub AS h ON h.HubId = th.HubId
            INNER JOIN intlgit.Occupancy AS o ON o.OccupancyId = c.OccupancyId;

    UPDATE intlgit.Tour SET ModifiedUtc = SYSUTCDATETIME() WHERE TourId = @TourId;

    COMMIT TRANSACTION;
END
GO

/* =========================================================================
   TOUR STRUCTURE - hubs and departures
   ========================================================================= */

-- ---------------------------------------------------------------------------
-- intlgit.usp_AddTourHub
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE intlgit.usp_AddTourHub
    @TourCode  VARCHAR(10),
    @HubCode   VARCHAR(10),
    @MarkupPct DECIMAL(5, 2) = NULL,   -- NULL takes the hub master default
    @CopyDates BIT           = 1       -- 1 keeps today's behaviour for old callers
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @TourId INT, @HubId INT, @Markup DECIMAL(5, 2);

    SELECT @TourId = TourId FROM intlgit.Tour WHERE Code = @TourCode;
    SELECT @HubId = HubId, @Markup = COALESCE(@MarkupPct, DefaultMarkupPct)
    FROM intlgit.Hub WHERE Code = @HubCode;

    IF @TourId IS NULL THROW 50001, 'Unknown tour code.', 1;
    IF @HubId  IS NULL THROW 50003, 'Unknown hub code.', 1;

    IF EXISTS (SELECT 1 FROM intlgit.TourHub WHERE TourId = @TourId AND HubId = @HubId)
        THROW 50004, 'That hub is already on this tour.', 1;

    BEGIN TRANSACTION;

    INSERT INTO intlgit.TourHub (TourId, HubId, MarkupPct, IsActive)
    VALUES (@TourId, @HubId, @Markup, 1);

    DECLARE @TourHubId INT = CAST(SCOPE_IDENTITY() AS INT);

    /*
        The dates the tour is actually selling. The DATES are copied; the FARES
        are not - see below.

        Both IsActive filters matter, and so does the date: a withdrawn date is
        a decision somebody took, and a past one cannot be sold. Without them
        this INSERT undoes the first and resurrects the second, and its literal
        1 in the SELECT list marks whatever it finds as live.
    */
    IF @CopyDates = 1
    BEGIN
        INSERT INTO intlgit.TourDeparture (TourHubId, DepartureDate, IsActive)
        SELECT DISTINCT @TourHubId, td.DepartureDate, 1
        FROM intlgit.TourDeparture AS td
            INNER JOIN intlgit.TourHub AS th ON th.TourHubId = td.TourHubId
        WHERE th.TourId = @TourId
          AND th.TourHubId <> @TourHubId
          AND th.IsActive = 1
          AND td.IsActive = 1
          AND td.DepartureDate >= CAST(SYSUTCDATETIME() AS DATE);
    END

    /*
        Fare rows are created empty. Copying another hub's fare would look like
        data and is not: airfare differs by hub, and a price built on a guessed
        fare is a price nobody costed. The departure shows "awaiting airfare"
        until somebody enters a real figure.

        With no departures there are no fare rows either - usp_EnsureFares is
        handed an empty list, which is correct and is what a hub looks like
        before anything has been added to it.
    */
    DECLARE @NewDepartures intlgit.DepartureList;
    INSERT INTO @NewDepartures (TourDepartureId)
    SELECT TourDepartureId FROM intlgit.TourDeparture WHERE TourHubId = @TourHubId;

    EXEC intlgit.usp_EnsureFares
        @TourId = @TourId, @Departures = @NewDepartures, @SeedFromBaseFare = 0;

    UPDATE intlgit.Tour SET ModifiedUtc = SYSUTCDATETIME() WHERE TourId = @TourId;

    COMMIT TRANSACTION;

    SELECT @TourHubId AS TourHubId;
END
GO

-- ---------------------------------------------------------------------------
-- intlgit.usp_UpdateTourHub
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE intlgit.usp_UpdateTourHub
    @TourCode         VARCHAR(10),
    @HubCode          VARCHAR(10),
    @MarkupPct DECIMAL(5, 2) = NULL,   -- NULL leaves it unchanged
    @IsActive  BIT           = NULL    -- NULL leaves it unchanged
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    -- Both nullable: a hub that has never carried a margin has no old value,
    -- and one whose margin is being cleared has no new one.
    DECLARE @Changed TABLE (TourHubId INT NOT NULL,
                            OldMarkup DECIMAL(5, 2) NULL,
                            NewMarkup DECIMAL(5, 2) NULL);

    UPDATE th
    SET th.MarkupPct = COALESCE(@MarkupPct, th.MarkupPct),
        th.IsActive  = COALESCE(@IsActive,  th.IsActive)
    OUTPUT inserted.TourHubId, deleted.MarkupPct, inserted.MarkupPct
        INTO @Changed (TourHubId, OldMarkup, NewMarkup)
    FROM intlgit.TourHub AS th
        INNER JOIN intlgit.Tour AS t ON t.TourId = th.TourId
        INNER JOIN intlgit.Hub  AS h ON h.HubId  = th.HubId
    WHERE t.Code = @TourCode
      AND h.Code = @HubCode;

    IF @@ROWCOUNT = 0
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50002, 'That hub is not on this tour.', 1;
    END

    /* A markup change moves every price on the hub, so the gate re-opens.

       IS DISTINCT FROM, not <>: setting a margin for the first time is the
       largest price change a hub ever sees, and <> called it no change at all. */
    DELETE conf
    FROM intlgit.PriceConfirmation AS conf
        INNER JOIN intlgit.TourDeparture AS td ON td.TourDepartureId = conf.TourDepartureId
        INNER JOIN @Changed AS c ON c.TourHubId = td.TourHubId
    WHERE c.OldMarkup IS DISTINCT FROM c.NewMarkup;

    UPDATE t SET ModifiedUtc = SYSUTCDATETIME()
    FROM intlgit.Tour AS t WHERE t.Code = @TourCode;

    COMMIT TRANSACTION;
END
GO

-- ---------------------------------------------------------------------------
-- intlgit.usp_AddDeparture
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE intlgit.usp_AddDeparture
    @TourCode      VARCHAR(10),
    @HubCode       VARCHAR(10),
    @DepartureDate DATE
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @TourId INT, @TourHubId INT;

    SELECT @TourId = t.TourId, @TourHubId = th.TourHubId
    FROM intlgit.Tour AS t
        INNER JOIN intlgit.TourHub AS th ON th.TourId = t.TourId
        INNER JOIN intlgit.Hub AS h ON h.HubId = th.HubId
    WHERE t.Code = @TourCode AND h.Code = @HubCode;

    IF @TourHubId IS NULL THROW 50002, 'That hub is not on this tour.', 1;

    IF EXISTS (SELECT 1 FROM intlgit.TourDeparture
               WHERE TourHubId = @TourHubId AND DepartureDate = @DepartureDate)
        THROW 50005, 'That departure date is already on this hub.', 1;

    -- The tour's Joining / Leaving hub, if it has one and it is not the hub
    -- being added to.
    DECLARE @DirectTourHubId INT;

    SELECT @DirectTourHubId = th.TourHubId
    FROM intlgit.TourHub AS th
        INNER JOIN intlgit.Hub AS h ON h.HubId = th.HubId
    WHERE th.TourId = @TourId
      AND h.HasPassengerAirfare = 0
      AND th.TourHubId <> @TourHubId;

    -- Asked BEFORE the insert: is any other passenger hub already selling it?
    DECLARE @SoldElsewhere BIT = CASE WHEN EXISTS
    (
        SELECT 1
        FROM intlgit.TourDeparture AS td
            INNER JOIN intlgit.TourHub AS th ON th.TourHubId = td.TourHubId
        WHERE th.TourId = @TourId
          AND th.TourHubId NOT IN (@TourHubId, COALESCE(@DirectTourHubId, -1))
          AND th.IsActive = 1
          AND td.IsActive = 1
          AND td.DepartureDate = @DepartureDate
    ) THEN 1 ELSE 0 END;

    BEGIN TRANSACTION;

    INSERT INTO intlgit.TourDeparture (TourHubId, DepartureDate, IsActive)
    VALUES (@TourHubId, @DepartureDate, 1);

    DECLARE @TourDepartureId INT = CAST(SCOPE_IDENTITY() AS INT);

    DECLARE @NewDepartures intlgit.DepartureList;
    INSERT INTO @NewDepartures (TourDepartureId) VALUES (@TourDepartureId);

    /*
        And the same date on Joining / Leaving.

        Missing: added, blank, like any new date - air-ticketing enters the
        tour manager's fare in its adult band.

        Present but withdrawn, and no other hub is selling it: restored. That
        is a date that was withdrawn everywhere and is now being sold again.
        Withdrawn while other hubs still sold it means somebody took it off
        Joining / Leaving on purpose, and it stays off.
    */
    IF @DirectTourHubId IS NOT NULL
    BEGIN
        DECLARE @DirectDepartureId INT, @DirectIsActive BIT;

        SELECT @DirectDepartureId = TourDepartureId, @DirectIsActive = IsActive
        FROM intlgit.TourDeparture
        WHERE TourHubId = @DirectTourHubId AND DepartureDate = @DepartureDate;

        IF @DirectDepartureId IS NULL
        BEGIN
            INSERT INTO intlgit.TourDeparture (TourHubId, DepartureDate, IsActive)
            VALUES (@DirectTourHubId, @DepartureDate, 1);

            INSERT INTO @NewDepartures (TourDepartureId)
            VALUES (CAST(SCOPE_IDENTITY() AS INT));
        END
        ELSE IF @DirectIsActive = 0 AND @SoldElsewhere = 0
        BEGIN
            UPDATE intlgit.TourDeparture
            SET IsActive = 1
            WHERE TourDepartureId = @DirectDepartureId;
        END
    END

    -- Blank, for the reason given in usp_AddTourHub.
    EXEC intlgit.usp_EnsureFares
        @TourId = @TourId, @Departures = @NewDepartures, @SeedFromBaseFare = 0;

    UPDATE intlgit.Tour SET ModifiedUtc = SYSUTCDATETIME() WHERE TourId = @TourId;

    COMMIT TRANSACTION;

    -- The departure that was asked for, as before.
    SELECT @TourDepartureId AS TourDepartureId;
END
GO

-- ---------------------------------------------------------------------------
-- intlgit.usp_SetDepartureActive
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE intlgit.usp_SetDepartureActive
    @TourCode      VARCHAR(10),
    @HubCode       VARCHAR(10),
    @DepartureDate DATE,
    @IsActive      BIT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE td
    SET td.IsActive = @IsActive
    FROM intlgit.TourDeparture AS td
        INNER JOIN intlgit.TourHub AS th ON th.TourHubId = td.TourHubId
        INNER JOIN intlgit.Tour AS t ON t.TourId = th.TourId
        INNER JOIN intlgit.Hub  AS h ON h.HubId  = th.HubId
    WHERE t.Code = @TourCode
      AND h.Code = @HubCode
      AND td.DepartureDate = @DepartureDate;

    IF @@ROWCOUNT = 0
        THROW 50006, 'That departure is not on this hub.', 1;

    UPDATE t SET ModifiedUtc = SYSUTCDATETIME()
    FROM intlgit.Tour AS t WHERE t.Code = @TourCode;
END
GO

/* =========================================================================
   AIRFARE - fares, flight notes, requests, queries, hand-over
   ========================================================================= */

-- ---------------------------------------------------------------------------
-- intlgit.usp_GetFares
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE intlgit.usp_GetFares
    @TourId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT df.TourDepartureId, df.FareBandId, df.Amount, df.PreviousAmount
    FROM intlgit.DepartureFare AS df
    WHERE df.TourId = @TourId;
END
GO

-- ---------------------------------------------------------------------------
-- intlgit.usp_GetAllFares
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE intlgit.usp_GetAllFares
AS
BEGIN
    SET NOCOUNT ON;

    SELECT df.TourDepartureId, df.FareBandId, df.Amount, df.PreviousAmount
    FROM intlgit.DepartureFare AS df;
END
GO

-- ---------------------------------------------------------------------------
-- intlgit.usp_EnsureFares
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE intlgit.usp_EnsureFares
    @TourId            INT,
    @Departures        intlgit.DepartureList READONLY,
    @SeedFromBaseFare  BIT = 0
AS
BEGIN
    SET NOCOUNT ON;

    -- By code, never by the literal id, for the reason fn_PriceMatrix gives
    -- where it resolves the same band for the tour manager's seat.
    DECLARE @AdultBandId TINYINT =
        (SELECT FareBandId FROM intlgit.FareBand WHERE Code = 'adult');

    INSERT INTO intlgit.DepartureFare (TourId, TourDepartureId, FareBandId, Amount)
    SELECT
        @TourId,
        d.TourDepartureId,
        bf.FareBandId,
        CASE WHEN @SeedFromBaseFare = 1 THEN bf.Amount ELSE NULL END
    FROM @Departures AS d
        INNER JOIN intlgit.TourDeparture AS td ON td.TourDepartureId = d.TourDepartureId
        INNER JOIN intlgit.TourHub AS th ON th.TourHubId = td.TourHubId
        INNER JOIN intlgit.Hub AS h ON h.HubId = th.HubId
        CROSS JOIN intlgit.TourBaseFare AS bf
    WHERE bf.TourId = @TourId
      /*
          A hub the passenger does not fly from has one fare and one only: the
          tour manager's ticket, which air-ticketing quotes in the adult band.

          The other two are not created, rather than created and left empty.
          An empty fare row is exactly what both "awaiting fares" counts key on
          (usp_GetTourList and usp_ListFareRequests), and a row nobody is ever
          allowed to fill would mark that departure as owing a fare for ever -
          which blocks the tour from being submitted, permanently.
      */
      AND (h.HasPassengerAirfare = 1 OR bf.FareBandId = @AdultBandId)
      AND NOT EXISTS
      (
          SELECT 1
          FROM intlgit.DepartureFare AS existing
          WHERE existing.TourDepartureId = d.TourDepartureId
            AND existing.FareBandId = bf.FareBandId
      );
END
GO

-- ---------------------------------------------------------------------------
-- intlgit.usp_SaveFares
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE intlgit.usp_SaveFares
    @TourCode  VARCHAR(10),
    @Fares     intlgit.FareEditList READONLY,
    @ChangedBy NVARCHAR(100) = NULL,
    @ActorRole VARCHAR(20)   = 'airticketing',

    -- Round-tripped from the page: the tour as the person saw it. NULL
    -- means the caller is not using the guard.
    @ExpectedModifiedUtc DATETIME2(0) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @TourId INT;
    SELECT @TourId = TourId FROM intlgit.Tour WHERE Code = @TourCode;

    IF @TourId IS NULL
        THROW 50001, 'Unknown tour code.', 1;

    DECLARE @Resolved intlgit.ResolvedFareList;

    INSERT INTO @Resolved (TourDepartureId, FareBandId, Amount)
    SELECT td.TourDepartureId, fb.FareBandId, f.Amount
    FROM @Fares AS f
        INNER JOIN intlgit.Hub AS h ON h.Code = f.HubCode
        INNER JOIN intlgit.TourHub AS th ON th.HubId = h.HubId AND th.TourId = @TourId
        INNER JOIN intlgit.TourDeparture AS td
            ON td.TourHubId = th.TourHubId AND td.DepartureDate = f.DepartureDate
        INNER JOIN intlgit.FareBand AS fb ON fb.Code = f.FareBandCode
    /*
        A hub whose customers fly themselves has one fare: the tour manager's,
        entered in the adult band. A child or infant figure posted against it
        would create exactly the row usp_EnsureFares declines to create - one
        nobody is allowed to fill, which both "awaiting fares" counts then read
        as work outstanding for ever.

        Here rather than only in the page, because this is the boundary every
        caller crosses. The grid posts Fare[hub|date|band] as plain form keys
        and anybody can add one; a page rendered before this shipped will post
        them by accident.

        Dropped rather than refused, which is how this procedure already treats
        a hub or date that does not resolve - the INNER JOINs above. One stray
        key must not fail the save of a whole grid.
    */
    WHERE h.HasPassengerAirfare = 1 OR fb.Code = 'adult';

    -- The guard lives in the inner procedure, which is where the transaction
    -- is. Checking here and delegating afterwards would be the check-then-act
    -- gap this migration exists to close.
    EXEC intlgit.usp_SaveFaresForTour
        @TourId = @TourId, @Fares = @Resolved,
        @ChangedBy = @ChangedBy, @ActorRole = @ActorRole,
        @ExpectedModifiedUtc = @ExpectedModifiedUtc;

    UPDATE intlgit.Tour SET ModifiedUtc = SYSUTCDATETIME() WHERE TourId = @TourId;
END
GO

-- ---------------------------------------------------------------------------
-- intlgit.usp_SaveFaresForTour
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE intlgit.usp_SaveFaresForTour
    @TourId    INT,
    @Fares     intlgit.ResolvedFareList READONLY,
    @ChangedBy NVARCHAR(100) = NULL,
    @ActorRole VARCHAR(20)   = 'airticketing',

    -- Round-tripped from the page: the tour as the person saw it. NULL
    -- means the caller is not using the guard.
    @ExpectedModifiedUtc DATETIME2(0) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    -- Inside the transaction, not before it: checking first and writing
    -- second leaves a gap in which two saves both pass the check.
    EXEC intlgit.usp_AssertUnchanged @TourId, @ExpectedModifiedUtc;

    DECLARE @Changed TABLE
    (
        TourDepartureId INT            NOT NULL,
        FareBandId      TINYINT        NOT NULL,
        OldAmount       DECIMAL(12, 2) NULL,
        NewAmount       DECIMAL(12, 2) NULL
    );

    UPDATE df
    SET df.PreviousAmount = df.Amount,
        df.Amount = f.Amount,
        df.ModifiedUtc = SYSUTCDATETIME()
    OUTPUT deleted.TourDepartureId, deleted.FareBandId, deleted.Amount, inserted.Amount
        INTO @Changed (TourDepartureId, FareBandId, OldAmount, NewAmount)
    FROM intlgit.DepartureFare AS df
        INNER JOIN @Fares AS f
            ON f.TourDepartureId = df.TourDepartureId
           AND f.FareBandId = df.FareBandId
    WHERE df.TourId = @TourId
      AND df.Amount IS DISTINCT FROM f.Amount;

    INSERT INTO intlgit.DepartureFare (TourId, TourDepartureId, FareBandId, Amount)
    OUTPUT inserted.TourDepartureId, inserted.FareBandId, NULL, inserted.Amount
        INTO @Changed (TourDepartureId, FareBandId, OldAmount, NewAmount)
    SELECT @TourId, f.TourDepartureId, f.FareBandId, f.Amount
    FROM @Fares AS f
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM intlgit.DepartureFare AS df
        WHERE df.TourDepartureId = f.TourDepartureId
          AND df.FareBandId = f.FareBandId
    );

    /*
        The gate re-opens for exactly the prices this fare feeds - which, since
        the tour manager's seat entered the formula, is more than it was.

        A child fare feeds the two child occupancies and nothing else, as
        before. The ADULT fare feeds all six, because every price on the
        departure carries a share of the manager's ticket and the manager flies
        on an adult fare. Clearing only the three adult occupancies left the
        child and infant prices reading as agreed while their figures had
        quietly moved underneath - which is the exact thing this gate exists to
        prevent, reintroduced by the migration that corrected the formula.
    */
    DELETE conf
    FROM intlgit.PriceConfirmation AS conf
        INNER JOIN intlgit.Occupancy AS o ON o.OccupancyId = conf.OccupancyId
        INNER JOIN @Changed AS c ON c.TourDepartureId = conf.TourDepartureId
        CROSS JOIN (SELECT FareBandId FROM intlgit.FareBand WHERE Code = 'adult') AS tm
    WHERE c.FareBandId = o.FareBandId     -- the band this occupancy prices from
       OR c.FareBandId = tm.FareBandId;   -- or the adult fare, which they all carry

    /*
        And any query about that figure is answered by the act of changing it.
        Asking air-ticketing to go and separately mark it resolved would leave
        the departure held back on a dispute that no longer exists, waiting on
        a click nobody has a reason to make.

        A whole-departure query (FareBandId IS NULL) closes when any of its
        fares moves - the ask was "look at this date", and they have.
    */
    DECLARE @Resolved TABLE (TourDepartureId INT, FareBandId TINYINT NULL);

    UPDATE fq
    SET fq.ResolvedUtc    = SYSUTCDATETIME(),
        fq.ResolvedBy     = COALESCE(@ChangedBy, N'Air-ticketing'),
        fq.ResolutionNote = N'Fare revised.'
    OUTPUT inserted.TourDepartureId, inserted.FareBandId INTO @Resolved
    FROM intlgit.FareQuery AS fq
        INNER JOIN @Changed AS c ON c.TourDepartureId = fq.TourDepartureId
    WHERE fq.ResolvedUtc IS NULL
      AND (fq.FareBandId IS NULL OR fq.FareBandId = c.FareBandId);

    IF @ChangedBy IS NOT NULL
    BEGIN
        INSERT INTO intlgit.Activity
            (TourId, ActorName, ActorRole, Action, HubCode, DepartureDate, Field, OldValue, NewValue)
        SELECT
            @TourId, @ChangedBy, @ActorRole, 'fare',
            h.Code, td.DepartureDate, fb.Code, c.OldAmount, c.NewAmount
        FROM @Changed AS c
            INNER JOIN intlgit.TourDeparture AS td ON td.TourDepartureId = c.TourDepartureId
            INNER JOIN intlgit.TourHub AS th ON th.TourHubId = td.TourHubId
            INNER JOIN intlgit.Hub AS h ON h.HubId = th.HubId
            INNER JOIN intlgit.FareBand AS fb ON fb.FareBandId = c.FareBandId
        ORDER BY h.Code, td.DepartureDate, fb.FareBandId;

        INSERT INTO intlgit.Activity
            (TourId, ActorName, ActorRole, Action, HubCode, DepartureDate, Field, Note)
        SELECT DISTINCT
            @TourId, @ChangedBy, @ActorRole, 'fare-requoted',
            h.Code, td.DepartureDate, fb.Code, N'Fare revised.'
        FROM @Resolved AS r
            INNER JOIN intlgit.TourDeparture AS td ON td.TourDepartureId = r.TourDepartureId
            INNER JOIN intlgit.TourHub AS th ON th.TourHubId = td.TourHubId
            INNER JOIN intlgit.Hub AS h ON h.HubId = th.HubId
            LEFT JOIN intlgit.FareBand AS fb ON fb.FareBandId = r.FareBandId;
    END

    COMMIT TRANSACTION;
END
GO

-- ---------------------------------------------------------------------------
-- intlgit.usp_SaveFlightDetails
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE intlgit.usp_SaveFlightDetails
    @TourCode  VARCHAR(10),
    @Details   intlgit.FlightDetailList READONLY,
    @ChangedBy NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @TourId INT;
    SELECT @TourId = TourId FROM intlgit.Tour WHERE Code = @TourCode;

    IF @TourId IS NULL
        THROW 50001, 'Unknown tour code.', 1;

    BEGIN TRANSACTION;

    DECLARE @Resolved TABLE
    (
        TourDepartureId INT           NOT NULL PRIMARY KEY,
        Details         NVARCHAR(1000) NULL
    );

    /*
        Trimmed at the ends only. The line breaks inside are the point - this
        is somebody's note about an outbound, a connection and a return, and
        flattening it would be the same as truncating it.
    */
    INSERT INTO @Resolved (TourDepartureId, Details)
    SELECT td.TourDepartureId, NULLIF(LTRIM(RTRIM(d.Details)), N'')
    FROM @Details AS d
        INNER JOIN intlgit.Hub AS h ON h.Code = d.HubCode
        INNER JOIN intlgit.TourHub AS th ON th.HubId = h.HubId AND th.TourId = @TourId
        INNER JOIN intlgit.TourDeparture AS td
            ON td.TourHubId = th.TourHubId AND td.DepartureDate = d.DepartureDate;

    MERGE intlgit.DepartureFlight AS target
    USING @Resolved AS source
        ON source.TourDepartureId = target.TourDepartureId
    WHEN MATCHED AND ISNULL(target.Details, N'') <> ISNULL(source.Details, N'') THEN
        UPDATE SET Details = source.Details,
                   ModifiedUtc = SYSUTCDATETIME(),
                   ModifiedBy = @ChangedBy
    WHEN NOT MATCHED BY TARGET AND source.Details IS NOT NULL THEN
        INSERT (TourDepartureId, Details, ModifiedBy)
        VALUES (source.TourDepartureId, source.Details, @ChangedBy);

    UPDATE intlgit.Tour SET ModifiedUtc = SYSUTCDATETIME() WHERE TourId = @TourId;

    COMMIT TRANSACTION;
END
GO

-- ---------------------------------------------------------------------------
-- intlgit.usp_GetFlightSheet
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE intlgit.usp_GetFlightSheet
    @TourCode VARCHAR(10)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @TourId INT;
    SELECT @TourId = TourId FROM intlgit.Tour WHERE Code = @TourCode;

    IF @TourId IS NULL
        THROW 50001, 'Unknown tour code.', 1;

    SELECT t.Code AS TourCode, t.Name AS TourName, t.Region, t.Duration
    FROM intlgit.Tour AS t
    WHERE t.TourId = @TourId;

    SELECT
        fs.FareSubmissionId,
        fs.SubmittedUtc,
        fs.SubmittedBy,
        fs.Note
    FROM intlgit.FareSubmission AS fs
    WHERE fs.TourId = @TourId
    ORDER BY fs.SubmittedUtc, fs.FareSubmissionId;

    /*
        Everything submitted, then what stands now as submission id 0.

        IsActive is only meaningful on the live column. A submitted line is a
        record of a hand-over that has already happened, so it is reported as
        active whatever the date has done since - saying a past hand-over was
        "withdrawn" would be rewriting it.
    */
    SELECT
        fsl.FareSubmissionId,
        fsl.HubCode,
        h.Name AS HubName,
        fsl.DepartureDate,
        fsl.AdultFare,
        fsl.ChildFare,
        fsl.InfantFare,
        fsl.FlightDetails,
        IsActive = CAST(1 AS BIT)
    FROM intlgit.FareSubmissionLine AS fsl
        INNER JOIN intlgit.FareSubmission AS fs
            ON fs.FareSubmissionId = fsl.FareSubmissionId
        LEFT JOIN intlgit.Hub AS h ON h.Code = fsl.HubCode
    WHERE fs.TourId = @TourId

    UNION ALL

    -- The live column. Withdrawn hubs and dates are INCLUDED here, flagged,
    -- so the row survives on the sheet instead of vanishing from the right.
    SELECT
        0,
        h.Code,
        h.Name,
        td.DepartureDate,
        MAX(CASE WHEN fb.Code = 'adult'  THEN df.Amount END),
        MAX(CASE WHEN fb.Code = 'child'  THEN df.Amount END),
        MAX(CASE WHEN fb.Code = 'infant' THEN df.Amount END),
        MAX(fl.Details),
        CASE WHEN th.IsActive = 1 AND td.IsActive = 1
             THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END
    FROM intlgit.TourDeparture AS td
        INNER JOIN intlgit.TourHub AS th ON th.TourHubId = td.TourHubId
        INNER JOIN intlgit.Hub AS h ON h.HubId = th.HubId
        LEFT JOIN intlgit.DepartureFare AS df ON df.TourDepartureId = td.TourDepartureId
        LEFT JOIN intlgit.FareBand AS fb ON fb.FareBandId = df.FareBandId
        LEFT JOIN intlgit.DepartureFlight AS fl ON fl.TourDepartureId = td.TourDepartureId
    WHERE th.TourId = @TourId
    GROUP BY h.Code, h.Name, td.DepartureDate, th.IsActive, td.IsActive

    ORDER BY HubName, DepartureDate, FareSubmissionId;
END
GO

-- ---------------------------------------------------------------------------
-- intlgit.usp_GetWorkingColumnActor
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE intlgit.usp_GetWorkingColumnActor
    @TourCode VARCHAR(10)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @TourId INT, @Since DATETIME2(0);
    SELECT @TourId = TourId FROM intlgit.Tour WHERE Code = @TourCode;

    -- Only what has happened since the last hand-over: anything before that
    -- is already frozen into a column of its own.
    SELECT @Since = MAX(SubmittedUtc) FROM intlgit.FareSubmission WHERE TourId = @TourId;

    SELECT TOP (1)
        a.ActorName,
        a.ActorRole,
        a.OccurredUtc
    FROM intlgit.Activity AS a
    WHERE a.TourId = @TourId
      AND a.Action = 'fare'
      AND (@Since IS NULL OR a.OccurredUtc > @Since)
    ORDER BY a.OccurredUtc DESC, a.ActivityId DESC;
END
GO

-- ---------------------------------------------------------------------------
-- intlgit.usp_RequestFares
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE intlgit.usp_RequestFares
    @TourCode    VARCHAR(10),
    @RequestedBy NVARCHAR(100),
    @Note        NVARCHAR(1000) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @TourId INT;
    SELECT @TourId = TourId FROM intlgit.Tour WHERE Code = @TourCode;

    IF @TourId IS NULL
        THROW 50001, 'Unknown tour code.', 1;

    EXEC intlgit.usp_RequestFaresForTour
        @TourId = @TourId, @RequestedBy = @RequestedBy, @Note = @Note;
END
GO

-- ---------------------------------------------------------------------------
-- intlgit.usp_RequestFaresForTour
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE intlgit.usp_RequestFaresForTour
    @TourId      INT,
    @RequestedBy NVARCHAR(100),
    @Note        NVARCHAR(1000) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    UPDATE intlgit.FareRequest
    SET RequestedUtc = SYSUTCDATETIME(),
        RequestedBy  = @RequestedBy,
        Note         = @Note
    WHERE TourId = @TourId AND ClosedUtc IS NULL;

    IF @@ROWCOUNT = 0
        INSERT INTO intlgit.FareRequest (TourId, RequestedUtc, RequestedBy, Note)
        VALUES (@TourId, SYSUTCDATETIME(), @RequestedBy, @Note);

    INSERT INTO intlgit.Activity (TourId, ActorName, ActorRole, Action, Note)
    VALUES (@TourId, @RequestedBy, 'product', 'fares-requested', @Note);

    /*
        And air-ticketing is told, in this transaction. Built into variables
        first because EXEC will not take an expression as an argument.
    */
    DECLARE @rfCode VARCHAR(10), @rfName NVARCHAR(200);
    SELECT @rfCode = Code, @rfName = Name FROM intlgit.Tour WHERE TourId = @TourId;

    DECLARE
        @rfSubject NVARCHAR(200) = N'Fares requested for ' + @rfCode,
        @rfBody    NVARCHAR(1000) =
            COALESCE(NULLIF(@RequestedBy, N''), N'The product team')
            + N' asked for fares on ' + @rfCode + N' — ' + COALESCE(@rfName, N'') + N'.'
            + CASE WHEN @Note IS NULL OR LEN(@Note) = 0
                   THEN N'' ELSE N' “' + @Note + N'”' END,
        @rfLink    NVARCHAR(400) = N'/IntlGit/Revision?code=' + @rfCode;

    EXEC core.usp_NotifyRole
        @Role     = 'airticketing.executive',
        @Event    = 'fares-requested',
        @Subject  = @rfSubject,
        @Body     = @rfBody,
        @TourCode = @rfCode,
        @Link     = @rfLink;

    COMMIT TRANSACTION;
END
GO

-- ---------------------------------------------------------------------------
-- intlgit.usp_ListFareRequests
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE intlgit.usp_ListFareRequests
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        fr.FareRequestId,
        t.Code          AS TourCode,
        t.Name          AS TourName,
        t.Region,
        fr.RequestedUtc,
        fr.RequestedBy,
        fr.Note,
        AwaitingFares = (
            SELECT COUNT(DISTINCT df.TourDepartureId)
            FROM intlgit.DepartureFare AS df
            WHERE df.TourId = t.TourId AND df.Amount IS NULL
        ),
        QueriedDepartures = (
            SELECT COUNT(DISTINCT fq.TourDepartureId)
            FROM intlgit.FareQuery AS fq
                INNER JOIN intlgit.TourDeparture AS td
                    ON td.TourDepartureId = fq.TourDepartureId
                INNER JOIN intlgit.TourHub AS th
                    ON th.TourHubId = td.TourHubId
            WHERE th.TourId = t.TourId
              AND fq.ResolvedUtc IS NULL
              AND th.IsActive = 1
              AND td.IsActive = 1
        ),
        EarliestDeparture = (
            SELECT MIN(d.DepartureDate)
            FROM (
                SELECT td.DepartureDate
                FROM intlgit.DepartureFare AS df
                    INNER JOIN intlgit.TourDeparture AS td
                        ON td.TourDepartureId = df.TourDepartureId
                WHERE df.TourId = t.TourId AND df.Amount IS NULL

                UNION ALL

                SELECT td.DepartureDate
                FROM intlgit.FareQuery AS fq
                    INNER JOIN intlgit.TourDeparture AS td
                        ON td.TourDepartureId = fq.TourDepartureId
                    INNER JOIN intlgit.TourHub AS th
                        ON th.TourHubId = td.TourHubId
                WHERE th.TourId = t.TourId
                  AND fq.ResolvedUtc IS NULL
                  AND th.IsActive = 1
                  AND td.IsActive = 1
            ) AS d
        )
    FROM intlgit.FareRequest AS fr
        INNER JOIN intlgit.Tour AS t ON t.TourId = fr.TourId
    WHERE fr.ClosedUtc IS NULL
    ORDER BY fr.RequestedUtc;
END
GO

-- ---------------------------------------------------------------------------
-- intlgit.usp_RaiseFareQuery
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE intlgit.usp_RaiseFareQuery
    @TourCode      VARCHAR(10),
    @HubCode       VARCHAR(10),
    @DepartureDate DATE,
    @FareBandCode  VARCHAR(10) = NULL,   -- null queries the whole departure
    @Reason        NVARCHAR(1000) = NULL,
    @RaisedBy      NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @TourId INT, @DepartureId INT, @BandId TINYINT;

    SELECT @TourId = TourId FROM intlgit.Tour WHERE Code = @TourCode;

    IF @TourId IS NULL
        THROW 50001, 'Unknown tour code.', 1;

    SELECT @DepartureId = td.TourDepartureId
    FROM intlgit.TourDeparture AS td
        INNER JOIN intlgit.TourHub AS th ON th.TourHubId = td.TourHubId
        INNER JOIN intlgit.Hub AS h ON h.HubId = th.HubId
    WHERE th.TourId = @TourId AND h.Code = @HubCode AND td.DepartureDate = @DepartureDate;

    IF @DepartureId IS NULL
        THROW 50002, 'That departure is not on this tour.', 1;

    IF @FareBandCode IS NOT NULL
    BEGIN
        SELECT @BandId = FareBandId FROM intlgit.FareBand WHERE Code = @FareBandCode;

        IF @BandId IS NULL
            THROW 50006, 'Unknown fare band.', 1;
    END

    BEGIN TRANSACTION;

    /*
        One open query per line, enforced here rather than by a unique filtered
        index - see the note on the index in 0041. A second ask about the same
        fare updates the first: air-ticketing has not answered yet, and two
        rows saying the same thing would hold the departure twice over and take
        two resolutions to release.
    */
    UPDATE intlgit.FareQuery
    SET RaisedUtc = SYSUTCDATETIME(),
        RaisedBy  = @RaisedBy,
        Reason    = @Reason
    WHERE TourDepartureId = @DepartureId
      AND ResolvedUtc IS NULL
      AND ((@BandId IS NULL AND FareBandId IS NULL)
           OR FareBandId = @BandId);

    IF @@ROWCOUNT = 0
        INSERT INTO intlgit.FareQuery
            (TourDepartureId, FareBandId, RaisedBy, Reason)
        VALUES (@DepartureId, @BandId, @RaisedBy, @Reason);

    /*
        And the tour goes on air-ticketing's queue, which is the only thing
        that makes a send-back reach them. In the same transaction as the
        query: a query that exists without the tour being on the queue is the
        bug this migration closes, and two statements outside one transaction
        would let it happen again on any failure between them.

        Only when nothing is open - see the header note on why this does not
        refresh an existing request.
    */
    IF NOT EXISTS (SELECT 1 FROM intlgit.FareRequest
                   WHERE TourId = @TourId AND ClosedUtc IS NULL)
        INSERT INTO intlgit.FareRequest (TourId, RequestedUtc, RequestedBy, Note)
        VALUES (@TourId, SYSUTCDATETIME(), @RaisedBy,
                N'Fare sent back for a re-quote.');

    -- The product team disagreeing with a figure is a change on this tour, and
    -- belongs in the same log as every other change.
    INSERT INTO intlgit.Activity
        (TourId, ActorName, ActorRole, Action, HubCode, DepartureDate, Field, Note)
    VALUES
        (@TourId, @RaisedBy, 'product', 'fare-queried',
         @HubCode, @DepartureDate, @FareBandCode, @Reason);

    UPDATE intlgit.Tour SET ModifiedUtc = SYSUTCDATETIME() WHERE TourId = @TourId;
    /*
        And air-ticketing is told. This is the most valuable of the four
        notifications: until round G a send-back reached them only if they
        happened to open that one tour, and the fare queue above was the first
        half of closing that. This is the second.
    */
    DECLARE @fqCode VARCHAR(10), @fqName NVARCHAR(200), @fqHub NVARCHAR(100);
    SELECT @fqCode = Code, @fqName = Name FROM intlgit.Tour WHERE TourId = @TourId;
    SELECT @fqHub = Name FROM intlgit.Hub WHERE Code = @HubCode;

    DECLARE
        @fqSubject NVARCHAR(200) = N'A fare was sent back on ' + @fqCode,
        @fqBody    NVARCHAR(1000) =
            CASE WHEN @FareBandCode IS NULL
                 THEN N'The fares on '
                 ELSE N'The ' + @FareBandCode + N' fare on ' END
            + @fqHub + N' ' + CONVERT(NVARCHAR(11), @DepartureDate, 106)
            + N' was sent back by ' + @RaisedBy + N'.'
            + CASE WHEN @Reason IS NULL OR LEN(@Reason) = 0
                   THEN N'' ELSE N' “' + @Reason + N'”' END,
        @fqLink    NVARCHAR(400) = N'/IntlGit/Revision?code=' + @fqCode;

    EXEC core.usp_NotifyRole
        @Role     = 'airticketing.executive',
        @Event    = 'fare-queried',
        @Subject  = @fqSubject,
        @Body     = @fqBody,
        @TourCode = @fqCode,
        @Link     = @fqLink;


    COMMIT TRANSACTION;
END
GO

-- ---------------------------------------------------------------------------
-- intlgit.usp_ResolveFareQuery
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE intlgit.usp_ResolveFareQuery
    @TourCode   VARCHAR(10),
    @FareQueryId INT,
    @Note       NVARCHAR(1000) = NULL,
    @ResolvedBy NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @TourId INT;
    SELECT @TourId = TourId FROM intlgit.Tour WHERE Code = @TourCode;

    IF @TourId IS NULL
        THROW 50001, 'Unknown tour code.', 1;

    BEGIN TRANSACTION;

    DECLARE @Closed TABLE (HubCode VARCHAR(10), DepartureDate DATE, BandCode VARCHAR(10));

    UPDATE fq
    SET fq.ResolvedUtc    = SYSUTCDATETIME(),
        fq.ResolvedBy     = @ResolvedBy,
        fq.ResolutionNote = @Note
    OUTPUT h.Code, td.DepartureDate, fb.Code INTO @Closed
    FROM intlgit.FareQuery AS fq
        INNER JOIN intlgit.TourDeparture AS td ON td.TourDepartureId = fq.TourDepartureId
        INNER JOIN intlgit.TourHub AS th ON th.TourHubId = td.TourHubId
        INNER JOIN intlgit.Hub AS h ON h.HubId = th.HubId
        LEFT JOIN intlgit.FareBand AS fb ON fb.FareBandId = fq.FareBandId
    WHERE fq.FareQueryId = @FareQueryId
      AND fq.ResolvedUtc IS NULL
      AND th.TourId = @TourId;

    IF @@ROWCOUNT = 0
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50007, 'That query is not open on this tour.', 1;
    END

    INSERT INTO intlgit.Activity
        (TourId, ActorName, ActorRole, Action, HubCode, DepartureDate, Field, Note)
    SELECT @TourId, @ResolvedBy, 'airticketing', 'fare-requoted',
           c.HubCode, c.DepartureDate, c.BandCode, @Note
    FROM @Closed AS c;

    UPDATE intlgit.Tour SET ModifiedUtc = SYSUTCDATETIME() WHERE TourId = @TourId;

    COMMIT TRANSACTION;
END
GO

-- ---------------------------------------------------------------------------
-- intlgit.usp_GetFareQueries
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE intlgit.usp_GetFareQueries
    @TourCode VARCHAR(10),
    @OpenOnly BIT = 1
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @TourId INT;
    SELECT @TourId = TourId FROM intlgit.Tour WHERE Code = @TourCode;

    IF @TourId IS NULL
        THROW 50001, 'Unknown tour code.', 1;

    SELECT
        fq.FareQueryId,
        h.Code          AS HubCode,
        h.Name          AS HubName,
        td.DepartureDate,
        fb.Code         AS FareBandCode,
        fb.Label        AS FareBandLabel,
        df.Amount       AS Airfare,
        fq.RaisedUtc,
        fq.RaisedBy,
        fq.Reason,
        fq.ResolvedUtc,
        fq.ResolvedBy,
        fq.ResolutionNote
    FROM intlgit.FareQuery AS fq
        INNER JOIN intlgit.TourDeparture AS td ON td.TourDepartureId = fq.TourDepartureId
        INNER JOIN intlgit.TourHub AS th ON th.TourHubId = td.TourHubId
        INNER JOIN intlgit.Hub AS h ON h.HubId = th.HubId
        LEFT JOIN intlgit.FareBand AS fb ON fb.FareBandId = fq.FareBandId
        LEFT JOIN intlgit.DepartureFare AS df
            ON df.TourDepartureId = fq.TourDepartureId
           AND df.FareBandId = fq.FareBandId
    WHERE th.TourId = @TourId
      AND (@OpenOnly = 0 OR fq.ResolvedUtc IS NULL)
    ORDER BY fq.ResolvedUtc, h.Code, td.DepartureDate, fb.FareBandId;
END
GO

-- ---------------------------------------------------------------------------
-- intlgit.usp_SubmitFares
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE intlgit.usp_SubmitFares
    @TourCode    VARCHAR(10),
    @SubmittedBy NVARCHAR(100),
    @Note        NVARCHAR(1000) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @TourId INT;
    SELECT @TourId = TourId FROM intlgit.Tour WHERE Code = @TourCode;

    IF @TourId IS NULL
        THROW 50001, 'Unknown tour code.', 1;

    EXEC intlgit.usp_SubmitFaresForTour
        @TourId = @TourId, @SubmittedBy = @SubmittedBy, @Note = @Note;
END
GO

-- ---------------------------------------------------------------------------
-- intlgit.usp_SubmitFaresForTour
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE intlgit.usp_SubmitFaresForTour
    @TourId      INT,
    @SubmittedBy NVARCHAR(100),
    @Note        NVARCHAR(1000) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    INSERT INTO intlgit.FareSubmission (TourId, SubmittedUtc, SubmittedBy, Note)
    VALUES (@TourId, SYSUTCDATETIME(), @SubmittedBy, @Note);

    DECLARE @SubmissionId INT = SCOPE_IDENTITY();

    /*
        One row per departure, all three fares across it, plus whatever flight
        details stand at this moment. Withdrawn hubs and dates are left out:
        they are off the website, so they are not part of what was handed
        over.
    */
    INSERT INTO intlgit.FareSubmissionLine
        (FareSubmissionId, HubCode, DepartureDate,
         AdultFare, ChildFare, InfantFare, FlightDetails)
    SELECT
        @SubmissionId,
        h.Code,
        td.DepartureDate,
        MAX(CASE WHEN fb.Code = 'adult'  THEN df.Amount END),
        MAX(CASE WHEN fb.Code = 'child'  THEN df.Amount END),
        MAX(CASE WHEN fb.Code = 'infant' THEN df.Amount END),
        MAX(fl.Details)
    FROM intlgit.TourDeparture AS td
        INNER JOIN intlgit.TourHub AS th ON th.TourHubId = td.TourHubId
        INNER JOIN intlgit.Hub AS h ON h.HubId = th.HubId
        LEFT JOIN intlgit.DepartureFare AS df ON df.TourDepartureId = td.TourDepartureId
        LEFT JOIN intlgit.FareBand AS fb ON fb.FareBandId = df.FareBandId
        LEFT JOIN intlgit.DepartureFlight AS fl ON fl.TourDepartureId = td.TourDepartureId
    WHERE th.TourId = @TourId
      AND th.IsActive = 1
      AND td.IsActive = 1
    GROUP BY h.Code, td.DepartureDate;

    UPDATE intlgit.FareRequest
    SET ClosedUtc = SYSUTCDATETIME()
    WHERE TourId = @TourId AND ClosedUtc IS NULL;

    INSERT INTO intlgit.Activity (TourId, ActorName, ActorRole, Action, Note)
    VALUES (@TourId, @SubmittedBy, 'airticketing', 'fares-sent', @Note);
    /*
        And the product team is told, in this transaction rather than after it.
        The fares saving and the product team hearing about it are one fact:
        if the submission rolls back there is nothing to tell them about, and
        if it commits they must not be able to miss it.

        The message is built into variables first because EXEC will not take an
        expression as an argument - only a constant or a variable.
    */
    DECLARE @sfCode VARCHAR(10), @sfName NVARCHAR(200);
    SELECT @sfCode = Code, @sfName = Name FROM intlgit.Tour WHERE TourId = @TourId;

    DECLARE
        @sfSubject NVARCHAR(200) = N'Air-ticketing sent fares for ' + @sfCode,
        @sfBody    NVARCHAR(1000) =
            N'Air-ticketing sent fares for ' + @sfCode + N' — ' + @sfName + N'.'
            + CASE WHEN @Note IS NULL OR LEN(@Note) = 0
                   THEN N'' ELSE N' “' + @Note + N'”' END,
        @sfLink    NVARCHAR(400) = N'/IntlGit/Revision?code=' + @sfCode;

    EXEC core.usp_NotifyRole
        @Role     = 'product.executive',
        @Event    = 'fares-received',
        @Subject  = @sfSubject,
        @Body     = @sfBody,
        @TourCode = @sfCode,
        @Link     = @sfLink;


    COMMIT TRANSACTION;
END
GO

/* =========================================================================
   HISTORY - activity log, versions, snapshots, promotion
   ========================================================================= */

-- ---------------------------------------------------------------------------
-- intlgit.usp_GetTourActivity
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE intlgit.usp_GetTourActivity
    @TourCode VARCHAR(10),
    @Take     INT = 200
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @TourId INT;
    SELECT @TourId = TourId FROM intlgit.Tour WHERE Code = @TourCode;

    IF @TourId IS NULL
        THROW 50001, 'Unknown tour code.', 1;

    IF @Take IS NULL OR @Take < 1 OR @Take > 1000 SET @Take = 200;

    SELECT TOP (@Take)
        a.ActivityId,
        a.OccurredUtc,
        a.ActorName,
        a.ActorRole,
        a.Action,
        a.HubCode,
        h.Name AS HubName,
        a.DepartureDate,
        a.Field,
        a.OldValue,
        a.NewValue,
        a.Note
    FROM intlgit.Activity AS a
        LEFT JOIN intlgit.Hub AS h ON h.Code = a.HubCode
    WHERE a.TourId = @TourId
    ORDER BY a.ActivityId DESC;
END
GO

-- ---------------------------------------------------------------------------
-- intlgit.usp_GetVersionHistory
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE intlgit.usp_GetVersionHistory
    @TourCode VARCHAR(10)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        tvh.Version,
        tvh.ChangeSetId AS ChangeSetRef,
        tvh.Changes,
        tvh.Summary,
        tvh.AppliedUtc,
        HasSnapshot = CASE WHEN vs.VersionSnapshotId IS NULL
                           THEN CAST(0 AS BIT) ELSE CAST(1 AS BIT) END
    FROM intlgit.TourVersionHistory AS tvh
        INNER JOIN intlgit.Tour AS t ON t.TourId = tvh.TourId
        LEFT JOIN intlgit.VersionSnapshot AS vs
            ON vs.TourId = tvh.TourId AND vs.Version = tvh.Version
    WHERE t.Code = @TourCode
    ORDER BY tvh.AppliedUtc DESC, tvh.TourVersionHistoryId DESC;
END
GO

-- ---------------------------------------------------------------------------
-- intlgit.usp_GetVersionSnapshot
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE intlgit.usp_GetVersionSnapshot
    @TourCode VARCHAR(10),
    @Version  VARCHAR(10)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @SnapshotId INT;

    SELECT @SnapshotId = vs.VersionSnapshotId
    FROM intlgit.VersionSnapshot AS vs
        INNER JOIN intlgit.Tour AS t ON t.TourId = vs.TourId
    WHERE t.Code = @TourCode AND vs.Version = @Version;

    SELECT
        vs.Version,
        vs.ChangeSetRef,
        vs.CapturedUtc,
        vs.FxRate,
        vs.StrikePct,
        vs.PaxSlab,
        vs.SharedCost,
        vs.Note,
        t.Code   AS TourCode,
        t.Name   AS TourName,
        t.Region,
        t.Duration,
        t.CurrentVersion
    FROM intlgit.VersionSnapshot AS vs
        INNER JOIN intlgit.Tour AS t ON t.TourId = vs.TourId
    WHERE vs.VersionSnapshotId = @SnapshotId;

    SELECT OccupancyCode, OccupancyLabel, SortOrder, LandCostFx, PerPersonInr
    FROM intlgit.VersionSnapshotOccupancy
    WHERE VersionSnapshotId = @SnapshotId
    ORDER BY SortOrder;

    SELECT HubCode, HubName, MarkupPct, IsActive
    FROM intlgit.VersionSnapshotHub
    WHERE VersionSnapshotId = @SnapshotId
    ORDER BY HubName;

    SELECT HubCode, DepartureDate, IsActive
    FROM intlgit.VersionSnapshotDeparture
    WHERE VersionSnapshotId = @SnapshotId
    ORDER BY HubCode, DepartureDate;

    SELECT HubCode, DepartureDate, BandCode, Amount
    FROM intlgit.VersionSnapshotFare
    WHERE VersionSnapshotId = @SnapshotId
    ORDER BY HubCode, DepartureDate, BandCode;

    SELECT HubCode, DepartureDate, OccupancyCode, PublishedPrice, StrikeThrough, HasConditions
    FROM intlgit.VersionSnapshotPrice
    WHERE VersionSnapshotId = @SnapshotId
    ORDER BY HubCode, DepartureDate, OccupancyCode;
END
GO

-- ---------------------------------------------------------------------------
-- intlgit.usp_RecordSubmittedPrices
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE intlgit.usp_RecordSubmittedPrices
    @TourCode     VARCHAR(10),
    @ChangeSetRef VARCHAR(20),
    @Prices       intlgit.PriceList READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @TourId INT;
    SELECT @TourId = TourId FROM intlgit.Tour WHERE Code = @TourCode;

    IF @TourId IS NULL
        THROW 50001, 'Unknown tour code.', 1;

    IF NOT EXISTS (SELECT 1 FROM @Prices)
        RETURN;

    BEGIN TRANSACTION;

    MERGE intlgit.SubmittedPriceSnapshot AS target
    USING
    (
        SELECT td.TourDepartureId, o.OccupancyId, p.Price
        FROM @Prices AS p
            INNER JOIN intlgit.Hub AS h ON h.Code = p.HubCode
            INNER JOIN intlgit.TourHub AS th
                ON th.HubId = h.HubId AND th.TourId = @TourId
            INNER JOIN intlgit.TourDeparture AS td
                ON td.TourHubId = th.TourHubId AND td.DepartureDate = p.DepartureDate
            INNER JOIN intlgit.Occupancy AS o ON o.Code = p.OccupancyCode
    ) AS source
        ON source.TourDepartureId = target.TourDepartureId
       AND source.OccupancyId = target.OccupancyId
    WHEN MATCHED THEN
        UPDATE SET Price = source.Price,
                   ChangeSetRef = @ChangeSetRef,
                   SubmittedUtc = SYSUTCDATETIME()
    WHEN NOT MATCHED BY TARGET THEN
        INSERT (TourDepartureId, OccupancyId, Price, ChangeSetRef, SubmittedUtc)
        VALUES (source.TourDepartureId, source.OccupancyId, source.Price,
                @ChangeSetRef, SYSUTCDATETIME());

    COMMIT TRANSACTION;
END
GO

-- ---------------------------------------------------------------------------
-- intlgit.usp_RecordSubmittedRemovals
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE intlgit.usp_RecordSubmittedRemovals
    @TourCode     VARCHAR(10),
    @ChangeSetRef VARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @TourId INT;
    SELECT @TourId = TourId FROM intlgit.Tour WHERE Code = @TourCode;

    IF @TourId IS NULL
        THROW 50001, 'Unknown tour code.', 1;

    /*
        Everything currently withdrawn AND still on the website is what the set
        just asked tech support to remove. Derived here rather than passed in:
        it is the same query the change list uses, so the two cannot disagree.
    */
    INSERT INTO intlgit.SubmittedRemoval (TourDepartureId, ChangeSetRef, SubmittedUtc)
    SELECT td.TourDepartureId, @ChangeSetRef, SYSUTCDATETIME()
    FROM intlgit.TourDeparture AS td
        INNER JOIN intlgit.TourHub AS th ON th.TourHubId = td.TourHubId
    WHERE th.TourId = @TourId
      AND (th.IsActive = 0 OR td.IsActive = 0)
      AND EXISTS (SELECT 1 FROM intlgit.LivePriceSnapshot AS s
                  WHERE s.TourDepartureId = td.TourDepartureId)
      AND NOT EXISTS (SELECT 1 FROM intlgit.SubmittedRemoval AS sr
                      WHERE sr.TourDepartureId = td.TourDepartureId);
END
GO

-- ---------------------------------------------------------------------------
-- intlgit.usp_ClearSubmittedPrices
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE intlgit.usp_ClearSubmittedPrices
    @TourId       INT,
    @ChangeSetRef VARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;

    /* The tour is still in the join. The reference alone would be enough -
       it is unique across the application - but a change set belongs to
       exactly one tour, and saying so here means a mismatched pair deletes
       nothing rather than reaching into another tour's rows. */
    DELETE sub
    FROM intlgit.SubmittedPriceSnapshot AS sub
        INNER JOIN intlgit.TourDeparture AS td ON td.TourDepartureId = sub.TourDepartureId
        INNER JOIN intlgit.TourHub AS th ON th.TourHubId = td.TourHubId
    WHERE th.TourId = @TourId
      AND sub.ChangeSetRef = @ChangeSetRef;

    DELETE sr
    FROM intlgit.SubmittedRemoval AS sr
        INNER JOIN intlgit.TourDeparture AS td ON td.TourDepartureId = sr.TourDepartureId
        INNER JOIN intlgit.TourHub AS th ON th.TourHubId = td.TourHubId
    WHERE th.TourId = @TourId
      AND sr.ChangeSetRef = @ChangeSetRef;
END
GO

-- ---------------------------------------------------------------------------
-- intlgit.usp_NotifyChangeSetSubmitted
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE intlgit.usp_NotifyChangeSetSubmitted
    @TourCode     VARCHAR(10),
    @ChangeSetRef VARCHAR(20),
    @SubmittedBy  NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @csName NVARCHAR(200);
    SELECT @csName = Name FROM intlgit.Tour WHERE Code = @TourCode;

    IF @csName IS NULL
        THROW 50001, 'Unknown tour code.', 1;

    DECLARE
        @csSubject NVARCHAR(200) = @ChangeSetRef + N' is ready for the website',
        @csBody    NVARCHAR(1000) =
            @TourCode + N' — ' + @csName + N'. Submitted by '
            + COALESCE(@SubmittedBy, N'the product team') + N'.',
        @csLink    NVARCHAR(400) = N'/IntlGit/ChangeSets/Detail?reference=' + @ChangeSetRef;

    EXEC core.usp_NotifyRole
        @Role      = 'techsupport',
        @Event     = 'changeset-submitted',
        @Subject   = @csSubject,
        @Body      = @csBody,
        @TourCode  = @TourCode,
        @Reference = @ChangeSetRef,
        @Link      = @csLink;
END
GO

-- ---------------------------------------------------------------------------
-- intlgit.usp_PromoteVersion
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE intlgit.usp_PromoteVersion
    @TourCode    VARCHAR(10),
    @NewVersion  VARCHAR(10),
    @ChangeSetId VARCHAR(20),
    @Changes     INT,
    @Summary     NVARCHAR(500),
    @SubmittedByEmail NVARCHAR(200) = NULL,
    @Prices      intlgit.PriceList READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @TourId INT;
    SELECT @TourId = TourId FROM intlgit.Tour WHERE Code = @TourCode;

    IF @TourId IS NULL THROW 50001, 'Unknown tour code.', 1;

    /*
        Already at or past this version, so this note is a repeat or has been
        overtaken. Nothing to do, and nothing wrong - see the header.

        Parsed rather than compared as text: 'v100' sorts before 'v99' as a
        string. An unreadable current version counts as 0 so that a tour in a
        state nobody planned still moves forward rather than sticking.
    */
    DECLARE @CurrentNumber INT =
    (
        SELECT COALESCE(TRY_CAST(SUBSTRING(CurrentVersion, 2, 10) AS INT), 0)
        FROM intlgit.Tour WHERE TourId = @TourId
    );

    IF COALESCE(TRY_CAST(SUBSTRING(@NewVersion, 2, 10) AS INT), 0) <= @CurrentNumber
        RETURN;

    BEGIN TRANSACTION;

    -- The supplied prices are written as given, never recalculated: what
    -- belongs in the snapshot is what actually went on the website.
    MERGE intlgit.LivePriceSnapshot AS target
    USING
    (
        SELECT td.TourDepartureId, o.OccupancyId, p.Price
        FROM @Prices AS p
            INNER JOIN intlgit.Hub AS h ON h.Code = p.HubCode
            INNER JOIN intlgit.TourHub AS th ON th.HubId = h.HubId AND th.TourId = @TourId
            INNER JOIN intlgit.TourDeparture AS td
                ON td.TourHubId = th.TourHubId AND td.DepartureDate = p.DepartureDate
            INNER JOIN intlgit.Occupancy AS o ON o.Code = p.OccupancyCode
    ) AS source
        ON source.TourDepartureId = target.TourDepartureId
       AND source.OccupancyId = target.OccupancyId
    WHEN MATCHED THEN
        UPDATE SET Price = source.Price, CapturedUtc = SYSUTCDATETIME()
    WHEN NOT MATCHED BY TARGET THEN
        INSERT (TourDepartureId, OccupancyId, Price, CapturedUtc)
        VALUES (source.TourDepartureId, source.OccupancyId, source.Price, SYSUTCDATETIME());

    -- Anything withdrawn is off the website, so it leaves the snapshot.
    DELETE s
    FROM intlgit.LivePriceSnapshot AS s
        INNER JOIN intlgit.TourDeparture AS td ON td.TourDepartureId = s.TourDepartureId
        INNER JOIN intlgit.TourHub AS th ON th.TourHubId = td.TourHubId
    WHERE th.TourId = @TourId
      AND (th.IsActive = 0 OR td.IsActive = 0);

    -- Live and submitted now agree, so the baseline goes.
    EXEC intlgit.usp_ClearSubmittedPrices
        @TourId = @TourId, @ChangeSetRef = @ChangeSetId;

    UPDATE intlgit.CostBuild SET PreviousFxRate = FxRate WHERE TourId = @TourId;

    UPDATE intlgit.Tour
    SET CurrentVersion = @NewVersion, ModifiedUtc = SYSUTCDATETIME()
    WHERE TourId = @TourId;

    INSERT INTO intlgit.TourVersionHistory
        (TourId, Version, ChangeSetId, Changes, Summary, AppliedUtc)
    VALUES
        (@TourId, @NewVersion, @ChangeSetId, @Changes, @Summary, SYSUTCDATETIME());

    /*
        And the photograph, last, so it records the page as it now stands.

        Everything below reads the tour's own tables, which is what makes this
        a snapshot of the PAGE rather than of the change set: a departure
        nobody touched this week is in it exactly as much as one that moved.
    */
    DECLARE @SnapshotId INT;

    INSERT INTO intlgit.VersionSnapshot
        (TourId, Version, ChangeSetRef, FxRate, StrikePct, PaxSlab, SharedCost, Note)
    SELECT
        @TourId, @NewVersion, @ChangeSetId,
        cb.FxRate, cb.StrikePct, cb.PaxSlab, cb.SharedCost, cb.Note
    FROM (SELECT 1 AS One) AS anchor
        LEFT JOIN intlgit.CostBuild AS cb ON cb.TourId = @TourId;

    SET @SnapshotId = SCOPE_IDENTITY();

    INSERT INTO intlgit.VersionSnapshotOccupancy
        (VersionSnapshotId, OccupancyCode, OccupancyLabel, SortOrder, LandCostFx, PerPersonInr)
    SELECT @SnapshotId, o.Code, o.Label, o.SortOrder, cbo.LandCostFx, cbo.PerPersonInr
    FROM intlgit.CostBuildOccupancy AS cbo
        INNER JOIN intlgit.Occupancy AS o ON o.OccupancyId = cbo.OccupancyId
    WHERE cbo.TourId = @TourId;

    INSERT INTO intlgit.VersionSnapshotHub
        (VersionSnapshotId, HubCode, HubName, MarkupPct, IsActive)
    SELECT @SnapshotId, h.Code, h.Name, th.MarkupPct, th.IsActive
    FROM intlgit.TourHub AS th
        INNER JOIN intlgit.Hub AS h ON h.HubId = th.HubId
    WHERE th.TourId = @TourId;

    INSERT INTO intlgit.VersionSnapshotDeparture
        (VersionSnapshotId, HubCode, DepartureDate, IsActive)
    SELECT @SnapshotId, h.Code, td.DepartureDate, td.IsActive
    FROM intlgit.TourDeparture AS td
        INNER JOIN intlgit.TourHub AS th ON th.TourHubId = td.TourHubId
        INNER JOIN intlgit.Hub AS h ON h.HubId = th.HubId
    WHERE th.TourId = @TourId;

    INSERT INTO intlgit.VersionSnapshotFare
        (VersionSnapshotId, HubCode, DepartureDate, BandCode, Amount)
    SELECT @SnapshotId, h.Code, td.DepartureDate, fb.Code, df.Amount
    FROM intlgit.DepartureFare AS df
        INNER JOIN intlgit.TourDeparture AS td ON td.TourDepartureId = df.TourDepartureId
        INNER JOIN intlgit.TourHub AS th ON th.TourHubId = td.TourHubId
        INNER JOIN intlgit.Hub AS h ON h.HubId = th.HubId
        INNER JOIN intlgit.FareBand AS fb ON fb.FareBandId = df.FareBandId
    WHERE th.TourId = @TourId
      /*
          A fare nobody has entered is not photographed.

          VersionSnapshotFare.Amount is NOT NULL and this insert had no filter,
          so a single blank fare anywhere on the tour failed the whole
          promotion - inside the transaction, AFTER LivePriceSnapshot had
          already been merged. Latent while every fixture fare was filled in,
          and live the moment real data arrived with a hub that quotes no child
          or infant fare.

          An absent row rather than a nullable column: a snapshot is a
          photograph of what air-ticketing had, and "there was no figure" is
          better recorded by there being no row. Every reader already copes -
          Version.cshtml guards each band with ContainsKey and renders a dash.
      */
      AND df.Amount IS NOT NULL;

    /*
        Prices come from LivePriceSnapshot, which the MERGE above has just
        brought up to date - so this is the figure that went to the website,
        not one recomputed from costs that may move tomorrow.

        The strike-through is worked out here, once, from the same rounding
        the grid uses, and then stored. Recomputing it on read would mean a
        future change to the rule silently rewriting what the website showed.
    */
    INSERT INTO intlgit.VersionSnapshotPrice
        (VersionSnapshotId, HubCode, DepartureDate, OccupancyCode,
         PublishedPrice, StrikeThrough, HasConditions)
    SELECT
        @SnapshotId, h.Code, td.DepartureDate, o.Code,
        lps.Price,
        ROUND(lps.Price * (1 + cb.StrikePct / 100.0), 0),
        CASE WHEN pc.TourDepartureId IS NULL THEN CAST(0 AS BIT) ELSE CAST(1 AS BIT) END
    FROM intlgit.LivePriceSnapshot AS lps
        INNER JOIN intlgit.TourDeparture AS td ON td.TourDepartureId = lps.TourDepartureId
        INNER JOIN intlgit.TourHub AS th ON th.TourHubId = td.TourHubId
        INNER JOIN intlgit.Hub AS h ON h.HubId = th.HubId
        INNER JOIN intlgit.Occupancy AS o ON o.OccupancyId = lps.OccupancyId
        LEFT JOIN intlgit.CostBuild AS cb ON cb.TourId = @TourId
        LEFT JOIN intlgit.PriceCondition AS pc
            ON pc.TourDepartureId = lps.TourDepartureId
           AND pc.OccupancyId = lps.OccupancyId
    WHERE th.TourId = @TourId;
    /*
        And the person who asked for this hears that it landed.

        By email address, not by name: a name does not identify an account -
        two people can share one, and anybody whose name is corrected stops
        matching. The address travels from the change set, through the outbox,
        to here. Null when the set predates that plumbing, and usp_NotifyPerson
        quietly does nothing rather than failing a promotion over it.

        The only one of the four addressed to an individual. Everybody else on
        the product team asked for nothing and would be hearing about somebody
        else's request.
    */
    DECLARE @pvName NVARCHAR(200);
    SELECT @pvName = Name FROM intlgit.Tour WHERE TourId = @TourId;

    DECLARE
        @pvSubject NVARCHAR(200) = @ChangeSetId + N' is on the website',
        @pvBody    NVARCHAR(1000) =
            @TourCode + N' — ' + @pvName + N' is now ' + @NewVersion + N'. '
            + CAST(@Changes AS NVARCHAR(10)) + N' change'
            + CASE WHEN @Changes = 1 THEN N'' ELSE N's' END + N' went live.',
        @pvLink    NVARCHAR(400) = N'/IntlGit/Revision?code=' + @TourCode;

    EXEC core.usp_NotifyPerson
        @Email     = @SubmittedByEmail,
        @Event     = 'changeset-live',
        @Subject   = @pvSubject,
        @Body      = @pvBody,
        @TourCode  = @TourCode,
        @Reference = @ChangeSetId,
        @Link      = @pvLink;


    COMMIT TRANSACTION;
END
GO

/* =========================================================================
   CHANGE SETS - the hand-over to tech support
   ========================================================================= */

-- ---------------------------------------------------------------------------
-- intlgit.usp_CreateChangeSet
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE intlgit.usp_CreateChangeSet
    @TourCode      VARCHAR(10),
    @TourName      NVARCHAR(200),
    @Region        NVARCHAR(60),
    @VersionBefore VARCHAR(10),
    @Note          NVARCHAR(1000),
    @SubmittedBy   NVARCHAR(100),

    -- The account that submitted it, so the promotion can tell them it
    -- landed. SubmittedBy above stays the readable name tech support sees.
    @SubmittedByEmail NVARCHAR(200) = NULL,
    @Rows          intlgit.ChangeRowList  READONLY,
    @Cells         intlgit.ChangeCellList READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @RowCount INT = (SELECT COUNT(*) FROM @Rows);

    IF @RowCount = 0
        THROW 60001, 'A change set must contain at least one row.', 1;

    BEGIN TRANSACTION;

    DECLARE @Year INT = DATEPART(YEAR, SYSUTCDATETIME());
    DECLARE @Number INT;

    UPDATE intlgit.ReferenceSequence WITH (UPDLOCK, HOLDLOCK)
    SET @Number = LastNumber = LastNumber + 1
    WHERE [Year] = @Year;

    IF @Number IS NULL
    BEGIN
        INSERT INTO intlgit.ReferenceSequence ([Year], LastNumber) VALUES (@Year, 1);
        SET @Number = 1;
    END

    DECLARE @Reference VARCHAR(20) =
        'CS-' + CAST(@Year AS VARCHAR(4)) + '-' +
        CASE WHEN @Number < 1000
             THEN RIGHT('000' + CAST(@Number AS VARCHAR(10)), 3)
             ELSE CAST(@Number AS VARCHAR(10))
        END;

    INSERT INTO intlgit.ChangeSet
        (Reference, TourCode, TourName, Region, VersionBefore,
         Note, SubmittedBy, SubmittedByEmail, SubmittedUtc, TotalRows)
    VALUES
        (@Reference, @TourCode, @TourName, @Region, @VersionBefore,
         @Note, @SubmittedBy, @SubmittedByEmail, SYSUTCDATETIME(), @RowCount);

    DECLARE @ChangeSetId INT = CAST(SCOPE_IDENTITY() AS INT);

    DECLARE @Inserted TABLE (ChangeSetRowId INT PRIMARY KEY, RowKey VARCHAR(80) NOT NULL);

    INSERT INTO intlgit.ChangeSetRow
        (ChangeSetId, RowKey, Kind, HubCode, HubName, DepartureDate,
         IsNewHub, RemovalAction, RemovalCount, LivePriceAtSubmit, SortOrder, HtmlBlock)
    OUTPUT inserted.ChangeSetRowId, inserted.RowKey INTO @Inserted
    SELECT
        @ChangeSetId, r.RowKey, r.Kind, r.HubCode, r.HubName, r.DepartureDate,
        r.IsNewHub, r.RemovalAction, r.RemovalCount, r.LivePriceAtSubmit, r.SortOrder,
        r.HtmlBlock
    FROM @Rows AS r;

    INSERT INTO intlgit.ChangeSetCell
        (ChangeSetRowId, OccupancyCode, OccupancyLabel, SortOrder,
         PublishedPrice, StrikeThrough, LivePrice, BaselinePrice,
         HasChanged, HasConditions)
    SELECT
        i.ChangeSetRowId, c.OccupancyCode, c.OccupancyLabel, c.SortOrder,
        c.PublishedPrice, c.StrikeThrough, c.LivePrice, c.BaselinePrice,
        c.HasChanged, c.HasConditions
    FROM @Cells AS c
        INNER JOIN @Inserted AS i ON i.RowKey = c.RowKey;

    COMMIT TRANSACTION;

    SELECT @Reference AS Reference, @ChangeSetId AS ChangeSetId, @RowCount AS TotalRows;
END
GO

-- ---------------------------------------------------------------------------
-- intlgit.usp_GetChangeSet
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE intlgit.usp_GetChangeSet
    @Reference VARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @ChangeSetId INT =
        (SELECT ChangeSetId FROM intlgit.ChangeSet WHERE Reference = @Reference);

    IF @ChangeSetId IS NULL
        THROW 60003, 'Unknown change set.', 1;

    SELECT
        cs.Reference, cs.TourCode, cs.TourName, cs.Region,
        cs.VersionBefore, cs.VersionAfter, cs.Note,
        cs.SubmittedBy, cs.SubmittedUtc, cs.AppliedUtc,
        p.TotalRows, p.CompletedRows, p.IsComplete
    FROM intlgit.ChangeSet AS cs
        INNER JOIN intlgit.vw_ChangeSetProgress AS p ON p.ChangeSetId = cs.ChangeSetId
    WHERE cs.ChangeSetId = @ChangeSetId;

    SELECT
        r.RowKey, r.Kind, r.HubCode, r.HubName, r.DepartureDate,
        r.IsNewHub, r.RemovalAction, r.RemovalCount, r.LivePriceAtSubmit,
        r.SortOrder, r.CompletedUtc, r.CompletedBy, r.HtmlBlock
    FROM intlgit.ChangeSetRow AS r
    WHERE r.ChangeSetId = @ChangeSetId
    ORDER BY r.SortOrder, r.ChangeSetRowId;

    SELECT
        r.RowKey, c.OccupancyCode, c.OccupancyLabel, c.SortOrder,
        c.PublishedPrice, c.StrikeThrough, c.LivePrice,
        BaselinePrice = COALESCE(c.BaselinePrice, c.LivePrice),
        c.HasChanged, c.HasConditions
    FROM intlgit.ChangeSetCell AS c
        INNER JOIN intlgit.ChangeSetRow AS r ON r.ChangeSetRowId = c.ChangeSetRowId
    WHERE r.ChangeSetId = @ChangeSetId
    ORDER BY r.SortOrder, c.SortOrder;
END
GO

-- ---------------------------------------------------------------------------
-- intlgit.usp_GetChangeSetWorkList
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE intlgit.usp_GetChangeSetWorkList
    @OpenOnly  BIT          = 0,
    @SortBy    VARCHAR(20)  = 'submitted',
    @Ascending BIT          = 1,
    @Page      INT          = 1,
    @PageSize  INT          = 50
AS
BEGIN
    SET NOCOUNT ON;

    IF @Page < 1 SET @Page = 1;
    IF @PageSize < 1 OR @PageSize > 200 SET @PageSize = 50;

    SELECT
        cs.Reference,
        cs.TourCode,
        cs.TourName,
        cs.Region,
        cs.VersionBefore,
        cs.VersionAfter,
        cs.Note,
        cs.SubmittedBy,
        cs.SubmittedUtc,
        cs.AppliedUtc,
        p.TotalRows,
        p.CompletedRows,
        p.IsComplete,
        TotalCount = COUNT(*) OVER ()
    FROM intlgit.ChangeSet AS cs
        INNER JOIN intlgit.vw_ChangeSetProgress AS p
            ON p.ChangeSetId = cs.ChangeSetId
    WHERE (@OpenOnly = 0 OR cs.AppliedUtc IS NULL)
    ORDER BY
        CASE WHEN @Ascending = 1 THEN
            CASE @SortBy
                WHEN 'submitted' THEN CONVERT(VARCHAR(30), cs.SubmittedUtc, 126)
                WHEN 'tour'      THEN cs.TourCode
                WHEN 'reference' THEN cs.Reference
                WHEN 'progress'  THEN RIGHT('0000' + CAST(p.CompletedRows AS VARCHAR(10)), 5)
            END
        END ASC,
        CASE WHEN @Ascending = 0 THEN
            CASE @SortBy
                WHEN 'submitted' THEN CONVERT(VARCHAR(30), cs.SubmittedUtc, 126)
                WHEN 'tour'      THEN cs.TourCode
                WHEN 'reference' THEN cs.Reference
                WHEN 'progress'  THEN RIGHT('0000' + CAST(p.CompletedRows AS VARCHAR(10)), 5)
            END
        END DESC,
        cs.ChangeSetId DESC
    OFFSET (@Page - 1) * @PageSize ROWS
    FETCH NEXT @PageSize ROWS ONLY;
END
GO

-- ---------------------------------------------------------------------------
-- intlgit.usp_GetChangeSetHistory
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE intlgit.usp_GetChangeSetHistory
    @TourCode VARCHAR(10)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        cs.Reference,
        cs.TourCode,
        cs.TourName,
        cs.Region,
        cs.VersionBefore,
        cs.VersionAfter,
        cs.Note,
        cs.SubmittedBy,
        cs.SubmittedUtc,
        cs.AppliedUtc,
        p.TotalRows,
        p.CompletedRows,
        p.IsComplete
    FROM intlgit.ChangeSet AS cs
        INNER JOIN intlgit.vw_ChangeSetProgress AS p
            ON p.ChangeSetId = cs.ChangeSetId
    WHERE cs.TourCode = @TourCode
    ORDER BY cs.ChangeSetId DESC;
END
GO

-- ---------------------------------------------------------------------------
-- intlgit.usp_GetChangeSetsByTour
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE intlgit.usp_GetChangeSetsByTour
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        cs.TourCode, cs.Reference, cs.SubmittedUtc,
        cs.AppliedUtc, cs.VersionAfter,
        p.TotalRows, p.CompletedRows, p.IsComplete
    FROM intlgit.ChangeSet AS cs
        INNER JOIN intlgit.vw_ChangeSetProgress AS p ON p.ChangeSetId = cs.ChangeSetId
    ORDER BY cs.TourCode, cs.SubmittedUtc DESC;
END
GO

-- ---------------------------------------------------------------------------
-- intlgit.usp_CompleteChangeSetRow
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE intlgit.usp_CompleteChangeSetRow
    @Reference   VARCHAR(20),
    @RowKey      VARCHAR(80),
    @Completed   BIT,
    @CompletedBy NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @ChangeSetId INT, @AppliedUtc DATETIME2(0);

    SELECT @ChangeSetId = ChangeSetId, @AppliedUtc = AppliedUtc
    FROM intlgit.ChangeSet
    WHERE Reference = @Reference;

    IF @ChangeSetId IS NULL
        THROW 60003, 'Unknown change set.', 1;

    -- An applied set is the record of what went live and does not change.
    IF @AppliedUtc IS NOT NULL
        THROW 60004, 'That change set has been applied and is now read-only.', 1;

    UPDATE intlgit.ChangeSetRow
    SET CompletedUtc = CASE WHEN @Completed = 1 THEN SYSUTCDATETIME() ELSE NULL END,
        CompletedBy  = CASE WHEN @Completed = 1 THEN @CompletedBy ELSE NULL END
    WHERE ChangeSetId = @ChangeSetId
      AND RowKey = @RowKey;

    IF @@ROWCOUNT = 0
        THROW 60005, 'That row is not in this change set.', 1;

    SELECT TotalRows, CompletedRows, IsComplete
    FROM intlgit.vw_ChangeSetProgress
    WHERE ChangeSetId = @ChangeSetId;
END
GO

-- ---------------------------------------------------------------------------
-- intlgit.usp_CompleteAllChangeSetRows
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE intlgit.usp_CompleteAllChangeSetRows
    @Reference   VARCHAR(20),
    @CompletedBy NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @ChangeSetId INT, @AppliedUtc DATETIME2(0);

    SELECT @ChangeSetId = ChangeSetId, @AppliedUtc = AppliedUtc
    FROM intlgit.ChangeSet
    WHERE Reference = @Reference;

    IF @ChangeSetId IS NULL
        THROW 60003, 'Unknown change set.', 1;

    IF @AppliedUtc IS NOT NULL
        THROW 60004, 'That change set has been applied and is now read-only.', 1;

    UPDATE intlgit.ChangeSetRow
    SET CompletedUtc = SYSUTCDATETIME(),
        CompletedBy  = @CompletedBy
    WHERE ChangeSetId = @ChangeSetId
      AND CompletedUtc IS NULL;

    SELECT TotalRows, CompletedRows, IsComplete
    FROM intlgit.vw_ChangeSetProgress
    WHERE ChangeSetId = @ChangeSetId;
END
GO

-- ---------------------------------------------------------------------------
-- intlgit.usp_MarkChangeSetApplied
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE intlgit.usp_MarkChangeSetApplied
    @Reference VARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ChangeSetId INT, @TourCode VARCHAR(10), @Note NVARCHAR(1000),
            @TotalRows INT, @AppliedUtc DATETIME2(0),
            @SubmittedByEmail NVARCHAR(200), @VersionBefore VARCHAR(10),
            @ExistingVersion VARCHAR(10);

    SELECT @ChangeSetId = cs.ChangeSetId, @TourCode = cs.TourCode,
           @Note = cs.Note, @TotalRows = cs.TotalRows, @AppliedUtc = cs.AppliedUtc,
           @SubmittedByEmail = cs.SubmittedByEmail, @VersionBefore = cs.VersionBefore,
           @ExistingVersion = cs.VersionAfter
    FROM intlgit.ChangeSet AS cs
    WHERE cs.Reference = @Reference;

    IF @ChangeSetId IS NULL
        THROW 60003, 'Unknown change set.', 1;

    /* Already applied: nothing to do, and no second promotion. Makes the
       caller safe to retry.

       The version it was given comes back with it, because the caller needs to
       report a version either way and no longer has one of its own to fall
       back on. */
    IF @AppliedUtc IS NOT NULL
    BEGIN
        SELECT AlreadyApplied = CAST(1 AS BIT), VersionAfter = @ExistingVersion;
        RETURN;
    END

    IF NOT EXISTS
    (
        SELECT 1 FROM intlgit.vw_ChangeSetProgress
        WHERE ChangeSetId = @ChangeSetId AND IsComplete = 1
    )
        THROW 60006, 'That change set still has rows outstanding.', 1;

    BEGIN TRANSACTION;

    /*
        The next version for this tour, allocated here so that two requests
        landing at once cannot take the same one.

        UPDLOCK and HOLDLOCK: the lock is held to the end of the transaction
        and covers the range this reads, so a second caller asking the same
        question waits for the stamp below rather than reading the same answer
        and allocating the same number.

        The number is parsed rather than compared as text: 'v100' sorts before
        'v99' as a string, and the padding below stops at two digits exactly as
        TourVersion.Next does.
    */
    DECLARE @Highest INT =
    (
        SELECT MAX(TRY_CAST(SUBSTRING(cs.VersionAfter, 2, 10) AS INT))
        FROM intlgit.ChangeSet AS cs WITH (UPDLOCK, HOLDLOCK)
        WHERE cs.TourCode = @TourCode
          AND cs.AppliedUtc IS NOT NULL
          AND cs.VersionAfter IS NOT NULL
    );

    -- Falling back to this request's own VersionBefore, which is what the
    -- tour was on when it was raised. 'v1' as written by the import parses
    -- here exactly as 'v01' does.
    DECLARE @Next INT =
        COALESCE(@Highest, TRY_CAST(SUBSTRING(@VersionBefore, 2, 10) AS INT), 0) + 1;

    DECLARE @VersionAfter VARCHAR(10) =
        'v' + CASE WHEN @Next < 10
                   THEN '0' + CAST(@Next AS VARCHAR(10))
                   ELSE CAST(@Next AS VARCHAR(10))
              END;

    UPDATE intlgit.ChangeSet
    SET AppliedUtc = SYSUTCDATETIME(),
        VersionAfter = @VersionAfter
    WHERE ChangeSetId = @ChangeSetId;

    /*
        And the tour is promoted, in this same transaction.

        This used to be written to an outbox (app.Outbox) and delivered to the
        Pricing web service a few seconds later, because the change sets and
        the prices lived in two different services. They now live in one
        database, so the promotion runs right here: the stamp and the new
        version either both happen or neither does. If the promotion fails, the
        set stays fully ticked but unstamped, and the application's
        UnstampedChangeSetSweeper tries again a few seconds later - the same
        "keep trying until it lands" the outbox used to give.

        The prices are the ones frozen at submission (every cell of every
        price row, unchanged ones included), exactly as the outbox payload
        carried them.
    */
    DECLARE @Prices intlgit.PriceList;

    INSERT INTO @Prices (HubCode, DepartureDate, OccupancyCode, Price)
    SELECT r.HubCode, r.DepartureDate, c.OccupancyCode, c.PublishedPrice
    FROM intlgit.ChangeSetRow AS r
        INNER JOIN intlgit.ChangeSetCell AS c
            ON c.ChangeSetRowId = r.ChangeSetRowId
    WHERE r.ChangeSetId = @ChangeSetId
      AND r.Kind = 'price';

    /*
        The version-history summary: the note the product team wrote, or
        "<n> prices revised" when there was none. TourVersionHistory.Summary is
        NVARCHAR(500), so a longer note is shortened visibly with an ellipsis
        rather than cut silently - the full note stays on the change set.
        (This was TourService.PromoteAsync / Shorten in the old Pricing API.)
    */
    DECLARE @Summary NVARCHAR(1000) =
        CASE
            WHEN @Note IS NULL
              OR PATINDEX(N'%[^ ' + NCHAR(9) + NCHAR(10) + NCHAR(13) + N']%', @Note) = 0
                THEN CAST(@TotalRows AS NVARCHAR(10)) + N' prices revised'
            ELSE @Note
        END;

    IF LEN(@Summary + N'x') - 1 > 500
        SET @Summary = LEFT(@Summary, 499) + N'…';

    EXEC intlgit.usp_PromoteVersion
        @TourCode         = @TourCode,
        @NewVersion       = @VersionAfter,
        @ChangeSetId      = @Reference,
        @Changes          = @TotalRows,
        @Summary          = @Summary,
        @SubmittedByEmail = @SubmittedByEmail,
        @Prices           = @Prices;

    COMMIT TRANSACTION;

    SELECT AlreadyApplied = CAST(0 AS BIT), VersionAfter = @VersionAfter;
END
GO

-- ---------------------------------------------------------------------------
-- intlgit.usp_ListUnstampedChangeSets
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE intlgit.usp_ListUnstampedChangeSets
AS
BEGIN
    SET NOCOUNT ON;

    SELECT cs.Reference
    FROM intlgit.ChangeSet AS cs
        INNER JOIN intlgit.vw_ChangeSetProgress AS p
            ON p.ChangeSetId = cs.ChangeSetId
    WHERE cs.AppliedUtc IS NULL
      AND p.IsComplete = 1
      AND p.TotalRows > 0
    ORDER BY cs.SubmittedUtc;
END
GO
