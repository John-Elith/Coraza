using Coraza.Core.Busqueda;
using Coraza.Core.Modelos;
using Coraza.Data;
using Coraza.Data.Repositorios;
using Serilog;

namespace Coraza.App.Servicios;

/// <summary>Preferencias del usuario (se guardan en la tabla de configuración como JSON).</summary>
public sealed class Preferencias
{
    public const string PantallaFlotante = "flotante";

    /// <summary>null = automática (segunda pantalla), «flotante» o el nombre del dispositivo (\\.\DISPLAY2).</summary>
    public string? Pantalla { get; set; }
    public string Proporcion { get; set; } = "Automática";
    public bool OcultarCursor { get; set; } = true;
    public bool ReducirMovimiento { get; set; }
    public TipoTransicion? TransicionForzada { get; set; }
    public int DuracionForzadaMs { get; set; } = 600;
    public Dictionary<TipoContenido, long> TemasPorContenido { get; set; } = new();
    public long? BibliaPredeterminada { get; set; }
    public string? RutaLogo { get; set; }
    public string? TextoLogo { get; set; }
    public string? LicenciaCcli { get; set; }
    public long? ServicioActual { get; set; }
    public double AnchoIzquierdo { get; set; } = 300;
    public double AnchoDerecho { get; set; } = 340;
    /// <summary>Posición y tamaño de la ventana de ensayo (x, y, ancho, alto).</summary>
    public double[]? VentanaEnsayo { get; set; }
    /// <summary>Paleta de la interfaz del operador («Vino Coraza» u «Oscuro grafito»).</summary>
    public string Paleta { get; set; } = Coraza.App.Infraestructura.Apariencia.Vino;
    /// <summary>El asistente de primer inicio ya se completó u omitió.</summary>
    public bool AsistenteCompletado { get; set; }
    public Dictionary<string, string> Atajos { get; set; } = new();
    /// <summary>Control remoto desde el celular (Fase 3).</summary>
    public PreferenciasControlRemoto ControlRemoto { get; set; } = new();
}

/// <summary>Ajustes del control remoto. Apagado por omisión: se enciende por servicio.</summary>
public sealed class PreferenciasControlRemoto
{
    public bool Activado { get; set; }
    public int Puerto { get; set; } = 8787;
    /// <summary>IPv4 elegida cuando el equipo tiene varias (wifi, ethernet, Hyper-V…).</summary>
    public string? DireccionElegida { get; set; }
    /// <summary>Ya se ofreció crear la regla del firewall; no insistir en cada arranque.</summary>
    public bool ReglaFirewallIntentada { get; set; }
    public bool PermitirRedesPublicas { get; set; }
}

/// <summary>Acceso a la base de datos, repositorios, índice de búsqueda y preferencias.</summary>
public sealed class Contexto
{
    private const string ClavePreferencias = "preferencias";

    private Contexto(RutasCoraza rutas, BaseDatos db)
    {
        Rutas = rutas;
        Db = db;
        Canciones = new RepositorioCanciones(db);
        Biblias = new RepositorioBiblias(db);
        Medios = new RepositorioMedios(db);
        Textos = new RepositorioTextos(db);
        Temas = new RepositorioTemas(db);
        Servicios = new RepositorioServicios(db);
        Configuracion = new RepositorioConfiguracion(db);
        Historial = new RepositorioHistorial(db);
        Preferencias = Configuracion.ObtenerJson<Preferencias>(ClavePreferencias) ?? new Preferencias();
    }

    public RutasCoraza Rutas { get; }
    public BaseDatos Db { get; }
    public RepositorioCanciones Canciones { get; }
    public RepositorioBiblias Biblias { get; }
    public RepositorioMedios Medios { get; }
    public RepositorioTextos Textos { get; }
    public RepositorioTemas Temas { get; }
    public RepositorioServicios Servicios { get; }
    public RepositorioConfiguracion Configuracion { get; }
    public RepositorioHistorial Historial { get; }
    public IndiceCanciones IndiceCanciones { get; } = new();
    public Preferencias Preferencias { get; }

    public static Contexto Crear(RutasCoraza rutas)
    {
        var db = new BaseDatos(rutas.ArchivoBaseDatos);
        db.Inicializar();
        try
        {
            var respaldo = GestorRespaldos.RespaldoDiario(db, rutas.Respaldos);
            if (respaldo is not null) Log.Information("Respaldo diario creado: {Ruta}", respaldo);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "No se pudo crear el respaldo diario");
        }
        var contexto = new Contexto(rutas, db);
        contexto.Temas.AsegurarPredefinidos();
        return contexto;
    }

    public void GuardarPreferencias() => Configuracion.GuardarJson(ClavePreferencias, Preferencias);

    /// <summary>Reconstruye el índice de búsqueda de canciones (en segundo plano al iniciar).</summary>
    public void ReconstruirIndice()
    {
        IndiceCanciones.Limpiar();
        foreach (var (id, titulo, letra) in Canciones.TextosParaIndice()) IndiceCanciones.Actualizar(id, titulo, letra);
    }
}
