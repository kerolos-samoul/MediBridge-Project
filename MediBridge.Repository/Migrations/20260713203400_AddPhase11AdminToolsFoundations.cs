using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediBridge.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddPhase11AdminToolsFoundations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WithdrawalRequests_DoctorId",
                table: "WithdrawalRequests");

            migrationBuilder.AddColumn<string>(
                name: "PayoutFailureReason",
                table: "WithdrawalRequests",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PayoutStatusChangedAtUtc",
                table: "WithdrawalRequests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayoutStatusChangedByAdminUserId",
                table: "WithdrawalRequests",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WithdrawalRequestId",
                table: "WalletTransactions",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PricingIsActive",
                table: "DoctorProfiles",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "PricingIsActive",
                table: "DoctorPriceHistories",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateIndex(
                name: "IX_WithdrawalRequests_DoctorId_RequestedAtUtc_Id",
                table: "WithdrawalRequests",
                columns: new[] { "DoctorId", "RequestedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_WithdrawalRequests_PayoutReference",
                table: "WithdrawalRequests",
                column: "PayoutReference");

            migrationBuilder.CreateIndex(
                name: "IX_WithdrawalRequests_PayoutStatusChangedByAdminUserId",
                table: "WithdrawalRequests",
                column: "PayoutStatusChangedByAdminUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WithdrawalRequests_ReviewedAtUtc_ReviewedByAdminUserId",
                table: "WithdrawalRequests",
                columns: new[] { "ReviewedAtUtc", "ReviewedByAdminUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_WithdrawalRequests_Status_RequestedAtUtc_Id",
                table: "WithdrawalRequests",
                columns: new[] { "Status", "RequestedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_WithdrawalRequestId_OperationType_CreatedAtUtc",
                table: "WalletTransactions",
                columns: new[] { "WithdrawalRequestId", "OperationType", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DoctorPriceHistories_DoctorId_PricingIsActive_CreatedAtUtc",
                table: "DoctorPriceHistories",
                columns: new[] { "DoctorId", "PricingIsActive", "CreatedAtUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_WalletTransactions_WithdrawalRequests_WithdrawalRequestId",
                table: "WalletTransactions",
                column: "WithdrawalRequestId",
                principalTable: "WithdrawalRequests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WithdrawalRequests_Users_PayoutStatusChangedByAdminUserId",
                table: "WithdrawalRequests",
                column: "PayoutStatusChangedByAdminUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WalletTransactions_WithdrawalRequests_WithdrawalRequestId",
                table: "WalletTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_WithdrawalRequests_Users_PayoutStatusChangedByAdminUserId",
                table: "WithdrawalRequests");

            migrationBuilder.DropIndex(
                name: "IX_WithdrawalRequests_DoctorId_RequestedAtUtc_Id",
                table: "WithdrawalRequests");

            migrationBuilder.DropIndex(
                name: "IX_WithdrawalRequests_PayoutReference",
                table: "WithdrawalRequests");

            migrationBuilder.DropIndex(
                name: "IX_WithdrawalRequests_PayoutStatusChangedByAdminUserId",
                table: "WithdrawalRequests");

            migrationBuilder.DropIndex(
                name: "IX_WithdrawalRequests_ReviewedAtUtc_ReviewedByAdminUserId",
                table: "WithdrawalRequests");

            migrationBuilder.DropIndex(
                name: "IX_WithdrawalRequests_Status_RequestedAtUtc_Id",
                table: "WithdrawalRequests");

            migrationBuilder.DropIndex(
                name: "IX_WalletTransactions_WithdrawalRequestId_OperationType_CreatedAtUtc",
                table: "WalletTransactions");

            migrationBuilder.DropIndex(
                name: "IX_DoctorPriceHistories_DoctorId_PricingIsActive_CreatedAtUtc",
                table: "DoctorPriceHistories");

            migrationBuilder.DropColumn(
                name: "PayoutFailureReason",
                table: "WithdrawalRequests");

            migrationBuilder.DropColumn(
                name: "PayoutStatusChangedAtUtc",
                table: "WithdrawalRequests");

            migrationBuilder.DropColumn(
                name: "PayoutStatusChangedByAdminUserId",
                table: "WithdrawalRequests");

            migrationBuilder.DropColumn(
                name: "WithdrawalRequestId",
                table: "WalletTransactions");

            migrationBuilder.DropColumn(
                name: "PricingIsActive",
                table: "DoctorProfiles");

            migrationBuilder.DropColumn(
                name: "PricingIsActive",
                table: "DoctorPriceHistories");

            migrationBuilder.CreateIndex(
                name: "IX_WithdrawalRequests_DoctorId",
                table: "WithdrawalRequests",
                column: "DoctorId");
        }
    }
}
