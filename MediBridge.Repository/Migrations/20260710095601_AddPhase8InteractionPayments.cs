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
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT 1
                    FROM [DoctorAdDeliveries]
                    WHERE [FeedbackText] IS NOT NULL AND LEN([FeedbackText]) > 1000
                )
                BEGIN
                    THROW 51008, 'Phase 8 feedback text migration cannot narrow DoctorAdDeliveries.FeedbackText while values exceed 1000 characters.', 1;
                END
                """);

            migrationBuilder.AlterColumn<string>(
                name: "FeedbackText",
                table: "DoctorAdDeliveries",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(4000)",
                oldMaxLength: 4000,
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "DeliveryInteractionOperations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    DoctorId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    DeliveryId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Decision = table.Column<int>(type: "int", nullable: false),
                    FeedbackText = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ChargeTransactionId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EarnTransactionId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SafeFailureSummary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ConcurrencyToken = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryInteractionOperations", x => x.Id);
                    table.CheckConstraint("CK_DeliveryInteractionOperations_FeedbackText_Length", "[FeedbackText] IS NULL OR LEN([FeedbackText]) <= 1000");
                    table.CheckConstraint("CK_DeliveryInteractionOperations_IdempotencyKey_Length", "LEN([IdempotencyKey]) BETWEEN 8 AND 128");
                    table.ForeignKey(
                        name: "FK_DeliveryInteractionOperations_DoctorAdDeliveries_DeliveryId",
                        column: x => x.DeliveryId,
                        principalTable: "DoctorAdDeliveries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DoctorAdDeliveries_DoctorId_DeliveryDateEgypt_Id",
                table: "DoctorAdDeliveries",
                columns: new[] { "DoctorId", "DeliveryDateEgypt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryInteractionOperations_DeliveryId_Decision",
                table: "DeliveryInteractionOperations",
                columns: new[] { "DeliveryId", "Decision" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryInteractionOperations_DoctorId_DeliveryId_IdempotencyKey",
                table: "DeliveryInteractionOperations",
                columns: new[] { "DoctorId", "DeliveryId", "IdempotencyKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeliveryInteractionOperations");

            migrationBuilder.DropIndex(
                name: "IX_DoctorAdDeliveries_DoctorId_DeliveryDateEgypt_Id",
                table: "DoctorAdDeliveries");

            migrationBuilder.AlterColumn<string>(
                name: "FeedbackText",
                table: "DoctorAdDeliveries",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(1000)",
                oldMaxLength: 1000,
                oldNullable: true);
        }
    }
}
