using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Coraza.App.Infraestructura;
using Coraza.App.Servicios;
using Coraza.App.ViewModels;
using Coraza.App.Vistas;
using Coraza.Data;
using Coraza.Rendering;
using Serilog;

namespace Coraza.App;

public partial class App : Application
{
    private static Mutex? _instancia;

    public static Contexto Contexto { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instancia = new Mutex(true, "Coraza.InstanciaUnica", out var esNueva);
        if (!esNueva)
        {
            Nativo.ActivarVentana("Coraza");
            Shutdown();
            return;
        }

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var rutas = RutasCoraza.Predeterminadas(AppContext.BaseDirectory);
        rutas.AsegurarCarpetas();

        Registro.Configurar(rutas.Registros);
        Log.Information("Coraza {Version} iniciando. Datos en {Raiz}", typeof(App).Assembly.GetName().Version, rutas.Raiz);

        // Un error nunca debe cerrar el programa en pleno culto: se registra y se avisa sin bloquear.
        DispatcherUnhandledException += AlErrorNoControlado;
        AppDomain.CurrentDomain.UnhandledException += (_, ev) => Log.Fatal(ev.ExceptionObject as Exception, "Error fatal");
        TaskScheduler.UnobservedTaskException += (_, ev) =>
        {
            Log.Error(ev.Exception, "Error en tarea en segundo plano");
            ev.SetObserved();
        };

        try
        {
            Contexto = Contexto.Crear(rutas);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "No se pudo abrir la base de datos");
            MessageBox.Show($"No se pudo abrir la biblioteca de Coraza.\n\n{ex.Message}\n\nRevise la carpeta {rutas.Biblioteca}.",
                "Coraza", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        FuentesCoraza.CargarCarpeta(rutas.Fuentes);
        Apariencia.Aplicar(Contexto.Preferencias.Paleta);

        var ventana = new MainWindow(new MainViewModel(Contexto));
        MainWindow = ventana;
        ventana.Show();
    }

    private void AlErrorNoControlado(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Error no controlado en la interfaz");
        e.Handled = true;
        if (MainWindow?.DataContext is MainViewModel vm)
            vm.MostrarMensaje("Ocurrió un problema, pero la proyección continúa. Detalle guardado en Registros.", esError: true);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            Contexto?.GuardarPreferencias();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "No se guardaron las preferencias al salir");
        }
        EvitarSuspension.Desactivar();
        Log.Information("Coraza cerrado");
        Log.CloseAndFlush();
        _instancia?.ReleaseMutex();
        base.OnExit(e);
    }
}
