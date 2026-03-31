using ExtractBlueprintSC.Application.Services;
using ExtractBlueprintSC.Application.UseCases;
using Microsoft.Extensions.DependencyInjection;

namespace ExtractBlueprintSC.Application.Extensions;

public static class ApplicationServiceExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddTransient<ResourceTransformService>();
        services.AddTransient<MissionTransformService>();
        services.AddTransient<BlueprintTransformService>();
        services.AddTransient<ExtractBlueprintsUseCase>();
        return services;
    }
}
