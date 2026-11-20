using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hub.Modules.Channels.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PartReelsMedias : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ReelsPerWeek",
                schema: "channels",
                table: "AutopilotSettings",
                newName: "ReelSharePercent");

            // L'ancien réglage était un nombre de Reels par semaine : la part minimale démarre à 45 %.
            migrationBuilder.Sql("UPDATE [channels].[AutopilotSettings] SET [ReelSharePercent] = 45;");

            migrationBuilder.AddColumn<string>(
                name: "MediaKind",
                schema: "channels",
                table: "Decisions",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "MediaPropensity",
                schema: "channels",
                table: "Decisions",
                type: "float",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MediaKind",
                schema: "channels",
                table: "Decisions");

            migrationBuilder.DropColumn(
                name: "MediaPropensity",
                schema: "channels",
                table: "Decisions");

            migrationBuilder.RenameColumn(
                name: "ReelSharePercent",
                schema: "channels",
                table: "AutopilotSettings",
                newName: "ReelsPerWeek");
        }
    }
}
