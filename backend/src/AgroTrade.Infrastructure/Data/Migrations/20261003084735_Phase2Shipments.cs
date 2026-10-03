using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgroTrade.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase2Shipments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Shipments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CarrierName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    TrackingReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Mode = table.Column<int>(type: "int", nullable: false),
                    OriginCountry = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: true),
                    OriginPort = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    DestinationCountry = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: true),
                    DestinationPort = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Incoterms = table.Column<int>(type: "int", nullable: false),
                    EstimatedArrivalUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PublicTrackingTokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shipments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Shipments_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShipmentTrackingEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShipmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Location = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RecordedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShipmentTrackingEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShipmentTrackingEvents_AspNetUsers_RecordedByUserId",
                        column: x => x.RecordedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShipmentTrackingEvents_Shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalTable: "Shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_Mode_EstimatedArrivalUtc",
                table: "Shipments",
                columns: new[] { "Mode", "EstimatedArrivalUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_OrderId",
                table: "Shipments",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_PublicTrackingTokenHash",
                table: "Shipments",
                column: "PublicTrackingTokenHash",
                unique: true,
                filter: "[PublicTrackingTokenHash] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_Reference",
                table: "Shipments",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentTrackingEvents_RecordedByUserId",
                table: "ShipmentTrackingEvents",
                column: "RecordedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentTrackingEvents_ShipmentId_OccurredAtUtc",
                table: "ShipmentTrackingEvents",
                columns: new[] { "ShipmentId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentTrackingEvents_Status",
                table: "ShipmentTrackingEvents",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShipmentTrackingEvents");

            migrationBuilder.DropTable(
                name: "Shipments");
        }
    }
}
