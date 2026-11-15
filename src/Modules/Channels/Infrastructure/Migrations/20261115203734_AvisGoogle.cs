using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hub.Modules.Channels.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AvisGoogle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GoogleReviews",
                schema: "channels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Channel = table.Column<int>(type: "int", nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Author = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Rating = table.Column<int>(type: "int", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    WrittenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DraftReply = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Reply = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    RepliedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoogleReviews", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GoogleReviews_TenantId",
                schema: "channels",
                table: "GoogleReviews",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_GoogleReviews_TenantId_Channel_ExternalId",
                schema: "channels",
                table: "GoogleReviews",
                columns: new[] { "TenantId", "Channel", "ExternalId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GoogleReviews",
                schema: "channels");
        }
    }
}
