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
        const string connectionString = "Server=localhost;Port=3306;Database=closure_table_specs;User=root;Password=;";

        services.AddSpecificationInfrastructure(connectionString, "11.4.5");
        services.AddScoped<SpecificationService>();
        services.AddScoped<ISpecificationService>(provider => provider.GetRequiredService<SpecificationService>());
        services.AddScoped<ISpecValueReader>(provider => provider.GetRequiredService<SpecificationService>());
        services.AddScoped<MainWindow>();
    }
}
