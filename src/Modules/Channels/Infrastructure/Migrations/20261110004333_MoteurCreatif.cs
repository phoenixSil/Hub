using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hub.Modules.Channels.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MoteurCreatif : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ArtStyle",
                schema: "channels",
                table: "Posts",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DisabledStyles",
                schema: "channels",
                table: "AutopilotSettings",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ArtStyle",
                schema: "channels",
                table: "Posts");

            migrationBuilder.DropColumn(
                name: "DisabledStyles",
                schema: "channels",
                table: "AutopilotSettings");
        }
    }
}
