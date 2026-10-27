using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plumy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLedgerAccountSuggestionStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NoSuggestionReason",
                table: "Invoices",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SuggestionStatus",
                table: "Invoices",
                type: "varchar(20)",
                unicode: false,
                maxLength: 20,
                nullable: true);

            // Read invoices so far: suggested if they have one; otherwise nothing was suggested, for a reason not
            // recorded then. Unread ones stay null until they are read
            migrationBuilder.Sql("""
                UPDATE [Invoices]
                SET [SuggestionStatus] = CASE WHEN [LedgerAccountSuggestion_Code] IS NOT NULL THEN 'Suggested' ELSE 'NoSuggestion' END
                WHERE [ExtractionStatus] = 'Extracted';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NoSuggestionReason",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "SuggestionStatus",
                table: "Invoices");
        }
    }
}
