using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hub.Modules.GiftCards.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CartesCadeaux : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "giftcards");

            migrationBuilder.CreateTable(
                name: "GiftCards",
                schema: "giftcards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    Balance = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    BuyerName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    BuyerEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    BuyerPhone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    RecipientName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    RecipientEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Message = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    DeliverOn = table.Column<DateOnly>(type: "date", nullable: true),
                    PaidAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PaidNote = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    SentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GiftCards", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GiftCardUses",
                schema: "giftcards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GiftCardId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    RecordedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GiftCardUses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GiftCardUses_GiftCards_GiftCardId",
                        column: x => x.GiftCardId,
                        principalSchema: "giftcards",
                        principalTable: "GiftCards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GiftCards_Code",
                schema: "giftcards",
                table: "GiftCards",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GiftCards_TenantId",
                schema: "giftcards",
                table: "GiftCards",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_GiftCards_TenantId_Status",
                schema: "giftcards",
                table: "GiftCards",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_GiftCardUses_GiftCardId",
                schema: "giftcards",
                table: "GiftCardUses",
                column: "GiftCardId");

            migrationBuilder.CreateIndex(
                name: "IX_GiftCardUses_TenantId",
                schema: "giftcards",
                table: "GiftCardUses",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GiftCardUses",
                schema: "giftcards");

            migrationBuilder.DropTable(
                name: "GiftCards",
                schema: "giftcards");
        }
    }
}
