using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediBridge.Repository.Migrations
{
    /// <inheritdoc />
    public partial class MapContactVerificationLifecycleModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ContactVerificationFlows_UserId",
                table: "ContactVerificationFlows");

            migrationBuilder.CreateIndex(
                name: "IX_ContactVerificationFlows_UserId_Channel_ConsumedAtUtc_SupersededAtUtc_ExpiresAtUtc",
                table: "ContactVerificationFlows",
                columns: new[] { "UserId", "Channel", "ConsumedAtUtc", "SupersededAtUtc", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ContactVerificationFlows_UserId_Channel_CreatedAtUtc",
                table: "ContactVerificationFlows",
                columns: new[] { "UserId", "Channel", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ContactVerificationFlows_UserId_Channel_ConsumedAtUtc_SupersededAtUtc_ExpiresAtUtc",
                table: "ContactVerificationFlows");

            migrationBuilder.DropIndex(
                name: "IX_ContactVerificationFlows_UserId_Channel_CreatedAtUtc",
                table: "ContactVerificationFlows");

            migrationBuilder.CreateIndex(
                name: "IX_ContactVerificationFlows_UserId",
                table: "ContactVerificationFlows",
                column: "UserId");
        }
    }
}
