using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plumy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAutoApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Invoices_ClientCompanyId_LedgerAccountConfirmedAt",
                table: "Invoices");

            migrationBuilder.AddColumn<bool>(
                name: "ApprovedAutomatically",
                table: "Invoices",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AutoApprovalEnabled",
                table: "Firms",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<double>(
                name: "AutoApprovalMinConfidence",
                table: "Firms",
                type: "float",
                nullable: false,
                defaultValue: 0.94999999999999996);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_ClientCompanyId_LedgerAccountConfirmedAt",
                table: "Invoices",
                columns: new[] { "ClientCompanyId", "LedgerAccountConfirmedAt" },
                filter: "[LedgerAccountCode] IS NOT NULL AND [LedgerAccountConfirmedBy] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Invoices_ClientCompanyId_LedgerAccountConfirmedAt",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "ApprovedAutomatically",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "AutoApprovalEnabled",
                table: "Firms");

            migrationBuilder.DropColumn(
                name: "AutoApprovalMinConfidence",
                table: "Firms");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_ClientCompanyId_LedgerAccountConfirmedAt",
                table: "Invoices",
                columns: new[] { "ClientCompanyId", "LedgerAccountConfirmedAt" },
                filter: "[LedgerAccountCode] IS NOT NULL");
        }
    }
}
