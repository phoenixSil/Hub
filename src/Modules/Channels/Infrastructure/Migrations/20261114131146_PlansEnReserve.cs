using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hub.Modules.Channels.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PlansEnReserve : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "StartsAutomatically",
                schema: "channels",
                table: "MarketingPlans",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "StoppedOn",
                schema: "channels",
                table: "MarketingPlans",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StartsAutomatically",
                schema: "channels",
                table: "MarketingPlans");

            migrationBuilder.DropColumn(
                name: "StoppedOn",
                schema: "channels",
                table: "MarketingPlans");
        }
    }
}
