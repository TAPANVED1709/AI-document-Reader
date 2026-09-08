-- ==========================================================
-- AI Document Reader - Database Initialization Script
-- Target: SQL Server 2022
-- ==========================================================

IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = N'AiDocumentReaderDb')
BEGIN
    CREATE DATABASE [AiDocumentReaderDb];
END
GO

USE [AiDocumentReaderDb];
GO

-- 1. Table: MedicalReports
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[MedicalReports]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[MedicalReports] (
        [Id]               UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_MedicalReports] PRIMARY KEY,
        [OriginalFileName] NVARCHAR(255)    NOT NULL,
        [StoredFileName]   NVARCHAR(255)    NOT NULL,
        [ContentType]      NVARCHAR(100)    NOT NULL,
        [FileSize]         BIGINT           NOT NULL,
        [Status]           NVARCHAR(50)     NOT NULL,
        [UploadedAt]       DATETIMEOFFSET   NOT NULL CONSTRAINT [DF_MedicalReports_UploadedAt] DEFAULT (SYSDATETIMEOFFSET()),
        [AnalysedAt]       DATETIMEOFFSET   NULL
    );

    CREATE NONCLUSTERED INDEX [IX_MedicalReports_UploadedAt] ON [dbo].[MedicalReports]([UploadedAt] DESC);
    CREATE NONCLUSTERED INDEX [IX_MedicalReports_Status] ON [dbo].[MedicalReports]([Status]);
END
GO

-- 2. Table: LabResults
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[LabResults]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[LabResults] (
        [Id]                   UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_LabResults] PRIMARY KEY,
        [MedicalReportId]      UNIQUEIDENTIFIER NOT NULL,
        [OriginalTestName]     NVARCHAR(255)    NOT NULL,
        [NormalizedTestName]   NVARCHAR(255)    NULL,
        [ValueNumeric]         DECIMAL(18, 4)   NULL,
        [ValueText]            NVARCHAR(100)    NOT NULL,
        [Unit]                 NVARCHAR(50)     NULL,
        [ReferenceMin]         DECIMAL(18, 4)   NULL,
        [ReferenceMax]         DECIMAL(18, 4)   NULL,
        [ReferenceText]        NVARCHAR(100)    NULL,
        [CalculatedStatus]     NVARCHAR(50)     NOT NULL, -- NORMAL, LOW, HIGH, UNKNOWN
        [ExtractionConfidence] DECIMAL(5, 4)    NOT NULL CONSTRAINT [DF_LabResults_ExtractionConfidence] DEFAULT (0.0000),
        [PageNumber]           INT              NOT NULL CONSTRAINT [DF_LabResults_PageNumber] DEFAULT (1),
        [BoundingBoxJson]      NVARCHAR(MAX)    NULL,
        [IsVerified]           BIT              NOT NULL CONSTRAINT [DF_LabResults_IsVerified] DEFAULT (0),
        [CreatedAt]            DATETIMEOFFSET   NOT NULL CONSTRAINT [DF_LabResults_CreatedAt] DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT [FK_LabResults_MedicalReports_MedicalReportId] FOREIGN KEY ([MedicalReportId])
            REFERENCES [dbo].[MedicalReports] ([Id]) ON DELETE CASCADE
    );

    CREATE NONCLUSTERED INDEX [IX_LabResults_MedicalReportId] ON [dbo].[LabResults]([MedicalReportId]);
    CREATE NONCLUSTERED INDEX [IX_LabResults_CalculatedStatus] ON [dbo].[LabResults]([CalculatedStatus]);
END
GO

-- 3. Table: AnalysisRuns
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[AnalysisRuns]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[AnalysisRuns] (
        [Id]               UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_AnalysisRuns] PRIMARY KEY,
        [MedicalReportId]  UNIQUEIDENTIFIER NOT NULL,
        [StartedAt]        DATETIMEOFFSET   NOT NULL CONSTRAINT [DF_AnalysisRuns_StartedAt] DEFAULT (SYSDATETIMEOFFSET()),
        [CompletedAt]      DATETIMEOFFSET   NULL,
        [Status]           NVARCHAR(50)     NOT NULL, -- Running, Completed, Failed
        [ProcessorVersion] NVARCHAR(50)     NOT NULL,
        [ErrorMessage]     NVARCHAR(MAX)    NULL,
        CONSTRAINT [FK_AnalysisRuns_MedicalReports_MedicalReportId] FOREIGN KEY ([MedicalReportId])
            REFERENCES [dbo].[MedicalReports] ([Id]) ON DELETE CASCADE
    );

    CREATE NONCLUSTERED INDEX [IX_AnalysisRuns_MedicalReportId] ON [dbo].[AnalysisRuns]([MedicalReportId]);
    CREATE NONCLUSTERED INDEX [IX_AnalysisRuns_Status] ON [dbo].[AnalysisRuns]([Status]);
END
GO
