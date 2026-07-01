using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediBridge.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddMockPaymentTransactionsClean : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[dbo].[MockPaymentTransactions]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[MockPaymentTransactions] (
                        [PaymentId] nvarchar(450) NOT NULL,
                        [CompanyId] nvarchar(450) NOT NULL,
                        [WalletId] nvarchar(450) NOT NULL,
                        [Amount] decimal(18,2) NOT NULL,
                        [Currency] nvarchar(3) NOT NULL,
                        [Status] int NOT NULL,
                        [CreatedAtUtc] datetime2 NOT NULL,
                        [TransactionReference] nvarchar(160) NOT NULL,
                        [IdempotencyKey] nvarchar(128) NOT NULL,
                        [WalletBalanceBefore] decimal(18,2) NOT NULL,
                        [WalletBalanceAfter] decimal(18,2) NOT NULL,
                        [WalletTransactionId] nvarchar(450) NOT NULL,
                        [AuditEventId] nvarchar(450) NULL,
                        CONSTRAINT [PK_MockPaymentTransactions] PRIMARY KEY ([PaymentId]),
                        CONSTRAINT [CK_MockPaymentTransactions_Amount_Positive] CHECK ([Amount] > 0),
                        CONSTRAINT [CK_MockPaymentTransactions_Balances_NonNegative] CHECK ([WalletBalanceBefore] >= 0 AND [WalletBalanceAfter] >= 0),
                        CONSTRAINT [CK_MockPaymentTransactions_Currency_EGP] CHECK ([Currency] = 'EGP'),
                        CONSTRAINT [CK_MockPaymentTransactions_Status_Succeeded] CHECK ([Status] = 1),
                        CONSTRAINT [FK_MockPaymentTransactions_AuditEvents_AuditEventId] FOREIGN KEY ([AuditEventId]) REFERENCES [dbo].[AuditEvents] ([Id]),
                        CONSTRAINT [FK_MockPaymentTransactions_CompanyProfiles_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [dbo].[CompanyProfiles] ([Id]),
                        CONSTRAINT [FK_MockPaymentTransactions_WalletTransactions_WalletTransactionId] FOREIGN KEY ([WalletTransactionId]) REFERENCES [dbo].[WalletTransactions] ([Id]),
                        CONSTRAINT [FK_MockPaymentTransactions_Wallets_WalletId] FOREIGN KEY ([WalletId]) REFERENCES [dbo].[Wallets] ([Id])
                    );

                    EXEC sys.sp_addextendedproperty
                        @name = N'MediBridgeMigrationOwner',
                        @value = N'20260701172632_AddMockPaymentTransactionsClean',
                        @level0type = N'SCHEMA',
                        @level0name = N'dbo',
                        @level1type = N'TABLE',
                        @level1name = N'MockPaymentTransactions';
                END
                ELSE IF
                    (SELECT COUNT(*) FROM sys.columns WHERE [object_id] = OBJECT_ID(N'[dbo].[MockPaymentTransactions]')) <> 13
                    OR COL_LENGTH(N'dbo.MockPaymentTransactions', N'PaymentId') <> 900
                    OR COL_LENGTH(N'dbo.MockPaymentTransactions', N'CompanyId') <> 900
                    OR COL_LENGTH(N'dbo.MockPaymentTransactions', N'WalletId') <> 900
                    OR COL_LENGTH(N'dbo.MockPaymentTransactions', N'Currency') <> 6
                    OR COL_LENGTH(N'dbo.MockPaymentTransactions', N'TransactionReference') <> 320
                    OR COL_LENGTH(N'dbo.MockPaymentTransactions', N'IdempotencyKey') < 256
                    OR COL_LENGTH(N'dbo.MockPaymentTransactions', N'WalletTransactionId') <> 900
                    OR NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE [name] = N'CK_MockPaymentTransactions_Status_Succeeded' AND [parent_object_id] = OBJECT_ID(N'[dbo].[MockPaymentTransactions]'))
                    OR NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE [name] = N'FK_MockPaymentTransactions_CompanyProfiles_CompanyId' AND [parent_object_id] = OBJECT_ID(N'[dbo].[MockPaymentTransactions]'))
                    OR NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE [name] = N'FK_MockPaymentTransactions_WalletTransactions_WalletTransactionId' AND [parent_object_id] = OBJECT_ID(N'[dbo].[MockPaymentTransactions]'))
                    OR NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE [name] = N'FK_MockPaymentTransactions_Wallets_WalletId' AND [parent_object_id] = OBJECT_ID(N'[dbo].[MockPaymentTransactions]'))
                BEGIN
                    THROW 51000, 'Existing MockPaymentTransactions table is incompatible with this migration.', 1;
                END;
                """);

            CreateIndexIfMissing(
                migrationBuilder,
                "IX_MockPaymentTransactions_AuditEventId",
                "[AuditEventId]");
            CreateIndexIfMissing(
                migrationBuilder,
                "IX_MockPaymentTransactions_CompanyId_CreatedAtUtc",
                "[CompanyId], [CreatedAtUtc]");
            CreateIndexIfMissing(
                migrationBuilder,
                "IX_MockPaymentTransactions_CompanyId_IdempotencyKey",
                "[CompanyId], [IdempotencyKey]",
                unique: true);
            CreateIndexIfMissing(
                migrationBuilder,
                "IX_MockPaymentTransactions_TransactionReference",
                "[TransactionReference]",
                unique: true);
            CreateIndexIfMissing(
                migrationBuilder,
                "IX_MockPaymentTransactions_WalletId",
                "[WalletId]");
            CreateIndexIfMissing(
                migrationBuilder,
                "IX_MockPaymentTransactions_WalletTransactionId",
                "[WalletTransactionId]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (
                    SELECT 1
                    FROM sys.extended_properties
                    WHERE [major_id] = OBJECT_ID(N'[dbo].[MockPaymentTransactions]')
                      AND [minor_id] = 0
                      AND [name] = N'MediBridgeMigrationOwner'
                      AND CONVERT(nvarchar(128), [value]) = N'20260701172632_AddMockPaymentTransactionsClean')
                BEGIN
                    DROP TABLE [dbo].[MockPaymentTransactions];
                END;
                """);
        }

        private static void CreateIndexIfMissing(
            MigrationBuilder migrationBuilder,
            string indexName,
            string columns,
            bool unique = false)
        {
            var uniqueSql = unique ? "UNIQUE " : string.Empty;
            migrationBuilder.Sql(
                $"""
                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE [name] = N'{indexName}'
                      AND [object_id] = OBJECT_ID(N'[dbo].[MockPaymentTransactions]'))
                BEGIN
                    CREATE {uniqueSql}INDEX [{indexName}]
                        ON [dbo].[MockPaymentTransactions] ({columns});
                END;
                """);
        }
    }
}
