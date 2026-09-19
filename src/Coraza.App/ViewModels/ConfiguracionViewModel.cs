using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coraza.App.Infraestructura;
using Coraza.App.Servicios;
using Coraza.App.Vistas;
using Coraza.Core.Modelos;
using Coraza.Data;
using Coraza.Rendering;
using Microsoft.Win32;

namespace Coraza.App.ViewModels;

public sealed partial class AtajoEditable : ObservableObject
{
    private List<Gesto> _gestos;

    public AtajoEditable(AccionAtajo accion, IEnumerable<Gesto> gestos)
    {
        Accion = accion;
        _gestos = gestos.ToList();
    }

    public AccionAtajo Accion { get; }
    public string Nombre => GestorAtajos.Nombres[Accion];
    public string Legible => _gestos.Count == 0 ? "(sin atajo)" : string.Join("  /  ", _gestos.Select(g => g.Legible()));
    public string Serializado => GestorAtajos.Serializar(_gestos);

    public void Agregar(Key tecla, ModifierKeys modificadores)
    {
        var gesto = new Gesto(tecla, modificadores);
        if (_gestos.Contains(gesto)) return;
        _gestos.Add(gesto);
        OnPropertyChanged(nameof(Legible));
    }

    [RelayCommand]
    private void Limpiar()
    {
        _gestos.Clear();
        OnPropertyChanged(nameof(Legible));
    }

    public void Restablecer()
    {
        _gestos = GestorAtajos.Parsear(GestorAtajos.Predeterminados.GetValueOrDefault(Accion, ""));
        OnPropertyChanged(nameof(Legible));
    }
}

