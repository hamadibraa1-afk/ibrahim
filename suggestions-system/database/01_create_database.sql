-- =============================================================================
-- نظام الاقتراحات الرقمي — جمعية الشارقة الخيرية
-- مخطط قاعدة البيانات لـ SQL Server
--
-- مولَّد من نموذج EF Core مباشرة حتى يطابق الخادم حرفياً:
--   cd api/ProposalSystem.Api
--   DatabaseProvider=SqlServer dotnet ef dbcontext script -o ../../database/01_create_database.sql
--
-- الجديد في هذا الإصدار: جلسات الخادم (UserSessions)، المدير المباشر وقفل الحساب
-- في Users، حقول التصعيد والكشف عن الهوية في Proposals، وجدول ImpactAssessments
-- لقياس الأثر الفعلي والعائد على الاستثمار.
-- =============================================================================

IF DB_ID(N'ProposalSystem') IS NULL
    CREATE DATABASE [ProposalSystem];
GO

USE [ProposalSystem];
GO

CREATE TABLE [AuditLogs] (
    [Id] bigint NOT NULL IDENTITY,
    [ProposalId] int NOT NULL,
    [ProposalCode] nvarchar(30) NOT NULL,
    [ActorId] int NULL,
    [ActorName] nvarchar(150) NOT NULL,
    [ActorRole] nvarchar(40) NOT NULL,
    [Action] nvarchar(40) NOT NULL,
    [FromStatus] nvarchar(40) NULL,
    [ToStatus] nvarchar(40) NULL,
    [Notes] nvarchar(max) NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_AuditLogs] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [FormFields] (
    [Id] int NOT NULL IDENTITY,
    [FieldKey] nvarchar(60) NOT NULL,
    [LabelAr] nvarchar(200) NOT NULL,
    [LabelEn] nvarchar(200) NULL,
    [Placeholder] nvarchar(300) NULL,
    [FieldType] nvarchar(40) NOT NULL,
    [Section] nvarchar(40) NOT NULL,
    [IsRequired] bit NOT NULL,
    [IsActive] bit NOT NULL,
    [IsSystem] bit NOT NULL,
    [SortOrder] int NOT NULL,
    [OptionsJson] nvarchar(max) NOT NULL,
    CONSTRAINT [PK_FormFields] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [Users] (
    [Id] int NOT NULL IDENTITY,
    [UserCode] nvarchar(30) NOT NULL,
    [ArabicName] nvarchar(150) NOT NULL,
    [EnglishName] nvarchar(150) NOT NULL,
    [Email] nvarchar(200) NOT NULL,
    [PhoneNumber] nvarchar(30) NOT NULL,
    [Department] nvarchar(150) NOT NULL,
    [JobTitle] nvarchar(150) NOT NULL,
    [Role] nvarchar(40) NOT NULL,
    [Status] nvarchar(40) NOT NULL,
    [PasswordHash] nvarchar(200) NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [ManagerId] int NULL,
    [FailedLoginCount] int NOT NULL,
    [LockoutEndAt] datetime2 NULL,
    CONSTRAINT [PK_Users] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Users_Users_ManagerId] FOREIGN KEY ([ManagerId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
);
GO


CREATE TABLE [Proposals] (
    [Id] int NOT NULL IDENTITY,
    [ProposalCode] nvarchar(30) NOT NULL,
    [Title] nvarchar(300) NOT NULL,
    [ImplementationMechanism] nvarchar(max) NOT NULL,
    [SubmissionReasons] nvarchar(max) NOT NULL,
    [Department] nvarchar(150) NOT NULL,
    [SubmitterId] int NOT NULL,
    [Status] nvarchar(40) NOT NULL,
    [ScreenerNotes] nvarchar(max) NULL,
    [RejectionReason] nvarchar(max) NULL,
    [Classification] nvarchar(40) NULL,
    [NotApplicableReasonValue] nvarchar(40) NULL,
    [CommitteeStudy] nvarchar(max) NULL,
    [CommitteeRecommendation] nvarchar(max) NULL,
    [ExecutiveDecision] nvarchar(40) NULL,
    [ExecutiveDecisionNotes] nvarchar(max) NULL,
    [ExecutiveDecisionById] int NULL,
    [ExecutiveDecisionAt] datetime2 NULL,
    [OwnerId] int NULL,
    [SubmittedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NOT NULL,
    [ResubmitCount] int NOT NULL,
    [LastResubmittedAt] datetime2 NULL,
    [StageEnteredAt] datetime2 NOT NULL,
    [SlaDueAt] datetime2 NOT NULL,
    [LastSlaAlertAt] datetime2 NULL,
    [EscalatedAt] datetime2 NULL,
    [EscalationNote] nvarchar(max) NULL,
    [EscalationLevel] int NOT NULL,
    [EscalatedToId] int NULL,
    [IdentityRevealedAt] datetime2 NULL,
    CONSTRAINT [PK_Proposals] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Proposals_Users_EscalatedToId] FOREIGN KEY ([EscalatedToId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Proposals_Users_ExecutiveDecisionById] FOREIGN KEY ([ExecutiveDecisionById]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Proposals_Users_OwnerId] FOREIGN KEY ([OwnerId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Proposals_Users_SubmitterId] FOREIGN KEY ([SubmitterId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
);
GO


CREATE TABLE [Sessions] (
    [Id] bigint NOT NULL IDENTITY,
    [UserId] int NOT NULL,
    [TokenHash] nvarchar(64) NOT NULL,
    [CsrfToken] nvarchar(64) NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [LastSeenAt] datetime2 NOT NULL,
    [ExpiresAt] datetime2 NOT NULL,
    [AbsoluteExpiresAt] datetime2 NOT NULL,
    [UserAgent] nvarchar(300) NULL,
    [IpAddress] nvarchar(64) NULL,
    CONSTRAINT [PK_Sessions] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Sessions_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
);
GO


CREATE TABLE [Attachments] (
    [Id] int NOT NULL IDENTITY,
    [ProposalId] int NOT NULL,
    [FileName] nvarchar(260) NOT NULL,
    [StoredName] nvarchar(100) NOT NULL,
    [ContentType] nvarchar(150) NOT NULL,
    [SizeBytes] bigint NOT NULL,
    [UploadedAt] datetime2 NOT NULL,
    [UploadedById] int NOT NULL,
    CONSTRAINT [PK_Attachments] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Attachments_Proposals_ProposalId] FOREIGN KEY ([ProposalId]) REFERENCES [Proposals] ([Id]) ON DELETE CASCADE
);
GO


CREATE TABLE [CommitteeVotes] (
    [Id] int NOT NULL IDENTITY,
    [ProposalId] int NOT NULL,
    [MemberId] int NOT NULL,
    [Signed] bit NOT NULL,
    [SignedAt] datetime2 NULL,
    CONSTRAINT [PK_CommitteeVotes] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_CommitteeVotes_Proposals_ProposalId] FOREIGN KEY ([ProposalId]) REFERENCES [Proposals] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_CommitteeVotes_Users_MemberId] FOREIGN KEY ([MemberId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
);
GO


CREATE TABLE [FieldValues] (
    [Id] int NOT NULL IDENTITY,
    [ProposalId] int NOT NULL,
    [FieldId] int NOT NULL,
    [Value] nvarchar(max) NULL,
    CONSTRAINT [PK_FieldValues] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_FieldValues_FormFields_FieldId] FOREIGN KEY ([FieldId]) REFERENCES [FormFields] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_FieldValues_Proposals_ProposalId] FOREIGN KEY ([ProposalId]) REFERENCES [Proposals] ([Id]) ON DELETE CASCADE
);
GO


CREATE TABLE [ImpactAssessments] (
    [Id] int NOT NULL IDENTITY,
    [ProposalId] int NOT NULL,
    [Status] nvarchar(40) NOT NULL,
    [OpensAt] datetime2 NOT NULL,
    [DueAt] datetime2 NOT NULL,
    [OpenedNoticeAt] datetime2 NULL,
    [EscalatedAt] datetime2 NULL,
    [ActualAnnualSavings] decimal(18,2) NULL,
    [ActualAnnualRevenue] decimal(18,2) NULL,
    [ImplementationCost] decimal(18,2) NULL,
    [HoursSavedPerMonth] decimal(18,2) NULL,
    [BeneficiariesReached] int NULL,
    [SatisfactionBefore] decimal(18,2) NULL,
    [SatisfactionAfter] decimal(18,2) NULL,
    [TargetAchievementPercent] decimal(18,2) NULL,
    [ImpactRating] int NULL,
    [Summary] nvarchar(max) NULL,
    [EvidenceReference] nvarchar(500) NULL,
    [MeasuredAt] datetime2 NULL,
    [MeasuredById] int NULL,
    [VerifiedAt] datetime2 NULL,
    [VerifiedById] int NULL,
    [ReviewNotes] nvarchar(max) NULL,
    CONSTRAINT [PK_ImpactAssessments] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ImpactAssessments_Proposals_ProposalId] FOREIGN KEY ([ProposalId]) REFERENCES [Proposals] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_ImpactAssessments_Users_MeasuredById] FOREIGN KEY ([MeasuredById]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ImpactAssessments_Users_VerifiedById] FOREIGN KEY ([VerifiedById]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
);
GO


CREATE TABLE [Notifications] (
    [Id] bigint NOT NULL IDENTITY,
    [UserId] int NOT NULL,
    [Type] nvarchar(40) NOT NULL,
    [Message] nvarchar(600) NOT NULL,
    [ProposalId] int NULL,
    [IsRead] bit NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_Notifications] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Notifications_Proposals_ProposalId] FOREIGN KEY ([ProposalId]) REFERENCES [Proposals] ([Id]) ON DELETE CASCADE
);
GO


