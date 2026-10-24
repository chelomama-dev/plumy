using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plumy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceExtraction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExtractedAt",
                table: "Invoices",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExtractedData_Currency",
                table: "Invoices",
                type: "char(3)",
                unicode: false,
                fixedLength: true,
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExtractedData_Discriminator",
                table: "Invoices",
                type: "varchar(50)",
                unicode: false,
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExtractedData_InvoiceNumber",
                table: "Invoices",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ExtractedData_IssueDate",
                table: "Invoices",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ExtractedData_NetAmount",
                table: "Invoices",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExtractedData_SupplierName",
                table: "Invoices",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExtractedData_SupplierTaxId",
                table: "Invoices",
                type: "varchar(20)",
                unicode: false,
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ExtractedData_TaxAmount",
                table: "Invoices",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ExtractedData_TotalAmount",
                table: "Invoices",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExtractionError",
                table: "Invoices",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExtractionStatus",
                table: "Invoices",
                type: "varchar(20)",
                unicode: false,
                maxLength: 20,
                nullable: false,
                // Invoices uploaded before this migration haven't been read either. Only for existing rows: new ones
                // always get their status from the app
                defaultValue: "Pending");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExtractedAt",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "ExtractedData_Currency",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "ExtractedData_Discriminator",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "ExtractedData_InvoiceNumber",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "ExtractedData_IssueDate",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "ExtractedData_NetAmount",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "ExtractedData_SupplierName",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "ExtractedData_SupplierTaxId",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "ExtractedData_TaxAmount",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "ExtractedData_TotalAmount",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "ExtractionError",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "ExtractionStatus",
                table: "Invoices");
        }
    }
}
