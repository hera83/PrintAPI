using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace api.data.Migrations
{
    /// <inheritdoc />
    public partial class SimplifyPrintJobOptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ColorMode",
                table: "PrintJobs");

            migrationBuilder.DropColumn(
                name: "JobName",
                table: "PrintJobs");

            migrationBuilder.DropColumn(
                name: "Media",
                table: "PrintJobs");

            migrationBuilder.DropColumn(
                name: "PrintQuality",
                table: "PrintJobs");

            migrationBuilder.DropColumn(
                name: "Sides",
                table: "PrintJobs");

            migrationBuilder.AddColumn<bool>(
                name: "Color",
                table: "PrintJobs",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Pages",
                table: "PrintJobs",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TwoSided",
                table: "PrintJobs",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Color",
                table: "PrintJobs");

            migrationBuilder.DropColumn(
                name: "Pages",
                table: "PrintJobs");

            migrationBuilder.DropColumn(
                name: "TwoSided",
                table: "PrintJobs");

            migrationBuilder.AddColumn<string>(
                name: "ColorMode",
                table: "PrintJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "JobName",
                table: "PrintJobs",
                type: "TEXT",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Media",
                table: "PrintJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrintQuality",
                table: "PrintJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Sides",
                table: "PrintJobs",
                type: "TEXT",
                nullable: true);
        }
    }
}
