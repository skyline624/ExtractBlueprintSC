using ExtractBlueprintSC.Core.Interfaces;
using ExtractBlueprintSC.Infrastructure.Configuration;
using ExtractBlueprintSC.Infrastructure.Exporters;
using ExtractBlueprintSC.Infrastructure.StarBreaker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ExtractBlueprintSC.Infrastructure.Extensions;

public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<ExtractionOptions>(options =>
            configuration.GetSection(ExtractionOptions.SectionName).Bind(options));

        services.AddTransient<IDataExporter, JsonExporter>();
        services.AddTransient<StarBreakerExtractor>();

        return services;
    }
}