public sealed partial class ConfiguracionViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly Preferencias _p;

    [ObservableProperty] private string _proporcion;
    [ObservableProperty] private bool _ocultarCursor;
    [ObservableProperty] private bool _reducirMovimiento;
    [ObservableProperty] private string? _rutaLogo;
    [ObservableProperty] private string _textoLogo;
    [ObservableProperty] private string _licenciaCcli;
    [ObservableProperty] private OpcionTema? _temaCanciones;
    [ObservableProperty] private OpcionTema? _temaBiblia;
    [ObservableProperty] private OpcionTema? _temaTextos;
    [ObservableProperty] private OpcionTema? _temaImagenes;
    [ObservableProperty] private VersionBiblia? _biblia;
    [ObservableProperty] private string _paleta;

    public ConfiguracionViewModel(MainViewModel main)
    {
        _main = main;
        _p = main.Ctx.Preferencias;
        _proporcion = _p.Proporcion;
        _ocultarCursor = _p.OcultarCursor;
        _reducirMovimiento = _p.ReducirMovimiento;
        _rutaLogo = _p.RutaLogo;
        _textoLogo = _p.TextoLogo ?? "";
        _licenciaCcli = _p.LicenciaCcli ?? "";
        _paleta = Apariencia.Paletas.Contains(_p.Paleta) ? _p.Paleta : Apariencia.Vino;

        OpcionesTema = main.Proveedor.Temas.Select(t => new OpcionTema(t.Id, t.Nombre)).ToList();
        _temaCanciones = Opcion(TipoContenido.Canciones);
        _temaBiblia = Opcion(TipoContenido.Biblia);
        _temaTextos = Opcion(TipoContenido.Textos);
        _temaImagenes = Opcion(TipoContenido.Imagenes);

        Biblias = main.Ctx.Biblias.Listar();
        _biblia = Biblias.FirstOrDefault(b => b.Id == _p.BibliaPredeterminada) ?? Biblias.FirstOrDefault();

        var atajos = new GestorAtajos(_p.Atajos);
        foreach (var accion in Enum.GetValues<AccionAtajo>()) Atajos.Add(new AtajoEditable(accion, atajos.GestosDe(accion)));
    }

    public IReadOnlyList<string> Proporciones => Listas.Proporciones;
    public IReadOnlyList<string> Paletas => Apariencia.Paletas;

    /// <summary>La paleta se ve al instante; si se cancela, se vuelve a la guardada.</summary>
    partial void OnPaletaChanged(string value) => Apariencia.Aplicar(value);

    public void RevertirApariencia() => Apariencia.Aplicar(_p.Paleta);

    public string EstadoControlRemoto => _main.Remoto.Activo
        ? $"Encendido · puerto {_main.Remoto.Puerto}"
        : "Apagado";

    // ---- Actualizaciones ----
    // Como la sección del control remoto, esta se aplica al momento y no espera al
    // botón Guardar: «comprobar actualizaciones» no tiene sentido como cambio pendiente.

    public bool ComprobarActualizaciones
    {
        get => _p.Actualizaciones.Comprobar;
        set
        {
            if (_p.Actualizaciones.Comprobar == value) return;
            _p.Actualizaciones.Comprobar = value;
            _main.Ctx.GuardarPreferencias();
            OnPropertyChanged();
        }
    }

    public string EstadoActualizacion => _main.Actualizaciones.HayActualizacionLista
        ? $"Lista para instalar: {_main.Actualizaciones.Disponible!.Nombre}"
        : $"Estás en la versión {Actualizador.VersionActual.ToString(3)}";

    public bool HayActualizacion => _main.Actualizaciones.HayActualizacionLista;

    /// <summary>Instala la versión descargada. Se niega si hay una proyección activa.</summary>
    [RelayCommand]
    private void InstalarActualizacion()
    {
        // Aplicar cierra Coraza: hacerlo en pleno culto sería justo el desastre que
        // este programa existe para evitar. Actualizador.Aplicar() lo comprueba.
        if (_main.Actualizaciones.Aplicar()) CerrarSolicitado?.Invoke(false);
        OnPropertyChanged(nameof(EstadoActualizacion));
        OnPropertyChanged(nameof(HayActualizacion));
    }

    [RelayCommand]
    private void OmitirActualizacion()
    {
        _main.Actualizaciones.Omitir();
        OnPropertyChanged(nameof(EstadoActualizacion));
        OnPropertyChanged(nameof(HayActualizacion));
    }

    /// <summary>
    /// Abre la ventana del control remoto. No se guarda con el resto de la
    /// configuración: esa ventana aplica sus cambios al momento, porque encender el
    /// servidor y emparejar un teléfono no tiene sentido «al pulsar Guardar».
    /// </summary>
    [RelayCommand]
    private void AbrirControlRemoto()
    {
        Vistas.Ventanas.ControlRemoto(_main);
        OnPropertyChanged(nameof(EstadoControlRemoto));
    }
    public IReadOnlyList<OpcionTema> OpcionesTema { get; }
    public IReadOnlyList<VersionBiblia> Biblias { get; }
    public ObservableCollection<AtajoEditable> Atajos { get; } = new();
    public string CarpetaDatos => _main.Ctx.Rutas.Raiz;
    public string ModoDatos => _main.Ctx.Rutas.Portatil ? "Versión portátil (datos junto al programa)" : "Datos en la carpeta Documentos";

    public event Action<bool>? CerrarSolicitado;

    private OpcionTema? Opcion(TipoContenido tipo)
    {
        var id = _main.Proveedor.TemaPara(tipo).Id;
        return OpcionesTema.FirstOrDefault(o => o.Id == id);
    }

    [RelayCommand]
    private void ElegirLogo()
    {
        var dialogo = new OpenFileDialog
        {
            Title = "Logotipo de la iglesia",
            Filter = "Imágenes|" + string.Join(";", FormatosMedios.Imagenes.Select(e => "*" + e)),
        };
        if (dialogo.ShowDialog() != true) return;
        var destino = System.IO.Path.Combine(_main.Ctx.Rutas.Fondos, "logo" + System.IO.Path.GetExtension(dialogo.FileName));
        System.IO.File.Copy(dialogo.FileName, destino, overwrite: true);
        CacheImagenes.Vaciar();
        RutaLogo = destino;
    }

    [RelayCommand]
    private void QuitarLogo() => RutaLogo = null;

    [RelayCommand]
    private void AbrirCarpetaDatos() => AbrirCarpeta(_main.Ctx.Rutas.Raiz);

    [RelayCommand]
    private void AbrirCarpetaFuentes() => AbrirCarpeta(_main.Ctx.Rutas.Fuentes);

    [RelayCommand]
    private void RecargarFuentes()
    {
        FuentesCoraza.CargarCarpeta(_main.Ctx.Rutas.Fuentes);
        _main.MostrarMensaje("Fuentes recargadas.");
    }

    [RelayCommand]
    private void RespaldarAhora()
    {
        var ruta = GestorRespaldos.RespaldoManual(_main.Ctx.Db, _main.Ctx.Rutas.Respaldos);
        Dialogo.Informar("Respaldo creado", $"Se guardó una copia de la biblioteca en:\n{ruta}");
    }

    [RelayCommand]
    private void AbrirAsistente() => Ventanas.Asistente(_main);

    [RelayCommand]
    private void RestablecerAtajos()
    {
        foreach (var a in Atajos) a.Restablecer();
    }

    [RelayCommand]
    private void Guardar()
    {
        _p.Proporcion = Proporcion;
        _p.Paleta = Paleta;
        _p.OcultarCursor = OcultarCursor;
        _p.ReducirMovimiento = ReducirMovimiento;
        _p.RutaLogo = RutaLogo;
        _p.TextoLogo = string.IsNullOrWhiteSpace(TextoLogo) ? null : TextoLogo.Trim();
        _p.LicenciaCcli = string.IsNullOrWhiteSpace(LicenciaCcli) ? null : LicenciaCcli.Trim();
        Asignar(TipoContenido.Canciones, TemaCanciones);
        Asignar(TipoContenido.Biblia, TemaBiblia);
        Asignar(TipoContenido.Textos, TemaTextos);
        Asignar(TipoContenido.Imagenes, TemaImagenes);
        if (Biblia is not null) _p.BibliaPredeterminada = Biblia.Id;
        _p.Atajos = Atajos.ToDictionary(a => a.Accion.ToString(), a => a.Serializado);
        _main.Ctx.GuardarPreferencias();
        CerrarSolicitado?.Invoke(true);
    }

    [RelayCommand]
    private void Cancelar() => CerrarSolicitado?.Invoke(false);

    private void Asignar(TipoContenido tipo, OpcionTema? opcion)
    {
        if (opcion?.Id is long id) _p.TemasPorContenido[tipo] = id;
    }

    private static void AbrirCarpeta(string ruta)
    {
        System.IO.Directory.CreateDirectory(ruta);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{ruta}\"") { UseShellExecute = true });
    }
}
