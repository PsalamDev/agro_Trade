using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgroTrade.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase2AdminProductModeration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ModerationReason",
                table: "Products",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ModerationStatus",
                table: "Products",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "SuspendedAtUtc",
                table: "Products",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SuspendedByUserId",
                table: "Products",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_ModerationStatus_CreatedAtUtc",
                table: "Products",
                columns: new[] { "ModerationStatus", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_SuspendedByUserId",
                table: "Products",
                column: "SuspendedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Products_AspNetUsers_SuspendedByUserId",
                table: "Products",
                column: "SuspendedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Products_AspNetUsers_SuspendedByUserId",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_ModerationStatus_CreatedAtUtc",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_SuspendedByUserId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ModerationReason",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ModerationStatus",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "SuspendedAtUtc",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "SuspendedByUserId",
                table: "Products");
        }
    }
}
