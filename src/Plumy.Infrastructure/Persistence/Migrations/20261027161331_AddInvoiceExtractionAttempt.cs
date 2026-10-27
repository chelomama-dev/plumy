using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plumy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceExtractionAttempt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ExtractionAttemptId",
                table: "Invoices",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExtractionQueuedAt",
                table: "Invoices",
                type: "datetimeoffset",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            // Existing invoices: each its own attempt, queued when it was uploaded (their last retry isn't known)
            migrationBuilder.Sql("UPDATE [Invoices] SET [ExtractionAttemptId] = NEWID(), [ExtractionQueuedAt] = [UploadedAt];");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExtractionAttemptId",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "ExtractionQueuedAt",
                table: "Invoices");
        }
    }
}
