using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediBridge.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddPhase7DeliveryExpiryJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DoctorMessageQueues_DoctorId_Status_CampaignSubmittedAtUtc_QueuedAtUtc_Id",
                table: "DoctorMessageQueues");

            migrationBuilder.AddColumn<byte[]>(
                name: "ConcurrencyToken",
                table: "DoctorMessageQueues",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiredAtUtc",
                table: "DoctorAdDeliveries",
                type: "datetime2",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE queueRows
                SET [CampaignSubmittedAtUtc] = campaigns.[SubmittedAtUtc]
                FROM [DoctorMessageQueues] AS queueRows
                INNER JOIN [Campaigns] AS campaigns ON campaigns.[Id] = queueRows.[CampaignId]
                WHERE queueRows.[Status] = 1
                    AND queueRows.[CampaignSubmittedAtUtc] IS NULL
                    AND campaigns.[SubmittedAtUtc] IS NOT NULL;

                IF EXISTS (
                    SELECT 1
                    FROM [DoctorMessageQueues]
                    WHERE [Status] = 1 AND [CampaignSubmittedAtUtc] IS NULL
                )
                BEGIN
                    THROW 51007, 'Phase 7 migration cannot resolve an authentic campaign submission timestamp for every Queued row.', 1;
                END
                """);

            migrationBuilder.CreateTable(
                name: "DeliveryJobRuns",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    JobType = table.Column<int>(type: "int", nullable: false),
                    BusinessDateEgypt = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExaminedCount = table.Column<int>(type: "int", nullable: false),
                    ActivatedCount = table.Column<int>(type: "int", nullable: false),
                    ExpiredCount = table.Column<int>(type: "int", nullable: false),
                    CancelledCount = table.Column<int>(type: "int", nullable: false),
                    SkippedCount = table.Column<int>(type: "int", nullable: false),
                    FailedCount = table.Column<int>(type: "int", nullable: false),
                    SafeFailureSummary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConcurrencyToken = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryJobRuns", x => x.Id);
                    table.CheckConstraint("CK_DeliveryJobRuns_Counters_NonNegative", "[ExaminedCount] >= 0 AND [ActivatedCount] >= 0 AND [ExpiredCount] >= 0 AND [CancelledCount] >= 0 AND [SkippedCount] >= 0 AND [FailedCount] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "DeliveryRecoveryDispatches",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    BusinessDateEgypt = table.Column<DateOnly>(type: "date", nullable: false),
                    JobType = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SchedulerJobId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DependsOnDispatchId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ClaimedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EnqueuedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SafeFailureSummary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ConcurrencyToken = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryRecoveryDispatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeliveryRecoveryDispatches_DeliveryRecoveryDispatches_DependsOnDispatchId",
                        column: x => x.DependsOnDispatchId,
                        principalTable: "DeliveryRecoveryDispatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DoctorMessageQueues_DoctorId",
                table: "DoctorMessageQueues",
                column: "DoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_DoctorMessageQueues_Status_DoctorId_CampaignSubmittedAtUtc_Id",
                table: "DoctorMessageQueues",
                columns: new[] { "Status", "DoctorId", "CampaignSubmittedAtUtc", "Id" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_DoctorMessageQueues_QueuedCampaignSubmittedAtUtc",
                table: "DoctorMessageQueues",
                sql: "[Status] <> 1 OR [CampaignSubmittedAtUtc] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DoctorAdDeliveries_DoctorId_DeliveryDateEgypt_DeliveredAtUtc_Id",
                table: "DoctorAdDeliveries",
                columns: new[] { "DoctorId", "DeliveryDateEgypt", "DeliveredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_DoctorAdDeliveries_Status_ReservationStatus_DeliveryDateEgypt_CompanyId_Id",
                table: "DoctorAdDeliveries",
                columns: new[] { "Status", "ReservationStatus", "DeliveryDateEgypt", "CompanyId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryJobRuns_JobType_BusinessDateEgypt_StartedAtUtc",
                table: "DeliveryJobRuns",
                columns: new[] { "JobType", "BusinessDateEgypt", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryJobRuns_Status_StartedAtUtc",
                table: "DeliveryJobRuns",
                columns: new[] { "Status", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryRecoveryDispatches_BusinessDateEgypt_JobType",
                table: "DeliveryRecoveryDispatches",
                columns: new[] { "BusinessDateEgypt", "JobType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryRecoveryDispatches_DependsOnDispatchId",
                table: "DeliveryRecoveryDispatches",
                column: "DependsOnDispatchId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryRecoveryDispatches_Status_ClaimedAtUtc",
                table: "DeliveryRecoveryDispatches",
                columns: new[] { "Status", "ClaimedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeliveryJobRuns");

            migrationBuilder.DropTable(
                name: "DeliveryRecoveryDispatches");

            migrationBuilder.DropIndex(
                name: "IX_DoctorMessageQueues_DoctorId",
                table: "DoctorMessageQueues");

            migrationBuilder.DropIndex(
                name: "IX_DoctorMessageQueues_Status_DoctorId_CampaignSubmittedAtUtc_Id",
                table: "DoctorMessageQueues");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DoctorMessageQueues_QueuedCampaignSubmittedAtUtc",
                table: "DoctorMessageQueues");

            migrationBuilder.DropIndex(
                name: "IX_DoctorAdDeliveries_DoctorId_DeliveryDateEgypt_DeliveredAtUtc_Id",
                table: "DoctorAdDeliveries");

            migrationBuilder.DropIndex(
                name: "IX_DoctorAdDeliveries_Status_ReservationStatus_DeliveryDateEgypt_CompanyId_Id",
                table: "DoctorAdDeliveries");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "DoctorMessageQueues");

            migrationBuilder.DropColumn(
                name: "ExpiredAtUtc",
                table: "DoctorAdDeliveries");

            migrationBuilder.CreateIndex(
                name: "IX_DoctorMessageQueues_DoctorId_Status_CampaignSubmittedAtUtc_QueuedAtUtc_Id",
                table: "DoctorMessageQueues",
                columns: new[] { "DoctorId", "Status", "CampaignSubmittedAtUtc", "QueuedAtUtc", "Id" });
        }
    }
}
