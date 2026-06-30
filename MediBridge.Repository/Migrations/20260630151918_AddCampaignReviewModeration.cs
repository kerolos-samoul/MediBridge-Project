using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediBridge.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddCampaignReviewModeration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DoctorMessageQueues_DoctorId_Status_QueuedAtUtc_Id",
                table: "DoctorMessageQueues");

            migrationBuilder.AddColumn<DateTime>(
                name: "SubmittedAtUtc",
                table: "Campaigns",
                type: "datetime2",
                nullable: true);

            migrationBuilder.Sql(
                """
                IF COL_LENGTH('dbo.CampaignReviewHistories', 'IdempotencyKey') IS NULL
                BEGIN
                    ALTER TABLE [CampaignReviewHistories]
                    ADD [IdempotencyKey] nvarchar(128) NULL;
                END;
                """);

            migrationBuilder.AddColumn<int>(
                name: "PriorStatus",
                table: "CampaignReviewHistories",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ResultingStatus",
                table: "CampaignReviewHistories",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(
                """
                UPDATE [CampaignReviewHistories]
                SET [PriorStatus] = 2,
                    [ResultingStatus] = CASE [Decision]
                        WHEN 1 THEN 3
                        WHEN 2 THEN 4
                        WHEN 3 THEN 1
                        ELSE 2
                    END;

                DECLARE @CurrentRevisionRequests TABLE
                (
                    [CampaignId] nvarchar(450) NOT NULL,
                    [ReviewHistoryId] nvarchar(450) NOT NULL
                );

                ;WITH [RankedReviews] AS
                (
                    SELECT
                        [Id],
                        [CampaignId],
                        [Decision],
                        ROW_NUMBER() OVER
                        (
                            PARTITION BY [CampaignId]
                            ORDER BY [CreatedAtUtc] DESC, [Id] DESC
                        ) AS [ReviewRank]
                    FROM [CampaignReviewHistories]
                )
                INSERT INTO @CurrentRevisionRequests ([CampaignId], [ReviewHistoryId])
                SELECT [campaign].[Id], [review].[Id]
                FROM [Campaigns] AS [campaign]
                INNER JOIN [RankedReviews] AS [review]
                    ON [review].[CampaignId] = [campaign].[Id]
                    AND [review].[ReviewRank] = 1
                WHERE [campaign].[Status] = 1
                    AND [campaign].[IsDeleted] = CAST(0 AS bit)
                    AND [review].[Decision] = 3;

                UPDATE [campaign]
                SET [campaign].[Status] = 9
                FROM [Campaigns] AS [campaign]
                INNER JOIN @CurrentRevisionRequests AS [current]
                    ON [current].[CampaignId] = [campaign].[Id];

                UPDATE [history]
                SET [history].[ResultingStatus] = 9
                FROM [CampaignReviewHistories] AS [history]
                INNER JOIN @CurrentRevisionRequests AS [current]
                    ON [current].[ReviewHistoryId] = [history].[Id];
                """);

            migrationBuilder.CreateTable(
                name: "CampaignSubmissionAttempts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CampaignId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TargetCount = table.Column<int>(type: "int", nullable: false),
                    EstimatedCost = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignSubmissionAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CampaignSubmissionAttempts_Campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "Campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DoctorMessageQueues_DoctorId_Status_CampaignSubmittedAtUtc_QueuedAtUtc_Id",
                table: "DoctorMessageQueues",
                columns: new[] { "DoctorId", "Status", "CampaignSubmittedAtUtc", "QueuedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_Status_SubmittedAtUtc_Id",
                table: "Campaigns",
                columns: new[] { "Status", "SubmittedAtUtc", "Id" });

            migrationBuilder.Sql(
                """
                IF NOT EXISTS
                (
                    SELECT 1
                    FROM sys.indexes
                    WHERE [object_id] = OBJECT_ID(N'[dbo].[CampaignReviewHistories]')
                        AND [name] = N'IX_CampaignReviewHistories_CampaignId_IdempotencyKey'
                )
                BEGIN
                    CREATE UNIQUE INDEX [IX_CampaignReviewHistories_CampaignId_IdempotencyKey]
                    ON [CampaignReviewHistories] ([CampaignId], [IdempotencyKey])
                    WHERE [IdempotencyKey] IS NOT NULL;
                END;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_CampaignSubmissionAttempts_CampaignId_IdempotencyKey",
                table: "CampaignSubmissionAttempts",
                columns: new[] { "CampaignId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CampaignSubmissionAttempts_CampaignId_SubmittedAtUtc_Id",
                table: "CampaignSubmissionAttempts",
                columns: new[] { "CampaignId", "SubmittedAtUtc", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CampaignSubmissionAttempts");

            migrationBuilder.DropIndex(
                name: "IX_DoctorMessageQueues_DoctorId_Status_CampaignSubmittedAtUtc_QueuedAtUtc_Id",
                table: "DoctorMessageQueues");

            migrationBuilder.DropIndex(
                name: "IX_Campaigns_Status_SubmittedAtUtc_Id",
                table: "Campaigns");

            migrationBuilder.Sql(
                """
                IF NOT EXISTS
                (
                    SELECT 1
                    FROM [__EFMigrationsHistory]
                    WHERE [MigrationId] = N'20260621200235_HardenCampaignReviewWorkflow'
                )
                BEGIN
                    IF EXISTS
                    (
                        SELECT 1
                        FROM sys.indexes
                        WHERE [object_id] = OBJECT_ID(N'[dbo].[CampaignReviewHistories]')
                            AND [name] = N'IX_CampaignReviewHistories_CampaignId_IdempotencyKey'
                    )
                    BEGIN
                        DROP INDEX [IX_CampaignReviewHistories_CampaignId_IdempotencyKey]
                        ON [CampaignReviewHistories];
                    END;

                    IF COL_LENGTH('dbo.CampaignReviewHistories', 'IdempotencyKey') IS NOT NULL
                    BEGIN
                        ALTER TABLE [CampaignReviewHistories]
                        DROP COLUMN [IdempotencyKey];
                    END;
                END;
                """);

            migrationBuilder.DropColumn(
                name: "SubmittedAtUtc",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "PriorStatus",
                table: "CampaignReviewHistories");

            migrationBuilder.DropColumn(
                name: "ResultingStatus",
                table: "CampaignReviewHistories");

            migrationBuilder.CreateIndex(
                name: "IX_DoctorMessageQueues_DoctorId_Status_QueuedAtUtc_Id",
                table: "DoctorMessageQueues",
                columns: new[] { "DoctorId", "Status", "QueuedAtUtc", "Id" });
        }
    }
}
