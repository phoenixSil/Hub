using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hub.Modules.Channels.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Reels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReelError",
                schema: "channels",
                table: "Posts",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReelKind",
                schema: "channels",
                table: "Posts",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VideoUrl",
                schema: "channels",
                table: "Posts",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReelsPerWeek",
                schema: "channels",
                table: "AutopilotSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "VideoUsages",
                schema: "channels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Month = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    Count = table.Column<int>(type: "int", nullable: false),
                    Seconds = table.Column<double>(type: "float", nullable: false),
                    Cost = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoUsages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VideoUsages_TenantId",
                schema: "channels",
                table: "VideoUsages",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_VideoUsages_TenantId_Month",
                schema: "channels",
                table: "VideoUsages",
                columns: new[] { "TenantId", "Month" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VideoUsages",
                schema: "channels");

            migrationBuilder.DropColumn(
                name: "ReelError",
                schema: "channels",
                table: "Posts");

            migrationBuilder.DropColumn(
                name: "ReelKind",
                schema: "channels",
                table: "Posts");

            migrationBuilder.DropColumn(
                name: "VideoUrl",
                schema: "channels",
                table: "Posts");

            migrationBuilder.DropColumn(
                name: "ReelsPerWeek",
                schema: "channels",
                table: "AutopilotSettings");
        }
    }
}
