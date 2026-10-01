/*
    AREA: Core - stored procedures
    Sign-in and accounts, notifications (the bell) and the email log.
    core.usp_NotifyRole / core.usp_NotifyPerson are what every pricing area
    calls to raise a notification inside its own transaction.
*/
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/* =========================================================================
   ACCOUNTS AND SIGN-IN
   ========================================================================= */

-- ---------------------------------------------------------------------------
-- core.usp_GetUserForSignIn
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE core.usp_GetUserForSignIn
    @Email NVARCHAR(200)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        u.UserId,
        u.Email,
        u.DisplayName,
        u.Role,
        u.PasswordHash,
        u.IsActive,
        u.FailedAttempts,
        u.LockedUntilUtc
    FROM core.[User] AS u
    WHERE u.Email = @Email;
END
GO

-- ---------------------------------------------------------------------------
-- core.usp_RecordSignIn
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE core.usp_RecordSignIn
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE core.[User]
    SET LastSignInUtc  = SYSUTCDATETIME(),
        FailedAttempts = 0,
        LockedUntilUtc = NULL
    WHERE UserId = @UserId;
END
GO

-- ---------------------------------------------------------------------------
-- core.usp_RecordFailedSignIn
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE core.usp_RecordFailedSignIn
    @UserId        INT,
    @MaxAttempts   INT = 5,
    @LockoutMinutes INT = 15
AS
BEGIN
    SET NOCOUNT ON;

    /*
        Counted per account rather than per address. Somebody guessing at one
        person's password is the case this stops; somebody spraying one
        password across many accounts is a different problem and needs a
        different answer, which this does not pretend to be.
    */
    UPDATE core.[User]
    SET FailedAttempts = FailedAttempts + 1,
        LockedUntilUtc = CASE
            WHEN FailedAttempts + 1 >= @MaxAttempts
            THEN DATEADD(MINUTE, @LockoutMinutes, SYSUTCDATETIME())
            ELSE LockedUntilUtc
        END
    WHERE UserId = @UserId;
END
GO

-- ---------------------------------------------------------------------------
-- core.usp_UpsertUser
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE core.usp_UpsertUser
    @Email        NVARCHAR(200),
    @DisplayName  NVARCHAR(100),
    @Role         VARCHAR(30),
    @PasswordHash NVARCHAR(500) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    IF EXISTS (SELECT 1 FROM core.[User] WHERE Email = @Email)
    BEGIN
        UPDATE core.[User]
        SET DisplayName = @DisplayName,
            Role = @Role,
            -- Only ever SETS a password, never clears one.
            PasswordHash = COALESCE(@PasswordHash, PasswordHash)
        WHERE Email = @Email;
    END
    ELSE
    BEGIN
        INSERT INTO core.[User] (Email, DisplayName, Role, PasswordHash)
        VALUES (@Email, @DisplayName, @Role, @PasswordHash);
    END

    COMMIT TRANSACTION;

    SELECT u.UserId, u.Email, u.DisplayName, u.Role,
           CASE WHEN u.PasswordHash IS NULL THEN CAST(0 AS BIT) ELSE CAST(1 AS BIT) END
               AS HasPassword
    FROM core.[User] AS u
    WHERE u.Email = @Email;
END
GO

-- ---------------------------------------------------------------------------
-- core.usp_ListUsers
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE core.usp_ListUsers
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        u.UserId,
        u.Email,
        u.DisplayName,
        u.Role,
        u.IsActive,
        u.CreatedUtc,
        u.LastSignInUtc,
        HasPassword = CASE WHEN u.PasswordHash IS NULL
                           THEN CAST(0 AS BIT) ELSE CAST(1 AS BIT) END,
        IsLockedOut = CASE WHEN u.LockedUntilUtc > SYSUTCDATETIME()
                           THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END
    FROM core.[User] AS u
    ORDER BY u.Role, u.DisplayName;
END
GO

-- ---------------------------------------------------------------------------
-- core.usp_SetPassword
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE core.usp_SetPassword
    @UserId       INT,
    @PasswordHash NVARCHAR(500)
