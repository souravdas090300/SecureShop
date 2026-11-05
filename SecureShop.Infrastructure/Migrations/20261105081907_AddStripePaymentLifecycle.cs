using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecureShop.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStripePaymentLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastPaymentFailureAt",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastPaymentFailureCode",
                table: "Orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StripeRefundId",
                table: "Orders",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProcessedStripeEvents",
                columns: table => new
                {
                    EventId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedStripeEvents", x => x.EventId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProcessedStripeEvents");

            migrationBuilder.DropColumn(
                name: "LastPaymentFailureAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "LastPaymentFailureCode",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "StripeRefundId",
                table: "Orders");
        }
    }
}
