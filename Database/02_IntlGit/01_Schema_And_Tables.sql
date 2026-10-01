/*
    AREA: IntlGit  (schema "intlgit")  -  International GIT tour pricing
    ---------------------------------------------------------------------------
    Everything the International GIT price revision needs, in one schema.

    Tables are grouped below by what they are for:
        1. Masters            FareBand, Occupancy, Hub
        2. Tour structure     Tour, TourHub, TourDeparture
        3. Cost build         CostBuild, CostBuildOccupancy
        4. Prices             PublishedPriceOverride, PriceConfirmation, PriceCondition,
                              LivePriceSnapshot, SubmittedPriceSnapshot, SubmittedRemoval
        5. Airfare            TourBaseFare, DepartureFare, DepartureFlight, FareRequest,
                              FareQuery, FareSubmission, FareSubmissionLine
        6. History            Activity, TourVersionHistory, VersionSnapshot (+5 detail tables)
        7. Change sets        ChangeSet, ChangeSetRow, ChangeSetCell, ReferenceSequence

    Removed as unused (nothing reads or writes them in the application):
        app.Outbox              - only existed to carry one event between two web
                                  services; the single application promotes the
                                  tour directly, in the same transaction.
        ChangeSet.IdempotencyKey - never sent by the application.
        dbo.SchemaVersions      - the old migration journal.
*/
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF SCHEMA_ID('intlgit') IS NULL
    EXEC('CREATE SCHEMA intlgit AUTHORIZATION dbo;');
GO

/* =========================================================================
   1. MASTERS
   ========================================================================= */

-- Fare bands an airfare is quoted in: adult (+ tour manager), child, infant.
IF OBJECT_ID('intlgit.FareBand', 'U') IS NULL
BEGIN
    CREATE TABLE intlgit.FareBand
    (
        FareBandId TINYINT      NOT NULL,
        Code       VARCHAR(10)  NOT NULL,
        Label      NVARCHAR(30) NOT NULL,

        CONSTRAINT PK_FareBand      PRIMARY KEY CLUSTERED (FareBandId),
        CONSTRAINT UQ_FareBand_Code UNIQUE (Code)
    );
END
GO

-- The six sellable occupancies. SortOrder fixes the column order of every grid.
IF OBJECT_ID('intlgit.Occupancy', 'U') IS NULL
BEGIN
    CREATE TABLE intlgit.Occupancy
    (
        OccupancyId TINYINT      NOT NULL,
        Code        VARCHAR(10)  NOT NULL,
        Label       NVARCHAR(20) NOT NULL,
        FareBandId  TINYINT      NOT NULL,
        SortOrder   TINYINT      NOT NULL,

        CONSTRAINT PK_Occupancy           PRIMARY KEY CLUSTERED (OccupancyId),
        CONSTRAINT UQ_Occupancy_Code      UNIQUE (Code),
        CONSTRAINT UQ_Occupancy_SortOrder UNIQUE (SortOrder),
        CONSTRAINT FK_Occupancy_FareBand  FOREIGN KEY (FareBandId) REFERENCES intlgit.FareBand (FareBandId)
    );
END
GO

-- The hub master (departure cities). HasPassengerAirfare = 0 is "Joining / Leaving".
IF OBJECT_ID('intlgit.Hub', 'U') IS NULL
BEGIN
    CREATE TABLE intlgit.Hub
    (
        HubId               INT           NOT NULL IDENTITY(1, 1),
        Code                VARCHAR(10)   NOT NULL,
        Name                NVARCHAR(60)  NOT NULL,
        DefaultMarkupPct    DECIMAL(5, 2) NULL,
        IsActive            BIT           NOT NULL CONSTRAINT DF_Hub_IsActive DEFAULT (1),
        HasPassengerAirfare BIT           NOT NULL CONSTRAINT DF_Hub_HasPassengerAirfare DEFAULT (1),
        SortOrder           INT           NOT NULL CONSTRAINT DF_Hub_SortOrder DEFAULT (100),

        CONSTRAINT PK_Hub               PRIMARY KEY CLUSTERED (HubId),
        CONSTRAINT UQ_Hub_Code          UNIQUE (Code),
        CONSTRAINT CK_Hub_DefaultMarkup CHECK (DefaultMarkupPct >= 0 AND DefaultMarkupPct < 100)
    );
END
GO

/* =========================================================================
   2. TOUR STRUCTURE
   ========================================================================= */

