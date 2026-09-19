using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coraza.App.Servicios;
using Coraza.App.Vistas;
using Coraza.Core.Busqueda;
using Coraza.Core.Canciones;
using Coraza.Core.Modelos;
using Microsoft.Win32;
using Serilog;

namespace Coraza.App.ViewModels;

public sealed partial class PanelCancionesViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private List<Cancion> _todas = new();
    private bool _silenciar;

    [ObservableProperty] private string _busqueda = "";
    [ObservableProperty] private string _filtro = "Todas";
    [ObservableProperty] private Cancion? _seleccionada;

    public PanelCancionesViewModel(MainViewModel main) => _main = main;

    public ObservableCollection<Cancion> Canciones { get; } = new();

    public IReadOnlyList<string> Filtros { get; } = new[] { "Todas", "Favoritas", "Recientes", "Más usadas", "Papelera" };

    public bool EnPapelera => Filtro == "Papelera";

    public bool BibliotecaVacia => _todas.Count == 0 && !EnPapelera;

    public string Resumen => EnPapelera ? $"{Canciones.Count} en la papelera" : $"{Canciones.Count} de {_todas.Count} canciones";

    public Cancion? PorId(long id) => _todas.FirstOrDefault(c => c.Id == id);

    public void Recargar(long? seleccionar = null)
    {
        _todas = _main.Ctx.Canciones.Listar();
        Filtrar();
        if (seleccionar is long id)
        {
            _silenciar = true;
            Seleccionada = Canciones.FirstOrDefault(c => c.Id == id);
            _silenciar = false;
        }
        OnPropertyChanged(nameof(BibliotecaVacia));
    }

    partial void OnBusquedaChanged(string value) => Filtrar();

    partial void OnFiltroChanged(string value)
    {
        OnPropertyChanged(nameof(EnPapelera));
        OnPropertyChanged(nameof(BibliotecaVacia));
        Filtrar();
    }

    partial void OnSeleccionadaChanged(Cancion? value)
    {
        if (_silenciar || value is null || EnPapelera) return;
        _main.MostrarElemento(FabricaElementos.DeCancion(value));
    }

    private void Filtrar()
    {
        IEnumerable<Cancion> fuente;
        if (EnPapelera)
        {
            var normal = Normalizador.Normalizar(Busqueda);
            fuente = _main.Ctx.Canciones.Listar(eliminadas: true)
                .Where(c => normal.Length == 0 || Normalizador.Normalizar(c.Titulo).Contains(normal));
        }
        else if (!string.IsNullOrWhiteSpace(Busqueda))
        {
            var mapa = _todas.ToDictionary(c => c.Id);
            fuente = _main.Ctx.IndiceCanciones.Buscar(Busqueda).Where(mapa.ContainsKey).Select(id => mapa[id]);
            if (Filtro == "Favoritas") fuente = fuente.Where(c => c.Favorita);
        }
        else
        {
            fuente = Filtro switch
            {
                "Favoritas" => _todas.Where(c => c.Favorita),
                "Recientes" => _todas.OrderByDescending(c => c.UltimaVez > c.FechaModificacion ? c.UltimaVez : c.FechaModificacion).Take(40),
                "Más usadas" => _todas.Where(c => c.VecesUsada > 0).OrderByDescending(c => c.VecesUsada).Take(40),
                _ => _todas,
            };
        }

        _silenciar = true;
        Canciones.Clear();
        foreach (var c in fuente) Canciones.Add(c);
        _silenciar = false;
        OnPropertyChanged(nameof(Resumen));
    }

    [RelayCommand]
    private void Nueva()
    {
        var id = Ventanas.EditarCancion(_main, null);
        if (id is long nuevo) AlGuardar(nuevo);
    }

    [RelayCommand]
    private void Editar(Cancion? cancion)
    {
        cancion ??= Seleccionada;
        if (cancion is not null) EditarPorId(cancion.Id);
    }

    public void EditarPorId(long id)
    {
        var guardado = Ventanas.EditarCancion(_main, id);
        if (guardado is long g) AlGuardar(g);
    }

    private void AlGuardar(long id)
    {
        var cancion = _main.Ctx.Canciones.Obtener(id);
        if (cancion is not null) _main.Ctx.IndiceCanciones.Actualizar(id, cancion.Titulo, cancion.LetraCompleta);
        Recargar(id);
        _main.AlModificarCancion(id);
        var yaVisible = _main.ElementoActual is { Tipo: TipoElemento.Cancion } actual && actual.ReferenciaId == id;
        if (cancion is not null && !yaVisible) _main.MostrarElemento(FabricaElementos.DeCancion(cancion));
    }

    /// <summary>Pega la letra copiada de una página web, limpia el formato y abre el editor.</summary>
    [RelayCommand]
    private void PegarLetra()
    {
        var texto = Clipboard.ContainsText() ? Clipboard.GetText() : "";
        if (string.IsNullOrWhiteSpace(texto))
        {
            _main.MostrarMensaje("El portapapeles no tiene texto. Copia la letra primero (Ctrl+C).");
            return;
        }
        var id = Ventanas.EditarCancion(_main, null, AnalizadorLetra.LimpiarTextoPegado(texto));
        if (id is long nuevo) AlGuardar(nuevo);
    }

    [RelayCommand]
    private void Importar()
    {
        var dialogo = new OpenFileDialog
        {
            Title = "Importar canciones",
            Multiselect = true,
            Filter = "Canciones (TXT, ChordPro, OpenLP, SongSelect, ProPresenter)|*.txt;*.cho;*.chopro;*.chordpro;*.crd;*.xml;*.usr;*.pro6;*.pro5;*.pro4"
                     + "|Todos los archivos (OpenSong no usa extensión)|*.*",
        };
        if (dialogo.ShowDialog() != true) return;

        var importadas = 0;
        long? ultima = null;
        var fallidas = new List<string>();
        foreach (var ruta in dialogo.FileNames)
        {
            try
            {
                var cancion = ImportadorCanciones.DesdeArchivo(Path.GetFileNameWithoutExtension(ruta), LeerTexto(ruta));
                if (cancion.Secciones.Count == 0)
                {
                    fallidas.Add(Path.GetFileName(ruta));
                    continue;
                }
                ultima = _main.Ctx.Canciones.Guardar(cancion);
                _main.Ctx.IndiceCanciones.Actualizar(cancion.Id, cancion.Titulo, cancion.LetraCompleta);
                importadas++;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "No se pudo importar {Ruta}", ruta);
                fallidas.Add(Path.GetFileName(ruta));
            }
        }
        Recargar(ultima);
        var mensaje = $"{importadas} canción(es) importada(s).";
        if (fallidas.Count > 0) mensaje += $" No se pudieron leer: {string.Join(", ", fallidas.Take(3))}{(fallidas.Count > 3 ? "…" : "")}";
        _main.MostrarMensaje(mensaje, esError: importadas == 0);
    }

    /// <summary>Lee texto en UTF-8 y, si no es válido, en ANSI (Windows-1252), la codificación de muchos archivos antiguos.</summary>
    public static string LeerTexto(string ruta)
    {
        var bytes = File.ReadAllBytes(ruta);
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes).TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(1252).GetString(bytes);
        }
    }

    [RelayCommand]
    private void Duplicar(Cancion? cancion)
    {
        cancion ??= Seleccionada;
        if (cancion is null) return;
        var id = _main.Ctx.Canciones.Duplicar(cancion.Id);
        AlGuardar(id);
    }

    [RelayCommand]
    private void AlternarFavorita(Cancion? cancion)
    {
        cancion ??= Seleccionada;
        if (cancion is null) return;
        cancion.Favorita = !cancion.Favorita;
        _main.Ctx.Canciones.EstablecerFavorita(cancion.Id, cancion.Favorita);
        Recargar(cancion.Id);
    }

    [RelayCommand]
    private void Eliminar(Cancion? cancion)
    {
        cancion ??= Seleccionada;
        if (cancion is null) return;
        if (!Dialogo.Confirmar("Mover a la papelera", $"¿Mover «{cancion.Titulo}» a la papelera? Podrás restaurarla después.", "Mover a la papelera", peligro: true))
            return;
        _main.Ctx.Canciones.MarcarEliminada(cancion.Id, true);
        _main.Ctx.IndiceCanciones.Quitar(cancion.Id);
        Recargar();
        _main.MostrarMensaje($"«{cancion.Titulo}» está en la papelera (filtro «Papelera» para restaurarla).");
    }

    [RelayCommand]
    private void Restaurar(Cancion? cancion)
    {
        cancion ??= Seleccionada;
        if (cancion is null) return;
        _main.Ctx.Canciones.MarcarEliminada(cancion.Id, false);
        var completa = _main.Ctx.Canciones.Obtener(cancion.Id);
        if (completa is not null) _main.Ctx.IndiceCanciones.Actualizar(completa.Id, completa.Titulo, completa.LetraCompleta);
        Recargar();
        _main.MostrarMensaje($"«{cancion.Titulo}» se restauró.");
    }

    [RelayCommand]
    private void EliminarDefinitivamente(Cancion? cancion)
    {
        cancion ??= Seleccionada;
        if (cancion is null) return;
        if (!Dialogo.Confirmar("Eliminar para siempre", $"«{cancion.Titulo}» se borrará definitivamente. Esta acción no se puede deshacer.", "Eliminar", peligro: true))
            return;
        _main.Ctx.Canciones.EliminarDefinitivamente(cancion.Id);
        Recargar();
    }

    [RelayCommand]
    private void AgregarAlServicio(Cancion? cancion)
    {
        cancion ??= Seleccionada;
        if (cancion is null || EnPapelera) return;
        _main.AgregarAlServicio(_main.ReutilizarActual(FabricaElementos.DeCancion(cancion)));
    }
}
