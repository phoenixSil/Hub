using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hub.Modules.Platform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Resultats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "LastWeeklyReportOn",
                schema: "platform",
                table: "Tenants",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WeeklyReport",
                schema: "platform",
                table: "Tenants",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastWeeklyReportOn",
                schema: "platform",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "WeeklyReport",
                schema: "platform",
                table: "Tenants");
        }
    }
}
