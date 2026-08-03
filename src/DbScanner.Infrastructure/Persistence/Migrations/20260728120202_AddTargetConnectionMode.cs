using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DbScanner.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTargetConnectionMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConnectionMode",
                table: "targets",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "DefaultValues");

            migrationBuilder.UpdateData(
                table: "targets",
                keyColumn: "Id",
                keyValue: new Guid("3d79cfbe-dbb5-4d95-b476-854ed970d43d"),
                column: "ConnectionMode",
                value: "DefaultValues");

            migrationBuilder.UpdateData(
                table: "targets",
                keyColumn: "Id",
                keyValue: new Guid("b2fd6c5a-98b4-4641-8e98-4b95c5ba98db"),
                column: "ConnectionMode",
                value: "DefaultValues");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConnectionMode",
                table: "targets");
        }
    }
}
