using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CatvIntegrationHub.Infrastructure.Persistence.Migrations
{
    public partial class AddResponseJsonToSvCards : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ResponseJson",
                schema: "dbo",
                table: "SvCards",
                type: "nvarchar(max)",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ResponseJson",
                schema: "dbo",
                table: "SvCards");
        }
    }
}
