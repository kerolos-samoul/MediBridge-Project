using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediBridge.Repository.Migrations
{
    /// <inheritdoc />
    public partial class Phase3US2IntegrityConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_WithdrawalRequests_Amount_Positive",
                table: "WithdrawalRequests",
                sql: "[Amount] > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WalletTransactions_Amount_Positive",
                table: "WalletTransactions",
                sql: "[Amount] > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Wallets_Balances_NonNegative",
                table: "Wallets",
                sql: "[AvailableBalance] >= 0 AND [ReservedBalance] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WalletLedgerEntries_Amount_Positive",
                table: "WalletLedgerEntries",
                sql: "[Amount] > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_DoctorAdDeliveries_Money_NonNegative",
                table: "DoctorAdDeliveries",
                sql: "[PricePerMessageSnapshot] > 0 AND [PlatformFeePercentSnapshot] > 0 AND [PlatformFeePercentSnapshot] <= 100 AND [PlatformFeeAmount] > 0 AND [DoctorEarnings] > 0 AND [ReservedAmount] > 0 AND [PlatformFeeAmount] + [DoctorEarnings] = [PricePerMessageSnapshot] AND [ReservedAmount] = [PricePerMessageSnapshot]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_WithdrawalRequests_Amount_Positive",
                table: "WithdrawalRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WalletTransactions_Amount_Positive",
                table: "WalletTransactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Wallets_Balances_NonNegative",
                table: "Wallets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WalletLedgerEntries_Amount_Positive",
                table: "WalletLedgerEntries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DoctorAdDeliveries_Money_NonNegative",
                table: "DoctorAdDeliveries");
        }
    }
}
