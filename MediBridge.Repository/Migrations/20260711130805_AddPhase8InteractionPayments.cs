using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediBridge.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddPhase8InteractionPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "PlatformFeePercentSnapshot",
                table: "DoctorAdDeliveries",
                type: "decimal(7,3)",
                precision: 7,
                scale: 3,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(5,2)",
                oldPrecision: 5,
                oldScale: 2);

            migrationBuilder.CreateTable(
                name: "DeliveryInteractions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    DeliveryId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    DoctorId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ActorUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    IdempotencyKeyHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    FeedbackText = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    FeedbackQualifiesForScore = table.Column<bool>(type: "bit", nullable: false),
                    ChargeTransactionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    EarnTransactionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    AuditEventId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConcurrencyToken = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryInteractions", x => x.Id);
                    table.CheckConstraint("CK_DeliveryInteractions_Feedback_Length", "[FeedbackText] IS NULL OR LEN([FeedbackText]) <= 2000");
                    table.CheckConstraint("CK_DeliveryInteractions_Outcome", "[Outcome] IN (1, 2)");
                    table.ForeignKey(
                        name: "FK_DeliveryInteractions_AuditEvents_AuditEventId",
                        column: x => x.AuditEventId,
                        principalTable: "AuditEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryInteractions_DoctorAdDeliveries_DeliveryId",
                        column: x => x.DeliveryId,
                        principalTable: "DoctorAdDeliveries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryInteractions_DoctorProfiles_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "DoctorProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryInteractions_WalletTransactions_ChargeTransactionId",
                        column: x => x.ChargeTransactionId,
                        principalTable: "WalletTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryInteractions_WalletTransactions_EarnTransactionId",
                        column: x => x.EarnTransactionId,
                        principalTable: "WalletTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DoctorAdDeliveries_DoctorId_DeliveryDateEgypt_Status_ReservationStatus_Id",
                table: "DoctorAdDeliveries",
                columns: new[] { "DoctorId", "DeliveryDateEgypt", "Status", "ReservationStatus", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryInteractions_AuditEventId",
                table: "DeliveryInteractions",
                column: "AuditEventId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryInteractions_ChargeTransactionId",
                table: "DeliveryInteractions",
                column: "ChargeTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryInteractions_DeliveryId",
                table: "DeliveryInteractions",
                column: "DeliveryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryInteractions_DoctorId_IdempotencyKeyHash",
                table: "DeliveryInteractions",
                columns: new[] { "DoctorId", "IdempotencyKeyHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryInteractions_EarnTransactionId",
                table: "DeliveryInteractions",
                column: "EarnTransactionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeliveryInteractions");

            migrationBuilder.DropIndex(
                name: "IX_DoctorAdDeliveries_DoctorId_DeliveryDateEgypt_Status_ReservationStatus_Id",
                table: "DoctorAdDeliveries");

            migrationBuilder.AlterColumn<decimal>(
                name: "PlatformFeePercentSnapshot",
                table: "DoctorAdDeliveries",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(7,3)",
                oldPrecision: 7,
                oldScale: 3);
        }
    }
}
