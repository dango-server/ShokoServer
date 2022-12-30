﻿#region
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog.Web;
using Shoko.Server.Server;
using Shoko.Server.Settings;
using Shoko.Server.Utilities;
#endregion
namespace Shoko.TrayService;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App
{
    private static TaskbarIcon? _icon;
    private ILogger _logger = null!;
    private static App s_instance = null!;

    private void OnStartup(object a, StartupEventArgs e)
    {
        s_instance = this;
        Console.CancelKeyPress += OnConsoleOnCancelKeyPress;
        InitialiseTaskbarIcon();
        try
        {
            UnhandledExceptionManager.AddHandler();
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }

        Utils.SetInstance();
        Utils.InitLogger();
        // startup DI builds ShokoServer and StartServer, then those build the runtime DI. The startup DI allows logging and other DI handling during startup
        new HostBuilder().UseContentRoot(Directory.GetCurrentDirectory())
            .ConfigureHost()
            .ConfigureApp()
            .ConfigureServiceProvider()
            .UseNLog()
            .ConfigureServices(ConfigureServices)
            .Build()
            // we use Start() instead of Run() to avoid blocking the main thread with a spin/wait
            .Start();
    }

    public static void OnInstanceOnServerShutdown(object? o, EventArgs eventArgs) 
        => s_instance.Shutdown();

    private void InitialiseTaskbarIcon()
    {
#pragma warning disable CA1416
        _icon = new TaskbarIcon{
                                   ToolTipText = "Shoko Server",
                                   ContextMenu = CreateContextMenu(),
                                   MenuActivation = PopupActivationMode.All,
                                   Visibility = Visibility.Visible,
                               };
        using var iconStream = GetResourceStream(new Uri("pack://application:,,,/ShokoServer;component/db.ico"))?.Stream;
        if (iconStream is not null)
            _icon.Icon = new Icon(iconStream);
#pragma warning restore CA1416
    }
    
    private ContextMenu CreateContextMenu()
    {
        var menu = new ContextMenu();
        var webui = new MenuItem{Header = "Open WebUI"};
        webui.Click += OnWebuiOpenWebUIClick;
        menu.Items.Add(webui);
        webui = new MenuItem{Header = "Exit"};
        webui.Click += OnWebuiExit;
        menu.Items.Add(webui);
        return menu;
    }
    
    private void OnWebuiExit(object? sender, RoutedEventArgs args) 
        => Dispatcher.Invoke(Shutdown);

    private void OnWebuiOpenWebUIClick(object? sender, RoutedEventArgs args)
    {
        try
        {
            var settings = Utils.ServiceContainer.GetRequiredService<ISettingsProvider>().GetSettings();
            OpenUrl($"http://localhost:{settings.ServerPort}");
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to Open WebUI: {Ex}", e);
        }
    }

    private void OnConsoleOnCancelKeyPress(object? sender, ConsoleCancelEventArgs args)
        => Dispatcher.Invoke(() =>
                             {
                                 args.Cancel = true;
                                 Shutdown();
                             }
                            );

    protected override void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);
        _logger.LogInformation("Exit was Requested. Shutting Down");
        ShokoService.CancelAndWaitForQueues();
        if (_icon is not null)
            _icon.Visibility = Visibility.Hidden;
    }

    ///hack because of this: https://github.com/dotnet/corefx/issues/10361
    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(url);
        }
        catch
        {
            url = url.Replace("&", "^&");
            Process.Start(new ProcessStartInfo("cmd", $"/c start {url}"){CreateNoWindow = true});
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddHostedService<Worker>();
        services.AddSingleton<ISettingsProvider, SettingsProvider>();
        services.AddSingleton<ShokoServer>();
        services.AddSingleton<StartServer>();
    }
}
