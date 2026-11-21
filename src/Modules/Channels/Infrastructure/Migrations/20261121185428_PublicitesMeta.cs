using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hub.Modules.Channels.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PublicitesMeta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdCampaigns",
                schema: "channels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Objective = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PostId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceChannel = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Message = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Headline = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LinkUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    TrackingCampaign = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    OnFacebook = table.Column<bool>(type: "bit", nullable: false),
                    OnInstagram = table.Column<bool>(type: "bit", nullable: false),
                    Budget = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    StartAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Latitude = table.Column<double>(type: "float", nullable: false),
                    Longitude = table.Column<double>(type: "float", nullable: false),
                    RadiusKm = table.Column<int>(type: "int", nullable: false),
                    AgeMin = table.Column<int>(type: "int", nullable: false),
                    AgeMax = table.Column<int>(type: "int", nullable: false),
                    MetaCampaignId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    MetaAdSetId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    MetaAdId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StatusDetail = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Spend = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    Reach = table.Column<int>(type: "int", nullable: false),
                    Impressions = table.Column<int>(type: "int", nullable: false),
                    Clicks = table.Column<int>(type: "int", nullable: false),
                    Engagements = table.Column<int>(type: "int", nullable: false),
                    SyncedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LaunchedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdCampaigns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdSettings",
                schema: "channels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AdAccountId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    AdAccountName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    ProtectedToken = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MonthlyCap = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: true),
                    Latitude = table.Column<double>(type: "float", nullable: false),
                    Longitude = table.Column<double>(type: "float", nullable: false),
                    RadiusKm = table.Column<int>(type: "int", nullable: false),
                    AgeMin = table.Column<int>(type: "int", nullable: false),
                    AgeMax = table.Column<int>(type: "int", nullable: false),
                    ConnectedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdCampaigns_TenantId",
                schema: "channels",
                table: "AdCampaigns",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AdCampaigns_TenantId_CreatedAt",
                schema: "channels",
                table: "AdCampaigns",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AdCampaigns_TenantId_PostId",
                schema: "channels",
                table: "AdCampaigns",
                columns: new[] { "TenantId", "PostId" });

            migrationBuilder.CreateIndex(
                name: "IX_AdSettings_TenantId",
                schema: "channels",
                table: "AdSettings",
                column: "TenantId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdCampaigns",
                schema: "channels");

            migrationBuilder.DropTable(
                name: "AdSettings",
                schema: "channels");
        }
    }
}
