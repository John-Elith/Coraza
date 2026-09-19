using System.Diagnostics;
using System.IO;
using Serilog;

namespace Coraza.App.Infraestructura;

public static class Registro
{
    public static void Configurar(string carpeta)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(carpeta, "coraza-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        // Los errores de enlace de datos de WPF fallan en silencio: se guardan en el registro para diagnosticarlos.
        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Listeners.Add(new OyenteEnlaces());
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
    }

    private sealed class OyenteEnlaces : TraceListener
    {
        public override void Write(string? message)
        {
        }

        public override void WriteLine(string? message)
        {
            if (!string.IsNullOrWhiteSpace(message)) Log.Warning("Enlace WPF: {Mensaje}", message);
        }
    }
}
