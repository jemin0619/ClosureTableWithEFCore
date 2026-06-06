using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Project.Domain.Ports;

namespace Project.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddSpecificationInfrastructure(
        this IServiceCollection services,
        string connectionString,
        string mariaDbVersion = "11.4.0")
    {
        var serverVersion = ServerVersion.Parse(mariaDbVersion);

        services.AddDbContext<SpecDbContext>(options =>
            options.UseMySql(connectionString, serverVersion));

        services.AddScoped<SpecificationTreeMapper>();
        services.AddScoped<ISpecificationRepository, EfSpecificationRepository>();

        return services;
    }
}
