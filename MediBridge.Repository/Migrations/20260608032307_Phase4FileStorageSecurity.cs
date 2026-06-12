using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediBridge.Repository.Migrations
{
    /// <inheritdoc />
    public partial class Phase4FileStorageSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConcurrencyStamp",
                table: "StoredFiles",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAtUtc",
                table: "StoredFiles",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RelatedCampaignId",
                table: "StoredFiles",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReplacedByFileId",
                table: "StoredFiles",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SafetyScanCheckedAtUtc",
                table: "StoredFiles",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SafetyScanStatus",
                table: "StoredFiles",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "StorageDeliveryType",
                table: "StoredFiles",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "StorageProvider",
                table: "StoredFiles",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "StorageResourceType",
                table: "StoredFiles",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "UploadStatus",
                table: "StoredFiles",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "FileAccessGrantAudits",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    StoredFileId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    RequestedByUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    RequesterRole = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileAccessGrantAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FileAccessGrantAudits_StoredFiles_StoredFileId",
                        column: x => x.StoredFileId,
                        principalTable: "StoredFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FileAccessGrantAudits_Users_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FileReviews",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    StoredFileId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    AdminUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Decision = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CorrectsReviewId = table.Column<string>(type: "nvarchar(450)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FileReviews_FileReviews_CorrectsReviewId",
                        column: x => x.CorrectsReviewId,
                        principalTable: "FileReviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FileReviews_StoredFiles_StoredFileId",
                        column: x => x.StoredFileId,
                        principalTable: "StoredFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FileReviews_Users_AdminUserId",
                        column: x => x.AdminUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StoredFiles_OwnerType_OwnerId_Purpose_CreatedAtUtc",
                table: "StoredFiles",
                columns: new[] { "OwnerType", "OwnerId", "Purpose", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StoredFiles_RelatedCampaignId_Purpose",
                table: "StoredFiles",
                columns: new[] { "RelatedCampaignId", "Purpose" });

            migrationBuilder.CreateIndex(
                name: "IX_StoredFiles_ReplacedByFileId",
                table: "StoredFiles",
                column: "ReplacedByFileId");

            migrationBuilder.CreateIndex(
                name: "IX_StoredFiles_UploadStatus_CreatedAtUtc",
                table: "StoredFiles",
                columns: new[] { "UploadStatus", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_FileAccessGrantAudits_RequestedByUserId_CreatedAtUtc",
                table: "FileAccessGrantAudits",
                columns: new[] { "RequestedByUserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_FileAccessGrantAudits_StoredFileId_CreatedAtUtc",
                table: "FileAccessGrantAudits",
                columns: new[] { "StoredFileId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_FileReviews_AdminUserId",
                table: "FileReviews",
                column: "AdminUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FileReviews_CorrectsReviewId",
                table: "FileReviews",
                column: "CorrectsReviewId");

            migrationBuilder.CreateIndex(
                name: "IX_FileReviews_StoredFileId_CreatedAtUtc",
                table: "FileReviews",
                columns: new[] { "StoredFileId", "CreatedAtUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_StoredFiles_StoredFiles_ReplacedByFileId",
                table: "StoredFiles",
                column: "ReplacedByFileId",
                principalTable: "StoredFiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StoredFiles_StoredFiles_ReplacedByFileId",
                table: "StoredFiles");

            migrationBuilder.DropTable(
                name: "FileAccessGrantAudits");

            migrationBuilder.DropTable(
                name: "FileReviews");

            migrationBuilder.DropIndex(
                name: "IX_StoredFiles_OwnerType_OwnerId_Purpose_CreatedAtUtc",
                table: "StoredFiles");

            migrationBuilder.DropIndex(
                name: "IX_StoredFiles_RelatedCampaignId_Purpose",
                table: "StoredFiles");

            migrationBuilder.DropIndex(
                name: "IX_StoredFiles_ReplacedByFileId",
                table: "StoredFiles");

            migrationBuilder.DropIndex(
                name: "IX_StoredFiles_UploadStatus_CreatedAtUtc",
                table: "StoredFiles");

            migrationBuilder.DropColumn(
                name: "ConcurrencyStamp",
                table: "StoredFiles");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "StoredFiles");

            migrationBuilder.DropColumn(
                name: "RelatedCampaignId",
                table: "StoredFiles");

            migrationBuilder.DropColumn(
                name: "ReplacedByFileId",
                table: "StoredFiles");

            migrationBuilder.DropColumn(
                name: "SafetyScanCheckedAtUtc",
                table: "StoredFiles");

            migrationBuilder.DropColumn(
                name: "SafetyScanStatus",
                table: "StoredFiles");

            migrationBuilder.DropColumn(
                name: "StorageDeliveryType",
                table: "StoredFiles");

            migrationBuilder.DropColumn(
                name: "StorageProvider",
                table: "StoredFiles");

            migrationBuilder.DropColumn(
                name: "StorageResourceType",
                table: "StoredFiles");

            migrationBuilder.DropColumn(
                name: "UploadStatus",
                table: "StoredFiles");
        }
    }
}
