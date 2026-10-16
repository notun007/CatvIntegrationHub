using CatvIntegrationHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CatvIntegrationHub.Infrastructure.Persistence;

public sealed class CasHubDbContext : DbContext
{
    public CasHubDbContext(DbContextOptions<CasHubDbContext> options) : base(options) { }

    public DbSet<SvCard> SvCards => Set<SvCard>();
    public DbSet<SvCardStatus> SvCardStatuses => Set<SvCardStatus>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CasHubDbContext).Assembly);
    }
}
