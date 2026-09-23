using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mise.Modules.Reservations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditLogEntryEntityTypeEntityIdIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_audit_log_entry_entity_type_entity_id",
                schema: "shared",
                table: "audit_log_entry",
                columns: new[] { "entity_type", "entity_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_audit_log_entry_entity_type_entity_id",
                schema: "shared",
                table: "audit_log_entry");
        }
    }
}
