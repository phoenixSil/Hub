using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hub.Modules.Channels.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Photos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ImageUsages",
                schema: "channels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Month = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    Count = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImageUsages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImageUsages_TenantId",
                schema: "channels",
                table: "ImageUsages",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ImageUsages_TenantId_Month",
                schema: "channels",
                table: "ImageUsages",
                columns: new[] { "TenantId", "Month" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImageUsages",
                schema: "channels");
        }
    }
}
