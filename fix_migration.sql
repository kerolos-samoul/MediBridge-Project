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

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260531185506_Phase2IdentityApproval'
)
BEGIN
    CREATE TABLE [AspNetRoles] (
        [Id] nvarchar(450) NOT NULL,
        [Name] nvarchar(256) NULL,
        [NormalizedName] nvarchar(256) NULL,
        [ConcurrencyStamp] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetRoles] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260531185506_Phase2IdentityApproval'
)
BEGIN
    CREATE TABLE [Users] (
        [Id] nvarchar(450) NOT NULL,
        [Role] nvarchar(32) NOT NULL,
        [AccountStatus] nvarchar(32) NOT NULL,
        [EmailVerified] bit NOT NULL,
        [PhoneVerified] bit NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [ApprovedAtUtc] datetime2 NULL,
        [LastStatusChangedAtUtc] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [UserName] nvarchar(256) NULL,
        [NormalizedUserName] nvarchar(256) NULL,
        [Email] nvarchar(256) NULL,
        [NormalizedEmail] nvarchar(256) NULL,
        [EmailConfirmed] bit NOT NULL,
        [PasswordHash] nvarchar(max) NULL,
        [SecurityStamp] nvarchar(max) NULL,
        [ConcurrencyStamp] nvarchar(max) NULL,
        [PhoneNumber] nvarchar(450) NULL,
        [PhoneNumberConfirmed] bit NOT NULL,
        [TwoFactorEnabled] bit NOT NULL,
        [LockoutEnd] datetimeoffset NULL,
        [LockoutEnabled] bit NOT NULL,
        [AccessFailedCount] int NOT NULL,
        CONSTRAINT [PK_Users] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260531185506_Phase2IdentityApproval'
)
BEGIN
    CREATE TABLE [AspNetRoleClaims] (
        [Id] int NOT NULL IDENTITY,
        [RoleId] nvarchar(450) NOT NULL,
        [ClaimType] nvarchar(max) NULL,
        [ClaimValue] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetRoleClaims] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AspNetRoleClaims_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260531185506_Phase2IdentityApproval'
)
BEGIN
    CREATE TABLE [AccountResubmissions] (
        [Id] nvarchar(450) NOT NULL,
        [UserId] nvarchar(450) NOT NULL,
        [SubmittedAtUtc] datetime2 NOT NULL,
        [UpdatedProfileFields] nvarchar(4000) NOT NULL,
        [UpdatedVerificationMetadata] nvarchar(4000) NOT NULL,
        CONSTRAINT [PK_AccountResubmissions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AccountResubmissions_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260531185506_Phase2IdentityApproval'
)
BEGIN
    CREATE TABLE [AdminAccountDecisions] (
        [Id] nvarchar(450) NOT NULL,
        [AdminUserId] nvarchar(450) NOT NULL,
        [TargetUserId] nvarchar(450) NOT NULL,
        [Decision] nvarchar(32) NOT NULL,
        [ResultingAccountStatus] nvarchar(32) NOT NULL,
        [Reason] nvarchar(1000) NULL,
        [Notes] nvarchar(2000) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_AdminAccountDecisions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AdminAccountDecisions_Users_AdminUserId] FOREIGN KEY ([AdminUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_AdminAccountDecisions_Users_TargetUserId] FOREIGN KEY ([TargetUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260531185506_Phase2IdentityApproval'
)
BEGIN
    CREATE TABLE [AspNetUserClaims] (
        [Id] int NOT NULL IDENTITY,
        [UserId] nvarchar(450) NOT NULL,
        [ClaimType] nvarchar(max) NULL,
        [ClaimValue] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetUserClaims] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AspNetUserClaims_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260531185506_Phase2IdentityApproval'
)
BEGIN
    CREATE TABLE [AspNetUserLogins] (
        [LoginProvider] nvarchar(450) NOT NULL,
        [ProviderKey] nvarchar(450) NOT NULL,
        [ProviderDisplayName] nvarchar(max) NULL,
        [UserId] nvarchar(450) NOT NULL,
        CONSTRAINT [PK_AspNetUserLogins] PRIMARY KEY ([LoginProvider], [ProviderKey]),
        CONSTRAINT [FK_AspNetUserLogins_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260531185506_Phase2IdentityApproval'
)
BEGIN
    CREATE TABLE [AspNetUserRoles] (
        [UserId] nvarchar(450) NOT NULL,
        [RoleId] nvarchar(450) NOT NULL,
        CONSTRAINT [PK_AspNetUserRoles] PRIMARY KEY ([UserId], [RoleId]),
        CONSTRAINT [FK_AspNetUserRoles_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_AspNetUserRoles_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260531185506_Phase2IdentityApproval'
)
BEGIN
    CREATE TABLE [AspNetUserTokens] (
        [UserId] nvarchar(450) NOT NULL,
        [LoginProvider] nvarchar(450) NOT NULL,
        [Name] nvarchar(450) NOT NULL,
        [Value] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetUserTokens] PRIMARY KEY ([UserId], [LoginProvider], [Name]),
        CONSTRAINT [FK_AspNetUserTokens_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260531185506_Phase2IdentityApproval'
)
BEGIN
    CREATE TABLE [AuthenticationAuditEvents] (
        [Id] nvarchar(450) NOT NULL,
        [EventType] nvarchar(64) NOT NULL,
        [ActorUserId] nvarchar(450) NULL,
        [TargetUserId] nvarchar(450) NULL,
        [Role] nvarchar(32) NULL,
        [Outcome] nvarchar(80) NOT NULL,
        [Reason] nvarchar(1000) NULL,
        [CorrelationId] nvarchar(128) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_AuthenticationAuditEvents] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AuthenticationAuditEvents_Users_ActorUserId] FOREIGN KEY ([ActorUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_AuthenticationAuditEvents_Users_TargetUserId] FOREIGN KEY ([TargetUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260531185506_Phase2IdentityApproval'
)
BEGIN
    CREATE TABLE [CompanyProfiles] (
        [Id] nvarchar(450) NOT NULL,
        [UserId] nvarchar(450) NOT NULL,
        [CompanyName] nvarchar(200) NOT NULL,
        [LicenseNumber] nvarchar(120) NOT NULL,
        [ContactName] nvarchar(160) NOT NULL,
        [VerificationDocumentType] nvarchar(80) NOT NULL,
        [VerificationOriginalFileName] nvarchar(260) NOT NULL,
        [VerificationContentType] nvarchar(120) NOT NULL,
        [VerificationSizeBytes] bigint NOT NULL,
        [VerificationReference] nvarchar(500) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NULL,
        CONSTRAINT [PK_CompanyProfiles] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CompanyProfiles_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260531185506_Phase2IdentityApproval'
)
BEGIN
    CREATE TABLE [ContactVerificationFlows] (
        [Id] nvarchar(450) NOT NULL,
        [UserId] nvarchar(450) NOT NULL,
        [Channel] nvarchar(24) NOT NULL,
        [DestinationHash] nvarchar(256) NOT NULL,
        [TokenHash] nvarchar(256) NOT NULL,
        [ExpiresAtUtc] datetime2 NOT NULL,
        [ConsumedAtUtc] datetime2 NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_ContactVerificationFlows] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ContactVerificationFlows_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260531185506_Phase2IdentityApproval'
)
BEGIN
    CREATE TABLE [DoctorProfiles] (
        [Id] nvarchar(450) NOT NULL,
        [UserId] nvarchar(450) NOT NULL,
        [Specialization] nvarchar(160) NOT NULL,
        [ExperienceYears] int NOT NULL,
        [Location] nvarchar(200) NOT NULL,
        [VerificationDocumentType] nvarchar(80) NOT NULL,
        [VerificationOriginalFileName] nvarchar(260) NOT NULL,
        [VerificationContentType] nvarchar(120) NOT NULL,
        [VerificationSizeBytes] bigint NOT NULL,
        [VerificationReference] nvarchar(500) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NULL,
        CONSTRAINT [PK_DoctorProfiles] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DoctorProfiles_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260531185506_Phase2IdentityApproval'
)
BEGIN
    CREATE TABLE [PasswordResetFlows] (
        [Id] nvarchar(450) NOT NULL,
        [UserId] nvarchar(450) NULL,
        [TokenHash] nvarchar(256) NOT NULL,
        [ExpiresAtUtc] datetime2 NOT NULL,
        [ConsumedAtUtc] datetime2 NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [RequestCorrelationId] nvarchar(128) NOT NULL,
        CONSTRAINT [PK_PasswordResetFlows] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PasswordResetFlows_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260531185506_Phase2IdentityApproval'
)
BEGIN
    CREATE TABLE [RefreshCredentials] (
        [Id] nvarchar(450) NOT NULL,
        [TokenHash] nvarchar(256) NOT NULL,
        [UserId] nvarchar(450) NOT NULL,
        [FamilyId] nvarchar(64) NOT NULL,
        [ExpiresAtUtc] datetime2 NOT NULL,
        [RevokedAtUtc] datetime2 NULL,
        [RevocationReason] nvarchar(80) NULL,
        [ReplacedByTokenHash] nvarchar(256) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_RefreshCredentials] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RefreshCredentials_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260531185506_Phase2IdentityApproval'
)
BEGIN
    CREATE TABLE [AccountResubmissionTokens] (
        [Id] nvarchar(450) NOT NULL,
        [UserId] nvarchar(450) NOT NULL,
        [TokenHash] nvarchar(256) NOT NULL,
        [ExpiresAtUtc] datetime2 NOT NULL,
        [ConsumedAtUtc] datetime2 NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedByAdminDecisionId] nvarchar(450) NOT NULL,
        CONSTRAINT [PK_AccountResubmissionTokens] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AccountResubmissionTokens_AdminAccountDecisions_CreatedByAdminDecisionId] FOREIGN KEY ([CreatedByAdminDecisionId]) REFERENCES [AdminAccountDecisions] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_AccountResubmissionTokens_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260531185506_Phase2IdentityApproval'
)
BEGIN
    CREATE INDEX [IX_AccountResubmissions_UserId] ON [AccountResubmissions] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260531185506_Phase2IdentityApproval'
)
BEGIN
    CREATE INDEX [IX_AccountResubmissionTokens_CreatedByAdminDecisionId] ON [AccountResubmissionTokens] ([CreatedByAdminDecisionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260531185506_Phase2IdentityApproval'
)
BEGIN
    CREATE UNIQUE INDEX [IX_AccountResubmissionTokens_TokenHash] ON [AccountResubmissionTokens] ([TokenHash]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260531185506_Phase2IdentityApproval'
)
BEGIN
    CREATE INDEX [IX_AccountResubmissionTokens_UserId] ON [AccountResubmissionTokens] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260531185506_Phase2IdentityApproval'
)
BEGIN
    CREATE INDEX [IX_AdminAccountDecisions_AdminUserId] ON [AdminAccountDecisions] ([AdminUserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260531185506_Phase2IdentityApproval'
)
BEGIN
    CREATE INDEX [IX_AdminAccountDecisions_TargetUserId] ON [AdminAccountDecisions] ([TargetUserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260531185506_Phase2IdentityApproval'
)
BEGIN
    CREATE INDEX [IX_AspNetRoleClaims_RoleId] ON [AspNetRoleClaims] ([RoleId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260531185506_Phase2IdentityApproval'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [RoleNameIndex] ON [AspNetRoles] ([NormalizedName]) WHERE [NormalizedName] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260531185506_Phase2IdentityApproval'
)
BEGIN
    CREATE INDEX [IX_AspNetUserClaims_UserId] ON [AspNetUserClaims] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260531185506_Phase2IdentityApproval'
)
BEGIN
    CREATE INDEX [IX_AspNetUserLogins_UserId] ON [AspNetUserLogins] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260531185506_Phase2IdentityApproval'
)
BEGIN
    CREATE INDEX [IX_AspNetUserRoles_RoleId] ON [AspNetUserRoles] ([RoleId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260531185506_Phase2IdentityApproval'
)
BEGIN
    CREATE INDEX [IX_AuthenticationAuditEvents_ActorUserId] ON [AuthenticationAuditEvents] ([ActorUserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260531185506_Phase2IdentityApproval'
)
BEGIN
    CREATE INDEX [IX_AuthenticationAuditEvents_CreatedAtUtc] ON [AuthenticationAuditEvents] ([CreatedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260531185506_Phase2IdentityApproval'
)
BEGIN
    CREATE INDEX [IX_AuthenticationAuditEvents_TargetUserId] ON [AuthenticationAuditEvents] ([TargetUserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260531185506_Phase2IdentityApproval'
)
BEGIN
    CREATE UNIQUE INDEX [IX_CompanyProfiles_LicenseNumber] ON [CompanyProfiles] ([LicenseNumber]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260531185506_Phase2IdentityApproval'
)
BEGIN
    CREATE UNIQUE INDEX [IX_CompanyProfiles_UserId] ON [CompanyProfiles] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260531185506_Phase2IdentityApproval'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ContactVerificationFlows_TokenHash] ON [ContactVerificationFlows] ([TokenHash]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260531185506_Phase2IdentityApproval'
)
BEGIN
    CREATE INDEX [IX_ContactVerificationFlows_UserId] ON [ContactVerificationFlows] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260531185506_Phase2IdentityApproval'
)
BEGIN
    CREATE UNIQUE INDEX [IX_DoctorProfiles_UserId] ON [DoctorProfiles] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260531185506_Phase2IdentityApproval'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PasswordResetFlows_TokenHash] ON [PasswordResetFlows] ([TokenHash]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260531185506_Phase2IdentityApproval'
)
BEGIN
    CREATE INDEX [IX_PasswordResetFlows_UserId] ON [PasswordResetFlows] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260531185506_Phase2IdentityApproval'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RefreshCredentials_TokenHash] ON [RefreshCredentials] ([TokenHash]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260531185506_Phase2IdentityApproval'
)
BEGIN
    CREATE INDEX [IX_RefreshCredentials_UserId_FamilyId] ON [RefreshCredentials] ([UserId], [FamilyId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260531185506_Phase2IdentityApproval'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [EmailIndex] ON [Users] ([NormalizedEmail]) WHERE [IsDeleted] = 0 AND [NormalizedEmail] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260531185506_Phase2IdentityApproval'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Users_PhoneNumber] ON [Users] ([PhoneNumber]) WHERE [IsDeleted] = 0 AND [PhoneNumber] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260531185506_Phase2IdentityApproval'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UserNameIndex] ON [Users] ([NormalizedUserName]) WHERE [NormalizedUserName] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260531185506_Phase2IdentityApproval'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260531185506_Phase2IdentityApproval', N'8.0.11');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    DROP INDEX [IX_CompanyProfiles_LicenseNumber] ON [CompanyProfiles];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    ALTER TABLE [DoctorProfiles] ADD [ActivityScore] decimal(5,2) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    ALTER TABLE [DoctorProfiles] ADD [DailyMessageLimit] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    ALTER TABLE [DoctorProfiles] ADD [DeletedAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    ALTER TABLE [DoctorProfiles] ADD [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    ALTER TABLE [DoctorProfiles] ADD [MinimumWeeklyRequirement] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    ALTER TABLE [DoctorProfiles] ADD [PricePerMessage] decimal(18,2) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    ALTER TABLE [DoctorProfiles] ADD [RequestedDailyMessageLimit] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    ALTER TABLE [DoctorProfiles] ADD [RequestedMinimumWeeklyRequirement] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    ALTER TABLE [DoctorProfiles] ADD [Status] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    ALTER TABLE [CompanyProfiles] ADD [DeletedAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    ALTER TABLE [CompanyProfiles] ADD [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    CREATE TABLE [ActivityScoreHistories] (
        [Id] nvarchar(450) NOT NULL,
        [DoctorId] nvarchar(450) NOT NULL,
        [ActivityScore] decimal(5,2) NOT NULL,
        [ResponseSpeedScore] decimal(5,2) NULL,
        [EngagementScore] decimal(5,2) NULL,
        [FeedbackScore] decimal(5,2) NULL,
        [WindowStartDateEgypt] date NOT NULL,
        [WindowEndDateEgypt] date NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CorrectsHistoryId] nvarchar(450) NULL,
        CONSTRAINT [PK_ActivityScoreHistories] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ActivityScoreHistories_ActivityScoreHistories_CorrectsHistoryId] FOREIGN KEY ([CorrectsHistoryId]) REFERENCES [ActivityScoreHistories] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ActivityScoreHistories_DoctorProfiles_DoctorId] FOREIGN KEY ([DoctorId]) REFERENCES [DoctorProfiles] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    CREATE TABLE [AuditEvents] (
        [Id] nvarchar(450) NOT NULL,
        [EventType] nvarchar(160) NOT NULL,
        [ActorUserId] nvarchar(450) NULL,
        [ActorRole] nvarchar(80) NULL,
        [TargetType] int NULL,
        [TargetId] nvarchar(450) NULL,
        [Outcome] int NOT NULL,
        [Reason] nvarchar(1000) NULL,
        [CorrelationId] nvarchar(160) NULL,
        [Metadata] nvarchar(4000) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CorrectsAuditEventId] nvarchar(450) NULL,
        CONSTRAINT [PK_AuditEvents] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AuditEvents_AuditEvents_CorrectsAuditEventId] FOREIGN KEY ([CorrectsAuditEventId]) REFERENCES [AuditEvents] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_AuditEvents_Users_ActorUserId] FOREIGN KEY ([ActorUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    CREATE TABLE [DoctorPriceHistories] (
        [Id] nvarchar(450) NOT NULL,
        [DoctorId] nvarchar(450) NOT NULL,
        [PreviousPricePerMessage] decimal(18,2) NULL,
        [NewPricePerMessage] decimal(18,2) NULL,
        [ChangedByAdminUserId] nvarchar(450) NOT NULL,
        [Reason] nvarchar(1000) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CorrectsHistoryId] nvarchar(450) NULL,
        CONSTRAINT [PK_DoctorPriceHistories] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DoctorPriceHistories_DoctorPriceHistories_CorrectsHistoryId] FOREIGN KEY ([CorrectsHistoryId]) REFERENCES [DoctorPriceHistories] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_DoctorPriceHistories_DoctorProfiles_DoctorId] FOREIGN KEY ([DoctorId]) REFERENCES [DoctorProfiles] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_DoctorPriceHistories_Users_ChangedByAdminUserId] FOREIGN KEY ([ChangedByAdminUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    CREATE TABLE [PlatformFeePolicyHistories] (
        [Id] nvarchar(450) NOT NULL,
        [FeePercent] decimal(5,2) NOT NULL,
        [EffectiveFromUtc] datetime2 NOT NULL,
        [EffectiveToUtc] datetime2 NULL,
        [ChangedByAdminUserId] nvarchar(450) NOT NULL,
        [Reason] nvarchar(1000) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CorrectsHistoryId] nvarchar(450) NULL,
        CONSTRAINT [PK_PlatformFeePolicyHistories] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PlatformFeePolicyHistories_PlatformFeePolicyHistories_CorrectsHistoryId] FOREIGN KEY ([CorrectsHistoryId]) REFERENCES [PlatformFeePolicyHistories] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PlatformFeePolicyHistories_Users_ChangedByAdminUserId] FOREIGN KEY ([ChangedByAdminUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    CREATE TABLE [StoredFiles] (
        [Id] nvarchar(450) NOT NULL,
        [OwnerType] int NOT NULL,
        [OwnerId] nvarchar(450) NOT NULL,
        [Purpose] int NOT NULL,
        [OriginalFileName] nvarchar(260) NOT NULL,
        [ContentType] nvarchar(120) NOT NULL,
        [SizeBytes] bigint NOT NULL,
        [StorageKey] nvarchar(500) NOT NULL,
        [Visibility] int NOT NULL,
        [ReviewStatus] int NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [ReviewedAtUtc] datetime2 NULL,
        [ReviewedByAdminId] nvarchar(450) NULL,
        [ReviewReason] nvarchar(1000) NULL,
        CONSTRAINT [PK_StoredFiles] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_StoredFiles_Users_ReviewedByAdminId] FOREIGN KEY ([ReviewedByAdminId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    CREATE TABLE [Wallets] (
        [Id] nvarchar(450) NOT NULL,
        [OwnerType] int NOT NULL,
        [OwnerUserId] nvarchar(450) NULL,
        [OwnerId] nvarchar(450) NOT NULL,
        [AvailableBalance] decimal(18,2) NOT NULL,
        [ReservedBalance] decimal(18,2) NOT NULL,
        [Currency] nvarchar(3) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [ConcurrencyToken] rowversion NOT NULL,
        CONSTRAINT [PK_Wallets] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Wallets_Users_OwnerUserId] FOREIGN KEY ([OwnerUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    CREATE TABLE [WithdrawalRequests] (
        [Id] nvarchar(450) NOT NULL,
        [DoctorId] nvarchar(450) NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [Status] int NOT NULL,
        [RequestedAtUtc] datetime2 NOT NULL,
        [ReviewedByAdminUserId] nvarchar(450) NULL,
        [ReviewedAtUtc] datetime2 NULL,
        [DecisionReason] nvarchar(1000) NULL,
        [PayoutReference] nvarchar(200) NULL,
        [ConcurrencyToken] rowversion NOT NULL,
        CONSTRAINT [PK_WithdrawalRequests] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_WithdrawalRequests_DoctorProfiles_DoctorId] FOREIGN KEY ([DoctorId]) REFERENCES [DoctorProfiles] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_WithdrawalRequests_Users_ReviewedByAdminUserId] FOREIGN KEY ([ReviewedByAdminUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    CREATE TABLE [Campaigns] (
        [Id] nvarchar(450) NOT NULL,
        [CompanyId] nvarchar(450) NOT NULL,
        [Title] nvarchar(200) NOT NULL,
        [MediaFileId] nvarchar(450) NULL,
        [VoiceNoteFileId] nvarchar(450) NULL,
        [ClinicalResearchInfo] nvarchar(4000) NULL,
        [Description] nvarchar(4000) NOT NULL,
        [Status] int NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        CONSTRAINT [PK_Campaigns] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Campaigns_CompanyProfiles_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [CompanyProfiles] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Campaigns_StoredFiles_MediaFileId] FOREIGN KEY ([MediaFileId]) REFERENCES [StoredFiles] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Campaigns_StoredFiles_VoiceNoteFileId] FOREIGN KEY ([VoiceNoteFileId]) REFERENCES [StoredFiles] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    CREATE TABLE [CampaignReviewHistories] (
        [Id] nvarchar(450) NOT NULL,
        [CampaignId] nvarchar(450) NOT NULL,
        [AdminUserId] nvarchar(450) NOT NULL,
        [Decision] int NOT NULL,
        [Reason] nvarchar(1000) NULL,
        [Notes] nvarchar(2000) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CorrectsHistoryId] nvarchar(450) NULL,
        CONSTRAINT [PK_CampaignReviewHistories] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CampaignReviewHistories_CampaignReviewHistories_CorrectsHistoryId] FOREIGN KEY ([CorrectsHistoryId]) REFERENCES [CampaignReviewHistories] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_CampaignReviewHistories_Campaigns_CampaignId] FOREIGN KEY ([CampaignId]) REFERENCES [Campaigns] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_CampaignReviewHistories_Users_AdminUserId] FOREIGN KEY ([AdminUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    CREATE TABLE [CampaignTargets] (
        [Id] nvarchar(450) NOT NULL,
        [CampaignId] nvarchar(450) NOT NULL,
        [DoctorId] nvarchar(450) NOT NULL,
        [SpecializationSnapshot] nvarchar(160) NOT NULL,
        [ExperienceYearsSnapshot] int NOT NULL,
        [LocationSnapshot] nvarchar(200) NOT NULL,
        [ActivityScoreSnapshot] decimal(5,2) NOT NULL,
        [PricePerMessageSnapshot] decimal(18,2) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_CampaignTargets] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CampaignTargets_Campaigns_CampaignId] FOREIGN KEY ([CampaignId]) REFERENCES [Campaigns] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_CampaignTargets_DoctorProfiles_DoctorId] FOREIGN KEY ([DoctorId]) REFERENCES [DoctorProfiles] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    CREATE TABLE [DoctorAdDeliveries] (
        [Id] nvarchar(450) NOT NULL,
        [DoctorId] nvarchar(450) NOT NULL,
        [CampaignId] nvarchar(450) NOT NULL,
        [CompanyId] nvarchar(450) NOT NULL,
        [DeliveryDateEgypt] date NOT NULL,
        [DeliveredAtUtc] datetime2 NOT NULL,
        [ReadAtUtc] datetime2 NULL,
        [Status] int NOT NULL,
        [InteractedAtUtc] datetime2 NULL,
        [FeedbackText] nvarchar(4000) NULL,
        [FeedbackCreatedAtUtc] datetime2 NULL,
        [FeedbackQualityStatus] int NULL,
        [PricePerMessageSnapshot] decimal(18,2) NOT NULL,
        [PlatformFeePercentSnapshot] decimal(5,2) NOT NULL,
        [PlatformFeeAmount] decimal(18,2) NOT NULL,
        [DoctorEarnings] decimal(18,2) NOT NULL,
        [ReservedAmount] decimal(18,2) NOT NULL,
        [ReservationStatus] int NOT NULL,
        [ConcurrencyToken] rowversion NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NULL,
        CONSTRAINT [PK_DoctorAdDeliveries] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DoctorAdDeliveries_Campaigns_CampaignId] FOREIGN KEY ([CampaignId]) REFERENCES [Campaigns] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_DoctorAdDeliveries_CompanyProfiles_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [CompanyProfiles] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_DoctorAdDeliveries_DoctorProfiles_DoctorId] FOREIGN KEY ([DoctorId]) REFERENCES [DoctorProfiles] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    CREATE TABLE [DoctorMessageQueues] (
        [Id] nvarchar(450) NOT NULL,
        [DoctorId] nvarchar(450) NOT NULL,
        [CampaignId] nvarchar(450) NOT NULL,
        [QueuedAtUtc] datetime2 NOT NULL,
        [CampaignSubmittedAtUtc] datetime2 NULL,
        [Status] int NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NULL,
        CONSTRAINT [PK_DoctorMessageQueues] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DoctorMessageQueues_Campaigns_CampaignId] FOREIGN KEY ([CampaignId]) REFERENCES [Campaigns] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_DoctorMessageQueues_DoctorProfiles_DoctorId] FOREIGN KEY ([DoctorId]) REFERENCES [DoctorProfiles] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    CREATE TABLE [WalletTransactions] (
        [Id] nvarchar(450) NOT NULL,
        [WalletId] nvarchar(450) NOT NULL,
        [OperationType] int NOT NULL,
        [IdempotencyKey] nvarchar(160) NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [RelatedDeliveryId] nvarchar(450) NULL,
        [Description] nvarchar(1000) NULL,
        [Metadata] nvarchar(4000) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CorrectsTransactionId] nvarchar(450) NULL,
        CONSTRAINT [PK_WalletTransactions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_WalletTransactions_DoctorAdDeliveries_RelatedDeliveryId] FOREIGN KEY ([RelatedDeliveryId]) REFERENCES [DoctorAdDeliveries] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_WalletTransactions_WalletTransactions_CorrectsTransactionId] FOREIGN KEY ([CorrectsTransactionId]) REFERENCES [WalletTransactions] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_WalletTransactions_Wallets_WalletId] FOREIGN KEY ([WalletId]) REFERENCES [Wallets] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    CREATE TABLE [WalletLedgerEntries] (
        [Id] nvarchar(450) NOT NULL,
        [WalletTransactionId] nvarchar(450) NOT NULL,
        [WalletId] nvarchar(450) NOT NULL,
        [Direction] int NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [BalanceType] int NOT NULL,
        [Currency] nvarchar(3) NOT NULL,
        [CampaignId] nvarchar(max) NULL,
        [MessageDeliveryId] nvarchar(max) NULL,
        [DoctorId] nvarchar(max) NULL,
        [CompanyId] nvarchar(max) NULL,
        [WithdrawalRequestId] nvarchar(max) NULL,
        [IdempotencyKey] nvarchar(160) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_WalletLedgerEntries] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_WalletLedgerEntries_WalletTransactions_WalletTransactionId] FOREIGN KEY ([WalletTransactionId]) REFERENCES [WalletTransactions] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_WalletLedgerEntries_Wallets_WalletId] FOREIGN KEY ([WalletId]) REFERENCES [Wallets] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_CompanyProfiles_LicenseNumber] ON [CompanyProfiles] ([LicenseNumber]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    CREATE INDEX [IX_ActivityScoreHistories_CorrectsHistoryId] ON [ActivityScoreHistories] ([CorrectsHistoryId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    CREATE INDEX [IX_ActivityScoreHistories_DoctorId_CreatedAtUtc] ON [ActivityScoreHistories] ([DoctorId], [CreatedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    CREATE INDEX [IX_AuditEvents_ActorUserId] ON [AuditEvents] ([ActorUserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    CREATE INDEX [IX_AuditEvents_CorrectsAuditEventId] ON [AuditEvents] ([CorrectsAuditEventId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    CREATE INDEX [IX_AuditEvents_TargetType_TargetId_CreatedAtUtc] ON [AuditEvents] ([TargetType], [TargetId], [CreatedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    CREATE INDEX [IX_CampaignReviewHistories_AdminUserId] ON [CampaignReviewHistories] ([AdminUserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    CREATE INDEX [IX_CampaignReviewHistories_CampaignId_CreatedAtUtc] ON [CampaignReviewHistories] ([CampaignId], [CreatedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    CREATE INDEX [IX_CampaignReviewHistories_CorrectsHistoryId] ON [CampaignReviewHistories] ([CorrectsHistoryId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    CREATE INDEX [IX_Campaigns_CompanyId_CreatedAtUtc] ON [Campaigns] ([CompanyId], [CreatedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    CREATE INDEX [IX_Campaigns_MediaFileId] ON [Campaigns] ([MediaFileId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    CREATE INDEX [IX_Campaigns_VoiceNoteFileId] ON [Campaigns] ([VoiceNoteFileId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    CREATE UNIQUE INDEX [IX_CampaignTargets_CampaignId_DoctorId] ON [CampaignTargets] ([CampaignId], [DoctorId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    CREATE INDEX [IX_CampaignTargets_DoctorId] ON [CampaignTargets] ([DoctorId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    CREATE INDEX [IX_DoctorAdDeliveries_CampaignId] ON [DoctorAdDeliveries] ([CampaignId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    CREATE INDEX [IX_DoctorAdDeliveries_CompanyId] ON [DoctorAdDeliveries] ([CompanyId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    CREATE UNIQUE INDEX [IX_DoctorAdDeliveries_DoctorId_DeliveryDateEgypt_CampaignId] ON [DoctorAdDeliveries] ([DoctorId], [DeliveryDateEgypt], [CampaignId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    CREATE INDEX [IX_DoctorMessageQueues_CampaignId] ON [DoctorMessageQueues] ([CampaignId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    CREATE INDEX [IX_DoctorMessageQueues_DoctorId_Status_QueuedAtUtc_Id] ON [DoctorMessageQueues] ([DoctorId], [Status], [QueuedAtUtc], [Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    CREATE INDEX [IX_DoctorPriceHistories_ChangedByAdminUserId] ON [DoctorPriceHistories] ([ChangedByAdminUserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    CREATE INDEX [IX_DoctorPriceHistories_CorrectsHistoryId] ON [DoctorPriceHistories] ([CorrectsHistoryId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    CREATE INDEX [IX_DoctorPriceHistories_DoctorId_CreatedAtUtc] ON [DoctorPriceHistories] ([DoctorId], [CreatedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    CREATE INDEX [IX_PlatformFeePolicyHistories_ChangedByAdminUserId] ON [PlatformFeePolicyHistories] ([ChangedByAdminUserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    CREATE INDEX [IX_PlatformFeePolicyHistories_CorrectsHistoryId] ON [PlatformFeePolicyHistories] ([CorrectsHistoryId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    CREATE INDEX [IX_PlatformFeePolicyHistories_EffectiveFromUtc_EffectiveToUtc] ON [PlatformFeePolicyHistories] ([EffectiveFromUtc], [EffectiveToUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    CREATE INDEX [IX_StoredFiles_OwnerType_OwnerId_Purpose] ON [StoredFiles] ([OwnerType], [OwnerId], [Purpose]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    CREATE INDEX [IX_StoredFiles_ReviewedByAdminId] ON [StoredFiles] ([ReviewedByAdminId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    CREATE INDEX [IX_StoredFiles_ReviewStatus_CreatedAtUtc] ON [StoredFiles] ([ReviewStatus], [CreatedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    CREATE INDEX [IX_WalletLedgerEntries_WalletId_CreatedAtUtc] ON [WalletLedgerEntries] ([WalletId], [CreatedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    CREATE INDEX [IX_WalletLedgerEntries_WalletTransactionId_CreatedAtUtc] ON [WalletLedgerEntries] ([WalletTransactionId], [CreatedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Wallets_OwnerType_OwnerId] ON [Wallets] ([OwnerType], [OwnerId]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    CREATE INDEX [IX_Wallets_OwnerUserId] ON [Wallets] ([OwnerUserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    CREATE INDEX [IX_WalletTransactions_CorrectsTransactionId] ON [WalletTransactions] ([CorrectsTransactionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    CREATE UNIQUE INDEX [IX_WalletTransactions_OperationType_IdempotencyKey] ON [WalletTransactions] ([OperationType], [IdempotencyKey]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    CREATE INDEX [IX_WalletTransactions_RelatedDeliveryId] ON [WalletTransactions] ([RelatedDeliveryId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    CREATE INDEX [IX_WalletTransactions_WalletId_CreatedAtUtc] ON [WalletTransactions] ([WalletId], [CreatedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    CREATE INDEX [IX_WithdrawalRequests_DoctorId] ON [WithdrawalRequests] ([DoctorId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    CREATE INDEX [IX_WithdrawalRequests_ReviewedByAdminUserId] ON [WithdrawalRequests] ([ReviewedByAdminUserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602022951_Phase3DatabaseCoreModels'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260602022951_Phase3DatabaseCoreModels', N'8.0.11');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602025256_Phase3US2IntegrityConstraints'
)
BEGIN
    EXEC(N'ALTER TABLE [WithdrawalRequests] ADD CONSTRAINT [CK_WithdrawalRequests_Amount_Positive] CHECK ([Amount] > 0)');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602025256_Phase3US2IntegrityConstraints'
)
BEGIN
    EXEC(N'ALTER TABLE [WalletTransactions] ADD CONSTRAINT [CK_WalletTransactions_Amount_Positive] CHECK ([Amount] > 0)');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602025256_Phase3US2IntegrityConstraints'
)
BEGIN
    EXEC(N'ALTER TABLE [Wallets] ADD CONSTRAINT [CK_Wallets_Balances_NonNegative] CHECK ([AvailableBalance] >= 0 AND [ReservedBalance] >= 0)');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602025256_Phase3US2IntegrityConstraints'
)
BEGIN
    EXEC(N'ALTER TABLE [WalletLedgerEntries] ADD CONSTRAINT [CK_WalletLedgerEntries_Amount_Positive] CHECK ([Amount] > 0)');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602025256_Phase3US2IntegrityConstraints'
)
BEGIN
    EXEC(N'ALTER TABLE [DoctorAdDeliveries] ADD CONSTRAINT [CK_DoctorAdDeliveries_Money_NonNegative] CHECK ([PricePerMessageSnapshot] > 0 AND [PlatformFeePercentSnapshot] > 0 AND [PlatformFeePercentSnapshot] <= 100 AND [PlatformFeeAmount] > 0 AND [DoctorEarnings] > 0 AND [ReservedAmount] > 0 AND [PlatformFeeAmount] + [DoctorEarnings] = [PricePerMessageSnapshot] AND [ReservedAmount] = [PricePerMessageSnapshot])');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260602025256_Phase3US2IntegrityConstraints'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260602025256_Phase3US2IntegrityConstraints', N'8.0.11');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260608032307_Phase4FileStorageSecurity'
)
BEGIN
    ALTER TABLE [StoredFiles] ADD [ConcurrencyStamp] nvarchar(64) NOT NULL DEFAULT N'';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260608032307_Phase4FileStorageSecurity'
)
BEGIN
    ALTER TABLE [StoredFiles] ADD [DeletedAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260608032307_Phase4FileStorageSecurity'
)
BEGIN
    ALTER TABLE [StoredFiles] ADD [RelatedCampaignId] nvarchar(450) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260608032307_Phase4FileStorageSecurity'
)
BEGIN
    ALTER TABLE [StoredFiles] ADD [ReplacedByFileId] nvarchar(450) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260608032307_Phase4FileStorageSecurity'
)
BEGIN
    ALTER TABLE [StoredFiles] ADD [SafetyScanCheckedAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260608032307_Phase4FileStorageSecurity'
)
BEGIN
    ALTER TABLE [StoredFiles] ADD [SafetyScanStatus] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260608032307_Phase4FileStorageSecurity'
)
BEGIN
    ALTER TABLE [StoredFiles] ADD [StorageDeliveryType] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260608032307_Phase4FileStorageSecurity'
)
BEGIN
    ALTER TABLE [StoredFiles] ADD [StorageProvider] nvarchar(80) NOT NULL DEFAULT N'';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260608032307_Phase4FileStorageSecurity'
)
BEGIN
    ALTER TABLE [StoredFiles] ADD [StorageResourceType] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260608032307_Phase4FileStorageSecurity'
)
BEGIN
    ALTER TABLE [StoredFiles] ADD [UploadStatus] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260608032307_Phase4FileStorageSecurity'
)
BEGIN
    CREATE TABLE [FileAccessGrantAudits] (
        [Id] nvarchar(450) NOT NULL,
        [StoredFileId] nvarchar(450) NOT NULL,
        [RequestedByUserId] nvarchar(450) NOT NULL,
        [RequesterRole] nvarchar(80) NOT NULL,
        [Outcome] int NOT NULL,
        [Reason] nvarchar(1000) NULL,
        [ExpiresAtUtc] datetime2 NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_FileAccessGrantAudits] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_FileAccessGrantAudits_StoredFiles_StoredFileId] FOREIGN KEY ([StoredFileId]) REFERENCES [StoredFiles] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_FileAccessGrantAudits_Users_RequestedByUserId] FOREIGN KEY ([RequestedByUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260608032307_Phase4FileStorageSecurity'
)
BEGIN
    CREATE TABLE [FileReviews] (
        [Id] nvarchar(450) NOT NULL,
        [StoredFileId] nvarchar(450) NOT NULL,
        [AdminUserId] nvarchar(450) NOT NULL,
        [Decision] int NOT NULL,
        [Reason] nvarchar(1000) NULL,
        [Notes] nvarchar(2000) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CorrectsReviewId] nvarchar(450) NULL,
        CONSTRAINT [PK_FileReviews] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_FileReviews_FileReviews_CorrectsReviewId] FOREIGN KEY ([CorrectsReviewId]) REFERENCES [FileReviews] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_FileReviews_StoredFiles_StoredFileId] FOREIGN KEY ([StoredFileId]) REFERENCES [StoredFiles] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_FileReviews_Users_AdminUserId] FOREIGN KEY ([AdminUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260608032307_Phase4FileStorageSecurity'
)
BEGIN
    CREATE INDEX [IX_StoredFiles_OwnerType_OwnerId_Purpose_CreatedAtUtc] ON [StoredFiles] ([OwnerType], [OwnerId], [Purpose], [CreatedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260608032307_Phase4FileStorageSecurity'
)
BEGIN
    CREATE INDEX [IX_StoredFiles_RelatedCampaignId_Purpose] ON [StoredFiles] ([RelatedCampaignId], [Purpose]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260608032307_Phase4FileStorageSecurity'
)
BEGIN
    CREATE INDEX [IX_StoredFiles_ReplacedByFileId] ON [StoredFiles] ([ReplacedByFileId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260608032307_Phase4FileStorageSecurity'
)
BEGIN
    CREATE INDEX [IX_StoredFiles_UploadStatus_CreatedAtUtc] ON [StoredFiles] ([UploadStatus], [CreatedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260608032307_Phase4FileStorageSecurity'
)
BEGIN
    CREATE INDEX [IX_FileAccessGrantAudits_RequestedByUserId_CreatedAtUtc] ON [FileAccessGrantAudits] ([RequestedByUserId], [CreatedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260608032307_Phase4FileStorageSecurity'
)
BEGIN
    CREATE INDEX [IX_FileAccessGrantAudits_StoredFileId_CreatedAtUtc] ON [FileAccessGrantAudits] ([StoredFileId], [CreatedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260608032307_Phase4FileStorageSecurity'
)
BEGIN
    CREATE INDEX [IX_FileReviews_AdminUserId] ON [FileReviews] ([AdminUserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260608032307_Phase4FileStorageSecurity'
)
BEGIN
    CREATE INDEX [IX_FileReviews_CorrectsReviewId] ON [FileReviews] ([CorrectsReviewId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260608032307_Phase4FileStorageSecurity'
)
BEGIN
    CREATE INDEX [IX_FileReviews_StoredFileId_CreatedAtUtc] ON [FileReviews] ([StoredFileId], [CreatedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260608032307_Phase4FileStorageSecurity'
)
BEGIN
    ALTER TABLE [StoredFiles] ADD CONSTRAINT [FK_StoredFiles_StoredFiles_ReplacedByFileId] FOREIGN KEY ([ReplacedByFileId]) REFERENCES [StoredFiles] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260608032307_Phase4FileStorageSecurity'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260608032307_Phase4FileStorageSecurity', N'8.0.11');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260609130925_EmailOtpContactVerification'
)
BEGIN
    ALTER TABLE [ContactVerificationFlows] ADD [LastSentAtUtc] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME());
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260609130925_EmailOtpContactVerification'
)
BEGIN
    UPDATE [ContactVerificationFlows]
    SET [LastSentAtUtc] = [CreatedAtUtc]
    WHERE [LastSentAtUtc] <> [CreatedAtUtc]
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260609130925_EmailOtpContactVerification'
)
BEGIN
    ALTER TABLE [ContactVerificationFlows] ADD [MaxAttemptsReachedAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260609130925_EmailOtpContactVerification'
)
BEGIN
    ALTER TABLE [ContactVerificationFlows] ADD [SupersededAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260609130925_EmailOtpContactVerification'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260609130925_EmailOtpContactVerification', N'8.0.11');
END;
GO

COMMIT;
GO

