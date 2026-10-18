using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atlas.Onboarding.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "onboarding");

            migrationBuilder.CreateTable(
                name: "AccessLog",
                schema: "onboarding",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ActorType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RecordType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RecordId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RetainUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessLog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Applicants",
                schema: "onboarding",
                columns: table => new
                {
                    ApplicationId = table.Column<string>(type: "varchar(35)", unicode: false, maxLength: 35, nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DateOfBirth = table.Column<DateOnly>(type: "date", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Nationality = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: false),
                    RetainUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IdentifierScheme = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    IdentifierValue = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Applicants", x => x.ApplicationId);
                });

            migrationBuilder.CreateTable(
                name: "Applications",
                schema: "onboarding",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(35)", unicode: false, maxLength: 35, nullable: false),
                    Market = table.Column<string>(type: "varchar(2)", unicode: false, maxLength: 2, nullable: false),
                    SubmittedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RetainUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    RejectionReason = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    IdentityVerificationReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ScreeningCaseId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ComplianceReviewDueAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ComplianceReviewOverdue = table.Column<bool>(type: "bit", nullable: false),
                    ReviewedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BranchVisitDeadline = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    BranchActivationStaffId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BranchActivationBranchCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    BranchActivationSignedFormReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BranchActivationAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CardOrderReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Applications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IdempotencyRecords",
                schema: "onboarding",
                columns: table => new
                {
                    ActorId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RequestSha256 = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ApplicationId = table.Column<string>(type: "varchar(35)", unicode: false, maxLength: 35, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyRecords", x => new { x.ActorId, x.Key });
                });

            migrationBuilder.CreateTable(
                name: "InboxMessages",
                schema: "onboarding",
                columns: table => new
                {
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Consumer = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboxMessages", x => new { x.MessageId, x.Consumer });
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                schema: "onboarding",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Market = table.Column<string>(type: "varchar(2)", unicode: false, maxLength: 2, nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ApplicantDocuments",
                schema: "onboarding",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ContentSha256 = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ApplicationId = table.Column<string>(type: "varchar(35)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicantDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApplicantDocuments_Applicants_ApplicationId",
                        column: x => x.ApplicationId,
                        principalSchema: "onboarding",
                        principalTable: "Applicants",
                        principalColumn: "ApplicationId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ApplicationStatusHistory",
                schema: "onboarding",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    From = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    To = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Actor = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ApplicationId = table.Column<string>(type: "varchar(35)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationStatusHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApplicationStatusHistory_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalSchema: "onboarding",
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccessLog_RecordType_RecordId",
                schema: "onboarding",
                table: "AccessLog",
                columns: new[] { "RecordType", "RecordId" });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicantDocuments_ApplicationId",
                schema: "onboarding",
                table: "ApplicantDocuments",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationStatusHistory_ApplicationId",
                schema: "onboarding",
                table: "ApplicationStatusHistory",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_Applications_NextAttemptAt",
                schema: "onboarding",
                table: "Applications",
                column: "NextAttemptAt",
                filter: "[NextAttemptAt] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_SentAt_OccurredAt",
                schema: "onboarding",
                table: "OutboxMessages",
                columns: new[] { "SentAt", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccessLog",
                schema: "onboarding");

            migrationBuilder.DropTable(
                name: "ApplicantDocuments",
                schema: "onboarding");

            migrationBuilder.DropTable(
                name: "ApplicationStatusHistory",
                schema: "onboarding");

            migrationBuilder.DropTable(
                name: "IdempotencyRecords",
                schema: "onboarding");

            migrationBuilder.DropTable(
                name: "InboxMessages",
                schema: "onboarding");

            migrationBuilder.DropTable(
                name: "OutboxMessages",
                schema: "onboarding");

            migrationBuilder.DropTable(
                name: "Applicants",
                schema: "onboarding");

            migrationBuilder.DropTable(
                name: "Applications",
                schema: "onboarding");
        }
    }
}
