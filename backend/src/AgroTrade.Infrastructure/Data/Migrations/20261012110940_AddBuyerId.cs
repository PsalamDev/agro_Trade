using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgroTrade.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBuyerId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BuyerId",
                table: "AspNetUsers",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_BuyerId",
                table: "AspNetUsers",
                column: "BuyerId",
                unique: true,
                filter: "[BuyerId] IS NOT NULL");

            // Backfill: accounts that registered as buyers before this column existed still need an
            // account number, or the demo/seed buyers would show a blank id and could not be found by
            // it in the admin queue. Numbered oldest-registration-first so the live allocator, which
            // continues from MAX+1, keeps issuing numbers in registration order. Only buyer-side
            // roles are numbered — the administrator and the Company supplier account are not buyers.
            migrationBuilder.Sql("""
                WITH [Buyers] AS (
                    SELECT [u].[Id],
                           ROW_NUMBER() OVER (ORDER BY [u].[CreatedAtUtc], [u].[Id]) AS [Rn]
                    FROM [AspNetUsers] AS [u]
                    WHERE [u].[BuyerId] IS NULL
                      AND EXISTS (
                          SELECT 1
                          FROM [AspNetUserRoles] AS [ur]
                          JOIN [AspNetRoles] AS [r] ON [r].[Id] = [ur].[RoleId]
                          WHERE [ur].[UserId] = [u].[Id]
                            AND [r].[Name] IN ('Buyer', 'Importer', 'Distributor'))
                )
                UPDATE [u]
                SET [u].[BuyerId] = 'BUY-ID-AG-' + RIGHT('000000' + CAST([b].[Rn] AS varchar(6)), 6)
                FROM [AspNetUsers] AS [u]
                JOIN [Buyers] AS [b] ON [b].[Id] = [u].[Id];
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_BuyerId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "BuyerId",
                table: "AspNetUsers");
        }
    }
}
