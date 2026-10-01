using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace api.data.Migrations
{
    /// <inheritdoc />
    public partial class AddHpPrinters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HpPrinters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Host = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    IpAddress = table.Column<string>(type: "TEXT", nullable: true),
                    MdnsHostName = table.Column<string>(type: "TEXT", nullable: true),
                    Port = table.Column<int>(type: "INTEGER", nullable: false),
                    ResourcePath = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    PrinterUri = table.Column<string>(type: "TEXT", nullable: false),
                    PrinterName = table.Column<string>(type: "TEXT", nullable: true),
                    Manufacturer = table.Column<string>(type: "TEXT", nullable: true),
                    Model = table.Column<string>(type: "TEXT", nullable: true),
                    MakeAndModel = table.Column<string>(type: "TEXT", nullable: true),
                    SerialNumber = table.Column<string>(type: "TEXT", nullable: true),
                    Uuid = table.Column<string>(type: "TEXT", nullable: true),
                    DeviceId = table.Column<string>(type: "TEXT", nullable: true),
                    FirmwareVersion = table.Column<string>(type: "TEXT", nullable: true),
                    Location = table.Column<string>(type: "TEXT", nullable: true),
                    Info = table.Column<string>(type: "TEXT", nullable: true),
                    AdminUrl = table.Column<string>(type: "TEXT", nullable: true),
                    State = table.Column<string>(type: "TEXT", nullable: true),
                    StateReasons = table.Column<string>(type: "TEXT", nullable: false),
                    StateMessage = table.Column<string>(type: "TEXT", nullable: true),
                    SupportsColor = table.Column<bool>(type: "INTEGER", nullable: true),
                    SupportsDuplex = table.Column<bool>(type: "INTEGER", nullable: true),
                    DocumentFormats = table.Column<string>(type: "TEXT", nullable: false),
                    MediaSupported = table.Column<string>(type: "TEXT", nullable: false),
                    MediaDefault = table.Column<string>(type: "TEXT", nullable: true),
                    Resolutions = table.Column<string>(type: "TEXT", nullable: false),
                    IppVersions = table.Column<string>(type: "TEXT", nullable: false),
                    MdnsServices = table.Column<string>(type: "TEXT", nullable: false),
                    IppAttributesJson = table.Column<string>(type: "TEXT", nullable: false),
                    MdnsTxtRecordsJson = table.Column<string>(type: "TEXT", nullable: false),
                    InfoRetrievedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HpPrinters", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HpPrinters_Host",
                table: "HpPrinters",
                column: "Host");

            migrationBuilder.CreateIndex(
                name: "IX_HpPrinters_Uuid",
                table: "HpPrinters",
                column: "Uuid",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HpPrinters");
        }
    }
}