IF OBJECT_ID('intlgit.Tour', 'U') IS NULL
BEGIN
    CREATE TABLE intlgit.Tour
    (
        TourId         INT           NOT NULL IDENTITY(1, 1),
        Code           VARCHAR(10)   NOT NULL,
        Name           NVARCHAR(200) NOT NULL,
        Region         NVARCHAR(60)  NOT NULL,
        Duration       NVARCHAR(20)  NOT NULL,
        CurrentVersion VARCHAR(10)   NOT NULL,
        ModifiedUtc    DATETIME2(0)  NOT NULL CONSTRAINT DF_Tour_ModifiedUtc DEFAULT (SYSUTCDATETIME()),

        CONSTRAINT PK_Tour      PRIMARY KEY CLUSTERED (TourId),
        CONSTRAINT UQ_Tour_Code UNIQUE (Code)
    );
END
GO

-- Which hubs a tour sells from, at what markup.
IF OBJECT_ID('intlgit.TourHub', 'U') IS NULL
BEGIN
    CREATE TABLE intlgit.TourHub
    (
        TourHubId INT           NOT NULL IDENTITY(1, 1),
        TourId    INT           NOT NULL,
        HubId     INT           NOT NULL,
        MarkupPct DECIMAL(5, 2) NULL,
        IsActive  BIT           NOT NULL CONSTRAINT DF_TourHub_IsActive DEFAULT (1),

        CONSTRAINT PK_TourHub         PRIMARY KEY CLUSTERED (TourHubId),
        CONSTRAINT UQ_TourHub_TourHub UNIQUE (TourId, HubId),
        CONSTRAINT FK_TourHub_Tour    FOREIGN KEY (TourId) REFERENCES intlgit.Tour (TourId),
        CONSTRAINT FK_TourHub_Hub     FOREIGN KEY (HubId)  REFERENCES intlgit.Hub (HubId),
        CONSTRAINT CK_TourHub_Markup  CHECK (MarkupPct >= 0 AND MarkupPct < 100)
    );

    CREATE NONCLUSTERED INDEX IX_TourHub_Tour
        ON intlgit.TourHub (TourId, IsActive)
        INCLUDE (HubId, MarkupPct);
END
GO

-- Departure dates per hub.
IF OBJECT_ID('intlgit.TourDeparture', 'U') IS NULL
BEGIN
    CREATE TABLE intlgit.TourDeparture
    (
        TourDepartureId INT  NOT NULL IDENTITY(1, 1),
        TourHubId       INT  NOT NULL,
        DepartureDate   DATE NOT NULL,
        IsActive        BIT  NOT NULL CONSTRAINT DF_TourDeparture_IsActive DEFAULT (1),

        CONSTRAINT PK_TourDeparture         PRIMARY KEY CLUSTERED (TourDepartureId),
        CONSTRAINT UQ_TourDeparture_HubDate UNIQUE (TourHubId, DepartureDate),
        CONSTRAINT FK_TourDeparture_TourHub FOREIGN KEY (TourHubId) REFERENCES intlgit.TourHub (TourHubId)
    );

    CREATE NONCLUSTERED INDEX IX_TourDeparture_Hub
        ON intlgit.TourDeparture (TourHubId, DepartureDate)
        INCLUDE (IsActive);
END
GO

/* =========================================================================
   3. COST BUILD
   ========================================================================= */

-- One row per tour: FX rate, strike-through %, pax slab, shared group cost.
-- NULL means "nobody has entered it" - never zero.
IF OBJECT_ID('intlgit.CostBuild', 'U') IS NULL
BEGIN
    CREATE TABLE intlgit.CostBuild
    (
        TourId         INT            NOT NULL,
        FxRate         DECIMAL(10, 4) NULL,
        PreviousFxRate DECIMAL(10, 4) NULL,
        StrikePct      DECIMAL(5, 2)  NULL,
        PaxSlab        INT            NOT NULL,
        SharedCost     DECIMAL(12, 2) NULL,
        Note           NVARCHAR(1000) NULL,
        SavedUtc       DATETIME2(0)   NULL,

        CONSTRAINT PK_CostBuild         PRIMARY KEY CLUSTERED (TourId),
        CONSTRAINT FK_CostBuild_Tour    FOREIGN KEY (TourId) REFERENCES intlgit.Tour (TourId),
        CONSTRAINT CK_CostBuild_PaxSlab CHECK (PaxSlab > 0),
        CONSTRAINT CK_CostBuild_FxRate  CHECK (FxRate > 0),
        CONSTRAINT CK_CostBuild_Strike  CHECK (StrikePct >= 0 AND StrikePct < 100),
        CONSTRAINT CK_CostBuild_Shared  CHECK (SharedCost >= 0)
    );
END
GO

