using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace src.persistence.Migrations
{
    /// <inheritdoc />
    public partial class WishlistEventProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "budget_estimate",
                table: "wishlist_events",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "category",
                table: "wishlist_events",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status_change",
                table: "wishlist_events",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "budget_estimate",
                table: "wishlist_events");

            migrationBuilder.DropColumn(
                name: "category",
                table: "wishlist_events");

            migrationBuilder.DropColumn(
                name: "status_change",
                table: "wishlist_events");
        }
    }
}
