using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgroTrade.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLogisticsDirectory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LogisticsProviders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    HomeCountry = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    ContactEmail = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ContactPhone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Website = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Modes = table.Column<int>(type: "int", nullable: false),
                    Services = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsVerified = table.Column<bool>(type: "bit", nullable: false),
                    Rating = table.Column<decimal>(type: "decimal(3,2)", precision: 3, scale: 2, nullable: false),
                    RatingCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LogisticsProviders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LogisticsProviders_AspNetUsers_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LogisticsBookings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LogisticsProviderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Mode = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ResponseNotes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LogisticsBookings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LogisticsBookings_AspNetUsers_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LogisticsBookings_LogisticsProviders_LogisticsProviderId",
                        column: x => x.LogisticsProviderId,
                        principalTable: "LogisticsProviders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LogisticsBookings_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LogisticsProviderCountries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LogisticsProviderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Country = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LogisticsProviderCountries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LogisticsProviderCountries_LogisticsProviders_LogisticsProviderId",
                        column: x => x.LogisticsProviderId,
                        principalTable: "LogisticsProviders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LogisticsBookings_LogisticsProviderId_Status",
                table: "LogisticsBookings",
                columns: new[] { "LogisticsProviderId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_LogisticsBookings_OrderId_LogisticsProviderId",
                table: "LogisticsBookings",
                columns: new[] { "OrderId", "LogisticsProviderId" });

            migrationBuilder.CreateIndex(
                name: "IX_LogisticsBookings_Reference",
                table: "LogisticsBookings",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LogisticsBookings_RequestedByUserId_Status",
                table: "LogisticsBookings",
                columns: new[] { "RequestedByUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_LogisticsProviderCountries_Country",
                table: "LogisticsProviderCountries",
                column: "Country");

            migrationBuilder.CreateIndex(
                name: "IX_LogisticsProviderCountries_LogisticsProviderId_Country",
                table: "LogisticsProviderCountries",
                columns: new[] { "LogisticsProviderId", "Country" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LogisticsProviders_HomeCountry",
                table: "LogisticsProviders",
                column: "HomeCountry");

            migrationBuilder.CreateIndex(
                name: "IX_LogisticsProviders_IsActive_IsVerified",
                table: "LogisticsProviders",
                columns: new[] { "IsActive", "IsVerified" });

            migrationBuilder.CreateIndex(
                name: "IX_LogisticsProviders_OwnerUserId",
                table: "LogisticsProviders",
                column: "OwnerUserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LogisticsBookings");

            migrationBuilder.DropTable(
                name: "LogisticsProviderCountries");

            migrationBuilder.DropTable(
                name: "LogisticsProviders");
        }
    }
}
