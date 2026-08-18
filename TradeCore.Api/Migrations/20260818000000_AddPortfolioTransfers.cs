using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradeCore.Api.Migrations;

public partial class AddPortfolioTransfers : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PortfolioTransfers",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                StockId = table.Column<Guid>(type: "uuid", nullable: false),
                Quantity = table.Column<int>(type: "integer", nullable: false),
                AveragePrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PortfolioTransfers", x => x.Id);
                table.ForeignKey("FK_PortfolioTransfers_Accounts_AccountId", x => x.AccountId, "Accounts", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_PortfolioTransfers_Stocks_StockId", x => x.StockId, "Stocks", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(name: "IX_PortfolioTransfers_AccountId", table: "PortfolioTransfers", column: "AccountId");
        migrationBuilder.CreateIndex(name: "IX_PortfolioTransfers_StockId", table: "PortfolioTransfers", column: "StockId");
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable(name: "PortfolioTransfers");
}
