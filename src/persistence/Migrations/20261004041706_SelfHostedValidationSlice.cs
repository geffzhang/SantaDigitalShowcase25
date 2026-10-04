using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace src.persistence.Migrations
{
    /// <inheritdoc />
    public partial class SelfHostedValidationSlice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    child_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    message = table.Column<string>(type: "text", nullable: false),
                    related_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notifications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "profile_snapshots",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    child_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    preferences = table.Column<string>(type: "jsonb", nullable: false),
                    budget_ceiling = table.Column<double>(type: "double precision", nullable: true),
                    behavior_summary = table.Column<string>(type: "text", nullable: true),
                    enrichment_source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    fallback_used = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_profile_snapshots", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "recommendations",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    child_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    profile_snapshot_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    fallback_used = table.Column<bool>(type: "boolean", nullable: false),
                    generation_source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    items = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recommendations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "wishlist_events",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    child_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    dedupe_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wishlist_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "wishlists",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    child_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    dedupe_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    request_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    category = table.Column<string>(type: "text", nullable: true),
                    budget_estimate = table.Column<double>(type: "double precision", nullable: true),
                    status_change = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wishlists", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_child_id_created_at",
                table: "notifications",
                columns: new[] { "child_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_profile_snapshots_child_id_created_at",
                table: "profile_snapshots",
                columns: new[] { "child_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_recommendations_child_id_created_at",
                table: "recommendations",
                columns: new[] { "child_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_wishlist_events_child_id_created_at",
                table: "wishlist_events",
                columns: new[] { "child_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_wishlists_child_id_created_at",
                table: "wishlists",
                columns: new[] { "child_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "profile_snapshots");

            migrationBuilder.DropTable(
                name: "recommendations");

            migrationBuilder.DropTable(
                name: "wishlist_events");

            migrationBuilder.DropTable(
                name: "wishlists");
        }
    }
}
