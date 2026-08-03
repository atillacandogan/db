SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_ID(N'DbScannerDemo') IS NULL
BEGIN
    CREATE DATABASE [DbScannerDemo];
END;
GO

DECLARE @AppPasswordBase64 varchar(max) = '$(DemoAppPasswordBase64)';
DECLARE @ScannerPasswordBase64 varchar(max) = '$(DemoScannerPasswordBase64)';
DECLARE @AppPassword varchar(128) =
    CONVERT(
        varchar(128),
        CAST('<binary>' + @AppPasswordBase64 + '</binary>' AS xml)
            .value('.', 'varbinary(max)'));
DECLARE @ScannerPassword varchar(128) =
    CONVERT(
        varchar(128),
        CAST('<binary>' + @ScannerPasswordBase64 + '</binary>' AS xml)
            .value('.', 'varbinary(max)'));

IF NULLIF(@AppPassword, '') IS NULL OR NULLIF(@ScannerPassword, '') IS NULL
BEGIN
    THROW 50001, 'Demo login passwords must not be empty.', 1;
END;

IF SUSER_ID(N'dbscanner_app') IS NULL
BEGIN
    DECLARE @CreateAppLoginSql nvarchar(max) =
        N'CREATE LOGIN [dbscanner_app] WITH PASSWORD = N'''
        + REPLACE(@AppPassword, '''', '''''')
        + N''''
        + N', CHECK_POLICY = ON, CHECK_EXPIRATION = ON;';
    EXEC sys.sp_executesql @CreateAppLoginSql;
END
ELSE
BEGIN
    DECLARE @AlterAppLoginSql nvarchar(max) =
        N'ALTER LOGIN [dbscanner_app] WITH PASSWORD = N'''
        + REPLACE(@AppPassword, '''', '''''')
        + N''''
        + N', CHECK_POLICY = ON, CHECK_EXPIRATION = ON;';
    EXEC sys.sp_executesql @AlterAppLoginSql;
END;

IF SUSER_ID(N'dbscanner_scanner') IS NULL
BEGIN
    DECLARE @CreateScannerLoginSql nvarchar(max) =
        N'CREATE LOGIN [dbscanner_scanner] WITH PASSWORD = N'''
        + REPLACE(@ScannerPassword, '''', '''''')
        + N''''
        + N', CHECK_POLICY = ON, CHECK_EXPIRATION = ON;';
    EXEC sys.sp_executesql @CreateScannerLoginSql;
END
ELSE
BEGIN
    DECLARE @AlterScannerLoginSql nvarchar(max) =
        N'ALTER LOGIN [dbscanner_scanner] WITH PASSWORD = N'''
        + REPLACE(@ScannerPassword, '''', '''''')
        + N''''
        + N', CHECK_POLICY = ON, CHECK_EXPIRATION = ON;';
    EXEC sys.sp_executesql @AlterScannerLoginSql;
END;

GRANT VIEW SERVER STATE TO [dbscanner_scanner];
GRANT VIEW ANY DEFINITION TO [dbscanner_scanner];
GRANT VIEW ANY DATABASE TO [dbscanner_scanner];
GRANT CONNECT ANY DATABASE TO [dbscanner_scanner];
GO

USE [DbScannerDemo];

IF DATABASE_PRINCIPAL_ID(N'dbscanner_app') IS NULL
BEGIN
    CREATE USER [dbscanner_app] FOR LOGIN [dbscanner_app];
END;

IF DATABASE_PRINCIPAL_ID(N'dbscanner_scanner') IS NULL
BEGIN
    CREATE USER [dbscanner_scanner] FOR LOGIN [dbscanner_scanner];
END;

IF OBJECT_ID(N'dbo.SampleAssets', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SampleAssets
    (
        AssetId int IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_SampleAssets PRIMARY KEY,
        AssetName nvarchar(160) NOT NULL,
        Classification nvarchar(32) NOT NULL,
        CreatedAt datetime2(0) NOT NULL
            CONSTRAINT DF_SampleAssets_CreatedAt DEFAULT SYSUTCDATETIME()
    );
END;

IF NOT EXISTS (SELECT 1 FROM dbo.SampleAssets)
BEGIN
    INSERT INTO dbo.SampleAssets (AssetName, Classification)
    VALUES
        (N'Demo customer database', N'confidential'),
        (N'Demo reporting database', N'internal');
END;

IF IS_ROLEMEMBER(N'db_datareader', N'dbscanner_app') <> 1
BEGIN
    ALTER ROLE [db_datareader] ADD MEMBER [dbscanner_app];
END;

IF IS_ROLEMEMBER(N'db_datawriter', N'dbscanner_app') <> 1
BEGIN
    ALTER ROLE [db_datawriter] ADD MEMBER [dbscanner_app];
END;

GRANT VIEW DEFINITION TO [dbscanner_scanner];
GRANT VIEW DATABASE STATE TO [dbscanner_scanner];
GO

USE [master];

DECLARE @EnableRiskySettings bit =
    CASE WHEN LOWER('$(EnableRiskySettings)') IN ('1', 'true', 'yes') THEN 1 ELSE 0 END;
DECLARE @IsLinux bit =
    CASE WHEN EXISTS (
        SELECT 1
        FROM sys.dm_os_host_info
        WHERE host_platform = N'Linux') THEN 1 ELSE 0 END;

EXEC sys.sp_configure N'show advanced options', 1;
RECONFIGURE;

IF @IsLinux = 0
BEGIN
    EXEC sys.sp_configure N'xp_cmdshell', @EnableRiskySettings;
    EXEC sys.sp_configure N'Ole Automation Procedures', @EnableRiskySettings;
END;

EXEC sys.sp_configure N'clr enabled', @EnableRiskySettings;
EXEC sys.sp_configure N'Ad Hoc Distributed Queries', @EnableRiskySettings;
RECONFIGURE;
EXEC sys.sp_configure N'show advanced options', 0;
RECONFIGURE;

IF @EnableRiskySettings = 1
BEGIN
    ALTER DATABASE [DbScannerDemo] SET TRUSTWORTHY ON;
    ALTER DATABASE [DbScannerDemo] SET DB_CHAINING ON;
END
ELSE
BEGIN
    ALTER DATABASE [DbScannerDemo] SET TRUSTWORTHY OFF;
    ALTER DATABASE [DbScannerDemo] SET DB_CHAINING OFF;
END;
GO

USE [DbScannerDemo];

DECLARE @EnableGuest bit =
    CASE WHEN LOWER('$(EnableRiskySettings)') IN ('1', 'true', 'yes') THEN 1 ELSE 0 END;

IF @EnableGuest = 1
BEGIN
    GRANT CONNECT TO [guest];
END
ELSE
BEGIN
    REVOKE CONNECT FROM [guest];
END;
GO