AS
BEGIN
    SET NOCOUNT ON;

    IF @PasswordHash IS NULL OR LEN(@PasswordHash) = 0
        THROW 50040, 'A password hash is required. This procedure never clears a password.', 1;

    UPDATE core.[User]
    SET PasswordHash   = @PasswordHash,
        FailedAttempts = 0,
        LockedUntilUtc = NULL
    WHERE UserId = @UserId;

    IF @@ROWCOUNT = 0
        THROW 50041, 'No such account.', 1;
END
GO

-- ---------------------------------------------------------------------------
-- core.usp_SetUserActive
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE core.usp_SetUserActive
    @Email    NVARCHAR(200),
    @IsActive BIT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE core.[User]
    SET IsActive = @IsActive,
        /*
            Deactivating also clears the password.

            Turning the flag off would be enough to stop a sign-in today. But
            an account that is off and still holds a hash is a live credential
            sitting in a table, and turning the flag back on would silently
            restore it - including for somebody who left months ago and whose
            password has been written down somewhere ever since. Coming back
            means being given a new password, which is the same thing that
            happens to a new joiner.
        */
        PasswordHash = CASE WHEN @IsActive = 0 THEN NULL ELSE PasswordHash END,
        FailedAttempts = 0,
        LockedUntilUtc = NULL
    WHERE Email = @Email;

    IF @@ROWCOUNT = 0
        THROW 50041, 'No such account.', 1;

    SELECT u.UserId, u.Email, u.DisplayName, u.Role, u.IsActive,
           HasPassword = CASE WHEN u.PasswordHash IS NULL
                              THEN CAST(0 AS BIT) ELSE CAST(1 AS BIT) END
    FROM core.[User] AS u
    WHERE u.Email = @Email;
END
GO

-- ---------------------------------------------------------------------------
-- core.usp_UnlockUser
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE core.usp_UnlockUser
    @Email NVARCHAR(200)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE core.[User]
    SET FailedAttempts = 0,
        LockedUntilUtc = NULL
    WHERE Email = @Email;

    IF @@ROWCOUNT = 0
        THROW 50041, 'No such account.', 1;
END
GO

/* =========================================================================
   NOTIFICATIONS
   ========================================================================= */

-- ---------------------------------------------------------------------------
-- core.usp_NotifyRole
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE core.usp_NotifyRole
    @Role      VARCHAR(30),
    @Event     VARCHAR(40),
    @Subject   NVARCHAR(200),
    @Body      NVARCHAR(1000),
    @TourCode  VARCHAR(10) = NULL,
    @Reference VARCHAR(20) = NULL,
    @Link      NVARCHAR(400) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO core.Notification
        (UserId, [Event], TourCode, Reference, [Subject], Body, Link)
    SELECT u.UserId, @Event, @TourCode, @Reference, @Subject, @Body, @Link
    FROM core.[User] AS u
    WHERE u.Role = @Role
      AND u.IsActive = 1;

    /*
        No row for a role nobody holds, and that is not an error. A tour can be
        priced before anybody has been given the tech support account, and
        refusing the pricing because there was nobody to tell would be the
        notification deciding whether the work may happen.
    */
END
GO

-- ---------------------------------------------------------------------------
-- core.usp_NotifyPerson
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE core.usp_NotifyPerson
    @Email     NVARCHAR(200),
    @Event     VARCHAR(40),
    @Subject   NVARCHAR(200),
    @Body      NVARCHAR(1000),
    @TourCode  VARCHAR(10) = NULL,
    @Reference VARCHAR(20) = NULL,
    @Link      NVARCHAR(400) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF @Email IS NULL OR LEN(@Email) = 0
        RETURN;

    INSERT INTO core.Notification
        (UserId, [Event], TourCode, Reference, [Subject], Body, Link)
    SELECT u.UserId, @Event, @TourCode, @Reference, @Subject, @Body, @Link
    FROM core.[User] AS u
    WHERE u.Email = @Email
      AND u.IsActive = 1;
END
GO

