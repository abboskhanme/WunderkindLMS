using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class McpReadOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mcp_audit",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    user_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    client_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    client_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    grant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tool = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    arguments = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    row_count = table.Column<int>(type: "integer", nullable: true),
                    duration_ms = table.Column<int>(type: "integer", nullable: false),
                    ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mcp_audit", x => x.id);
                    table.CheckConstraint("ck_mcp_audit_outcome", "outcome in ('ok','denied','error')");
                });

            migrationBuilder.CreateTable(
                name: "mcp_clients",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    redirect_uris = table.Column<List<string>>(type: "text[]", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mcp_clients", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "mcp_auth_codes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    client_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    redirect_uri = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    code_challenge = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    scope = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    resource = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    grant_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mcp_auth_codes", x => x.id);
                    table.ForeignKey(
                        name: "fk_mcp_auth_codes_mcp_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "mcp_clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "mcp_grants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    scope = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    resource = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mcp_grants", x => x.id);
                    table.ForeignKey(
                        name: "fk_mcp_grants_mcp_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "mcp_clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "mcp_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    grant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mcp_tokens", x => x.id);
                    table.CheckConstraint("ck_mcp_tokens_kind", "kind in ('access','refresh')");
                    table.ForeignKey(
                        name: "fk_mcp_tokens_mcp_grants_grant_id",
                        column: x => x.grant_id,
                        principalTable: "mcp_grants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mcp_audit_at",
                table: "mcp_audit",
                column: "at");

            migrationBuilder.CreateIndex(
                name: "ix_mcp_audit_user_id_at",
                table: "mcp_audit",
                columns: new[] { "user_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_mcp_auth_codes_client_id",
                table: "mcp_auth_codes",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_mcp_auth_codes_code_hash",
                table: "mcp_auth_codes",
                column: "code_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mcp_auth_codes_expires_at",
                table: "mcp_auth_codes",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_mcp_grants_client_id",
                table: "mcp_grants",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_mcp_grants_user_id",
                table: "mcp_grants",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_mcp_tokens_expires_at",
                table: "mcp_tokens",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_mcp_tokens_grant_id",
                table: "mcp_tokens",
                column: "grant_id");

            migrationBuilder.CreateIndex(
                name: "ix_mcp_tokens_token_hash",
                table: "mcp_tokens",
                column: "token_hash",
                unique: true);

            // app_rw: CRUD on OAuth tables, mcp_audit append-only (+ retention function);
            // app_ro: explicit allow-list via mcp_apply_app_ro_grants(). Mirrored in deploy/init-roles.sql.
            migrationBuilder.Sql(MigrationSql.Read("mcp_guards.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP FUNCTION IF EXISTS public.mcp_prune_audit(integer); "
                + "DROP FUNCTION IF EXISTS public.mcp_apply_app_ro_grants();");

            migrationBuilder.DropTable(
                name: "mcp_audit");

            migrationBuilder.DropTable(
                name: "mcp_auth_codes");

            migrationBuilder.DropTable(
                name: "mcp_tokens");

            migrationBuilder.DropTable(
                name: "mcp_grants");

            migrationBuilder.DropTable(
                name: "mcp_clients");
        }
    }
}
