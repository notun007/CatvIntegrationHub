BEGIN TRANSACTION;
GO

IF NOT EXISTS(SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261016071853_AddPacketHexToSvCards')
BEGIN
    ALTER TABLE [dbo].[SvCards] ADD [RequestHex] varchar(max) NULL;
END;
GO

IF NOT EXISTS(SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261016071853_AddPacketHexToSvCards')
BEGIN
    ALTER TABLE [dbo].[SvCards] ADD [ResponseHex] varchar(max) NULL;
END;
GO

IF NOT EXISTS(SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261016071853_AddPacketHexToSvCards')
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261016071853_AddPacketHexToSvCards', N'6.0.36');
END;
GO

COMMIT;
GO

