using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hub.Modules.Platform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CartesCadeaux : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GiftCardPaymentInstructions",
                schema: "platform",
                table: "Tenants",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GiftCardPaymentInstructions",
                schema: "platform",
                table: "Tenants");
        }
    }
}