-- Land cost (foreign currency) and per-person INR cost, per occupancy.
IF OBJECT_ID('intlgit.CostBuildOccupancy', 'U') IS NULL
BEGIN
    CREATE TABLE intlgit.CostBuildOccupancy
    (
        TourId       INT            NOT NULL,
        OccupancyId  TINYINT        NOT NULL,
        LandCostFx   DECIMAL(12, 2) NULL,
        PerPersonInr DECIMAL(12, 2) NULL,

        CONSTRAINT PK_CostBuildOccupancy           PRIMARY KEY CLUSTERED (TourId, OccupancyId),
        CONSTRAINT FK_CostBuildOccupancy_Tour      FOREIGN KEY (TourId)      REFERENCES intlgit.Tour (TourId),
        CONSTRAINT FK_CostBuildOccupancy_Occupancy FOREIGN KEY (OccupancyId) REFERENCES intlgit.Occupancy (OccupancyId),
        CONSTRAINT CK_CostBuildOccupancy_Land      CHECK (LandCostFx >= 0),
        CONSTRAINT CK_CostBuildOccupancy_PerPerson CHECK (PerPersonInr >= 0)
    );
END
GO

/* =========================================================================
   4. PRICES
   ========================================================================= */

-- A selling price typed over the calculated one.
IF OBJECT_ID('intlgit.PublishedPriceOverride', 'U') IS NULL
BEGIN
    CREATE TABLE intlgit.PublishedPriceOverride
    (
        TourDepartureId INT            NOT NULL,
        OccupancyId     TINYINT        NOT NULL,
        PublishedPrice  DECIMAL(12, 2) NOT NULL,
        SetUtc          DATETIME2(0)   NOT NULL CONSTRAINT DF_PublishedPriceOverride_SetUtc DEFAULT (SYSUTCDATETIME()),

        CONSTRAINT PK_PublishedPriceOverride           PRIMARY KEY CLUSTERED (TourDepartureId, OccupancyId),
        CONSTRAINT FK_PublishedPriceOverride_Departure FOREIGN KEY (TourDepartureId) REFERENCES intlgit.TourDeparture (TourDepartureId),
        CONSTRAINT FK_PublishedPriceOverride_Occupancy FOREIGN KEY (OccupancyId)     REFERENCES intlgit.Occupancy (OccupancyId),
        CONSTRAINT CK_PublishedPriceOverride_Price     CHECK (PublishedPrice >= 0)
    );
END
GO

-- Somebody agreed to this price. A cell is "blank" exactly while it has no row here.
IF OBJECT_ID('intlgit.PriceConfirmation', 'U') IS NULL
BEGIN
    CREATE TABLE intlgit.PriceConfirmation
    (
        TourDepartureId INT           NOT NULL,
        OccupancyId     TINYINT       NOT NULL,
        ConfirmedUtc    DATETIME2(0)  NOT NULL CONSTRAINT DF_PriceConfirmation_Utc DEFAULT (SYSUTCDATETIME()),
        ConfirmedBy     NVARCHAR(100) NOT NULL,

        CONSTRAINT PK_PriceConfirmation           PRIMARY KEY CLUSTERED (TourDepartureId, OccupancyId),
        CONSTRAINT FK_PriceConfirmation_Departure FOREIGN KEY (TourDepartureId) REFERENCES intlgit.TourDeparture (TourDepartureId),
        CONSTRAINT FK_PriceConfirmation_Occupancy FOREIGN KEY (OccupancyId)     REFERENCES intlgit.Occupancy (OccupancyId)
    );
END
GO

-- "Conditions apply" star on a price.
IF OBJECT_ID('intlgit.PriceCondition', 'U') IS NULL
BEGIN
    CREATE TABLE intlgit.PriceCondition
    (
        TourDepartureId INT           NOT NULL,
        OccupancyId     TINYINT       NOT NULL,
        SetUtc          DATETIME2(0)  NOT NULL CONSTRAINT DF_PriceCondition_Utc DEFAULT (SYSUTCDATETIME()),
        SetBy           NVARCHAR(100) NULL,

        CONSTRAINT PK_PriceCondition           PRIMARY KEY CLUSTERED (TourDepartureId, OccupancyId),
        CONSTRAINT FK_PriceCondition_Departure FOREIGN KEY (TourDepartureId) REFERENCES intlgit.TourDeparture (TourDepartureId),
        CONSTRAINT FK_PriceCondition_Occupancy FOREIGN KEY (OccupancyId)     REFERENCES intlgit.Occupancy (OccupancyId)
    );
END
GO