CREATE INDEX [IX_Attachments_ProposalId] ON [Attachments] ([ProposalId]);
GO


CREATE INDEX [IX_AuditLogs_CreatedAt] ON [AuditLogs] ([CreatedAt]);
GO


CREATE INDEX [IX_AuditLogs_ProposalId] ON [AuditLogs] ([ProposalId]);
GO


CREATE INDEX [IX_CommitteeVotes_MemberId] ON [CommitteeVotes] ([MemberId]);
GO


CREATE UNIQUE INDEX [IX_CommitteeVotes_ProposalId_MemberId] ON [CommitteeVotes] ([ProposalId], [MemberId]);
GO


CREATE INDEX [IX_FieldValues_FieldId] ON [FieldValues] ([FieldId]);
GO


CREATE UNIQUE INDEX [IX_FieldValues_ProposalId_FieldId] ON [FieldValues] ([ProposalId], [FieldId]);
GO


CREATE UNIQUE INDEX [IX_FormFields_FieldKey] ON [FormFields] ([FieldKey]);
GO


CREATE INDEX [IX_ImpactAssessments_MeasuredById] ON [ImpactAssessments] ([MeasuredById]);
GO


CREATE UNIQUE INDEX [IX_ImpactAssessments_ProposalId] ON [ImpactAssessments] ([ProposalId]);
GO


