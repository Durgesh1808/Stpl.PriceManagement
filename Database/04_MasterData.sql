/*
    04_MasterData.sql
    The reference data a brand-new Int_StplGITPricing needs to work:
      - intlgit.FareBand    adult (+ tour manager), child, infant
      - intlgit.Occupancy   the six sellable occupancies, in grid order
      - intlgit.Hub         the twelve departure hubs, Joining / Leaving first
      - core.[User]         the three starter accounts

    Safe to run more than once: rows are MERGEd by their code, and accounts
    are only created when missing (an existing password is never touched).

*/
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
GO

-- ---------------------------------------------------------------------------
-- Fare bands
-- ---------------------------------------------------------------------------
MERGE intlgit.FareBand AS target
USING (VALUES
    (1, 'adult',  N'Adult + TM'),
    (2, 'child',  N'Child'),
    (3, 'infant', N'Infant')
) AS source (FareBandId, Code, Label)
    ON source.FareBandId = target.FareBandId
WHEN MATCHED THEN UPDATE SET Code = source.Code, Label = source.Label
WHEN NOT MATCHED THEN INSERT (FareBandId, Code, Label)
    VALUES (source.FareBandId, source.Code, source.Label);
GO

-- ---------------------------------------------------------------------------
-- Occupancies. SortOrder is the column order of every price grid.
-- ---------------------------------------------------------------------------
MERGE intlgit.Occupancy AS target
USING (VALUES
    (1, 'twin',   N'Twin',   1, 1),
    (2, 'triple', N'Triple', 1, 2),
    (3, 'single', N'Single', 1, 3),
    (4, 'cwb',    N'CWB',    2, 4),
    (5, 'cnb',    N'CNB',    2, 5),
    (6, 'infant', N'Infant', 3, 6)
) AS source (OccupancyId, Code, Label, FareBandId, SortOrder)
    ON source.OccupancyId = target.OccupancyId
WHEN MATCHED THEN UPDATE SET
    Code = source.Code, Label = source.Label,
    FareBandId = source.FareBandId, SortOrder = source.SortOrder
WHEN NOT MATCHED THEN INSERT (OccupancyId, Code, Label, FareBandId, SortOrder)
    VALUES (source.OccupancyId, source.Code, source.Label, source.FareBandId, source.SortOrder);
GO

-- ---------------------------------------------------------------------------
-- Hub master, as supplied by Southern Travels.
--   'direct' (Joining / Leaving) carries no passenger airfare: its guests make
--   their own way, so its price is land + the tour manager's seat only.
--   DefaultMarkupPct is NULL on purpose - every markup is set per tour.
-- ---------------------------------------------------------------------------
MERGE intlgit.Hub AS target
USING (VALUES
    ('direct', N'Joining / Leaving', 0,   0),
    ('hyd',    N'Hyderabad',         1,  20),
    ('blr',    N'Bengaluru',         1,  30),
    ('vtz',    N'Vizag',             1,  40),
    ('ccu',    N'Kolkata',           1,  50),
    ('maa',    N'Chennai',           1,  60),
    ('del',    N'Delhi',             1,  70),
    ('bom',    N'Mumbai',            1,  80),
    ('amd',    N'Ahmedabad',         1,  90),
    ('cok',    N'Cochin',            1, 100),
    ('pnq',    N'Pune',              1, 110),
    ('nag',    N'Nagpur',            1, 120)
) AS source (Code, Name, HasPassengerAirfare, SortOrder)
    ON source.Code = target.Code
WHEN MATCHED THEN UPDATE SET
    Name = source.Name,
    HasPassengerAirfare = source.HasPassengerAirfare,
    SortOrder = source.SortOrder
WHEN NOT MATCHED THEN INSERT (Code, Name, DefaultMarkupPct, IsActive, HasPassengerAirfare, SortOrder)
    VALUES (source.Code, source.Name, NULL, 1, source.HasPassengerAirfare, source.SortOrder);
GO

-- ---------------------------------------------------------------------------
-- Starter accounts.
-- Change their passwords from "Change password" after the first sign-in.
-- ---------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM core.[User] WHERE Email = N'product@southerntravels.com')
    INSERT INTO core.[User] (Email, DisplayName, Role, PasswordHash)
    VALUES (N'product@southerntravels.com', N'Product Executive', 'product.executive',
            N'AQAAAAIAAYagAAAAEOccMidvOmNnJRhDcJ1VsziLOOoqTAIrfnR33UoKiueN0B7dCP705U9I9omyFG01zg==');

IF NOT EXISTS (SELECT 1 FROM core.[User] WHERE Email = N'airticketing@southerntravels.com')
    INSERT INTO core.[User] (Email, DisplayName, Role, PasswordHash)
    VALUES (N'airticketing@southerntravels.com', N'Air-ticketing Executive', 'airticketing.executive',
            N'AQAAAAIAAYagAAAAEJ02ljOMOoeLLh073/0Zm6i/ildLYTJHa1Nui3BpMau78dLgJ29KOrHqsw3FCty7TA==');

IF NOT EXISTS (SELECT 1 FROM core.[User] WHERE Email = N'techsupport@southerntravels.com')
    INSERT INTO core.[User] (Email, DisplayName, Role, PasswordHash)
    VALUES (N'techsupport@southerntravels.com', N'Tech Support', 'techsupport',
            N'AQAAAAIAAYagAAAAENWIsrewvLS38/71haNp8GKUQgmgR02N35GA/2Y57wSH4+yAcbXqkAtEYqEJjHPvew==');
GO

SELECT 'FareBand' AS [Table], COUNT(*) AS [Rows] FROM intlgit.FareBand
UNION ALL SELECT 'Occupancy', COUNT(*) FROM intlgit.Occupancy
UNION ALL SELECT 'Hub', COUNT(*) FROM intlgit.Hub
UNION ALL SELECT 'User', COUNT(*) FROM core.[User];
GO
