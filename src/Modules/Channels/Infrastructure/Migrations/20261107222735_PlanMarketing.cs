using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hub.Modules.Channels.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PlanMarketing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Pillar",
                schema: "channels",
                table: "Posts",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AudienceSnapshots",
                schema: "channels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    Followers = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AudienceSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MarketingPlans",
                schema: "channels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Objective = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StartsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    EndsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    PostsPerWeek = table.Column<int>(type: "int", nullable: false),
                    PhasesJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    PillarsJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    BaselineJson = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    BaselineOn = table.Column<DateOnly>(type: "date", nullable: true),
                    TargetsJson = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ProposedTargetsJson = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ProposedPillarsJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ProposalNote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    LastReviewedPhase = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ApprovalReviewOn = table.Column<DateOnly>(type: "date", nullable: true),
                    InsightsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    InsightsNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketingPlans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PostStats",
                schema: "channels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PostId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Reach = table.Column<int>(type: "int", nullable: false),
                    Interactions = table.Column<int>(type: "int", nullable: false),
                    MeasuredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostStats", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AudienceSnapshots_TenantId",
                schema: "channels",
                table: "AudienceSnapshots",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AudienceSnapshots_TenantId_Channel_Day",
                schema: "channels",
                table: "AudienceSnapshots",
                columns: new[] { "TenantId", "Channel", "Day" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarketingPlans_TenantId",
                schema: "channels",
                table: "MarketingPlans",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketingPlans_TenantId_Status",
                schema: "channels",
                table: "MarketingPlans",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PostStats_TenantId",
                schema: "channels",
                table: "PostStats",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PostStats_TenantId_PostId_Channel",
                schema: "channels",
                table: "PostStats",
                columns: new[] { "TenantId", "PostId", "Channel" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AudienceSnapshots",
                schema: "channels");

            migrationBuilder.DropTable(
                name: "MarketingPlans",
                schema: "channels");

            migrationBuilder.DropTable(
                name: "PostStats",
                schema: "channels");

            migrationBuilder.DropColumn(
                name: "Pillar",
                schema: "channels",
                table: "Posts");
        }
    }
}
