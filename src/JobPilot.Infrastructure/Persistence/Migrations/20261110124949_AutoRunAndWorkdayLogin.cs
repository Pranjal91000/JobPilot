using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AutoRunAndWorkdayLogin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_application_session_application_id",
                schema: "jobpilot",
                table: "application_session");

            migrationBuilder.DropIndex(
                name: "ix_application_session_session_status",
                schema: "jobpilot",
                table: "application_session");

            migrationBuilder.AddColumn<string>(
                name: "run_message",
                schema: "jobpilot",
                table: "application_session",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "run_state",
                schema: "jobpilot",
                table: "application_session",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "workday_credential",
                schema: "jobpilot",
                columns: table => new
                {
                    credential_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    password_enc = table.Column<string>(type: "text", nullable: false),
                    account_exists = table.Column<bool>(type: "boolean", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workday_credential", x => x.credential_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_application_session_application_id_session_status",
                schema: "jobpilot",
                table: "application_session",
                columns: new[] { "application_id", "session_status" });

            migrationBuilder.CreateIndex(
                name: "ix_workday_credential_user_id_tenant_key",
                schema: "jobpilot",
                table: "workday_credential",
                columns: new[] { "user_id", "tenant_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "workday_credential",
                schema: "jobpilot");

            migrationBuilder.DropIndex(
                name: "ix_application_session_application_id_session_status",
                schema: "jobpilot",
                table: "application_session");

            migrationBuilder.DropColumn(
                name: "run_message",
                schema: "jobpilot",
                table: "application_session");

            migrationBuilder.DropColumn(
                name: "run_state",
                schema: "jobpilot",
                table: "application_session");

            migrationBuilder.CreateIndex(
                name: "ix_application_session_application_id",
                schema: "jobpilot",
                table: "application_session",
                column: "application_id");

            migrationBuilder.CreateIndex(
                name: "ix_application_session_session_status",
                schema: "jobpilot",
                table: "application_session",
                column: "session_status",
                unique: true,
                filter: "session_status IN ('Pending','Launching','AwaitingAuthentication','AwaitingUser','Inspecting','Mapping','Filling','Navigating','WatchingForConfirmation','Paused')");
        }
    }
}
