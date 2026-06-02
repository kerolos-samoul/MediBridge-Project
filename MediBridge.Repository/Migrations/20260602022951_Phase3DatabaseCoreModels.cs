using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediBridge.Repository.Migrations
{
    /// <inheritdoc />
    public partial class Phase3DatabaseCoreModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CompanyProfiles_LicenseNumber",
                table: "CompanyProfiles");

            migrationBuilder.AddColumn<decimal>(
                name: "ActivityScore",
                table: "DoctorProfiles",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "DailyMessageLimit",
                table: "DoctorProfiles",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAtUtc",
                table: "DoctorProfiles",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "DoctorProfiles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MinimumWeeklyRequirement",
                table: "DoctorProfiles",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "PricePerMessage",
                table: "DoctorProfiles",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RequestedDailyMessageLimit",
                table: "DoctorProfiles",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RequestedMinimumWeeklyRequirement",
                table: "DoctorProfiles",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "DoctorProfiles",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAtUtc",
                table: "CompanyProfiles",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "CompanyProfiles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ActivityScoreHistories",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    DoctorId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ActivityScore = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    ResponseSpeedScore = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    EngagementScore = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    FeedbackScore = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    WindowStartDateEgypt = table.Column<DateOnly>(type: "date", nullable: false),
                    WindowEndDateEgypt = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CorrectsHistoryId = table.Column<string>(type: "nvarchar(450)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityScoreHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActivityScoreHistories_ActivityScoreHistories_CorrectsHistoryId",
                        column: x => x.CorrectsHistoryId,
                        principalTable: "ActivityScoreHistories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActivityScoreHistories_DoctorProfiles_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "DoctorProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    ActorUserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    ActorRole = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    TargetType = table.Column<int>(type: "int", nullable: true),
                    TargetId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    Metadata = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CorrectsAuditEventId = table.Column<string>(type: "nvarchar(450)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditEvents_AuditEvents_CorrectsAuditEventId",
                        column: x => x.CorrectsAuditEventId,
                        principalTable: "AuditEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AuditEvents_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DoctorPriceHistories",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    DoctorId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    PreviousPricePerMessage = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    NewPricePerMessage = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    ChangedByAdminUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CorrectsHistoryId = table.Column<string>(type: "nvarchar(450)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DoctorPriceHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DoctorPriceHistories_DoctorPriceHistories_CorrectsHistoryId",
                        column: x => x.CorrectsHistoryId,
                        principalTable: "DoctorPriceHistories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DoctorPriceHistories_DoctorProfiles_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "DoctorProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DoctorPriceHistories_Users_ChangedByAdminUserId",
                        column: x => x.ChangedByAdminUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlatformFeePolicyHistories",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    FeePercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    EffectiveFromUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EffectiveToUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ChangedByAdminUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CorrectsHistoryId = table.Column<string>(type: "nvarchar(450)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformFeePolicyHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlatformFeePolicyHistories_PlatformFeePolicyHistories_CorrectsHistoryId",
                        column: x => x.CorrectsHistoryId,
                        principalTable: "PlatformFeePolicyHistories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlatformFeePolicyHistories_Users_ChangedByAdminUserId",
                        column: x => x.ChangedByAdminUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StoredFiles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    OwnerType = table.Column<int>(type: "int", nullable: false),
                    OwnerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Purpose = table.Column<int>(type: "int", nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Visibility = table.Column<int>(type: "int", nullable: false),
                    ReviewStatus = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewedByAdminId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    ReviewReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoredFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoredFiles_Users_ReviewedByAdminId",
                        column: x => x.ReviewedByAdminId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Wallets",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    OwnerType = table.Column<int>(type: "int", nullable: false),
                    OwnerUserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    OwnerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    AvailableBalance = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ReservedBalance = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConcurrencyToken = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Wallets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Wallets_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WithdrawalRequests",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    DoctorId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReviewedByAdminUserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecisionReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PayoutReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ConcurrencyToken = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WithdrawalRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WithdrawalRequests_DoctorProfiles_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "DoctorProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WithdrawalRequests_Users_ReviewedByAdminUserId",
                        column: x => x.ReviewedByAdminUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Campaigns",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CompanyId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    MediaFileId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    VoiceNoteFileId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    ClinicalResearchInfo = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Campaigns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Campaigns_CompanyProfiles_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "CompanyProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Campaigns_StoredFiles_MediaFileId",
                        column: x => x.MediaFileId,
                        principalTable: "StoredFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Campaigns_StoredFiles_VoiceNoteFileId",
                        column: x => x.VoiceNoteFileId,
                        principalTable: "StoredFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CampaignReviewHistories",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CampaignId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    AdminUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Decision = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CorrectsHistoryId = table.Column<string>(type: "nvarchar(450)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignReviewHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CampaignReviewHistories_CampaignReviewHistories_CorrectsHistoryId",
                        column: x => x.CorrectsHistoryId,
                        principalTable: "CampaignReviewHistories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CampaignReviewHistories_Campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "Campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CampaignReviewHistories_Users_AdminUserId",
                        column: x => x.AdminUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CampaignTargets",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CampaignId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    DoctorId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    SpecializationSnapshot = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    ExperienceYearsSnapshot = table.Column<int>(type: "int", nullable: false),
                    LocationSnapshot = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ActivityScoreSnapshot = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    PricePerMessageSnapshot = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignTargets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CampaignTargets_Campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "Campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CampaignTargets_DoctorProfiles_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "DoctorProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DoctorAdDeliveries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    DoctorId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CampaignId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CompanyId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    DeliveryDateEgypt = table.Column<DateOnly>(type: "date", nullable: false),
                    DeliveredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReadAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    InteractedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FeedbackText = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    FeedbackCreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FeedbackQualityStatus = table.Column<int>(type: "int", nullable: true),
                    PricePerMessageSnapshot = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PlatformFeePercentSnapshot = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    PlatformFeeAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DoctorEarnings = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ReservedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ReservationStatus = table.Column<int>(type: "int", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DoctorAdDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DoctorAdDeliveries_Campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "Campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DoctorAdDeliveries_CompanyProfiles_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "CompanyProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DoctorAdDeliveries_DoctorProfiles_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "DoctorProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DoctorMessageQueues",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    DoctorId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CampaignId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    QueuedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CampaignSubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DoctorMessageQueues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DoctorMessageQueues_Campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "Campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DoctorMessageQueues_DoctorProfiles_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "DoctorProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WalletTransactions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    WalletId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    OperationType = table.Column<int>(type: "int", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    RelatedDeliveryId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Metadata = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CorrectsTransactionId = table.Column<string>(type: "nvarchar(450)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WalletTransactions_DoctorAdDeliveries_RelatedDeliveryId",
                        column: x => x.RelatedDeliveryId,
                        principalTable: "DoctorAdDeliveries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WalletTransactions_WalletTransactions_CorrectsTransactionId",
                        column: x => x.CorrectsTransactionId,
                        principalTable: "WalletTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WalletTransactions_Wallets_WalletId",
                        column: x => x.WalletId,
                        principalTable: "Wallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WalletLedgerEntries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    WalletTransactionId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    WalletId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Direction = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    BalanceType = table.Column<int>(type: "int", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    CampaignId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MessageDeliveryId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DoctorId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CompanyId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WithdrawalRequestId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletLedgerEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WalletLedgerEntries_WalletTransactions_WalletTransactionId",
                        column: x => x.WalletTransactionId,
                        principalTable: "WalletTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WalletLedgerEntries_Wallets_WalletId",
                        column: x => x.WalletId,
                        principalTable: "Wallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyProfiles_LicenseNumber",
                table: "CompanyProfiles",
                column: "LicenseNumber",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityScoreHistories_CorrectsHistoryId",
                table: "ActivityScoreHistories",
                column: "CorrectsHistoryId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityScoreHistories_DoctorId_CreatedAtUtc",
                table: "ActivityScoreHistories",
                columns: new[] { "DoctorId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_ActorUserId",
                table: "AuditEvents",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_CorrectsAuditEventId",
                table: "AuditEvents",
                column: "CorrectsAuditEventId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_TargetType_TargetId_CreatedAtUtc",
                table: "AuditEvents",
                columns: new[] { "TargetType", "TargetId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CampaignReviewHistories_AdminUserId",
                table: "CampaignReviewHistories",
                column: "AdminUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignReviewHistories_CampaignId_CreatedAtUtc",
                table: "CampaignReviewHistories",
                columns: new[] { "CampaignId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CampaignReviewHistories_CorrectsHistoryId",
                table: "CampaignReviewHistories",
                column: "CorrectsHistoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_CompanyId_CreatedAtUtc",
                table: "Campaigns",
                columns: new[] { "CompanyId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_MediaFileId",
                table: "Campaigns",
                column: "MediaFileId");

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_VoiceNoteFileId",
                table: "Campaigns",
                column: "VoiceNoteFileId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignTargets_CampaignId_DoctorId",
                table: "CampaignTargets",
                columns: new[] { "CampaignId", "DoctorId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CampaignTargets_DoctorId",
                table: "CampaignTargets",
                column: "DoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_DoctorAdDeliveries_CampaignId",
                table: "DoctorAdDeliveries",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_DoctorAdDeliveries_CompanyId",
                table: "DoctorAdDeliveries",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_DoctorAdDeliveries_DoctorId_DeliveryDateEgypt_CampaignId",
                table: "DoctorAdDeliveries",
                columns: new[] { "DoctorId", "DeliveryDateEgypt", "CampaignId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DoctorMessageQueues_CampaignId",
                table: "DoctorMessageQueues",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_DoctorMessageQueues_DoctorId_Status_QueuedAtUtc_Id",
                table: "DoctorMessageQueues",
                columns: new[] { "DoctorId", "Status", "QueuedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_DoctorPriceHistories_ChangedByAdminUserId",
                table: "DoctorPriceHistories",
                column: "ChangedByAdminUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DoctorPriceHistories_CorrectsHistoryId",
                table: "DoctorPriceHistories",
                column: "CorrectsHistoryId");

            migrationBuilder.CreateIndex(
                name: "IX_DoctorPriceHistories_DoctorId_CreatedAtUtc",
                table: "DoctorPriceHistories",
                columns: new[] { "DoctorId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformFeePolicyHistories_ChangedByAdminUserId",
                table: "PlatformFeePolicyHistories",
                column: "ChangedByAdminUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformFeePolicyHistories_CorrectsHistoryId",
                table: "PlatformFeePolicyHistories",
                column: "CorrectsHistoryId");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformFeePolicyHistories_EffectiveFromUtc_EffectiveToUtc",
                table: "PlatformFeePolicyHistories",
                columns: new[] { "EffectiveFromUtc", "EffectiveToUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StoredFiles_OwnerType_OwnerId_Purpose",
                table: "StoredFiles",
                columns: new[] { "OwnerType", "OwnerId", "Purpose" });

            migrationBuilder.CreateIndex(
                name: "IX_StoredFiles_ReviewedByAdminId",
                table: "StoredFiles",
                column: "ReviewedByAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_StoredFiles_ReviewStatus_CreatedAtUtc",
                table: "StoredFiles",
                columns: new[] { "ReviewStatus", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WalletLedgerEntries_WalletId_CreatedAtUtc",
                table: "WalletLedgerEntries",
                columns: new[] { "WalletId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WalletLedgerEntries_WalletTransactionId_CreatedAtUtc",
                table: "WalletLedgerEntries",
                columns: new[] { "WalletTransactionId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Wallets_OwnerType_OwnerId",
                table: "Wallets",
                columns: new[] { "OwnerType", "OwnerId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Wallets_OwnerUserId",
                table: "Wallets",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_CorrectsTransactionId",
                table: "WalletTransactions",
                column: "CorrectsTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_OperationType_IdempotencyKey",
                table: "WalletTransactions",
                columns: new[] { "OperationType", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_RelatedDeliveryId",
                table: "WalletTransactions",
                column: "RelatedDeliveryId");

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_WalletId_CreatedAtUtc",
                table: "WalletTransactions",
                columns: new[] { "WalletId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WithdrawalRequests_DoctorId",
                table: "WithdrawalRequests",
                column: "DoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_WithdrawalRequests_ReviewedByAdminUserId",
                table: "WithdrawalRequests",
                column: "ReviewedByAdminUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityScoreHistories");

            migrationBuilder.DropTable(
                name: "AuditEvents");

            migrationBuilder.DropTable(
                name: "CampaignReviewHistories");

            migrationBuilder.DropTable(
                name: "CampaignTargets");

            migrationBuilder.DropTable(
                name: "DoctorMessageQueues");

            migrationBuilder.DropTable(
                name: "DoctorPriceHistories");

            migrationBuilder.DropTable(
                name: "PlatformFeePolicyHistories");

            migrationBuilder.DropTable(
                name: "WalletLedgerEntries");

            migrationBuilder.DropTable(
                name: "WithdrawalRequests");

            migrationBuilder.DropTable(
                name: "WalletTransactions");

            migrationBuilder.DropTable(
                name: "DoctorAdDeliveries");

            migrationBuilder.DropTable(
                name: "Wallets");

            migrationBuilder.DropTable(
                name: "Campaigns");

            migrationBuilder.DropTable(
                name: "StoredFiles");

            migrationBuilder.DropIndex(
                name: "IX_CompanyProfiles_LicenseNumber",
                table: "CompanyProfiles");

            migrationBuilder.DropColumn(
                name: "ActivityScore",
                table: "DoctorProfiles");

            migrationBuilder.DropColumn(
                name: "DailyMessageLimit",
                table: "DoctorProfiles");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "DoctorProfiles");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "DoctorProfiles");

            migrationBuilder.DropColumn(
                name: "MinimumWeeklyRequirement",
                table: "DoctorProfiles");

            migrationBuilder.DropColumn(
                name: "PricePerMessage",
                table: "DoctorProfiles");

            migrationBuilder.DropColumn(
                name: "RequestedDailyMessageLimit",
                table: "DoctorProfiles");

            migrationBuilder.DropColumn(
                name: "RequestedMinimumWeeklyRequirement",
                table: "DoctorProfiles");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "DoctorProfiles");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "CompanyProfiles");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "CompanyProfiles");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyProfiles_LicenseNumber",
                table: "CompanyProfiles",
                column: "LicenseNumber",
                unique: true);
        }
    }
}
