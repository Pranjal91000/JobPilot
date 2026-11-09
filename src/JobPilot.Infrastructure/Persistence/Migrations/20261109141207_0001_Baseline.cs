using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class _0001_Baseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "jobpilot");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:citext", ",,")
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.CreateTable(
                name: "ai_interaction_log",
                schema: "jobpilot",
                columns: table => new
                {
                    interaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    prompt_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    purpose = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    request_redacted = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    response_raw = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    schema_valid = table.Column<bool>(type: "boolean", nullable: false),
                    input_tokens = table.Column<int>(type: "integer", nullable: false),
                    output_tokens = table.Column<int>(type: "integer", nullable: false),
                    latency_ms = table.Column<int>(type: "integer", nullable: false),
                    retry_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_interaction_log", x => x.interaction_id);
                });

            migrationBuilder.CreateTable(
                name: "app_user",
                schema: "jobpilot",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    email_login = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_app_user", x => x.user_id);
                });

            migrationBuilder.CreateTable(
                name: "ats_tenant",
                schema: "jobpilot",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ats_key = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    tenant_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    host = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    display_company_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ats_tenant", x => x.tenant_id);
                });

            migrationBuilder.CreateTable(
                name: "provider_setting",
                schema: "jobpilot",
                columns: table => new
                {
                    setting_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    api_key_enc = table.Column<byte[]>(type: "bytea", nullable: true),
                    default_model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_provider_setting", x => x.setting_id);
                });

            migrationBuilder.CreateTable(
                name: "worker_heartbeat",
                schema: "jobpilot",
                columns: table => new
                {
                    worker_instance_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_heartbeat_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_worker_heartbeat", x => x.worker_instance_id);
                });

            migrationBuilder.CreateTable(
                name: "achievement",
                schema: "jobpilot",
                columns: table => new
                {
                    achievement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    achieved_on = table.Column<DateOnly>(type: "date", nullable: true),
                    verification_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_achievement", x => x.achievement_id);
                    table.ForeignKey(
                        name: "fk_achievement_app_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "jobpilot",
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "certification",
                schema: "jobpilot",
                columns: table => new
                {
                    certification_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    issuer = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    credential_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    issued_on = table.Column<DateOnly>(type: "date", nullable: true),
                    expires_on = table.Column<DateOnly>(type: "date", nullable: true),
                    credential_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    verification_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_certification", x => x.certification_id);
                    table.ForeignKey(
                        name: "fk_certification_app_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "jobpilot",
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "location_preference",
                schema: "jobpilot",
                columns: table => new
                {
                    preference_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_text = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    country_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    is_relocation_ok = table.Column<bool>(type: "boolean", nullable: false),
                    remote_preference = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    verification_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_location_preference", x => x.preference_id);
                    table.ForeignKey(
                        name: "fk_location_preference_app_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "jobpilot",
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "professional_link",
                schema: "jobpilot",
                columns: table => new
                {
                    link_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    link_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    verification_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_professional_link", x => x.link_id);
                    table.ForeignKey(
                        name: "fk_professional_link_app_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "jobpilot",
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "project",
                schema: "jobpilot",
                columns: table => new
                {
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    role = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    technologies = table.Column<string[]>(type: "text[]", nullable: false),
                    url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    verification_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project", x => x.project_id);
                    table.ForeignKey(
                        name: "fk_project_app_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "jobpilot",
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "resume",
                schema: "jobpilot",
                columns: table => new
                {
                    resume_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    storage_reference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    resume_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_resume", x => x.resume_id);
                    table.ForeignKey(
                        name: "fk_resume_app_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "jobpilot",
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "saved_answer",
                schema: "jobpilot",
                columns: table => new
                {
                    saved_answer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    canonical_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    question_text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    question_normalized = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    answer_value = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    answer_value_enc = table.Column<byte[]>(type: "bytea", nullable: true),
                    answer_option_text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    scope = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    scope_ref = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    is_sensitive = table.Column<bool>(type: "boolean", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    use_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_saved_answer", x => x.saved_answer_id);
                    table.ForeignKey(
                        name: "fk_saved_answer_app_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "jobpilot",
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "skill",
                schema: "jobpilot",
                columns: table => new
                {
                    skill_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    skill_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    proficiency_level = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    years_of_experience = table.Column<decimal>(type: "numeric(4,1)", precision: 4, scale: 1, nullable: true),
                    verification_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_skill", x => x.skill_id);
                    table.ForeignKey(
                        name: "fk_skill_app_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "jobpilot",
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_profile",
                schema: "jobpilot",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    middle_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    preferred_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    phone_e164 = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    phone_country_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    address_line1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    address_line2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    state_region = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    postal_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    country_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    professional_summary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    current_location = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    notice_period_days = table.Column<int>(type: "integer", nullable: true),
                    expected_salary_enc = table.Column<byte[]>(type: "bytea", nullable: true),
                    expected_salary_currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    total_experience_months = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_profile", x => x.user_id);
                    table.ForeignKey(
                        name: "fk_user_profile_app_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "jobpilot",
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_setting",
                schema: "jobpilot",
                columns: table => new
                {
                    setting_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    setting_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    setting_value = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_setting", x => x.setting_id);
                    table.ForeignKey(
                        name: "fk_user_setting_app_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "jobpilot",
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "work_authorization",
                schema: "jobpilot",
                columns: table => new
                {
                    authorization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    country_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    status_description = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    requires_sponsorship = table.Column<bool>(type: "boolean", nullable: true),
                    valid_until = table.Column<DateOnly>(type: "date", nullable: true),
                    verification_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_authorization", x => x.authorization_id);
                    table.ForeignKey(
                        name: "fk_work_authorization_app_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "jobpilot",
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "job_post",
                schema: "jobpilot",
                columns: table => new
                {
                    job_post_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    external_job_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    site_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    locale = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    normalized_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    company_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    job_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    job_description = table.Column<string>(type: "text", nullable: true),
                    location = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    employment_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    experience_requirement = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    salary_information = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    posted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    discovered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    discovered_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    discovered_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    discovered_by = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_closed = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_post", x => x.job_post_id);
                    table.ForeignKey(
                        name: "fk_job_post_ats_tenant_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "jobpilot",
                        principalTable: "ats_tenant",
                        principalColumn: "tenant_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "source_document",
                schema: "jobpilot",
                columns: table => new
                {
                    source_document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    resume_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    parsed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    parser_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_source_document", x => x.source_document_id);
                    table.ForeignKey(
                        name: "fk_source_document_app_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "jobpilot",
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_source_document_resume_resume_id",
                        column: x => x.resume_id,
                        principalSchema: "jobpilot",
                        principalTable: "resume",
                        principalColumn: "resume_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "job_application",
                schema: "jobpilot",
                columns: table => new
                {
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_post_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    resume_id = table.Column<Guid>(type: "uuid", nullable: true),
                    application_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    review_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    applied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_activity_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    application_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    completion_percentage = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    submission_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    submission_confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    submission_confirmation_source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_application", x => x.application_id);
                    table.CheckConstraint("ck_completion", "completion_percentage BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "fk_job_application_app_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "jobpilot",
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_job_application_job_posts_job_post_id",
                        column: x => x.job_post_id,
                        principalSchema: "jobpilot",
                        principalTable: "job_post",
                        principalColumn: "job_post_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_job_application_resumes_resume_id",
                        column: x => x.resume_id,
                        principalSchema: "jobpilot",
                        principalTable: "resume",
                        principalColumn: "resume_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "job_post_snapshot",
                schema: "jobpilot",
                columns: table => new
                {
                    snapshot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_post_id = table.Column<Guid>(type: "uuid", nullable: false),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    payload = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_post_snapshot", x => x.snapshot_id);
                    table.ForeignKey(
                        name: "fk_job_post_snapshot_job_post_job_post_id",
                        column: x => x.job_post_id,
                        principalSchema: "jobpilot",
                        principalTable: "job_post",
                        principalColumn: "job_post_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "education",
                schema: "jobpilot",
                columns: table => new
                {
                    education_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    institution = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    degree = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    specialization = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    grade = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    country_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    verification_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    education_source_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_education", x => x.education_id);
                    table.ForeignKey(
                        name: "fk_education_app_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "jobpilot",
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_education_source_documents_source_document_id",
                        column: x => x.education_source_document_id,
                        principalSchema: "jobpilot",
                        principalTable: "source_document",
                        principalColumn: "source_document_id");
                });

            migrationBuilder.CreateTable(
                name: "employment_history",
                schema: "jobpilot",
                columns: table => new
                {
                    employment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    job_title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    employment_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    location = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    is_current = table.Column<bool>(type: "boolean", nullable: false),
                    responsibilities = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    technologies = table.Column<string[]>(type: "text[]", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    verification_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    employment_history_source_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employment_history", x => x.employment_id);
                    table.CheckConstraint("ck_employment_current_or_end_date", "is_current = true OR end_date IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_employment_history_app_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "jobpilot",
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_employment_history_source_documents_source_document_id",
                        column: x => x.employment_history_source_document_id,
                        principalSchema: "jobpilot",
                        principalTable: "source_document",
                        principalColumn: "source_document_id");
                });

            migrationBuilder.CreateTable(
                name: "profile_field_verification",
                schema: "jobpilot",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_path = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    verification_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_profile_field_verification", x => new { x.user_id, x.profile_path });
                    table.ForeignKey(
                        name: "fk_profile_field_verification_app_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "jobpilot",
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_profile_field_verification_source_documents_source_document",
                        column: x => x.source_document_id,
                        principalSchema: "jobpilot",
                        principalTable: "source_document",
                        principalColumn: "source_document_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "application_session",
                schema: "jobpilot",
                columns: table => new
                {
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    browser_session_reference = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    current_step_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_activity_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    failure_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    worker_instance_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_application_session", x => x.session_id);
                    table.ForeignKey(
                        name: "fk_application_session_job_application_application_id",
                        column: x => x.application_id,
                        principalSchema: "jobpilot",
                        principalTable: "job_application",
                        principalColumn: "application_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "job_application_status_history",
                schema: "jobpilot",
                columns: table => new
                {
                    history_id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    new_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    change_source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    session_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_application_status_history", x => x.history_id);
                    table.ForeignKey(
                        name: "fk_job_application_status_history_job_application_application_",
                        column: x => x.application_id,
                        principalSchema: "jobpilot",
                        principalTable: "job_application",
                        principalColumn: "application_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "application_field",
                schema: "jobpilot",
                columns: table => new
                {
                    field_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    field_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    field_identifier = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    field_label = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    field_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    is_required = table.Column<bool>(type: "boolean", nullable: false),
                    available_options = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    detected_context = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    page_section = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    step_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    repeat_group_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    repeat_index = table.Column<int>(type: "integer", nullable: true),
                    locator_recipe = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_present = table.Column<bool>(type: "boolean", nullable: false),
                    page_value_text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    fill_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    fill_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_application_field", x => x.field_id);
                    table.ForeignKey(
                        name: "fk_application_field_application_session_session_id",
                        column: x => x.session_id,
                        principalSchema: "jobpilot",
                        principalTable: "application_session",
                        principalColumn: "session_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "automation_command",
                schema: "jobpilot",
                columns: table => new
                {
                    command_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    command_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    payload = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    outcome = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    attempt = table.Column<int>(type: "integer", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    enqueued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_automation_command", x => x.command_id);
                    table.ForeignKey(
                        name: "fk_automation_command_application_session_session_id",
                        column: x => x.session_id,
                        principalSchema: "jobpilot",
                        principalTable: "application_session",
                        principalColumn: "session_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "automation_event",
                schema: "jobpilot",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    command_id = table.Column<Guid>(type: "uuid", nullable: true),
                    event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    event_description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    event_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    event_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    error_details = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    field_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_automation_event", x => x.event_id);
                    table.ForeignKey(
                        name: "fk_automation_event_application_session_session_id",
                        column: x => x.session_id,
                        principalSchema: "jobpilot",
                        principalTable: "application_session",
                        principalColumn: "session_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "submission_approval",
                schema: "jobpilot",
                columns: table => new
                {
                    approval_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    field_values_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    validation_report_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    invalidated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_submission_approval", x => x.approval_id);
                    table.ForeignKey(
                        name: "fk_submission_approval_application_session_session_id",
                        column: x => x.session_id,
                        principalSchema: "jobpilot",
                        principalTable: "application_session",
                        principalColumn: "session_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "application_answer",
                schema: "jobpilot",
                columns: table => new
                {
                    answer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    field_id = table.Column<Guid>(type: "uuid", nullable: true),
                    question = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    answer_value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    answer_value_enc = table.Column<byte[]>(type: "bytea", nullable: true),
                    answer_source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    is_user_approved = table.Column<bool>(type: "boolean", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_application_answer", x => x.answer_id);
                    table.ForeignKey(
                        name: "fk_application_answer_application_field_field_id",
                        column: x => x.field_id,
                        principalSchema: "jobpilot",
                        principalTable: "application_field",
                        principalColumn: "field_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_application_answer_job_application_application_id",
                        column: x => x.application_id,
                        principalSchema: "jobpilot",
                        principalTable: "job_application",
                        principalColumn: "application_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "field_mapping",
                schema: "jobpilot",
                columns: table => new
                {
                    mapping_id = table.Column<Guid>(type: "uuid", nullable: false),
                    field_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_field_reference = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    saved_answer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    suggested_value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    suggested_option_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    confidence_score = table.Column<decimal>(type: "numeric(4,3)", precision: 4, scale: 3, nullable: false),
                    confidence_breakdown = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    mapping_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    mapping_method = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    mapping_source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    requires_intervention = table.Column<bool>(type: "boolean", nullable: false),
                    is_sensitive = table.Column<bool>(type: "boolean", nullable: false),
                    is_user_verified = table.Column<bool>(type: "boolean", nullable: false),
                    is_current = table.Column<bool>(type: "boolean", nullable: false),
                    ai_interaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_field_mapping", x => x.mapping_id);
                    table.ForeignKey(
                        name: "fk_field_mapping_ai_interaction_log_ai_interaction_id",
                        column: x => x.ai_interaction_id,
                        principalSchema: "jobpilot",
                        principalTable: "ai_interaction_log",
                        principalColumn: "interaction_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_field_mapping_application_field_field_id",
                        column: x => x.field_id,
                        principalSchema: "jobpilot",
                        principalTable: "application_field",
                        principalColumn: "field_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_field_mapping_saved_answers_saved_answer_id",
                        column: x => x.saved_answer_id,
                        principalSchema: "jobpilot",
                        principalTable: "saved_answer",
                        principalColumn: "saved_answer_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_achievement_user_id",
                schema: "jobpilot",
                table: "achievement",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_interaction_log_created_at",
                schema: "jobpilot",
                table: "ai_interaction_log",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_app_user_email_login",
                schema: "jobpilot",
                table: "app_user",
                column: "email_login",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_application_answer_application_id",
                schema: "jobpilot",
                table: "application_answer",
                column: "application_id");

            migrationBuilder.CreateIndex(
                name: "ix_application_answer_field_id",
                schema: "jobpilot",
                table: "application_answer",
                column: "field_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_application_field_session_id_field_fingerprint",
                schema: "jobpilot",
                table: "application_field",
                columns: new[] { "session_id", "field_fingerprint" },
                unique: true);

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

            migrationBuilder.CreateIndex(
                name: "ix_ats_tenant_ats_key_tenant_key",
                schema: "jobpilot",
                table: "ats_tenant",
                columns: new[] { "ats_key", "tenant_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_automation_command_idempotency_key",
                schema: "jobpilot",
                table: "automation_command",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_automation_command_session_id",
                schema: "jobpilot",
                table: "automation_command",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_automation_command_status_enqueued_at",
                schema: "jobpilot",
                table: "automation_command",
                columns: new[] { "status", "enqueued_at" });

            migrationBuilder.CreateIndex(
                name: "ix_automation_event_session_id_event_timestamp",
                schema: "jobpilot",
                table: "automation_event",
                columns: new[] { "session_id", "event_timestamp" });

            migrationBuilder.CreateIndex(
                name: "ix_certification_user_id",
                schema: "jobpilot",
                table: "certification",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_education_source_document_id",
                schema: "jobpilot",
                table: "education",
                column: "education_source_document_id");

            migrationBuilder.CreateIndex(
                name: "ix_education_user_id",
                schema: "jobpilot",
                table: "education",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_employment_history_source_document_id",
                schema: "jobpilot",
                table: "employment_history",
                column: "employment_history_source_document_id");

            migrationBuilder.CreateIndex(
                name: "ix_employment_history_user_id",
                schema: "jobpilot",
                table: "employment_history",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_field_mapping_ai_interaction_id",
                schema: "jobpilot",
                table: "field_mapping",
                column: "ai_interaction_id");

            migrationBuilder.CreateIndex(
                name: "ix_field_mapping_field_id",
                schema: "jobpilot",
                table: "field_mapping",
                column: "field_id",
                unique: true,
                filter: "is_current = true");

            migrationBuilder.CreateIndex(
                name: "ix_field_mapping_saved_answer_id",
                schema: "jobpilot",
                table: "field_mapping",
                column: "saved_answer_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_application_job_post_id",
                schema: "jobpilot",
                table: "job_application",
                column: "job_post_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_application_resume_id",
                schema: "jobpilot",
                table: "job_application",
                column: "resume_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_application_user_id_application_status",
                schema: "jobpilot",
                table: "job_application",
                columns: new[] { "user_id", "application_status" });

            migrationBuilder.CreateIndex(
                name: "ix_job_application_user_id_job_post_id",
                schema: "jobpilot",
                table: "job_application",
                columns: new[] { "user_id", "job_post_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_job_application_user_id_last_activity_at",
                schema: "jobpilot",
                table: "job_application",
                columns: new[] { "user_id", "last_activity_at" });

            migrationBuilder.CreateIndex(
                name: "ix_job_application_status_history_application_id_changed_at",
                schema: "jobpilot",
                table: "job_application_status_history",
                columns: new[] { "application_id", "changed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_job_post_normalized_url",
                schema: "jobpilot",
                table: "job_post",
                column: "normalized_url",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_job_post_tenant_id_external_job_id",
                schema: "jobpilot",
                table: "job_post",
                columns: new[] { "tenant_id", "external_job_id" },
                unique: true,
                filter: "external_job_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_job_post_snapshot_job_post_id",
                schema: "jobpilot",
                table: "job_post_snapshot",
                column: "job_post_id");

            migrationBuilder.CreateIndex(
                name: "ix_location_preference_user_id",
                schema: "jobpilot",
                table: "location_preference",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_professional_link_user_id_link_type",
                schema: "jobpilot",
                table: "professional_link",
                columns: new[] { "user_id", "link_type" },
                unique: true,
                filter: "link_type != 'Other'");

            migrationBuilder.CreateIndex(
                name: "ix_profile_field_verification_source_document_id",
                schema: "jobpilot",
                table: "profile_field_verification",
                column: "source_document_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_user_id",
                schema: "jobpilot",
                table: "project",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_provider_setting_provider_name",
                schema: "jobpilot",
                table: "provider_setting",
                column: "provider_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_resume_user_id",
                schema: "jobpilot",
                table: "resume",
                column: "user_id",
                unique: true,
                filter: "is_default = true AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_saved_answer_question_normalized",
                schema: "jobpilot",
                table: "saved_answer",
                column: "question_normalized");

            migrationBuilder.CreateIndex(
                name: "ix_saved_answer_user_id",
                schema: "jobpilot",
                table: "saved_answer",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_skill_user_id_normalized_name",
                schema: "jobpilot",
                table: "skill",
                columns: new[] { "user_id", "normalized_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_source_document_resume_id",
                schema: "jobpilot",
                table: "source_document",
                column: "resume_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_source_document_user_id",
                schema: "jobpilot",
                table: "source_document",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_submission_approval_session_id",
                schema: "jobpilot",
                table: "submission_approval",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_setting_user_id_setting_key",
                schema: "jobpilot",
                table: "user_setting",
                columns: new[] { "user_id", "setting_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_work_authorization_user_id_country_code",
                schema: "jobpilot",
                table: "work_authorization",
                columns: new[] { "user_id", "country_code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "achievement",
                schema: "jobpilot");

            migrationBuilder.DropTable(
                name: "application_answer",
                schema: "jobpilot");

            migrationBuilder.DropTable(
                name: "automation_command",
                schema: "jobpilot");

            migrationBuilder.DropTable(
                name: "automation_event",
                schema: "jobpilot");

            migrationBuilder.DropTable(
                name: "certification",
                schema: "jobpilot");

            migrationBuilder.DropTable(
                name: "education",
                schema: "jobpilot");

            migrationBuilder.DropTable(
                name: "employment_history",
                schema: "jobpilot");

            migrationBuilder.DropTable(
                name: "field_mapping",
                schema: "jobpilot");

            migrationBuilder.DropTable(
                name: "job_application_status_history",
                schema: "jobpilot");

            migrationBuilder.DropTable(
                name: "job_post_snapshot",
                schema: "jobpilot");

            migrationBuilder.DropTable(
                name: "location_preference",
                schema: "jobpilot");

            migrationBuilder.DropTable(
                name: "professional_link",
                schema: "jobpilot");

            migrationBuilder.DropTable(
                name: "profile_field_verification",
                schema: "jobpilot");

            migrationBuilder.DropTable(
                name: "project",
                schema: "jobpilot");

            migrationBuilder.DropTable(
                name: "provider_setting",
                schema: "jobpilot");

            migrationBuilder.DropTable(
                name: "skill",
                schema: "jobpilot");

            migrationBuilder.DropTable(
                name: "submission_approval",
                schema: "jobpilot");

            migrationBuilder.DropTable(
                name: "user_profile",
                schema: "jobpilot");

            migrationBuilder.DropTable(
                name: "user_setting",
                schema: "jobpilot");

            migrationBuilder.DropTable(
                name: "work_authorization",
                schema: "jobpilot");

            migrationBuilder.DropTable(
                name: "worker_heartbeat",
                schema: "jobpilot");

            migrationBuilder.DropTable(
                name: "ai_interaction_log",
                schema: "jobpilot");

            migrationBuilder.DropTable(
                name: "application_field",
                schema: "jobpilot");

            migrationBuilder.DropTable(
                name: "saved_answer",
                schema: "jobpilot");

            migrationBuilder.DropTable(
                name: "source_document",
                schema: "jobpilot");

            migrationBuilder.DropTable(
                name: "application_session",
                schema: "jobpilot");

            migrationBuilder.DropTable(
                name: "job_application",
                schema: "jobpilot");

            migrationBuilder.DropTable(
                name: "job_post",
                schema: "jobpilot");

            migrationBuilder.DropTable(
                name: "resume",
                schema: "jobpilot");

            migrationBuilder.DropTable(
                name: "ats_tenant",
                schema: "jobpilot");

            migrationBuilder.DropTable(
                name: "app_user",
                schema: "jobpilot");
        }
    }
}
