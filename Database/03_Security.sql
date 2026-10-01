/*
    03_Security.sql
    One database role for the application: EXECUTE on the two area schemas,
    and nothing else. Every data access in the application is a stored
    procedure call, so the application login needs no table rights.

    Grant on SCHEMA covers procedures, functions AND the table types a
    procedure takes as a table-valued parameter.

    Put the application's SQL login (or IIS app-pool identity) into the role:
        ALTER ROLE stpl_pricing_app ADD MEMBER [YourAppLogin];

    A new pricing area (for example schema "intlfit") needs one more line:
        GRANT EXECUTE ON SCHEMA::intlfit TO stpl_pricing_app;
*/
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = 'stpl_pricing_app' AND type = 'R')
    CREATE ROLE stpl_pricing_app;
GO

GRANT EXECUTE ON SCHEMA::core    TO stpl_pricing_app;
GRANT EXECUTE ON SCHEMA::intlgit TO stpl_pricing_app;
GO
