using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediBridge.Repository.Migrations
{
    /// <inheritdoc />
    public partial class ReconcileOtpAndFileStorageLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
IF COL_LENGTH('dbo.ContactVerificationFlows', 'FailedAttemptCount') IS NULL
    ALTER TABLE [dbo].[ContactVerificationFlows]
    ADD [FailedAttemptCount] int NOT NULL CONSTRAINT [DF_ContactVerificationFlows_FailedAttemptCount] DEFAULT 0;
""");

            migrationBuilder.Sql("""
IF COL_LENGTH('dbo.ContactVerificationFlows', 'HashVersion') IS NULL
    ALTER TABLE [dbo].[ContactVerificationFlows]
    ADD [HashVersion] nvarchar(40) NOT NULL CONSTRAINT [DF_ContactVerificationFlows_HashVersion] DEFAULT N'hmac-sha256-v1';
""");

            migrationBuilder.Sql("""
IF COL_LENGTH('dbo.ContactVerificationFlows', 'LastSentAtUtc') IS NULL
    ALTER TABLE [dbo].[ContactVerificationFlows]
    ADD [LastSentAtUtc] datetime2 NULL;
""");

            migrationBuilder.Sql("""
IF COL_LENGTH('dbo.ContactVerificationFlows', 'MaxAttemptCount') IS NULL
    ALTER TABLE [dbo].[ContactVerificationFlows]
    ADD [MaxAttemptCount] int NOT NULL CONSTRAINT [DF_ContactVerificationFlows_MaxAttemptCount] DEFAULT 5;
""");

            migrationBuilder.Sql("""
IF COL_LENGTH('dbo.ContactVerificationFlows', 'ResendCount') IS NULL
    ALTER TABLE [dbo].[ContactVerificationFlows]
    ADD [ResendCount] int NOT NULL CONSTRAINT [DF_ContactVerificationFlows_ResendCount] DEFAULT 0;
""");

            migrationBuilder.Sql("""
IF COL_LENGTH('dbo.ContactVerificationFlows', 'ResendWindowStartedAtUtc') IS NULL
    ALTER TABLE [dbo].[ContactVerificationFlows]
    ADD [ResendWindowStartedAtUtc] datetime2 NULL;
""");

            migrationBuilder.Sql("""
IF COL_LENGTH('dbo.ContactVerificationFlows', 'SupersededAtUtc') IS NULL
    ALTER TABLE [dbo].[ContactVerificationFlows]
    ADD [SupersededAtUtc] datetime2 NULL;
""");

            migrationBuilder.Sql("""
IF COL_LENGTH('dbo.ContactVerificationFlows', 'MaxAttemptsReachedAtUtc') IS NULL
    ALTER TABLE [dbo].[ContactVerificationFlows]
    ADD [MaxAttemptsReachedAtUtc] datetime2 NULL;
""");

            migrationBuilder.AlterColumn<string>(
                name: "SupersededByFileId",
                table: "StoredFiles",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "StorageResourceType",
                table: "StoredFiles",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.CreateIndex(
                name: "IX_StoredFiles_OwnerType_OwnerId_Purpose_StorageState",
                table: "StoredFiles",
                columns: new[] { "OwnerType", "OwnerId", "Purpose", "StorageState" });

            migrationBuilder.CreateIndex(
                name: "IX_StoredFiles_SupersededByFileId",
                table: "StoredFiles",
                column: "SupersededByFileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StoredFiles_OwnerType_OwnerId_Purpose_StorageState",
                table: "StoredFiles");

            migrationBuilder.DropIndex(
                name: "IX_StoredFiles_SupersededByFileId",
                table: "StoredFiles");

            migrationBuilder.AlterColumn<string>(
                name: "SupersededByFileId",
                table: "StoredFiles",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldMaxLength: 450,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "StorageResourceType",
                table: "StoredFiles",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(40)",
                oldMaxLength: 40);
        }
    }
}
