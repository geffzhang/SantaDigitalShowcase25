using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace src.persistence.Migrations
{
    /// <inheritdoc />
    public partial class DrasiWishlistEventsPublication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $drasi_publication$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM pg_publication
                        WHERE pubname = 'drasi_wishlist_events'
                    ) THEN
                        CREATE PUBLICATION drasi_wishlist_events
                        FOR TABLE public.wishlist_events;
                    ELSIF NOT EXISTS (
                        SELECT 1
                        FROM pg_publication_tables
                        WHERE pubname = 'drasi_wishlist_events'
                          AND schemaname = 'public'
                          AND tablename = 'wishlist_events'
                    ) THEN
                        ALTER PUBLICATION drasi_wishlist_events
                        ADD TABLE public.wishlist_events;
                    END IF;
                END
                $drasi_publication$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $drasi_publication$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM pg_publication_tables
                        WHERE pubname = 'drasi_wishlist_events'
                          AND schemaname = 'public'
                          AND tablename = 'wishlist_events'
                    ) THEN
                        ALTER PUBLICATION drasi_wishlist_events
                        DROP TABLE public.wishlist_events;
                    END IF;
                END
                $drasi_publication$;
                """);
        }
    }
}
