using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coraza.App.Infraestructura;
using Coraza.Core.Modelos;

namespace Coraza.App.ViewModels;

/// <summary>
/// Asistente de primer inicio en cuatro pasos: la iglesia (e idioma), las pantallas y el Video Beam,
/// la apariencia y el tema de las canciones, y la Biblia con la licencia CCLI.
/// </summary>
public sealed partial class AsistenteViewModel : ObservableObject
{
    public const int Pasos = 4;

    private readonly MainViewModel _main;
    private readonly string _paletaInicial;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EsPrimero), nameof(EsUltimo), nameof(TextoSiguiente), nameof(PasoTexto))]
    private int _paso;

    [ObservableProperty] private string _nombreIglesia;
    [ObservableProperty] private string _paleta;
    [ObservableProperty] private Tema? _temaCanciones;
    [ObservableProperty] private VersionBiblia? _biblia;
    [ObservableProperty] private string _licenciaCcli;
    [ObservableProperty] private string _estadoPantallas = "";
    [ObservableProperty] private bool _duplicando;

    public AsistenteViewModel(MainViewModel main)
    {
        _main = main;
        var p = main.Ctx.Preferencias;
        _nombreIglesia = p.TextoLogo ?? "";
        _paleta = _paletaInicial = Apariencia.Paletas.Contains(p.Paleta) ? p.Paleta : Apariencia.Vino;
        _licenciaCcli = p.LicenciaCcli ?? "";
        Temas = main.Proveedor.Temas;
        _temaCanciones = main.Proveedor.TemaPara(TipoContenido.Canciones);
        RecargarBiblias();
        ActualizarPantallas();
    }

    public ObservableCollection<string> Pantallas { get; } = new();
    public ObservableCollection<VersionBiblia> Biblias { get; } = new();
    public IReadOnlyList<Tema> Temas { get; }
    public IReadOnlyList<string> Paletas => Apariencia.Paletas;
    public Diapositiva Muestra => PanelTemasViewModel.MuestraCancion;
    public bool EsPrimero => Paso == 0;
    public bool EsUltimo => Paso == Pasos - 1;
    public string TextoSiguiente => EsUltimo ? "Comenzar a usar Coraza" : "Siguiente";
    public string PasoTexto => $"Paso {Paso + 1} de {Pasos}";

    public event Action? Cerrar;

    /// <summary>La paleta se ve al instante mientras se elige.</summary>
    partial void OnPaletaChanged(string value) => Apariencia.Aplicar(value);

    [RelayCommand]
    private void Siguiente()
    {
        if (EsUltimo) Finalizar();
        else Paso++;
    }

    [RelayCommand]
    private void Atras()
    {
        if (Paso > 0) Paso--;
    }

    [RelayCommand]
    public void Omitir()
    {
        Apariencia.Aplicar(_paletaInicial);
        _main.Ctx.Preferencias.AsistenteCompletado = true;
        _main.Ctx.GuardarPreferencias();
        if (_main.Proyeccion.PatronPrueba) _main.Proyeccion.PatronPrueba = false;
        Cerrar?.Invoke();
    }

    [RelayCommand]
    private void ActualizarPantallas()
    {
        var lista = GestorPantallas.Enumerar();
        Pantallas.Clear();
        foreach (var p in lista) Pantallas.Add(p.Descripcion);
        Duplicando = GestorPantallas.ModoDuplicar();
        var secundaria = lista.FirstOrDefault(p => !p.Principal);
        EstadoPantallas = Duplicando
            ? "Windows está duplicando la pantalla: el público vería la ventana del operador. Pulsa «Cambiar a Extender»."
            : secundaria is not null
                ? $"¡Listo! Se detectó una segunda pantalla ({secundaria.Nombre}, {secundaria.Limites.Width}×{secundaria.Limites.Height}). Coraza proyectará allí automáticamente."
                : "Solo hay una pantalla. Conecta el Video Beam por HDMI o VGA y pulsa «Volver a detectar». Mientras tanto podrás ensayar en una ventana.";
    }

    [RelayCommand]
    private void CambiarAExtender()
    {
        GestorPantallas.CambiarAExtender();
        EstadoPantallas = "Cambiando Windows a «Extender»… pulsa «Volver a detectar» en unos segundos.";
    }

    [RelayCommand]
    private void ProbarPantalla() => _main.AlternarPatronPruebaCommand.Execute(null);

    [RelayCommand]
    private async Task ImportarBibliaAsync()
    {
        await _main.Biblia.ImportarCommand.ExecuteAsync(null);
        RecargarBiblias();
    }

    private void RecargarBiblias()
    {
        var id = Biblia?.Id ?? _main.Ctx.Preferencias.BibliaPredeterminada;
        Biblias.Clear();
        foreach (var v in _main.Ctx.Biblias.Listar()) Biblias.Add(v);
        Biblia = Biblias.FirstOrDefault(v => v.Id == id) ?? Biblias.FirstOrDefault();
    }

    private void Finalizar()
    {
        var p = _main.Ctx.Preferencias;
        p.TextoLogo = string.IsNullOrWhiteSpace(NombreIglesia) ? null : NombreIglesia.Trim();
        p.Paleta = Paleta;
        if (TemaCanciones is not null) p.TemasPorContenido[TipoContenido.Canciones] = TemaCanciones.Id;
        if (Biblia is not null) p.BibliaPredeterminada = Biblia.Id;
        p.LicenciaCcli = string.IsNullOrWhiteSpace(LicenciaCcli) ? null : LicenciaCcli.Trim();
        p.AsistenteCompletado = true;
        _main.Ctx.GuardarPreferencias();
        if (_main.Proyeccion.PatronPrueba) _main.Proyeccion.PatronPrueba = false;
        _main.AplicarConfiguracion();
        Cerrar?.Invoke();
    }
}
