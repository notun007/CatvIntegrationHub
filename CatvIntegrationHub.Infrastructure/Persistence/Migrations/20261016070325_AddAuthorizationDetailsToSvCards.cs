using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CatvIntegrationHub.Infrastructure.Persistence.Migrations
{
    public partial class AddAuthorizationDetailsToSvCards : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EndTime",
                schema: "dbo",
                table: "SvCards",
                type: "datetimeoffset(7)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProductId",
                schema: "dbo",
                table: "SvCards",
                type: "varchar(50)",
                unicode: false,
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StartTime",
                schema: "dbo",
                table: "SvCards",
                type: "datetimeoffset(7)",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EndTime",
                schema: "dbo",
                table: "SvCards");

            migrationBuilder.DropColumn(
                name: "ProductId",
                schema: "dbo",
                table: "SvCards");

            migrationBuilder.DropColumn(
                name: "StartTime",
                schema: "dbo",
                table: "SvCards");
        }
    }
}
