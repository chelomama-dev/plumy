using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plumy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStripeBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StripeCustomerId",
                table: "Firms",
                type: "varchar(255)",
                unicode: false,
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StripeSubscriptionId",
                table: "Firms",
                type: "varchar(255)",
                unicode: false,
                maxLength: 255,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Firms_StripeCustomerId",
                table: "Firms",
                column: "StripeCustomerId",
                unique: true,
                filter: "[StripeCustomerId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Firms_StripeCustomerId",
                table: "Firms");

            migrationBuilder.DropColumn(
                name: "StripeCustomerId",
                table: "Firms");

            migrationBuilder.DropColumn(
                name: "StripeSubscriptionId",
                table: "Firms");
        }
    }
}
