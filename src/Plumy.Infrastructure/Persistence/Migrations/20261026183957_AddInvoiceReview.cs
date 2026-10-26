using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plumy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CorrectedFields",
                table: "Invoices",
                type: "varchar(500)",
                unicode: false,
                maxLength: 500,
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "FieldConfidence",
                table: "Invoices",
                type: "varchar(1000)",
                unicode: false,
                maxLength: 1000,
                nullable: false,
                // Invoices read before this: no confidence was kept
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "Invoices",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewStatus",
                table: "Invoices",
                type: "varchar(20)",
                unicode: false,
                maxLength: 20,
                nullable: false,
                // Nobody has reviewed the invoices that already exist
                defaultValue: "Unreviewed");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReviewedAt",
                table: "Invoices",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReviewedBy",
                table: "Invoices",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CorrectedFields",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "FieldConfidence",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "ReviewStatus",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "ReviewedBy",
                table: "Invoices");
        }
    }
}
