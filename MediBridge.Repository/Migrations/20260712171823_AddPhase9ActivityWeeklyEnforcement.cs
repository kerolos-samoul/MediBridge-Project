using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediBridge.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddPhase9ActivityWeeklyEnforcement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastStatusChangedAtUtc",
                table: "DoctorProfiles",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SuspendedAtUtc",
                table: "DoctorProfiles",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SuspendedUntilUtc",
                table: "DoctorProfiles",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ActivityEnforcementJobRuns",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    JobType = table.Column<int>(type: "int", nullable: false),
                    TargetScoreDateEgypt = table.Column<DateOnly>(type: "date", nullable: true),
                    TargetWeekStartDateEgypt = table.Column<DateOnly>(type: "date", nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ProcessedCount = table.Column<int>(type: "int", nullable: false),
                    SkippedCount = table.Column<int>(type: "int", nullable: false),
                    CreatedCount = table.Column<int>(type: "int", nullable: false),
                    UpdatedCount = table.Column<int>(type: "int", nullable: false),
                    FailedCount = table.Column<int>(type: "int", nullable: false),
                    SafeFailureSummary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RequestedByAdminUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityEnforcementJobRuns", x => x.Id);
                    table.CheckConstraint("CK_ActivityEnforcementJobRuns_Counters_NonNegative", "[ProcessedCount] >= 0 AND [SkippedCount] >= 0 AND [CreatedCount] >= 0 AND [UpdatedCount] >= 0 AND [FailedCount] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "DoctorEnforcementActions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DoctorId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ActorAdminUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ActionType = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PreviousStatus = table.Column<int>(type: "int", nullable: false),
                    NewStatus = table.Column<int>(type: "int", nullable: false),
                    PreviousDailyMessageLimit = table.Column<int>(type: "int", nullable: false),
                    NewDailyMessageLimit = table.Column<int>(type: "int", nullable: true),
                    SuspendedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SuspendedUntilUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EffectiveAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    AuditEventId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DoctorEnforcementActions", x => x.Id);
                    table.CheckConstraint("CK_DoctorEnforcementActions_DailyLimits_NonNegative", "[PreviousDailyMessageLimit] >= 0 AND ([NewDailyMessageLimit] IS NULL OR [NewDailyMessageLimit] >= 0)");
                    table.ForeignKey(
                        name: "FK_DoctorEnforcementActions_AuditEvents_AuditEventId",
                        column: x => x.AuditEventId,
                        principalTable: "AuditEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DoctorEnforcementActions_DoctorProfiles_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "DoctorProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DoctorActivityScoreHistories",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DoctorId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ScoreDateEgypt = table.Column<DateOnly>(type: "date", nullable: false),
                    WindowStartDateEgypt = table.Column<DateOnly>(type: "date", nullable: false),
                    WindowEndDateEgypt = table.Column<DateOnly>(type: "date", nullable: false),
                    DeliveredCount = table.Column<int>(type: "int", nullable: false),
                    InteractedCount = table.Column<int>(type: "int", nullable: false),
                    FeedbackQualifiedCount = table.Column<int>(type: "int", nullable: false),
                    ResponseSpeedScore = table.Column<decimal>(type: "decimal(5,1)", precision: 5, scale: 1, nullable: false),
                    EngagementScore = table.Column<decimal>(type: "decimal(5,1)", precision: 5, scale: 1, nullable: false),
                    FeedbackScore = table.Column<decimal>(type: "decimal(5,1)", precision: 5, scale: 1, nullable: false),
                    FinalScore = table.Column<decimal>(type: "decimal(5,1)", precision: 5, scale: 1, nullable: false),
                    CalculationMode = table.Column<int>(type: "int", nullable: false),
                    DoctorWasSuspended = table.Column<bool>(type: "bit", nullable: false),
                    CalculatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    JobRunId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DoctorActivityScoreHistories", x => x.Id);
                    table.CheckConstraint("CK_DoctorActivityScoreHistories_Counts_NonNegative", "[DeliveredCount] >= 0 AND [InteractedCount] >= 0 AND [FeedbackQualifiedCount] >= 0");
                    table.CheckConstraint("CK_DoctorActivityScoreHistories_Scores_Range", "[ResponseSpeedScore] >= 0 AND [ResponseSpeedScore] <= 100 AND [EngagementScore] >= 0 AND [EngagementScore] <= 100 AND [FeedbackScore] >= 0 AND [FeedbackScore] <= 100 AND [FinalScore] >= 0 AND [FinalScore] <= 100");
                    table.ForeignKey(
                        name: "FK_DoctorActivityScoreHistories_ActivityEnforcementJobRuns_JobRunId",
                        column: x => x.JobRunId,
                        principalTable: "ActivityEnforcementJobRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DoctorActivityScoreHistories_DoctorProfiles_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "DoctorProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WeeklyEnforcementDecisions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DoctorId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    WeekStartDateEgypt = table.Column<DateOnly>(type: "date", nullable: false),
                    WeekEndDateEgypt = table.Column<DateOnly>(type: "date", nullable: false),
                    MinimumWeeklyRequirement = table.Column<int>(type: "int", nullable: false),
                    InteractionCount = table.Column<int>(type: "int", nullable: false),
                    Decision = table.Column<int>(type: "int", nullable: false),
                    SuspensionOverlapped = table.Column<bool>(type: "bit", nullable: false),
                    RollingViolationCountAfterDecision = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    JobRunId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeeklyEnforcementDecisions", x => x.Id);
                    table.CheckConstraint("CK_WeeklyEnforcementDecisions_Counts_NonNegative", "[MinimumWeeklyRequirement] >= 0 AND [InteractionCount] >= 0 AND [RollingViolationCountAfterDecision] >= 0");
                    table.ForeignKey(
                        name: "FK_WeeklyEnforcementDecisions_ActivityEnforcementJobRuns_JobRunId",
                        column: x => x.JobRunId,
                        principalTable: "ActivityEnforcementJobRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WeeklyEnforcementDecisions_DoctorProfiles_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "DoctorProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DoctorWeeklyViolations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DoctorId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    WeeklyEnforcementDecisionId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WeekStartDateEgypt = table.Column<DateOnly>(type: "date", nullable: false),
                    WeekEndDateEgypt = table.Column<DateOnly>(type: "date", nullable: false),
                    MinimumWeeklyRequirement = table.Column<int>(type: "int", nullable: false),
                    InteractionCount = table.Column<int>(type: "int", nullable: false),
                    RollingViolationCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AuditEventId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DoctorWeeklyViolations", x => x.Id);
                    table.CheckConstraint("CK_DoctorWeeklyViolations_Counts", "[MinimumWeeklyRequirement] >= 0 AND [InteractionCount] >= 0 AND [RollingViolationCount] >= 0 AND [InteractionCount] < [MinimumWeeklyRequirement]");
                    table.ForeignKey(
                        name: "FK_DoctorWeeklyViolations_AuditEvents_AuditEventId",
                        column: x => x.AuditEventId,
                        principalTable: "AuditEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DoctorWeeklyViolations_DoctorProfiles_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "DoctorProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DoctorWeeklyViolations_WeeklyEnforcementDecisions_WeeklyEnforcementDecisionId",
                        column: x => x.WeeklyEnforcementDecisionId,
                        principalTable: "WeeklyEnforcementDecisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DoctorProfiles_Status_SuspendedUntilUtc",
                table: "DoctorProfiles",
                columns: new[] { "Status", "SuspendedUntilUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEnforcementJobRuns_JobType_StartedAtUtc",
                table: "ActivityEnforcementJobRuns",
                columns: new[] { "JobType", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEnforcementJobRuns_JobType_TargetScoreDateEgypt",
                table: "ActivityEnforcementJobRuns",
                columns: new[] { "JobType", "TargetScoreDateEgypt" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEnforcementJobRuns_JobType_TargetWeekStartDateEgypt",
                table: "ActivityEnforcementJobRuns",
                columns: new[] { "JobType", "TargetWeekStartDateEgypt" });

            migrationBuilder.CreateIndex(
                name: "IX_DoctorActivityScoreHistories_DoctorId_ScoreDateEgypt",
                table: "DoctorActivityScoreHistories",
                columns: new[] { "DoctorId", "ScoreDateEgypt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DoctorActivityScoreHistories_JobRunId",
                table: "DoctorActivityScoreHistories",
                column: "JobRunId");

            migrationBuilder.CreateIndex(
                name: "IX_DoctorActivityScoreHistories_ScoreDateEgypt_DoctorId",
                table: "DoctorActivityScoreHistories",
                columns: new[] { "ScoreDateEgypt", "DoctorId" });

            migrationBuilder.CreateIndex(
                name: "IX_DoctorEnforcementActions_ActorAdminUserId_CreatedAtUtc",
                table: "DoctorEnforcementActions",
                columns: new[] { "ActorAdminUserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DoctorEnforcementActions_AuditEventId",
                table: "DoctorEnforcementActions",
                column: "AuditEventId");

            migrationBuilder.CreateIndex(
                name: "IX_DoctorEnforcementActions_DoctorId_CreatedAtUtc",
                table: "DoctorEnforcementActions",
                columns: new[] { "DoctorId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DoctorWeeklyViolations_AuditEventId",
                table: "DoctorWeeklyViolations",
                column: "AuditEventId");

            migrationBuilder.CreateIndex(
                name: "IX_DoctorWeeklyViolations_DoctorId_WeekStartDateEgypt",
                table: "DoctorWeeklyViolations",
                columns: new[] { "DoctorId", "WeekStartDateEgypt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DoctorWeeklyViolations_WeeklyEnforcementDecisionId",
                table: "DoctorWeeklyViolations",
                column: "WeeklyEnforcementDecisionId");

            migrationBuilder.CreateIndex(
                name: "IX_DoctorWeeklyViolations_WeekStartDateEgypt_DoctorId",
                table: "DoctorWeeklyViolations",
                columns: new[] { "WeekStartDateEgypt", "DoctorId" });

            migrationBuilder.CreateIndex(
                name: "IX_WeeklyEnforcementDecisions_DoctorId_WeekStartDateEgypt",
                table: "WeeklyEnforcementDecisions",
                columns: new[] { "DoctorId", "WeekStartDateEgypt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WeeklyEnforcementDecisions_JobRunId",
                table: "WeeklyEnforcementDecisions",
                column: "JobRunId");

            migrationBuilder.CreateIndex(
                name: "IX_WeeklyEnforcementDecisions_WeekStartDateEgypt_Decision_DoctorId",
                table: "WeeklyEnforcementDecisions",
                columns: new[] { "WeekStartDateEgypt", "Decision", "DoctorId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DoctorActivityScoreHistories");

            migrationBuilder.DropTable(
                name: "DoctorEnforcementActions");

            migrationBuilder.DropTable(
                name: "DoctorWeeklyViolations");

            migrationBuilder.DropTable(
                name: "WeeklyEnforcementDecisions");

            migrationBuilder.DropTable(
                name: "ActivityEnforcementJobRuns");

            migrationBuilder.DropIndex(
                name: "IX_DoctorProfiles_Status_SuspendedUntilUtc",
                table: "DoctorProfiles");

            migrationBuilder.DropColumn(
                name: "LastStatusChangedAtUtc",
                table: "DoctorProfiles");

            migrationBuilder.DropColumn(
                name: "SuspendedAtUtc",
                table: "DoctorProfiles");

            migrationBuilder.DropColumn(
                name: "SuspendedUntilUtc",
                table: "DoctorProfiles");
        }
    }
}
