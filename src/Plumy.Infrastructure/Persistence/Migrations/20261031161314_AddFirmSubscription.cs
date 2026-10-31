using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plumy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFirmSubscription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Plan",
                table: "Firms",
                type: "varchar(20)",
                unicode: false,
                maxLength: 20,
                nullable: false,
                defaultValue: "Trial");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SubscriptionPeriodEnd",
                table: "Firms",
                type: "datetimeoffset",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SubscriptionPeriodStart",
                table: "Firms",
                type: "datetimeoffset",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "SubscriptionStatus",
                table: "Firms",
                type: "varchar(20)",
                unicode: false,
                maxLength: 20,
                nullable: false,
                defaultValue: "Trialing");

            // The firms that existed before plans get the same trial as a new one, starting now
            migrationBuilder.Sql("""
                UPDATE Firms
                SET SubscriptionPeriodStart = SYSUTCDATETIME(),
                    SubscriptionPeriodEnd = DATEADD(day, 14, SYSUTCDATETIME());
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Plan",
                table: "Firms");

            migrationBuilder.DropColumn(
                name: "SubscriptionPeriodEnd",
                table: "Firms");

            migrationBuilder.DropColumn(
                name: "SubscriptionPeriodStart",
                table: "Firms");

            migrationBuilder.DropColumn(
                name: "SubscriptionStatus",
                table: "Firms");
        }
    }
}
