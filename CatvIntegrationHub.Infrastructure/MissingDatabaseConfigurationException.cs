namespace CatvIntegrationHub.Infrastructure;

public sealed class MissingDatabaseConfigurationException : InvalidOperationException
{
    public MissingDatabaseConfigurationException()
        : base("Configure ConnectionStrings:CasHub using user secrets or ConnectionStrings__CasHub.") { }
}