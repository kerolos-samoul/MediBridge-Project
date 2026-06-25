using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediBridge.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddStoredFileStorageObjectLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAtUtc",
                table: "StoredFiles",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StorageResourceType",
                table: "StoredFiles",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "StorageState",
                table: "StoredFiles",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SupersededByFileId",
                table: "StoredFiles",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "StoredFiles");

            migrationBuilder.DropColumn(
                name: "StorageResourceType",
                table: "StoredFiles");

            migrationBuilder.DropColumn(
                name: "StorageState",
                table: "StoredFiles");

            migrationBuilder.DropColumn(
                name: "SupersededByFileId",
                table: "StoredFiles");
        }
    }
}
