using System.Configuration;
using System.Data.Common;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Project.Application;
using Project.Domain.Ports;
using Project.Infrastructure;

namespace Project.Presentation;

public partial class App : System.Windows.Application
{
    private ServiceProvider? _serviceProvider;
    private IServiceScope? _serviceScope;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        ConfigureServices(services);

        _serviceProvider = services.BuildServiceProvider();
        _serviceScope = _serviceProvider.CreateScope();

        var dbContext = _serviceScope.ServiceProvider.GetRequiredService<SpecDbContext>();
        dbContext.Database.EnsureCreated();

        var mainWindow = _serviceScope.ServiceProvider.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceScope?.Dispose();
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        var connectionString = GetConnectionString();
        services.AddSpecificationInfrastructure(connectionString, "11.4.5");
        services.AddScoped<SpecificationService>();
        services.AddScoped<ISpecificationService>(provider => provider.GetRequiredService<SpecificationService>());
        services.AddScoped<ISpecValueReader>(provider => provider.GetRequiredService<SpecificationService>());
        services.AddScoped<MainWindow>();
    }

    private static string GetConnectionString()
    {
        var configuredOverride = Environment.GetEnvironmentVariable("SPEC_DB_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(configuredOverride))
        {
            return configuredOverride;
        }

        var configuredBase = ConfigurationManager.ConnectionStrings["SpecificationDatabase"]?.ConnectionString;
        if (string.IsNullOrWhiteSpace(configuredBase))
        {
            throw new InvalidOperationException("SpecificationDatabase connection string is not configured.");
        }

        var passwordEnvVarName = ConfigurationManager.AppSettings["SpecDbPasswordEnvVar"] ?? "SPEC_DB_PASSWORD";
        var password = Environment.GetEnvironmentVariable(passwordEnvVarName);

        if (string.IsNullOrWhiteSpace(password))
        {
            return configuredBase;
        }

        var builder = new DbConnectionStringBuilder
        {
            ConnectionString = configuredBase
        };

        builder["Password"] = password;
        return builder.ConnectionString;
    }
}
