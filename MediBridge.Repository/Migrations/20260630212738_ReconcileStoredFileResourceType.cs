using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediBridge.Repository.Migrations
{
    /// <inheritdoc />
    public partial class ReconcileStoredFileResourceType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS
                (
                    SELECT 1
                    FROM sys.columns AS [column]
                    INNER JOIN sys.types AS [type]
                        ON [column].[user_type_id] = [type].[user_type_id]
                    WHERE [column].[object_id] = OBJECT_ID(N'[dbo].[StoredFiles]')
                        AND [column].[name] = N'StorageResourceType'
                        AND [type].[name] IN (N'nvarchar', N'varchar')
                )
                BEGIN
                    IF EXISTS
                    (
                        SELECT 1
                        FROM [StoredFiles]
                        WHERE LOWER(LTRIM(RTRIM([StorageResourceType])))
                            NOT IN (N'image', N'video', N'raw')
                    )
                    BEGIN
                        THROW 51002, 'StoredFiles contains an unsupported storage resource type.', 1;
                    END;

                    ALTER TABLE [StoredFiles]
                    ADD [StorageResourceType_Integrated] int NULL;

                    EXEC sys.sp_executesql N'
                        UPDATE [StoredFiles]
                        SET [StorageResourceType_Integrated] = CASE LOWER(LTRIM(RTRIM([StorageResourceType])))
                            WHEN N''image'' THEN 1
                            WHEN N''video'' THEN 2
                            WHEN N''raw'' THEN 3
                        END;

                        IF EXISTS
                        (
                            SELECT 1
                            FROM [StoredFiles]
                            WHERE [StorageResourceType_Integrated] IS NULL
                        )
                        BEGIN
                            THROW 51003, ''StoredFiles resource type conversion produced a null value.'', 1;
                        END;';

                    DECLARE @DefaultConstraintName sysname;
                    SELECT @DefaultConstraintName = [constraint].[name]
                    FROM sys.default_constraints AS [constraint]
                    INNER JOIN sys.columns AS [column]
                        ON [constraint].[parent_object_id] = [column].[object_id]
                        AND [constraint].[parent_column_id] = [column].[column_id]
                    WHERE [constraint].[parent_object_id] = OBJECT_ID(N'[dbo].[StoredFiles]')
                        AND [column].[name] = N'StorageResourceType';

                    IF @DefaultConstraintName IS NOT NULL
                    BEGIN
                        DECLARE @DropDefaultSql nvarchar(max) =
                            N'ALTER TABLE [StoredFiles] DROP CONSTRAINT ' + QUOTENAME(@DefaultConstraintName);
                        EXEC sys.sp_executesql @DropDefaultSql;
                    END;

                    ALTER TABLE [StoredFiles]
                    DROP COLUMN [StorageResourceType];

                    EXEC sp_rename
                        N'[dbo].[StoredFiles].[StorageResourceType_Integrated]',
                        N'StorageResourceType',
                        N'COLUMN';

                    ALTER TABLE [StoredFiles]
                    ALTER COLUMN [StorageResourceType] int NOT NULL;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // This compatibility migration restores the integrated model's integer enum mapping.
            // Reverting it to the divergent string mapping would break the preceding integrated model.
        }
    }
}