-- What the website shows now (written when tech support finishes a change set).
IF OBJECT_ID('intlgit.LivePriceSnapshot', 'U') IS NULL
BEGIN
    CREATE TABLE intlgit.LivePriceSnapshot
    (
        TourDepartureId INT            NOT NULL,
        OccupancyId     TINYINT        NOT NULL,
        Price           DECIMAL(12, 2) NOT NULL,
        CapturedUtc     DATETIME2(0)   NOT NULL CONSTRAINT DF_LivePriceSnapshot_CapturedUtc DEFAULT (SYSUTCDATETIME()),

        CONSTRAINT PK_LivePriceSnapshot           PRIMARY KEY CLUSTERED (TourDepartureId, OccupancyId),
        CONSTRAINT FK_LivePriceSnapshot_Departure FOREIGN KEY (TourDepartureId) REFERENCES intlgit.TourDeparture (TourDepartureId),
        CONSTRAINT FK_LivePriceSnapshot_Occupancy FOREIGN KEY (OccupancyId)     REFERENCES intlgit.Occupancy (OccupancyId),
        CONSTRAINT CK_LivePriceSnapshot_Price     CHECK (Price >= 0)
    );
END
GO

-- What was handed to tech support and is not live yet (the baseline for the next set).
IF OBJECT_ID('intlgit.SubmittedPriceSnapshot', 'U') IS NULL
BEGIN
    CREATE TABLE intlgit.SubmittedPriceSnapshot
    (
        TourDepartureId INT            NOT NULL,
        OccupancyId     TINYINT        NOT NULL,
        Price           DECIMAL(12, 2) NOT NULL,
        ChangeSetRef    VARCHAR(20)    NOT NULL,
        SubmittedUtc    DATETIME2(0)   NOT NULL CONSTRAINT DF_SubmittedPriceSnapshot_Utc DEFAULT (SYSUTCDATETIME()),

        CONSTRAINT PK_SubmittedPriceSnapshot           PRIMARY KEY CLUSTERED (TourDepartureId, OccupancyId),
        CONSTRAINT FK_SubmittedPriceSnapshot_Departure FOREIGN KEY (TourDepartureId) REFERENCES intlgit.TourDeparture (TourDepartureId),
        CONSTRAINT FK_SubmittedPriceSnapshot_Occupancy FOREIGN KEY (OccupancyId)     REFERENCES intlgit.Occupancy (OccupancyId)
    );
END
GO

-- A withdrawn hub / departure already sent to tech support for removal.
IF OBJECT_ID('intlgit.SubmittedRemoval', 'U') IS NULL
BEGIN
    CREATE TABLE intlgit.SubmittedRemoval
    (
        TourDepartureId INT          NOT NULL,
        ChangeSetRef    VARCHAR(20)  NOT NULL,
        SubmittedUtc    DATETIME2(0) NOT NULL CONSTRAINT DF_SubmittedRemoval_Utc DEFAULT (SYSUTCDATETIME()),

        CONSTRAINT PK_SubmittedRemoval           PRIMARY KEY CLUSTERED (TourDepartureId),
        CONSTRAINT FK_SubmittedRemoval_Departure FOREIGN KEY (TourDepartureId) REFERENCES intlgit.TourDeparture (TourDepartureId)
    );
END
GO

/* =========================================================================
   5. AIRFARE
   ========================================================================= */

-- Which fare bands a tour is quoted in (rows define the bands; Amount is the
-- old base figure, kept for history).
IF OBJECT_ID('intlgit.TourBaseFare', 'U') IS NULL
BEGIN
    CREATE TABLE intlgit.TourBaseFare
    (
        TourId     INT            NOT NULL,
        FareBandId TINYINT        NOT NULL,
        Amount     DECIMAL(12, 2) NOT NULL,

        CONSTRAINT PK_TourBaseFare        PRIMARY KEY CLUSTERED (TourId, FareBandId),
        CONSTRAINT CK_TourBaseFare_Amount CHECK (Amount >= 0)
    );
END
GO

-- The airfare per departure and band. NULL Amount = awaiting airfare.
IF OBJECT_ID('intlgit.DepartureFare', 'U') IS NULL
BEGIN
    CREATE TABLE intlgit.DepartureFare
    (
        TourId          INT            NOT NULL,
        TourDepartureId INT            NOT NULL,
        FareBandId      TINYINT        NOT NULL,
        Amount          DECIMAL(12, 2) NULL,
        ModifiedUtc     DATETIME2(0)   NOT NULL CONSTRAINT DF_DepartureFare_ModifiedUtc DEFAULT (SYSUTCDATETIME()),
        PreviousAmount  DECIMAL(12, 2) NULL,

        CONSTRAINT PK_DepartureFare        PRIMARY KEY CLUSTERED (TourDepartureId, FareBandId),
        CONSTRAINT CK_DepartureFare_Amount CHECK (Amount IS NULL OR Amount >= 0)
    );

    CREATE NONCLUSTERED INDEX IX_DepartureFare_Tour
        ON intlgit.DepartureFare (TourId)
        INCLUDE (TourDepartureId, FareBandId, Amount);
END
GO

