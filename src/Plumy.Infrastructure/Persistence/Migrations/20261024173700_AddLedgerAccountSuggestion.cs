using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plumy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLedgerAccountSuggestion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExtractedData_ItemsDescription",
                table: "Invoices",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LedgerAccountCode",
                table: "Invoices",
                type: "varchar(10)",
                unicode: false,
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LedgerAccountConfirmedAt",
                table: "Invoices",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LedgerAccountConfirmedBy",
                table: "Invoices",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LedgerAccountSuggestion_Code",
                table: "Invoices",
                type: "varchar(10)",
                unicode: false,
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LedgerAccountSuggestion_Reason",
                table: "Invoices",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LedgerAccountSuggestion_Source",
                table: "Invoices",
                type: "varchar(20)",
                unicode: false,
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LedgerAccountSuggestion_SuggestedAt",
                table: "Invoices",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_ClientCompanyId_LedgerAccountConfirmedAt",
                table: "Invoices",
                columns: new[] { "ClientCompanyId", "LedgerAccountConfirmedAt" },
                filter: "[LedgerAccountCode] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Invoices_ClientCompanyId_LedgerAccountConfirmedAt",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "ExtractedData_ItemsDescription",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "LedgerAccountCode",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "LedgerAccountConfirmedAt",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "LedgerAccountConfirmedBy",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "LedgerAccountSuggestion_Code",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "LedgerAccountSuggestion_Reason",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "LedgerAccountSuggestion_Source",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "LedgerAccountSuggestion_SuggestedAt",
                table: "Invoices");
        }
    }
}
