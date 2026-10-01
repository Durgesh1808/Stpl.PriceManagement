/*
    AREA: Core  (schema "core")
    ---------------------------------------------------------------------------
    What every pricing type shares: the people who sign in, the notifications
    they receive, and the log of every email sent with a notification.

    International GIT pricing lives in schema "intlgit". A future pricing type
    (International FIT, Domestic fixed departures, ...) gets its own schema
    beside it and re-uses everything in "core".
*/
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF SCHEMA_ID('core') IS NULL
    EXEC('CREATE SCHEMA core AUTHORIZATION dbo;');
GO

-- ---------------------------------------------------------------------------
-- core.[User] - one row per person who can sign in.
-- Role is the job, not a rank: product.executive, airticketing.executive,
-- techsupport.
-- ---------------------------------------------------------------------------
IF OBJECT_ID('core.[User]', 'U') IS NULL
BEGIN
    CREATE TABLE core.[User]
    (
        UserId         INT            NOT NULL IDENTITY(1, 1),
        Email          NVARCHAR(200)  NOT NULL,
        DisplayName    NVARCHAR(100)  NOT NULL,
        Role           VARCHAR(30)    NOT NULL,
        PasswordHash   NVARCHAR(500)  NULL,
        IsActive       BIT            NOT NULL CONSTRAINT DF_User_IsActive DEFAULT (1),
        CreatedUtc     DATETIME2(0)   NOT NULL CONSTRAINT DF_User_CreatedUtc DEFAULT (SYSUTCDATETIME()),
        LastSignInUtc  DATETIME2(0)   NULL,
        FailedAttempts INT            NOT NULL CONSTRAINT DF_User_FailedAttempts DEFAULT (0),
        LockedUntilUtc DATETIME2(0)   NULL,

        CONSTRAINT PK_User       PRIMARY KEY CLUSTERED (UserId),
        CONSTRAINT UQ_User_Email UNIQUE (Email),
        CONSTRAINT CK_User_Role  CHECK (Role IN ('product.executive', 'airticketing.executive', 'techsupport'))
    );
END
GO

-- ---------------------------------------------------------------------------
-- core.Notification - the bell. Written by the stored procedure that raises
-- the event, inside that event's own transaction.
-- ---------------------------------------------------------------------------
IF OBJECT_ID('core.Notification', 'U') IS NULL
BEGIN
    CREATE TABLE core.Notification
    (
        NotificationId BIGINT         NOT NULL IDENTITY(1, 1),
        UserId         INT            NOT NULL,
        [Event]        VARCHAR(40)    NOT NULL,
        TourCode       VARCHAR(10)    NULL,
        Reference      VARCHAR(20)    NULL,
        [Subject]      NVARCHAR(200)  NOT NULL,
        Body           NVARCHAR(1000) NOT NULL,
        Link           NVARCHAR(400)  NULL,
        CreatedUtc     DATETIME2(0)   NOT NULL CONSTRAINT DF_Notification_CreatedUtc DEFAULT (SYSUTCDATETIME()),
        ReadUtc        DATETIME2(0)   NULL,
        EmailSentUtc   DATETIME2(0)   NULL,

        CONSTRAINT PK_Notification      PRIMARY KEY CLUSTERED (NotificationId),
        CONSTRAINT FK_Notification_User FOREIGN KEY (UserId) REFERENCES core.[User] (UserId),
        CONSTRAINT CK_Notification_Event CHECK ([Event] IN
            ('fares-received', 'fare-queried', 'fares-requested', 'changeset-submitted', 'changeset-live'))
    );

    CREATE NONCLUSTERED INDEX IX_Notification_Recipient
        ON core.Notification (UserId, CreatedUtc DESC)
        INCLUDE (ReadUtc);
END
GO

-- ---------------------------------------------------------------------------
-- core.EmailLog - one row per email attempt, sent or failed.
-- ---------------------------------------------------------------------------
IF OBJECT_ID('core.EmailLog', 'U') IS NULL
BEGIN
    CREATE TABLE core.EmailLog
    (
        EmailLogId     BIGINT         NOT NULL IDENTITY(1, 1),
        NotificationId BIGINT         NULL,
        [Event]        VARCHAR(40)    NULL,
        RecipientRole  VARCHAR(30)    NULL,
        TourCode       VARCHAR(10)    NULL,
        FromAddress    NVARCHAR(200)  NULL,
        ToAddress      NVARCHAR(1000) NULL,
        CcAddress      NVARCHAR(1000) NULL,
        BccAddress     NVARCHAR(1000) NULL,
        [Subject]      NVARCHAR(400)  NULL,
        Body           NVARCHAR(MAX)  NULL,
        [Status]       VARCHAR(20)    NOT NULL,   -- 'Sent' or 'Failed'
        ErrorMessage   NVARCHAR(MAX)  NULL,
        CreatedUtc     DATETIME2(0)   NOT NULL CONSTRAINT DF_EmailLog_CreatedUtc DEFAULT (SYSUTCDATETIME()),

        CONSTRAINT PK_EmailLog PRIMARY KEY CLUSTERED (EmailLogId)
    );

    CREATE NONCLUSTERED INDEX IX_EmailLog_Created
        ON core.EmailLog (CreatedUtc DESC)
        INCLUDE ([Status], NotificationId);
END
GO