-- Air-ticketing's flight notes per departure.
IF OBJECT_ID('intlgit.DepartureFlight', 'U') IS NULL
BEGIN
    CREATE TABLE intlgit.DepartureFlight
    (
        TourDepartureId INT            NOT NULL,
        Details         NVARCHAR(1000) NULL,
        ModifiedUtc     DATETIME2(0)   NOT NULL CONSTRAINT DF_DepartureFlight_ModifiedUtc DEFAULT (SYSUTCDATETIME()),
        ModifiedBy      NVARCHAR(100)  NULL,

        CONSTRAINT PK_DepartureFlight           PRIMARY KEY CLUSTERED (TourDepartureId),
        CONSTRAINT FK_DepartureFlight_Departure FOREIGN KEY (TourDepartureId) REFERENCES intlgit.TourDeparture (TourDepartureId)
    );
END
GO

-- The product team asking air-ticketing for fares.
IF OBJECT_ID('intlgit.FareRequest', 'U') IS NULL
BEGIN
    CREATE TABLE intlgit.FareRequest
    (
        FareRequestId INT            NOT NULL IDENTITY(1, 1),
        TourId        INT            NOT NULL,
        RequestedUtc  DATETIME2(0)   NOT NULL CONSTRAINT DF_FareRequest_Utc DEFAULT (SYSUTCDATETIME()),
        RequestedBy   NVARCHAR(100)  NOT NULL,
        Note          NVARCHAR(1000) NULL,
        ClosedUtc     DATETIME2(0)   NULL,

        CONSTRAINT PK_FareRequest PRIMARY KEY CLUSTERED (FareRequestId)
    );

    CREATE NONCLUSTERED INDEX IX_FareRequest_Tour
        ON intlgit.FareRequest (TourId, ClosedUtc, RequestedUtc DESC);
END
GO

-- One fare sent back to air-ticketing; holds its departure out of the change set.
IF OBJECT_ID('intlgit.FareQuery', 'U') IS NULL
BEGIN
    CREATE TABLE intlgit.FareQuery
    (
        FareQueryId     INT            NOT NULL IDENTITY(1, 1),
        TourDepartureId INT            NOT NULL,
        FareBandId      TINYINT        NULL,
        RaisedUtc       DATETIME2(0)   NOT NULL CONSTRAINT DF_FareQuery_Raised DEFAULT (SYSUTCDATETIME()),
        RaisedBy        NVARCHAR(100)  NOT NULL,
        Reason          NVARCHAR(1000) NULL,
        ResolvedUtc     DATETIME2(0)   NULL,
        ResolvedBy      NVARCHAR(100)  NULL,
        ResolutionNote  NVARCHAR(1000) NULL,

        CONSTRAINT PK_FareQuery           PRIMARY KEY CLUSTERED (FareQueryId),
        CONSTRAINT FK_FareQuery_Departure FOREIGN KEY (TourDepartureId) REFERENCES intlgit.TourDeparture (TourDepartureId),
        CONSTRAINT FK_FareQuery_Band      FOREIGN KEY (FareBandId)      REFERENCES intlgit.FareBand (FareBandId)
    );

    CREATE NONCLUSTERED INDEX IX_FareQuery_Open
        ON intlgit.FareQuery (TourDepartureId, ResolvedUtc)
        INCLUDE (FareBandId, RaisedUtc, RaisedBy);
END
GO

-- Each time air-ticketing hands its fares over to the product team.
IF OBJECT_ID('intlgit.FareSubmission', 'U') IS NULL
BEGIN
    CREATE TABLE intlgit.FareSubmission
    (
        FareSubmissionId INT            NOT NULL IDENTITY(1, 1),
        TourId           INT            NOT NULL,
        SubmittedUtc     DATETIME2(0)   NOT NULL,
        SubmittedBy      NVARCHAR(100)  NOT NULL,
        Note             NVARCHAR(1000) NULL,

        CONSTRAINT PK_FareSubmission PRIMARY KEY CLUSTERED (FareSubmissionId)
    );

    CREATE NONCLUSTERED INDEX IX_FareSubmission_Tour
        ON intlgit.FareSubmission (TourId, SubmittedUtc DESC);
END
GO

-- What one hand-over carried, per hub and date (the airfare summary sheet).
IF OBJECT_ID('intlgit.FareSubmissionLine', 'U') IS NULL
BEGIN
    CREATE TABLE intlgit.FareSubmissionLine
    (
        FareSubmissionId INT            NOT NULL,
        HubCode          VARCHAR(10)    NOT NULL,
        DepartureDate    DATE           NOT NULL,
        AdultFare        DECIMAL(12, 2) NULL,
        ChildFare        DECIMAL(12, 2) NULL,
        InfantFare       DECIMAL(12, 2) NULL,
        FlightDetails    NVARCHAR(1000) NULL,

        CONSTRAINT PK_FareSubmissionLine            PRIMARY KEY CLUSTERED (FareSubmissionId, HubCode, DepartureDate),
        CONSTRAINT FK_FareSubmissionLine_Submission FOREIGN KEY (FareSubmissionId) REFERENCES intlgit.FareSubmission (FareSubmissionId)
    );
