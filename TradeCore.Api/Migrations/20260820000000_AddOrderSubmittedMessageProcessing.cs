using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradeCore.Api.Migrations;

public partial class AddOrderSubmittedMessageProcessing : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "SubmittedMessageProcessedAt",
            table: "Orders",
            type: "timestamp with time zone",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "SubmittedMessageProcessedAt",
            table: "Orders");
    }
}
