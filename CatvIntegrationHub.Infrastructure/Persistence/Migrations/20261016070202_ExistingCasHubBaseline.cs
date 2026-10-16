using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CatvIntegrationHub.Infrastructure.Persistence.Migrations
{
    /// <summary>Adopts the pre-existing CasHub schema without creating or dropping its tables.</summary>
    public partial class ExistingCasHubBaseline : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SvCards and SvCardStatuses already exist. Keep this baseline non-destructive.
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Existing tables are not owned by this baseline and must not be dropped.
        }
    }
}