END
GO

/* =========================================================================
   6. HISTORY
   ========================================================================= */

-- Who changed what, figure by figure.
IF OBJECT_ID('intlgit.Activity', 'U') IS NULL
BEGIN
    CREATE TABLE intlgit.Activity
    (
        ActivityId    BIGINT         NOT NULL IDENTITY(1, 1),
        TourId        INT            NOT NULL,
        OccurredUtc   DATETIME2(0)   NOT NULL CONSTRAINT DF_Activity_Utc DEFAULT (SYSUTCDATETIME()),
        ActorName     NVARCHAR(100)  NOT NULL,
        ActorRole     VARCHAR(20)    NOT NULL,
        Action        VARCHAR(20)    NOT NULL,
        HubCode       VARCHAR(10)    NULL,
        DepartureDate DATE           NULL,
        Field         VARCHAR(30)    NULL,
        OldValue      DECIMAL(12, 2) NULL,
        NewValue      DECIMAL(12, 2) NULL,
        Note          NVARCHAR(1000) NULL,

        CONSTRAINT PK_Activity PRIMARY KEY CLUSTERED (ActivityId)
    );

    CREATE NONCLUSTERED INDEX IX_Activity_Tour
        ON intlgit.Activity (TourId, OccurredUtc DESC)
        INCLUDE (ActorName, ActorRole, Action);
END
GO

-- Every version a tour has been on.
IF OBJECT_ID('intlgit.TourVersionHistory', 'U') IS NULL
BEGIN
    CREATE TABLE intlgit.TourVersionHistory
    (
        TourVersionHistoryId INT           NOT NULL IDENTITY(1, 1),
        TourId               INT           NOT NULL,
        Version              VARCHAR(10)   NOT NULL,
        ChangeSetId          VARCHAR(20)   NULL,
        Changes              INT           NOT NULL,
        Summary              NVARCHAR(500) NULL,
        AppliedUtc           DATETIME2(0)  NOT NULL,

        CONSTRAINT PK_TourVersionHistory      PRIMARY KEY CLUSTERED (TourVersionHistoryId),
        CONSTRAINT FK_TourVersionHistory_Tour FOREIGN KEY (TourId) REFERENCES intlgit.Tour (TourId)
    );

    CREATE NONCLUSTERED INDEX IX_TourVersionHistory_Tour
        ON intlgit.TourVersionHistory (TourId, AppliedUtc DESC);
END
GO

-- A photograph of the whole product page, taken when a version goes live.
IF OBJECT_ID('intlgit.VersionSnapshot', 'U') IS NULL
BEGIN
    CREATE TABLE intlgit.VersionSnapshot
    (
        VersionSnapshotId INT            NOT NULL IDENTITY(1, 1),
        TourId            INT            NOT NULL,
        Version           VARCHAR(10)    NOT NULL,
        ChangeSetRef      VARCHAR(20)    NULL,
        CapturedUtc       DATETIME2(0)   NOT NULL CONSTRAINT DF_VersionSnapshot_CapturedUtc DEFAULT (SYSUTCDATETIME()),
        FxRate            DECIMAL(10, 4) NULL,
        StrikePct         DECIMAL(5, 2)  NULL,
        PaxSlab           INT            NULL,
        SharedCost        DECIMAL(12, 2) NULL,
        Note              NVARCHAR(1000) NULL,

        CONSTRAINT PK_VersionSnapshot             PRIMARY KEY CLUSTERED (VersionSnapshotId),
        CONSTRAINT UQ_VersionSnapshot_TourVersion UNIQUE (TourId, Version),
        CONSTRAINT FK_VersionSnapshot_Tour        FOREIGN KEY (TourId) REFERENCES intlgit.Tour (TourId)
    );

    CREATE NONCLUSTERED INDEX IX_VersionSnapshot_Tour
        ON intlgit.VersionSnapshot (TourId, CapturedUtc DESC);
END
GO

IF OBJECT_ID('intlgit.VersionSnapshotOccupancy', 'U') IS NULL
BEGIN
    CREATE TABLE intlgit.VersionSnapshotOccupancy
    (
        VersionSnapshotId INT            NOT NULL,
        OccupancyCode     VARCHAR(10)    NOT NULL,
        OccupancyLabel    NVARCHAR(50)   NOT NULL,
        SortOrder         TINYINT        NOT NULL,
        LandCostFx        DECIMAL(12, 2) NULL,
        PerPersonInr      DECIMAL(12, 2) NULL,

        CONSTRAINT PK_VersionSnapshotOccupancy          PRIMARY KEY CLUSTERED (VersionSnapshotId, OccupancyCode),
        CONSTRAINT FK_VersionSnapshotOccupancy_Snapshot FOREIGN KEY (VersionSnapshotId) REFERENCES intlgit.VersionSnapshot (VersionSnapshotId)
    );
