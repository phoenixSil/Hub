using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hub.Modules.Channels.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CampagneAssistant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PlanId",
                schema: "channels",
                table: "Posts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "FromWizard",
                schema: "channels",
                table: "MarketingPlans",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "LastReminderOn",
                schema: "channels",
                table: "AutopilotSettings",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PlanId",
                schema: "channels",
                table: "Posts");

            migrationBuilder.DropColumn(
                name: "FromWizard",
                schema: "channels",
                table: "MarketingPlans");

            migrationBuilder.DropColumn(
                name: "LastReminderOn",
                schema: "channels",
                table: "AutopilotSettings");
        }
    }
}
