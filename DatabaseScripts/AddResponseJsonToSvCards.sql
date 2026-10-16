BEGIN TRANSACTION;
GO

IF NOT EXISTS(SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261016084509_AddResponseJsonToSvCards')
BEGIN
    ALTER TABLE [dbo].[SvCards] ADD [ResponseJson] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS(SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261016084509_AddResponseJsonToSvCards')
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261016084509_AddResponseJsonToSvCards', N'6.0.36');
END;
GO

COMMIT;
GO

