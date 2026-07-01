using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediBridge.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddMockPaymentTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MockPaymentTransactions",
                columns: table => new
                {
                    PaymentId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CompanyId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    WalletId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TransactionReference = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    WalletBalanceBefore = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    WalletBalanceAfter = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    WalletTransactionId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    AuditEventId = table.Column<string>(type: "nvarchar(450)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MockPaymentTransactions", x => x.PaymentId);
                    table.CheckConstraint("CK_MockPaymentTransactions_Amount_Positive", "[Amount] > 0");
                    table.CheckConstraint("CK_MockPaymentTransactions_Balances_NonNegative", "[WalletBalanceBefore] >= 0 AND [WalletBalanceAfter] >= 0");
                    table.CheckConstraint("CK_MockPaymentTransactions_Currency_EGP", "[Currency] = 'EGP'");
                    table.CheckConstraint("CK_MockPaymentTransactions_Status_Succeeded", "[Status] = 1");
                    table.ForeignKey(
                        name: "FK_MockPaymentTransactions_AuditEvents_AuditEventId",
                        column: x => x.AuditEventId,
                        principalTable: "AuditEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MockPaymentTransactions_CompanyProfiles_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "CompanyProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MockPaymentTransactions_WalletTransactions_WalletTransactionId",
                        column: x => x.WalletTransactionId,
                        principalTable: "WalletTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MockPaymentTransactions_Wallets_WalletId",
                        column: x => x.WalletId,
                        principalTable: "Wallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MockPaymentTransactions_AuditEventId",
                table: "MockPaymentTransactions",
                column: "AuditEventId");

            migrationBuilder.CreateIndex(
                name: "IX_MockPaymentTransactions_CompanyId_CreatedAtUtc",
                table: "MockPaymentTransactions",
                columns: new[] { "CompanyId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MockPaymentTransactions_CompanyId_IdempotencyKey",
                table: "MockPaymentTransactions",
                columns: new[] { "CompanyId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MockPaymentTransactions_TransactionReference",
                table: "MockPaymentTransactions",
                column: "TransactionReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MockPaymentTransactions_WalletId",
                table: "MockPaymentTransactions",
                column: "WalletId");

            migrationBuilder.CreateIndex(
                name: "IX_MockPaymentTransactions_WalletTransactionId",
                table: "MockPaymentTransactions",
                column: "WalletTransactionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MockPaymentTransactions");
        }
    }
}