END
GO

IF OBJECT_ID('intlgit.VersionSnapshotHub', 'U') IS NULL
BEGIN
    CREATE TABLE intlgit.VersionSnapshotHub
    (
        VersionSnapshotId INT           NOT NULL,
        HubCode           VARCHAR(10)   NOT NULL,
        HubName           NVARCHAR(100) NOT NULL,
        MarkupPct         DECIMAL(5, 2) NULL,
        IsActive          BIT           NOT NULL,

        CONSTRAINT PK_VersionSnapshotHub          PRIMARY KEY CLUSTERED (VersionSnapshotId, HubCode),
        CONSTRAINT FK_VersionSnapshotHub_Snapshot FOREIGN KEY (VersionSnapshotId) REFERENCES intlgit.VersionSnapshot (VersionSnapshotId)
    );
END
GO

IF OBJECT_ID('intlgit.VersionSnapshotDeparture', 'U') IS NULL
BEGIN
    CREATE TABLE intlgit.VersionSnapshotDeparture
    (
        VersionSnapshotId INT         NOT NULL,
        HubCode           VARCHAR(10) NOT NULL,
        DepartureDate     DATE        NOT NULL,
        IsActive          BIT         NOT NULL,

        CONSTRAINT PK_VersionSnapshotDeparture          PRIMARY KEY CLUSTERED (VersionSnapshotId, HubCode, DepartureDate),
        CONSTRAINT FK_VersionSnapshotDeparture_Snapshot FOREIGN KEY (VersionSnapshotId) REFERENCES intlgit.VersionSnapshot (VersionSnapshotId)
    );
END
GO

IF OBJECT_ID('intlgit.VersionSnapshotFare', 'U') IS NULL
BEGIN
    CREATE TABLE intlgit.VersionSnapshotFare
    (
        VersionSnapshotId INT            NOT NULL,
        HubCode           VARCHAR(10)    NOT NULL,
        DepartureDate     DATE           NOT NULL,
        BandCode          VARCHAR(10)    NOT NULL,
        Amount            DECIMAL(12, 2) NOT NULL,

        CONSTRAINT PK_VersionSnapshotFare          PRIMARY KEY CLUSTERED (VersionSnapshotId, HubCode, DepartureDate, BandCode),
        CONSTRAINT FK_VersionSnapshotFare_Snapshot FOREIGN KEY (VersionSnapshotId) REFERENCES intlgit.VersionSnapshot (VersionSnapshotId)
    );
END
GO

IF OBJECT_ID('intlgit.VersionSnapshotPrice', 'U') IS NULL
BEGIN
    CREATE TABLE intlgit.VersionSnapshotPrice
    (
        VersionSnapshotId INT            NOT NULL,
        HubCode           VARCHAR(10)    NOT NULL,
        DepartureDate     DATE           NOT NULL,
        OccupancyCode     VARCHAR(10)    NOT NULL,
        PublishedPrice    DECIMAL(12, 2) NOT NULL,
        StrikeThrough     DECIMAL(12, 2) NULL,
        HasConditions     BIT            NOT NULL,

        CONSTRAINT PK_VersionSnapshotPrice          PRIMARY KEY CLUSTERED (VersionSnapshotId, HubCode, DepartureDate, OccupancyCode),
        CONSTRAINT FK_VersionSnapshotPrice_Snapshot FOREIGN KEY (VersionSnapshotId) REFERENCES intlgit.VersionSnapshot (VersionSnapshotId)
    );
END
GO

/* =========================================================================
   7. CHANGE SETS  (the hand-over to tech support)
   ========================================================================= */

-- A frozen list of website changes. Immutable once created.
IF OBJECT_ID('intlgit.ChangeSet', 'U') IS NULL
BEGIN
    CREATE TABLE intlgit.ChangeSet
    (
        ChangeSetId      INT            NOT NULL IDENTITY(1, 1),
        Reference        VARCHAR(20)    NOT NULL,
        TourCode         VARCHAR(10)    NOT NULL,
        TourName         NVARCHAR(200)  NOT NULL,
        Region           NVARCHAR(60)   NOT NULL,
        VersionBefore    VARCHAR(10)    NOT NULL,
        VersionAfter     VARCHAR(10)    NULL,
        Note             NVARCHAR(1000) NULL,
        SubmittedBy      NVARCHAR(100)  NOT NULL,
        SubmittedUtc     DATETIME2(0)   NOT NULL,
        AppliedUtc       DATETIME2(0)   NULL,
        TotalRows        INT            NOT NULL,
        SubmittedByEmail NVARCHAR(200)  NULL,

        CONSTRAINT PK_ChangeSet           PRIMARY KEY CLUSTERED (ChangeSetId),
        CONSTRAINT UQ_ChangeSet_Reference UNIQUE (Reference),
        CONSTRAINT CK_ChangeSet_TotalRows CHECK (TotalRows > 0)
    );

    CREATE NONCLUSTERED INDEX IX_ChangeSet_Tour
        ON intlgit.ChangeSet (TourCode, SubmittedUtc DESC)
        INCLUDE (Reference, AppliedUtc, VersionAfter, TotalRows);
