using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hub.Modules.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PrixEtRappels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Notes",
                schema: "catalog",
                table: "PricingModels",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublicNote",
                schema: "catalog",
                table: "PricingModels",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PublicNote",
                schema: "catalog",
                table: "PricingModels");

            migrationBuilder.AlterColumn<string>(
                name: "Notes",
                schema: "catalog",
                table: "PricingModels",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(1000)",
                oldMaxLength: 1000,
                oldNullable: true);
        }
    }
}
