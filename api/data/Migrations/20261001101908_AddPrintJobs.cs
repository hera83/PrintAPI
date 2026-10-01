using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace api.data.Migrations
{
    /// <inheritdoc />
    public partial class AddPrintJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PrintJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PrinterId = table.Column<int>(type: "INTEGER", nullable: false),
                    JobName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    DocumentSizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    PageCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Copies = table.Column<int>(type: "INTEGER", nullable: false),
                    ColorMode = table.Column<string>(type: "TEXT", nullable: true),
                    Sides = table.Column<string>(type: "TEXT", nullable: true),
                    Media = table.Column<string>(type: "TEXT", nullable: true),
                    PrintQuality = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    StatusMessage = table.Column<string>(type: "TEXT", nullable: true),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DocumentFormatSent = table.Column<string>(type: "TEXT", nullable: true),
                    PrinterJobId = table.Column<int>(type: "INTEGER", nullable: true),
                    PrinterJobState = table.Column<string>(type: "TEXT", nullable: true),
                    PrinterJobStateReasons = table.Column<string>(type: "TEXT", nullable: false),
                    PrinterImpressionsCompleted = table.Column<int>(type: "INTEGER", nullable: true),
                    SubmittedByKeyId = table.Column<int>(type: "INTEGER", nullable: true),
                    SubmittedBy = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SentAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FinishedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrintJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrintJobs_HpPrinters_PrinterId",
                        column: x => x.PrinterId,
                        principalTable: "HpPrinters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PrintJobs_CreatedAt",
                table: "PrintJobs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PrintJobs_PrinterId",
                table: "PrintJobs",
                column: "PrinterId");

            migrationBuilder.CreateIndex(
                name: "IX_PrintJobs_Status",
                table: "PrintJobs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_PrintJobs_SubmittedByKeyId",
                table: "PrintJobs",
                column: "SubmittedByKeyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PrintJobs");
        }
    }
}
