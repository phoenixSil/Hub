using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hub.Modules.Channels.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class JournalDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Decisions",
                schema: "channels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Domain = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    PostId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Policy = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Explored = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Propensity = table.Column<double>(type: "float", nullable: true),
                    ExplorationRate = table.Column<double>(type: "float", nullable: false),
                    Slot = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Intensity = table.Column<double>(type: "float", nullable: false),
                    Format = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Audience = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Theme = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Pillar = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    VisualKind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Channels = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EditedByHuman = table.Column<bool>(type: "bit", nullable: false),
                    ContextJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Reach = table.Column<int>(type: "int", nullable: false),
                    Interactions = table.Column<int>(type: "int", nullable: false),
                    Visits = table.Column<int>(type: "int", nullable: false),
                    Leads = table.Column<int>(type: "int", nullable: false),
                    Appointments = table.Column<int>(type: "int", nullable: false),
                    Won = table.Column<int>(type: "int", nullable: false),
                    Cost = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    Reward = table.Column<double>(type: "float", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FinalizedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Decisions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Decisions_TenantId",
                schema: "channels",
                table: "Decisions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Decisions_TenantId_DecidedAt",
                schema: "channels",
                table: "Decisions",
                columns: new[] { "TenantId", "DecidedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Decisions_TenantId_PostId",
                schema: "channels",
                table: "Decisions",
                columns: new[] { "TenantId", "PostId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Decisions",
                schema: "channels");
        }
    }
}
