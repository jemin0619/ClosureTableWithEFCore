using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Project.Domain.Ports;

namespace Project.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddSpecificationInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<SpecDbContext>(options =>
            options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

        services.AddScoped<SpecificationTreeMapper>();
        services.AddScoped<ISpecificationRepository, EfSpecificationRepository>();

        return services;
    }
}
