using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediBridge.Repository.Migrations
{
    /// <inheritdoc />
    public partial class HardenCampaignReviewWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DoctorMessageQueues_CampaignId",
                table: "DoctorMessageQueues");

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "CampaignReviewHistories",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE [CampaignReviewHistories]
                SET [IdempotencyKey] = SUBSTRING([Notes], 16, 128)
                WHERE [IdempotencyKey] IS NULL
                    AND [Notes] LIKE 'IdempotencyKey=%'
                    AND LEN(SUBSTRING([Notes], 16, 128)) BETWEEN 8 AND 128;

                IF EXISTS (
                    SELECT 1
                    FROM [CampaignReviewHistories]
                    WHERE [IdempotencyKey] IS NOT NULL
                    GROUP BY [CampaignId], [IdempotencyKey]
                    HAVING COUNT(*) > 1)
                BEGIN
                    THROW 51000, 'Duplicate campaign review idempotency keys must be resolved before applying this migration.', 1;
                END;

                IF EXISTS (
                    SELECT 1
                    FROM [DoctorMessageQueues]
                    WHERE [Status] IN (1, 2)
                    GROUP BY [CampaignId], [DoctorId]
                    HAVING COUNT(*) > 1)
                BEGIN
                    THROW 51001, 'Duplicate active campaign queue rows must be resolved before applying this migration.', 1;
                END;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_DoctorMessageQueues_CampaignId_DoctorId",
                table: "DoctorMessageQueues",
                columns: new[] { "CampaignId", "DoctorId" },
                unique: true,
                filter: "[Status] IN (1, 2)");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignReviewHistories_CampaignId_IdempotencyKey",
                table: "CampaignReviewHistories",
                columns: new[] { "CampaignId", "IdempotencyKey" },
                unique: true,
                filter: "[IdempotencyKey] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DoctorMessageQueues_CampaignId_DoctorId",
                table: "DoctorMessageQueues");

            migrationBuilder.DropIndex(
                name: "IX_CampaignReviewHistories_CampaignId_IdempotencyKey",
                table: "CampaignReviewHistories");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "CampaignReviewHistories");

            migrationBuilder.CreateIndex(
                name: "IX_DoctorMessageQueues_CampaignId",
                table: "DoctorMessageQueues",
                column: "CampaignId");
        }
    }
}