END
GO

-- One row of work for tech support: a departure to price, or a listing to remove.
IF OBJECT_ID('intlgit.ChangeSetRow', 'U') IS NULL
BEGIN
    CREATE TABLE intlgit.ChangeSetRow
    (
        ChangeSetRowId    INT            NOT NULL IDENTITY(1, 1),
        ChangeSetId       INT            NOT NULL,
        RowKey            VARCHAR(80)    NOT NULL,
        Kind              VARCHAR(10)    NOT NULL,
        HubCode           VARCHAR(10)    NOT NULL,
        HubName           NVARCHAR(60)   NOT NULL,
        DepartureDate     DATE           NULL,
        IsNewHub          BIT            NOT NULL CONSTRAINT DF_ChangeSetRow_IsNewHub DEFAULT (0),
        RemovalAction     NVARCHAR(40)   NULL,
        RemovalCount      INT            NULL,
        LivePriceAtSubmit DECIMAL(12, 2) NULL,
        SortOrder         INT            NOT NULL,
        CompletedUtc      DATETIME2(0)   NULL,
        CompletedBy       NVARCHAR(100)  NULL,
        HtmlBlock         NVARCHAR(MAX)  NULL,

        CONSTRAINT PK_ChangeSetRow       PRIMARY KEY CLUSTERED (ChangeSetRowId),
        CONSTRAINT UQ_ChangeSetRow_Key   UNIQUE (ChangeSetId, RowKey),
        CONSTRAINT FK_ChangeSetRow_Set   FOREIGN KEY (ChangeSetId) REFERENCES intlgit.ChangeSet (ChangeSetId),
        CONSTRAINT CK_ChangeSetRow_Kind  CHECK (Kind IN ('price', 'removal')),
        CONSTRAINT CK_ChangeSetRow_Shape CHECK ((Kind = 'price' AND DepartureDate IS NOT NULL) OR (Kind = 'removal'))
    );

    CREATE NONCLUSTERED INDEX IX_ChangeSetRow_Set
        ON intlgit.ChangeSetRow (ChangeSetId, CompletedUtc)
        INCLUDE (RowKey, SortOrder);
END
GO

-- The six prices of one change-set row, frozen at submission.
IF OBJECT_ID('intlgit.ChangeSetCell', 'U') IS NULL
BEGIN
    CREATE TABLE intlgit.ChangeSetCell
    (
        ChangeSetRowId INT            NOT NULL,
        OccupancyCode  VARCHAR(10)    NOT NULL,
        OccupancyLabel NVARCHAR(20)   NOT NULL,
        SortOrder      TINYINT        NOT NULL,
        PublishedPrice DECIMAL(12, 2) NOT NULL,
        StrikeThrough  DECIMAL(12, 2) NULL,
        LivePrice      DECIMAL(12, 2) NULL,
        HasChanged     BIT            NOT NULL,
        BaselinePrice  DECIMAL(12, 2) NULL,
        HasConditions  BIT            NOT NULL CONSTRAINT DF_ChangeSetCell_Conditions DEFAULT (0),

        CONSTRAINT PK_ChangeSetCell     PRIMARY KEY CLUSTERED (ChangeSetRowId, OccupancyCode),
        CONSTRAINT FK_ChangeSetCell_Row FOREIGN KEY (ChangeSetRowId) REFERENCES intlgit.ChangeSetRow (ChangeSetRowId),
        CONSTRAINT CK_ChangeSetCell_Pub CHECK (PublishedPrice >= 0),
        CONSTRAINT CK_ChangeSetCell_Mrp CHECK (StrikeThrough >= 0)
    );
END
GO

-- Numbers change sets per year: CS-2026-001, CS-2026-002, ...
IF OBJECT_ID('intlgit.ReferenceSequence', 'U') IS NULL
BEGIN
    CREATE TABLE intlgit.ReferenceSequence
    (
        [Year]     INT NOT NULL,
        LastNumber INT NOT NULL,

        CONSTRAINT PK_ReferenceSequence PRIMARY KEY CLUSTERED ([Year])
    );
END
GO
