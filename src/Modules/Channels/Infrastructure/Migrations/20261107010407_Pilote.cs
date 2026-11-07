using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hub.Modules.Channels.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Pilote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Audience",
                schema: "channels",
                table: "Posts",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "FromAutopilot",
                schema: "channels",
                table: "Posts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "AutopilotSettings",
                schema: "channels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    PublishWithoutApproval = table.Column<bool>(type: "bit", nullable: false),
                    PostsPerWeek = table.Column<int>(type: "int", nullable: false),
                    AudiencesJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Channels = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Guidelines = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    LastRunAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastRunSummary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutopilotSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AutopilotSettings_TenantId",
                schema: "channels",
                table: "AutopilotSettings",
                column: "TenantId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AutopilotSettings",
                schema: "channels");

            migrationBuilder.DropColumn(
                name: "Audience",
                schema: "channels",
                table: "Posts");

            migrationBuilder.DropColumn(
                name: "FromAutopilot",
                schema: "channels",
                table: "Posts");
        }
    }
}
