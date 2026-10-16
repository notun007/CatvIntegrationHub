IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS(SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261016070202_ExistingCasHubBaseline')
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261016070202_ExistingCasHubBaseline', N'6.0.36');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS(SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261016070325_AddAuthorizationDetailsToSvCards')
BEGIN
    ALTER TABLE [dbo].[SvCards] ADD [EndTime] datetimeoffset(7) NULL;
END;
GO

IF NOT EXISTS(SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261016070325_AddAuthorizationDetailsToSvCards')
BEGIN
    ALTER TABLE [dbo].[SvCards] ADD [ProductId] varchar(50) NULL;
END;
GO

IF NOT EXISTS(SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261016070325_AddAuthorizationDetailsToSvCards')
BEGIN
    ALTER TABLE [dbo].[SvCards] ADD [StartTime] datetimeoffset(7) NULL;
END;
GO

IF NOT EXISTS(SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261016070325_AddAuthorizationDetailsToSvCards')
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261016070325_AddAuthorizationDetailsToSvCards', N'6.0.36');
END;
GO

COMMIT;
GO

