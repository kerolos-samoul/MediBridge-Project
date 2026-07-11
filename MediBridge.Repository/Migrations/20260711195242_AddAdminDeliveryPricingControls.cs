using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediBridge.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminDeliveryPricingControls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT 1
                    FROM sys.columns
                    WHERE object_id = OBJECT_ID(N'[dbo].[DoctorAdDeliveries]')
                        AND name = N'FeedbackText'
                        AND max_length > 2000
                )
                BEGIN
                    ALTER TABLE [dbo].[DoctorAdDeliveries] ALTER COLUMN [FeedbackText] nvarchar(1000) NULL;
                END
                """);

            migrationBuilder.Sql("""
                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'[dbo].[DoctorAdDeliveries]')
                        AND name = N'IX_DoctorAdDeliveries_DoctorId_DeliveryDateEgypt_Id'
                )
                BEGIN
                    CREATE INDEX [IX_DoctorAdDeliveries_DoctorId_DeliveryDateEgypt_Id]
                    ON [dbo].[DoctorAdDeliveries] ([DoctorId], [DeliveryDateEgypt], [Id]);
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'[dbo].[DoctorAdDeliveries]')
                        AND name = N'IX_DoctorAdDeliveries_DoctorId_DeliveryDateEgypt_Id'
                )
                BEGIN
                    DROP INDEX [IX_DoctorAdDeliveries_DoctorId_DeliveryDateEgypt_Id]
                    ON [dbo].[DoctorAdDeliveries];
                END
                """);

            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT 1
                    FROM sys.columns
                    WHERE object_id = OBJECT_ID(N'[dbo].[DoctorAdDeliveries]')
                        AND name = N'FeedbackText'
                        AND max_length = 2000
                )
                BEGIN
                    ALTER TABLE [dbo].[DoctorAdDeliveries] ALTER COLUMN [FeedbackText] nvarchar(4000) NULL;
                END
                """);
        }
    }
}
