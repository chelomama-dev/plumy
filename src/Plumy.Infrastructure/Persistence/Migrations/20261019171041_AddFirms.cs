using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plumy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFirms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Companies created before multi-tenancy were test data with no firm to own them.
            // Removing them lets FirmId be required with a real foreign key instead of a placeholder firm
            migrationBuilder.Sql("DELETE FROM [ClientCompanies];");

            migrationBuilder.DropIndex(
                name: "IX_ClientCompanies_TaxId",
                table: "ClientCompanies");

            migrationBuilder.AddColumn<Guid>(
                name: "FirmId",
                table: "ClientCompanies",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "Firms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Firms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FirmUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FirmId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FirmUsers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FirmUsers_Firms_FirmId",
                        column: x => x.FirmId,
                        principalTable: "Firms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClientCompanies_FirmId_TaxId",
                table: "ClientCompanies",
                columns: new[] { "FirmId", "TaxId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FirmUsers_FirmId",
                table: "FirmUsers",
                column: "FirmId");

            migrationBuilder.CreateIndex(
                name: "IX_FirmUsers_ObjectId",
                table: "FirmUsers",
                column: "ObjectId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ClientCompanies_Firms_FirmId",
                table: "ClientCompanies",
                column: "FirmId",
                principalTable: "Firms",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ClientCompanies_Firms_FirmId",
                table: "ClientCompanies");

            migrationBuilder.DropTable(
                name: "FirmUsers");

            migrationBuilder.DropTable(
                name: "Firms");

            migrationBuilder.DropIndex(
                name: "IX_ClientCompanies_FirmId_TaxId",
                table: "ClientCompanies");

            migrationBuilder.DropColumn(
                name: "FirmId",
                table: "ClientCompanies");

            migrationBuilder.CreateIndex(
                name: "IX_ClientCompanies_TaxId",
                table: "ClientCompanies",
                column: "TaxId",
                unique: true);
        }
    }
}
