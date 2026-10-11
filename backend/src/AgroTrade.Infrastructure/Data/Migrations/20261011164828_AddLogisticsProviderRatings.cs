using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgroTrade.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLogisticsProviderRatings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LogisticsProviderRatings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LogisticsBookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LogisticsProviderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Rating = table.Column<byte>(type: "tinyint", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LogisticsProviderRatings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LogisticsProviderRatings_AspNetUsers_RatedByUserId",
                        column: x => x.RatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LogisticsProviderRatings_LogisticsBookings_LogisticsBookingId",
                        column: x => x.LogisticsBookingId,
                        principalTable: "LogisticsBookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LogisticsProviderRatings_LogisticsProviders_LogisticsProviderId",
                        column: x => x.LogisticsProviderId,
                        principalTable: "LogisticsProviders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LogisticsProviderRatings_LogisticsBookingId",
                table: "LogisticsProviderRatings",
                column: "LogisticsBookingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LogisticsProviderRatings_LogisticsProviderId",
                table: "LogisticsProviderRatings",
                column: "LogisticsProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_LogisticsProviderRatings_RatedByUserId",
                table: "LogisticsProviderRatings",
                column: "RatedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LogisticsProviderRatings");
        }
    }
}