CREATE INDEX [IX_ImpactAssessments_Status] ON [ImpactAssessments] ([Status]);
GO


CREATE INDEX [IX_ImpactAssessments_VerifiedById] ON [ImpactAssessments] ([VerifiedById]);
GO


CREATE INDEX [IX_Notifications_ProposalId] ON [Notifications] ([ProposalId]);
GO


CREATE INDEX [IX_Notifications_UserId_IsRead] ON [Notifications] ([UserId], [IsRead]);
GO


CREATE INDEX [IX_Proposals_EscalatedToId] ON [Proposals] ([EscalatedToId]);
GO


CREATE INDEX [IX_Proposals_ExecutiveDecisionById] ON [Proposals] ([ExecutiveDecisionById]);
GO


CREATE INDEX [IX_Proposals_OwnerId] ON [Proposals] ([OwnerId]);
GO


CREATE UNIQUE INDEX [IX_Proposals_ProposalCode] ON [Proposals] ([ProposalCode]);
GO


CREATE INDEX [IX_Proposals_SlaDueAt] ON [Proposals] ([SlaDueAt]);
GO


CREATE INDEX [IX_Proposals_Status] ON [Proposals] ([Status]);
GO


CREATE INDEX [IX_Proposals_SubmitterId] ON [Proposals] ([SubmitterId]);
GO


CREATE INDEX [IX_Sessions_ExpiresAt] ON [Sessions] ([ExpiresAt]);
GO


CREATE UNIQUE INDEX [IX_Sessions_TokenHash] ON [Sessions] ([TokenHash]);
GO


CREATE INDEX [IX_Sessions_UserId] ON [Sessions] ([UserId]);
GO


CREATE INDEX [IX_Users_ManagerId] ON [Users] ([ManagerId]);
GO


CREATE UNIQUE INDEX [IX_Users_UserCode] ON [Users] ([UserCode]);
GO


