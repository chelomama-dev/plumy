using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plumy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClientCompanies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TaxId = table.Column<string>(type: "char(9)", unicode: false, fixedLength: true, maxLength: 9, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientCompanies", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClientCompanies_TaxId",
                table: "ClientCompanies",
                column: "TaxId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClientCompanies");
        }
    }
}
