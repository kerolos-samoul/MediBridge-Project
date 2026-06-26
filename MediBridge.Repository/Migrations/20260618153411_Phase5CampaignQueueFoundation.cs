using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediBridge.Repository.Migrations
{
    /// <inheritdoc />
    public partial class Phase5CampaignQueueFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DoctorMessageQueues_CampaignId",
                table: "DoctorMessageQueues");

            migrationBuilder.CreateTable(
                name: "CampaignSubmissionRequests",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CompanyId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CampaignId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    RequestHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignSubmissionRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CampaignSubmissionRequests_Campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "Campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CampaignSubmissionRequests_CompanyProfiles_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "CompanyProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DoctorMessageQueues_CampaignId_DoctorId",
                table: "DoctorMessageQueues",
                columns: new[] { "CampaignId", "DoctorId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CampaignSubmissionRequests_CampaignId",
                table: "CampaignSubmissionRequests",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignSubmissionRequests_CompanyId_IdempotencyKey",
                table: "CampaignSubmissionRequests",
                columns: new[] { "CompanyId", "IdempotencyKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CampaignSubmissionRequests");

            migrationBuilder.DropIndex(
                name: "IX_DoctorMessageQueues_CampaignId_DoctorId",
                table: "DoctorMessageQueues");

            migrationBuilder.CreateIndex(
                name: "IX_DoctorMessageQueues_CampaignId",
                table: "DoctorMessageQueues",
                column: "CampaignId");
        }
    }
}
