using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mise.Modules.Tables.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTableGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "table_group",
                schema: "tables",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    table_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_table_group", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "table_group",
                schema: "tables");
        }
    }
}
