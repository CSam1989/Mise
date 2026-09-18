using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mise.Modules.Reservations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReservationDetailsAndConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "created_at_utc",
                schema: "reservations",
                table: "reservation",
                type: "timestamptz",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<Guid>(
                name: "created_by_staff_id",
                schema: "reservations",
                table: "reservation",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "customer_email",
                schema: "reservations",
                table: "reservation",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "customer_phone",
                schema: "reservations",
                table: "reservation",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "duration_minutes",
                schema: "reservations",
                table: "reservation",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "notes",
                schema: "reservations",
                table: "reservation",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "table_id",
                schema: "reservations",
                table: "reservation",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "updated_at_utc",
                schema: "reservations",
                table: "reservation",
                type: "timestamptz",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "reservations",
                table: "reservation",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateIndex(
                name: "IX_reservation_customer_phone",
                schema: "reservations",
                table: "reservation",
                column: "customer_phone");

            migrationBuilder.CreateIndex(
                name: "IX_reservation_reservation_date_time",
                schema: "reservations",
                table: "reservation",
                column: "reservation_date_time");

            migrationBuilder.CreateIndex(
                name: "IX_reservation_table_id",
                schema: "reservations",
                table: "reservation",
                column: "table_id");

            // --- Hand-written from here down: no EF Core fluent API for either of these. ---

            // Charter §10 Key Indexes / US-02: a GIN trigram index for partial/case-insensitive
            // CustomerName search — Postgres text comparison is case-sensitive by default,
            // unlike SQL Server's default collation (ADR-001 Amendment 1). CREATE EXTENSION runs
            // inside this same migration (not Mise.MigrationService's Program.cs, despite
            // docs/plan.md's original text suggesting that) because the integration-test
            // fixtures call ReservationsDbContext.Database.MigrateAsync() directly, bypassing
            // MigrationService entirely — putting it here means every caller of MigrateAsync
            // gets it, with no separate bootstrapping step to remember.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");
            migrationBuilder.Sql(
                "CREATE INDEX ix_reservation_customer_name_trgm ON reservations.reservation USING gin (customer_name gin_trgm_ops);");

            // docs/plan.md correction #1 (BR-01, verbatim): enforce "no two overlapping
            // reservations on one table" at the database via a Postgres exclusion constraint —
            // the only way to close the TOCTOU race a check-then-insert can't (there's no stale
            // *row* to detect when the conflict is between two brand-new inserts). NULL table_id
            // (an unassigned reservation) never conflicts with anything, since exclusion
            // constraints — like unique constraints — treat NULL as distinct from every other
            // value under the "=" operator. The application-level pre-check
            // (ReservationsData.HasOverlapAsync) exists purely so most violations surface as a
            // clean 409 before ever reaching this constraint; this is what's actually
            // authoritative.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");

            // A GiST exclusion constraint's index expression must be IMMUTABLE — but Postgres
            // marks the built-in `timestamptz + interval` operator STABLE (interval arithmetic
            // involving months/days depends on the session's timezone across DST boundaries), so
            // the exclusion constraint below can't call it directly (fails at migration time with
            // "functions in index expression must be marked IMMUTABLE"). This wrapper is safe to
            // declare IMMUTABLE despite Postgres never verifying that claim: DurationMinutes is
            // always a pure minutes-only interval, which Postgres adds straight to the
            // microsecond-precision UTC instant with no calendar/timezone-dependent step —
            // unlike a month/day interval, a minutes interval's result never depends on the
            // session's timezone setting.
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION reservations.reservation_end_time(start_time timestamptz, duration_minutes integer)
                RETURNS timestamptz
                LANGUAGE sql
                IMMUTABLE
                AS $$
                    SELECT start_time + (duration_minutes || ' minutes')::interval;
                $$;
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE reservations.reservation
                    ADD CONSTRAINT no_overlapping_reservations
                    EXCLUDE USING gist (
                        table_id WITH =,
                        tstzrange(reservation_date_time, reservations.reservation_end_time(reservation_date_time, duration_minutes)) WITH &&
                    )
                    WHERE (status NOT IN ('Cancelled', 'NoShow'));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE reservations.reservation DROP CONSTRAINT no_overlapping_reservations;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS reservations.reservation_end_time(timestamptz, integer);");
            migrationBuilder.Sql("DROP INDEX reservations.ix_reservation_customer_name_trgm;");

            migrationBuilder.DropIndex(
                name: "IX_reservation_customer_phone",
                schema: "reservations",
                table: "reservation");

            migrationBuilder.DropIndex(
                name: "IX_reservation_reservation_date_time",
                schema: "reservations",
                table: "reservation");

            migrationBuilder.DropIndex(
                name: "IX_reservation_table_id",
                schema: "reservations",
                table: "reservation");

            migrationBuilder.DropColumn(
                name: "created_at_utc",
                schema: "reservations",
                table: "reservation");

            migrationBuilder.DropColumn(
                name: "created_by_staff_id",
                schema: "reservations",
                table: "reservation");

            migrationBuilder.DropColumn(
                name: "customer_email",
                schema: "reservations",
                table: "reservation");

            migrationBuilder.DropColumn(
                name: "customer_phone",
                schema: "reservations",
                table: "reservation");

            migrationBuilder.DropColumn(
                name: "duration_minutes",
                schema: "reservations",
                table: "reservation");

            migrationBuilder.DropColumn(
                name: "notes",
                schema: "reservations",
                table: "reservation");

            migrationBuilder.DropColumn(
                name: "table_id",
                schema: "reservations",
                table: "reservation");

            migrationBuilder.DropColumn(
                name: "updated_at_utc",
                schema: "reservations",
                table: "reservation");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "reservations",
                table: "reservation");
        }
    }
}
