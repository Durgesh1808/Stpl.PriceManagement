/*
    00_CreateDatabase.sql
    Creates the new database Int_StplGITPricing.

    Run this ONCE in SSMS while connected to the server (any database is fine).
    Then run the rest of the scripts against Int_StplGITPricing, in folder order -
    or simply run Int_StplGITPricing.sql, which contains all of them.
*/
IF DB_ID(N'Int_StplGITPricing') IS NULL
BEGIN
    CREATE DATABASE Int_StplGITPricing;
END
GO
