using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace src.persistence.Migrations
{
    /// <inheritdoc />
    public partial class SelfHostedLogisticsAssessments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "logistics_assessments",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    child_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    recommendation_set_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    checked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    overall_status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    fallback_used = table.Column<bool>(type: "boolean", nullable: false),
                    items = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_logistics_assessments", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_logistics_assessments_child_id_checked_at",
                table: "logistics_assessments",
                columns: new[] { "child_id", "checked_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "logistics_assessments");
        }
    }
}
