using CatvIntegrationHub.Application.Cards;
using CatvIntegrationHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CatvIntegrationHub.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<CasHubDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString("CasHub");
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new MissingDatabaseConfigurationException();
            options.UseSqlServer(connectionString, sql => sql.CommandTimeout(30));
        });
        services.AddScoped<ISvCardReader, SvCardReader>();
        services.AddScoped<ISvCardOperationRecorder, SvCardOperationRecorder>();
        return services;
    }
}