-- ---------------------------------------------------------------------------
-- core.usp_ListNotifications
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE core.usp_ListNotifications
    @UserId INT,
    @Limit  INT = 100
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (@Limit)
        n.NotificationId,
        n.[Event],
        n.TourCode,
        n.Reference,
        n.[Subject],
        n.Body,
        n.Link,
        n.CreatedUtc,
        n.ReadUtc
    FROM core.Notification AS n
    WHERE n.UserId = @UserId
    ORDER BY n.CreatedUtc DESC, n.NotificationId DESC;
END
GO

-- ---------------------------------------------------------------------------
-- core.usp_CountUnreadNotifications
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE core.usp_CountUnreadNotifications
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT COUNT_BIG(*) AS Unread
    FROM core.Notification AS n
    WHERE n.UserId = @UserId
      AND n.ReadUtc IS NULL;
END
GO

-- ---------------------------------------------------------------------------
-- core.usp_MarkNotificationRead
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE core.usp_MarkNotificationRead
    @NotificationId BIGINT,
    @UserId         INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE core.Notification
    SET ReadUtc = SYSUTCDATETIME()
    WHERE NotificationId = @NotificationId
      AND UserId = @UserId
      AND ReadUtc IS NULL;

    -- The link the caller should follow, whether or not anything was updated.
    -- A second click on a notification already read still goes where it says.
    SELECT n.Link
    FROM core.Notification AS n
    WHERE n.NotificationId = @NotificationId
      AND n.UserId = @UserId;
END
GO

-- ---------------------------------------------------------------------------
-- core.usp_MarkAllNotificationsRead
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE core.usp_MarkAllNotificationsRead
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE core.Notification
    SET ReadUtc = SYSUTCDATETIME()
    WHERE UserId = @UserId
      AND ReadUtc IS NULL;
END
GO

/* =========================================================================
   EMAIL WITH EVERY NOTIFICATION
   ========================================================================= */

-- ---------------------------------------------------------------------------
-- core.usp_ClaimUnsentNotifications
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE core.usp_ClaimUnsentNotifications
    @Max           INT = 50,
    @WithinMinutes INT = 10
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (@Max)
        n.NotificationId,
        u.Email       AS ToAddress,
        u.DisplayName AS ToName,
        u.Role        AS Role,
        n.[Event]     AS [Event],
        n.TourCode,
        n.[Subject],
        n.Body,
        n.Link,
        n.CreatedUtc
    FROM core.Notification AS n
        INNER JOIN core.[User] AS u ON u.UserId = n.UserId
    WHERE n.EmailSentUtc IS NULL
      AND u.IsActive = 1
      AND n.CreatedUtc >= DATEADD(MINUTE, -@WithinMinutes, SYSUTCDATETIME())
    ORDER BY n.NotificationId;
END
GO

-- ---------------------------------------------------------------------------
-- core.usp_MarkNotificationEmailed
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE core.usp_MarkNotificationEmailed
    @NotificationId BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE core.Notification
    SET EmailSentUtc = SYSUTCDATETIME()
    WHERE NotificationId = @NotificationId
      AND EmailSentUtc IS NULL;
END
GO

-- ---------------------------------------------------------------------------
-- core.usp_LogEmail
-- ---------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE core.usp_LogEmail
    @NotificationId BIGINT         = NULL,
    @Event          VARCHAR(40)    = NULL,
    @RecipientRole  VARCHAR(30)    = NULL,
    @TourCode       VARCHAR(10)    = NULL,
    @FromAddress    NVARCHAR(200)  = NULL,
    @ToAddress      NVARCHAR(1000) = NULL,
    @CcAddress      NVARCHAR(1000) = NULL,
    @BccAddress     NVARCHAR(1000) = NULL,
    @Subject        NVARCHAR(400)  = NULL,
    @Body           NVARCHAR(MAX)  = NULL,
    @Status         VARCHAR(20),
    @ErrorMessage   NVARCHAR(MAX)  = NULL
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO core.EmailLog
        (NotificationId, [Event], RecipientRole, TourCode, FromAddress, ToAddress,
         CcAddress, BccAddress, [Subject], Body, [Status], ErrorMessage)
    VALUES
        (@NotificationId, @Event, @RecipientRole, @TourCode, @FromAddress, @ToAddress,
         @CcAddress, @BccAddress, @Subject, @Body, @Status, @ErrorMessage);
END
GO
