using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhaleDeck.Infrastructure.Persistence.Migrations;
    /// <inheritdoc />
    public partial class FileArchiveRegistrations : Migration
    {
        private static readonly string[] RegistrationKeyColumns = ["actor_subject", "login_binding", "idempotency_key"];
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "file_archive_registrations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    login_binding = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    plan_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    canceled_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_file_archive_registrations", x => x.id);
                    table.CheckConstraint("ck_file_archive_registration_hashes", "login_binding ~ '^[A-F0-9]{64}$' AND request_hash ~ '^[A-F0-9]{64}$' AND idempotency_key ~ '^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$'");
                    table.CheckConstraint("ck_file_archive_registration_limits", "id<>'00000000-0000-0000-0000-000000000000'::uuid AND expires_at_utc>created_at_utc AND octet_length(plan_json::text)<=32768");
                });

            migrationBuilder.CreateIndex(
                name: "IX_file_archive_registrations_actor_subject_login_binding_idem~",
                table: "file_archive_registrations",
                columns: RegistrationKeyColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_file_archive_registrations_expires_at_utc",
                table: "file_archive_registrations",
                column: "expires_at_utc");

            // Preserve pre-extension jobs. Their old intent seal is not a
            // registration-domain seal: these rows are linked history only,
            // never an authority to reissue an unconfirmed plan.
            migrationBuilder.Sql("""
                INSERT INTO file_archive_registrations
                  (id,actor_subject,login_binding,idempotency_key,request_hash,plan_json,created_at_utc,expires_at_utc)
                SELECT a.id,a.actor_subject,a.login_binding,j.idempotency_key,a.request_hash,a.plan_json,
                       a.created_at_utc,a.created_at_utc+INTERVAL '5 minutes'
                FROM file_archive_operations a JOIN operation_jobs j ON j.id=a.id;
                CREATE FUNCTION whaledeck_file_archive_registration_guard() RETURNS trigger
                LANGUAGE plpgsql AS $function$
                BEGIN
                    IF ROW(NEW.id,NEW.actor_subject,NEW.login_binding,NEW.idempotency_key,NEW.request_hash,NEW.plan_json,NEW.created_at_utc,NEW.expires_at_utc)
                       IS DISTINCT FROM ROW(OLD.id,OLD.actor_subject,OLD.login_binding,OLD.idempotency_key,OLD.request_hash,OLD.plan_json,OLD.created_at_utc,OLD.expires_at_utc)
                       OR OLD.canceled_at_utc IS NOT NULL AND NEW.canceled_at_utc IS DISTINCT FROM OLD.canceled_at_utc THEN
                        RAISE EXCEPTION 'file_archive_registration_immutable' USING ERRCODE='23514';
                    END IF;
                    RETURN NEW;
                END
                $function$;
                CREATE TRIGGER file_archive_registration_guard BEFORE UPDATE ON file_archive_registrations
                FOR EACH ROW EXECUTE FUNCTION whaledeck_file_archive_registration_guard();
                """);
            migrationBuilder.AddForeignKey(
                name: "FK_file_archive_operations_file_archive_registrations_id",
                table: "file_archive_operations",
                column: "id",
                principalTable: "file_archive_registrations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $guard$ BEGIN
                  IF EXISTS (SELECT 1 FROM file_archive_registrations) THEN
                    RAISE EXCEPTION 'archive_registration_downgrade_requires_empty_table';
                  END IF;
                END $guard$;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_file_archive_operations_file_archive_registrations_id",
                table: "file_archive_operations");

            migrationBuilder.DropTable(
                name: "file_archive_registrations");
            migrationBuilder.Sql("DROP FUNCTION whaledeck_file_archive_registration_guard();");
        }
    }
