-- Local/development provisioning only. Run against master using the local administrator.
IF DB_ID(N'MedicalSoftwareIntegrationDb') IS NULL
    CREATE DATABASE [MedicalSoftwareIntegrationDb];
GO
USE [MedicalSoftwareIntegrationDb];
GO
IF OBJECT_ID(N'dbo.SelfUploadedReportData', N'U') IS NULL
CREATE TABLE [dbo].[SelfUploadedReportData](
    [Id] [bigint] IDENTITY(1,1) NOT NULL,
    [TestName] [nvarchar](max) NULL,
    [Result] [decimal](18, 5) NULL,
    [Unit] [nvarchar](max) NULL,
    [ReferenceRange] [nvarchar](max) NULL,
    PRIMARY KEY CLUSTERED ([Id] ASC)
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY];
