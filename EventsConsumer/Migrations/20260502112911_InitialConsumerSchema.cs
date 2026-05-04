using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventsConsumer.Migrations
{
    /// <inheritdoc />
    public partial class InitialConsumerSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MerchantCategoryEvents",
                columns: table => new
                {
                    MessageId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    MerchantCategoryId = table.Column<int>(type: "integer", nullable: false),
                    MerchantId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MerchantCategoryEvents", x => x.MessageId);
                });

            migrationBuilder.CreateTable(
                name: "ProductEvents",
                columns: table => new
                {
                    MessageId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ProductId = table.Column<int>(type: "integer", nullable: false),
                    MerchantId = table.Column<int>(type: "integer", nullable: false),
                    ProductCategoryId = table.Column<int>(type: "integer", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductEvents", x => x.MessageId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MerchantCategoryEvents");

            migrationBuilder.DropTable(
                name: "ProductEvents");
        }
    }
}
