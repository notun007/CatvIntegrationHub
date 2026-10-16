using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CatvIntegrationHub.Infrastructure.Persistence.Migrations
{
    public partial class AddPacketHexToSvCards : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RequestHex",
                schema: "dbo",
                table: "SvCards",
                type: "varchar(max)",
                unicode: false,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResponseHex",
                schema: "dbo",
                table: "SvCards",
                type: "varchar(max)",
                unicode: false,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequestHex",
                schema: "dbo",
                table: "SvCards");

            migrationBuilder.DropColumn(
                name: "ResponseHex",
                schema: "dbo",
                table: "SvCards");
        }
    }
}
