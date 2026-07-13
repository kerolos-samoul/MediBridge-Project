using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediBridge.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyReportingAnalyticsIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WalletTransactions_RelatedDeliveryId",
                table: "WalletTransactions");

            migrationBuilder.DropIndex(
                name: "IX_DoctorAdDeliveries_CompanyId",
                table: "DoctorAdDeliveries");

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_RelatedDeliveryId_OperationType_CreatedAtUtc",
                table: "WalletTransactions",
                columns: new[] { "RelatedDeliveryId", "OperationType", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DoctorAdDeliveries_CompanyId_CampaignId_DeliveryDateEgypt_Status_DeliveredAtUtc_Id",
                table: "DoctorAdDeliveries",
                columns: new[] { "CompanyId", "CampaignId", "DeliveryDateEgypt", "Status", "DeliveredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_DoctorAdDeliveries_CompanyId_CampaignId_FeedbackCreatedAtUtc_Id",
                table: "DoctorAdDeliveries",
                columns: new[] { "CompanyId", "CampaignId", "FeedbackCreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_DoctorAdDeliveries_CompanyId_CampaignId_InteractedAtUtc_Status",
                table: "DoctorAdDeliveries",
                columns: new[] { "CompanyId", "CampaignId", "InteractedAtUtc", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WalletTransactions_RelatedDeliveryId_OperationType_CreatedAtUtc",
                table: "WalletTransactions");

            migrationBuilder.DropIndex(
                name: "IX_DoctorAdDeliveries_CompanyId_CampaignId_DeliveryDateEgypt_Status_DeliveredAtUtc_Id",
                table: "DoctorAdDeliveries");

            migrationBuilder.DropIndex(
                name: "IX_DoctorAdDeliveries_CompanyId_CampaignId_FeedbackCreatedAtUtc_Id",
                table: "DoctorAdDeliveries");

            migrationBuilder.DropIndex(
                name: "IX_DoctorAdDeliveries_CompanyId_CampaignId_InteractedAtUtc_Status",
                table: "DoctorAdDeliveries");

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_RelatedDeliveryId",
                table: "WalletTransactions",
                column: "RelatedDeliveryId");

            migrationBuilder.CreateIndex(
                name: "IX_DoctorAdDeliveries_CompanyId",
                table: "DoctorAdDeliveries",
                column: "CompanyId");
        }
    }
}
