using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coraza.App.Servicios;
using Coraza.App.Vistas;
using Coraza.Core.Modelos;
using Coraza.Rendering;
using Microsoft.Win32;
using Serilog;

namespace Coraza.App.ViewModels;

// ===================== Medios =====================

public sealed partial class MedioViewModel : ObservableObject
{
    [ObservableProperty] private BitmapSource? _miniatura;

    public MedioViewModel(Medio modelo, string rutaAbsoluta)
    {
        Modelo = modelo;
        RutaAbsoluta = rutaAbsoluta;
        if (modelo.Tipo == TipoMedio.Documento) Paginas = Medio.PaginasDe(rutaAbsoluta);
    }

    public bool EsPresentacion => Modelo.Etiquetas == Medio.EtiquetaPresentacion;

    public Medio Modelo { get; }
    public string RutaAbsoluta { get; }
    public IReadOnlyList<string> Paginas { get; } = Array.Empty<string>();
    public string Nombre => Modelo.Nombre;
    public bool EsAudio => Modelo.Tipo == TipoMedio.Audio;

    /// <summary>Insignia sobre la miniatura: VIDEO, AUDIO o la cantidad de páginas.</summary>
    public string Insignia => Modelo.Tipo switch
    {
        TipoMedio.Video => "VIDEO",
        TipoMedio.Audio => "AUDIO",
        TipoMedio.Documento => EsPresentacion ? $"{Paginas.Count} FOTOS" : $"{Paginas.Count} PÁG.",
        _ => "",
    };

    public string Detalle => Modelo.Tipo switch
    {
        TipoMedio.Video => "Video",
        TipoMedio.Audio => "Música de fondo",
        TipoMedio.Documento => EsPresentacion ? $"Presentación · {Paginas.Count} fotos" : $"Documento · {Paginas.Count} páginas",
        _ => Modelo.Ancho is > 0 ? $"{Modelo.Ancho}×{Modelo.Alto}" : "",
    };

    public bool Existe => File.Exists(RutaAbsoluta) || Directory.Exists(RutaAbsoluta);

    public async void CargarMiniatura()
    {
        Miniatura = Modelo.Tipo switch
        {
            TipoMedio.Imagen => await Task.Run(() => CacheImagenes.Obtener(RutaAbsoluta, 240)),
            TipoMedio.Documento when Paginas.Count > 0 => await Task.Run(() => CacheImagenes.Obtener(Paginas[0], 240)),
            TipoMedio.Video => await MiniaturasShell.ObtenerAsync(RutaAbsoluta, 256),
            _ => null,
        };
    }
}

/// <summary>Imágenes: agregar desde el equipo o una USB, arrastrar y soltar, o pegar (Ctrl+V).</summary>
public sealed partial class PanelMediosViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private bool _silenciar;

    [ObservableProperty] private MedioViewModel? _seleccionado;
    [ObservableProperty] private Opcion<ModoAjuste>? _ajusteSeleccionado;

    public PanelMediosViewModel(MainViewModel main) => _main = main;

    public ObservableCollection<MedioViewModel> Medios { get; } = new();

    public IReadOnlyList<Opcion<ModoAjuste>> Ajustes => Listas.Ajustes;

    public bool SinMedios => Medios.Count == 0;

    public void Recargar(long? seleccionar = null)
    {
        Medios.Clear();
        foreach (var m in _main.Ctx.Medios.Listar())
        {
            var vm = new MedioViewModel(m, _main.Ctx.Rutas.AAbsoluta(m.Ruta));
            Medios.Add(vm);
            vm.CargarMiniatura();
        }
        OnPropertyChanged(nameof(SinMedios));
        if (seleccionar is long id)
        {
            _silenciar = true;
            Seleccionado = Medios.FirstOrDefault(m => m.Modelo.Id == id);
            _silenciar = false;
        }
    }

    partial void OnSeleccionadoChanged(MedioViewModel? value)
    {
        _silenciar = true;
        AjusteSeleccionado = value is null ? null : Ajustes.FirstOrDefault(a => a.Valor == value.Modelo.Ajuste);
        _silenciar = false;
        if (value is not null) _main.MostrarElemento(FabricaElementos.DeMedio(value.Modelo));
    }

    partial void OnAjusteSeleccionadoChanged(Opcion<ModoAjuste>? value)
    {
        if (_silenciar || value is null || Seleccionado is null) return;
        Seleccionado.Modelo.Ajuste = value.Valor;
        _main.Ctx.Medios.Actualizar(Seleccionado.Modelo);
        _main.RefrescarElementoActual();
    }

    [RelayCommand]
    private async Task AgregarArchivosAsync()
    {
        static string Filtro(IEnumerable<string> extensiones) => string.Join(";", extensiones.Select(e => "*" + e));
        var dialogo = new OpenFileDialog
        {
            Title = "Agregar imágenes, videos, audios o documentos",
            Multiselect = true,
            Filter = "Todos los medios|" + Filtro(FormatosMedios.Imagenes.Concat(FormatosMedios.Videos).Concat(FormatosMedios.Audios).Concat(FormatosMedios.Documentos))
                     + "|Imágenes|" + Filtro(FormatosMedios.Imagenes)
                     + "|Videos (MP4 recomendado)|" + Filtro(FormatosMedios.Videos)
                     + "|Audio (MP3, WAV)|" + Filtro(FormatosMedios.Audios)
                     + "|PDF y PowerPoint|" + Filtro(FormatosMedios.Documentos)
                     + "|Todos los archivos|*.*",
        };
        if (dialogo.ShowDialog() == true) await ImportarAsync(dialogo.FileNames);
    }

    [RelayCommand]
    private async Task AgregarCarpetaAsync()
    {
        var dialogo = new OpenFolderDialog { Title = "Importar una carpeta de imágenes (por ejemplo, de una memoria USB)" };
        if (dialogo.ShowDialog() == true) await ImportarAsync(new[] { dialogo.FolderName });
    }

    /// <summary>Presentaciones de PowerPoint y PDF: cada diapositiva o página se convierte en una imagen proyectable.</summary>
    [RelayCommand]
    private async Task AgregarPresentacionAsync()
    {
        var dialogo = new OpenFileDialog
        {
            Title = "Importar una presentación de PowerPoint o un PDF",
            Multiselect = true,
            Filter = "Presentaciones y PDF|" + string.Join(";", FormatosMedios.Documentos.Select(e => "*" + e))
                     + "|PowerPoint|*.pptx;*.ppt;*.ppsx;*.pps;*.pptm"
                     + "|PDF|*.pdf",
        };
        if (dialogo.ShowDialog() != true) return;
        if (dialogo.FileNames.Any(ConversorDocumentos.EsPresentacion)
            && Type.GetTypeFromProgID("PowerPoint.Application") is null
            && ConversorDocumentos.BuscarLibreOffice() is null)
        {
            _main.MostrarMensaje(
                "Para convertir PowerPoint hace falta PowerPoint o LibreOffice en este equipo. "
                + "Si no los tienes, exporta la presentación a PDF (Archivo → Exportar) e impórtala como PDF.", esError: true);
            return;
        }
        await ImportarAsync(dialogo.FileNames);
    }

    [RelayCommand]
    private async Task PegarAsync()
    {
        if (Clipboard.ContainsFileDropList())
        {
            await ImportarAsync(Clipboard.GetFileDropList().Cast<string>().ToList());
            return;
        }
        if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } imagen)
        {
            var ruta = Path.Combine(_main.Ctx.Rutas.Imagenes, $"Pegada {DateTime.Now:yyyy-MM-dd HHmmss}.png");
            using (var archivo = File.Create(ruta))
            {
                var codificador = new PngBitmapEncoder();
                codificador.Frames.Add(BitmapFrame.Create(imagen));
                codificador.Save(archivo);
            }
            await ImportarAsync(new[] { ruta });
            return;
        }
        _main.MostrarMensaje("El portapapeles no tiene imágenes ni archivos.");
    }

    /// <summary>
    /// Copia las imágenes a la carpeta de medios de Coraza (así el servicio no falla si se retira la USB)
    /// y las registra en la biblioteca.
    /// </summary>
    public async Task<List<Medio>> ImportarAsync(IEnumerable<string> rutas)
    {
        var archivos = rutas
            .SelectMany(r => Directory.Exists(r) ? Directory.EnumerateFiles(r, "*", SearchOption.AllDirectories) : new[] { r })
            .Where(FormatosMedios.EsCompatible)
            .ToList();
        if (archivos.Count == 0)
        {
            _main.MostrarMensaje("No se encontraron archivos compatibles (imágenes, videos MP4, audios MP3, PDF o PowerPoint).", esError: true);
            return new List<Medio>();
        }

        var nuevos = new List<Medio>();
        var fallidos = new List<string>();
        _main.Preparando = archivos.Count > 3 || archivos.Any(FormatosMedios.EsDocumento);
        try
        {
            foreach (var origen in archivos)
            {
                try
                {
                    _main.TextoPreparando = $"Agregando «{Path.GetFileName(origen)}» a la biblioteca…";
                    nuevos.Add(FormatosMedios.EsDocumento(origen)
                        ? await ImportarDocumentoAsync(origen)
                        : await Task.Run(() => ImportarArchivo(origen)));
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "No se pudo importar {Ruta}", origen);
                    fallidos.Add(Path.GetFileName(origen) + (ex is InvalidOperationException ? $" ({ex.Message})" : ""));
                }
            }
        }
        finally
        {
            _main.Preparando = false;
        }

        Recargar(nuevos.LastOrDefault()?.Id);
        var mensaje = $"{nuevos.Count} archivo(s) agregado(s).";
        if (fallidos.Count > 0) mensaje += $" No se pudieron agregar: {string.Join(", ", fallidos.Take(2))}{(fallidos.Count > 2 ? "…" : "")}";
        _main.MostrarMensaje(mensaje, fallidos.Count > 0);
        return nuevos;
    }

    /// <summary>Copia imágenes, videos y audios a su carpeta de la biblioteca (el servicio no falla si se retira la USB).</summary>
    private Medio ImportarArchivo(string origen)
    {
        var rutas = _main.Ctx.Rutas;
        var (tipo, carpeta) = FormatosMedios.EsVideo(origen) ? (TipoMedio.Video, rutas.Videos)
            : FormatosMedios.EsAudio(origen) ? (TipoMedio.Audio, rutas.Audio)
            : (TipoMedio.Imagen, rutas.Imagenes);
        var destino = CopiarAMedios(origen, carpeta);
        var dimensiones = tipo == TipoMedio.Imagen ? CacheImagenes.Dimensiones(destino) : null;
        var medio = new Medio
        {
            Tipo = tipo,
            Ruta = rutas.ARelativa(destino),
            Nombre = Path.GetFileNameWithoutExtension(origen),
            Ancho = dimensiones?.Ancho,
            Alto = dimensiones?.Alto,
        };
        _main.Ctx.Medios.Agregar(medio);
        return medio;
    }

    /// <summary>PDF (o PowerPoint exportado a PDF): cada página se guarda como imagen y es una diapositiva.</summary>
    private async Task<Medio> ImportarDocumentoAsync(string origen)
    {
        var rutas = _main.Ctx.Rutas;
        var nombre = Path.GetFileNameWithoutExtension(origen);
        var carpeta = Path.Combine(rutas.Documentos, nombre);
        for (var i = 2; Directory.Exists(carpeta); i++) carpeta = Path.Combine(rutas.Documentos, $"{nombre} ({i})");
        var progreso = new Progress<string>(texto => _main.TextoPreparando = texto);

        var pdf = origen;
        string? temporal = null;
        if (ConversorDocumentos.EsPresentacion(origen))
        {
            _main.TextoPreparando = $"Exportando «{Path.GetFileName(origen)}» con PowerPoint…";
            temporal = Path.Combine(Path.GetTempPath(), $"coraza-{Guid.NewGuid():N}.pdf");
            await ConversorDocumentos.PresentacionAPdfAsync(origen, temporal);
            pdf = temporal;
        }
        try
        {
            await ConversorDocumentos.PdfAImagenesAsync(pdf, carpeta, progreso);
            var medio = new Medio { Tipo = TipoMedio.Documento, Ruta = rutas.ARelativa(carpeta), Nombre = nombre };
            _main.Ctx.Medios.Agregar(medio);
            return medio;
        }
        finally
        {
            if (temporal is not null)
            {
                try { File.Delete(temporal); }
                catch (IOException) { }
            }
        }
    }

    /// <summary>Presentación de fotos: todas las imágenes de una carpeta como un solo elemento que avanza solo.</summary>
    [RelayCommand]
    private async Task CrearPresentacionAsync()
    {
        var dialogo = new OpenFolderDialog { Title = "Elige la carpeta con las fotos de la presentación (por ejemplo, del campamento)" };
        if (dialogo.ShowDialog() != true) return;
        var fotos = Directory.EnumerateFiles(dialogo.FolderName).Where(FormatosMedios.EsImagen).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        if (fotos.Count == 0)
        {
            _main.MostrarMensaje("La carpeta no tiene imágenes compatibles.", esError: true);
            return;
        }
        var rutas = _main.Ctx.Rutas;
        var nombre = Path.GetFileName(dialogo.FolderName.TrimEnd('\\'));
        var carpeta = Path.Combine(rutas.Documentos, nombre);
        for (var i = 2; Directory.Exists(carpeta); i++) carpeta = Path.Combine(rutas.Documentos, $"{nombre} ({i})");

        _main.Preparando = true;
        _main.TextoPreparando = $"Copiando {fotos.Count} fotos a la biblioteca…";
        try
        {
            await Task.Run(() =>
            {
                Directory.CreateDirectory(carpeta);
                for (var i = 0; i < fotos.Count; i++)
                    File.Copy(fotos[i], Path.Combine(carpeta, $"foto-{i + 1:000}{Path.GetExtension(fotos[i]).ToLowerInvariant()}"));
            });
            var medio = new Medio { Tipo = TipoMedio.Documento, Ruta = rutas.ARelativa(carpeta), Nombre = nombre, Etiquetas = Medio.EtiquetaPresentacion };
            _main.Ctx.Medios.Agregar(medio);
            Recargar(medio.Id);
            _main.MostrarMensaje($"Presentación «{nombre}» con {fotos.Count} fotos. En el servicio avanza sola cada 6 segundos y se repite (clic derecho para cambiarlo).");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "No se pudo crear la presentación de fotos");
            _main.MostrarMensaje("No se pudo crear la presentación de fotos.", esError: true);
        }
        finally
        {
            _main.Preparando = false;
        }
    }

    [RelayCommand]
    private void ReproducirMusica(MedioViewModel? medio)
    {
        medio ??= Seleccionado;
        if (medio is null || !medio.EsAudio)
        {
            _main.MostrarMensaje("Elige un archivo de audio (MP3, WAV…) para usarlo como música de fondo.");
            return;
        }
        _main.Proyeccion.ReproducirMusica(medio.RutaAbsoluta, medio.Nombre);
        _main.MostrarMensaje($"Música de fondo: «{medio.Nombre}» (se repite en bucle).");
    }

    private static string CopiarAMedios(string origen, string carpeta)
    {
        var completa = Path.GetFullPath(origen);
        if (completa.StartsWith(Path.GetFullPath(carpeta), StringComparison.OrdinalIgnoreCase)) return completa;
        var nombre = Path.GetFileNameWithoutExtension(origen);
        var extension = Path.GetExtension(origen);
        var destino = Path.Combine(carpeta, nombre + extension);
        for (var i = 2; File.Exists(destino); i++) destino = Path.Combine(carpeta, $"{nombre} ({i}){extension}");
        File.Copy(origen, destino);
        return destino;
    }

    [RelayCommand]
    private void Eliminar(MedioViewModel? medio)
    {
        medio ??= Seleccionado;
        if (medio is null) return;
        if (!Dialogo.Confirmar("Eliminar imagen", $"¿Quitar «{medio.Nombre}» de la biblioteca?", "Eliminar", peligro: true)) return;
        _main.Ctx.Medios.Eliminar(medio.Modelo.Id);
        try
        {
            if (medio.RutaAbsoluta.StartsWith(_main.Ctx.Rutas.Medios, StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(medio.RutaAbsoluta)) File.Delete(medio.RutaAbsoluta);
                else if (Directory.Exists(medio.RutaAbsoluta)) Directory.Delete(medio.RutaAbsoluta, recursive: true);
            }
        }
        catch (IOException ex)
        {
            Log.Warning(ex, "No se pudo borrar el archivo {Ruta}", medio.RutaAbsoluta);
        }
        Recargar();
    }

    [RelayCommand]
    private void AgregarAlServicio(MedioViewModel? medio)
    {
        medio ??= Seleccionado;
        if (medio is not null) _main.AgregarAlServicio(_main.ReutilizarActual(FabricaElementos.DeMedio(medio.Modelo)));
    }
}

// ===================== Textos =====================

public sealed partial class PanelTextosViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private bool _silenciar;

    [ObservableProperty] private TextoLibre? _seleccionado;

    public PanelTextosViewModel(MainViewModel main) => _main = main;

    public ObservableCollection<TextoLibre> Textos { get; } = new();

    public bool SinTextos => Textos.Count == 0;

    public void Recargar(long? seleccionar = null)
    {
        Textos.Clear();
        foreach (var t in _main.Ctx.Textos.Listar()) Textos.Add(t);
        OnPropertyChanged(nameof(SinTextos));
        if (seleccionar is long id)
        {
            _silenciar = true;
            Seleccionado = Textos.FirstOrDefault(t => t.Id == id);
            _silenciar = false;
        }
    }

    partial void OnSeleccionadoChanged(TextoLibre? value)
    {
        if (!_silenciar && value is not null) _main.MostrarElemento(FabricaElementos.DeTexto(value));
    }

    [RelayCommand]
    private void Nuevo()
    {
        var texto = new TextoLibre();
        if (!Ventanas.EditarTexto(_main, texto)) return;
        Recargar(texto.Id);
        _main.MostrarElemento(FabricaElementos.DeTexto(texto));
    }

    [RelayCommand]
    private void Editar(TextoLibre? texto)
    {
        texto ??= Seleccionado;
        if (texto is not null) EditarPorId(texto.Id);
    }

    public void EditarPorId(long id)
    {
        var texto = _main.Ctx.Textos.Obtener(id);
        if (texto is null || !Ventanas.EditarTexto(_main, texto)) return;
        Recargar(texto.Id);
        foreach (var vm in _main.Elementos.Where(e => e.Tipo == TipoElemento.Texto && e.Modelo.ReferenciaId == id))
        {
            vm.Modelo.Titulo = texto.Titulo;
            vm.NotificarTitulo();
        }
        if (_main.ElementoActual is { Tipo: TipoElemento.Texto } actual && actual.ReferenciaId == id)
        {
            actual.Titulo = texto.Titulo;
            _main.RefrescarElementoActual();
        }
        else
        {
            _main.MostrarElemento(FabricaElementos.DeTexto(texto));
        }
    }

    [RelayCommand]
    private void Eliminar(TextoLibre? texto)
    {
        texto ??= Seleccionado;
        if (texto is null) return;
        if (!Dialogo.Confirmar("Eliminar texto", $"¿Eliminar «{texto.Titulo}»?", "Eliminar", peligro: true)) return;
        _main.Ctx.Textos.Eliminar(texto.Id);
        Recargar();
    }

    [RelayCommand]
    private void AgregarAlServicio(TextoLibre? texto)
    {
        texto ??= Seleccionado;
        if (texto is not null) _main.AgregarAlServicio(_main.ReutilizarActual(FabricaElementos.DeTexto(texto)));
    }

    /// <summary>Muestra el anuncio como marquesina en la parte inferior de la proyección.</summary>
    [RelayCommand]
    private void ComoMarquesina(TextoLibre? texto)
    {
        texto ??= Seleccionado;
        if (texto is null) return;
        var lineas = texto.Contenido.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _main.Proyeccion.Marquesina = string.Join("   ·   ", lineas.Length > 0 ? lineas : new[] { texto.Titulo });
        _main.MostrarMensaje($"«{texto.Titulo}» en la marquesina.");
    }
}

// ===================== Temas =====================

public sealed record TemaItem(Tema Tema, string Usos)
{
    public string Nombre => Tema.Nombre;
    public bool Predefinido => Tema.Predefinido;
}

public sealed partial class PanelTemasViewModel : ObservableObject
{
    public static readonly Diapositiva MuestraCancion = new()
    {
        Texto = "Santo, santo, santo,\nSeñor omnipotente",
        Etiqueta = "Muestra",
    };

    public static readonly Diapositiva MuestraVersiculo = new()
    {
        Texto = "Estad pues firmes, ceñidos vuestros lomos de verdad, y vestidos de la cota de justicia,",
        Versiculos = new[] { new SegmentoVersiculo(14, "Estad pues firmes, ceñidos vuestros lomos de verdad, y vestidos de la cota de justicia,") },
        Pie = "Efesios 6:14 · RV1909",
        Etiqueta = "Muestra",
    };

    private readonly MainViewModel _main;

    [ObservableProperty] private TemaItem? _seleccionado;

    public PanelTemasViewModel(MainViewModel main) => _main = main;

    public ObservableCollection<TemaItem> Temas { get; } = new();

    public Diapositiva Muestra => MuestraCancion;

    public void Recargar(long? seleccionar = null)
    {
        var id = seleccionar ?? Seleccionado?.Tema.Id;
        Temas.Clear();
        foreach (var tema in _main.Proveedor.Temas)
        {
            var usos = Enum.GetValues<TipoContenido>()
                .Where(t => _main.Proveedor.TemaPara(t).Id == tema.Id)
                .Select(t => t switch
                {
                    TipoContenido.Canciones => "Canciones",
                    TipoContenido.Biblia => "Biblia",
                    TipoContenido.Textos => "Textos",
                    _ => "Imágenes",
                });
            Temas.Add(new TemaItem(tema, string.Join(" · ", usos)));
        }
        Seleccionado = Temas.FirstOrDefault(t => t.Tema.Id == id);
    }

    [RelayCommand]
    private void Nuevo()
    {
        var baseTema = Seleccionado?.Tema ?? _main.Proveedor.TemaPara(TipoContenido.Canciones);
        var tema = baseTema.Clonar();
        tema.Id = 0;
        tema.Predefinido = false;
        tema.Nombre = "Mi tema";
        if (Ventanas.EditarTema(_main, tema)) Actualizar(tema.Id);
    }

    [RelayCommand]
    private void Editar(TemaItem? item)
    {
        item ??= Seleccionado;
        if (item is null) return;
        var tema = item.Tema.Clonar();
        if (Ventanas.EditarTema(_main, tema)) Actualizar(tema.Id);
    }

    [RelayCommand]
    private void Duplicar(TemaItem? item)
    {
        item ??= Seleccionado;
        if (item is null) return;
        var copia = item.Tema.Clonar();
        copia.Id = 0;
        copia.Predefinido = false;
        copia.Nombre = $"{item.Tema.Nombre} (copia)";
        _main.Ctx.Temas.Guardar(copia);
        Actualizar(copia.Id);
    }

    [RelayCommand]
    private void Eliminar(TemaItem? item)
    {
        item ??= Seleccionado;
        if (item is null) return;
        if (item.Predefinido)
        {
            _main.MostrarMensaje("Los temas incluidos no se pueden eliminar, pero sí editar o duplicar.");
            return;
        }
        if (!Dialogo.Confirmar("Eliminar tema", $"¿Eliminar el tema «{item.Nombre}»?", "Eliminar", peligro: true)) return;
        _main.Ctx.Temas.Eliminar(item.Tema.Id);
        foreach (var clave in _main.Ctx.Preferencias.TemasPorContenido.Where(p => p.Value == item.Tema.Id).Select(p => p.Key).ToList())
            _main.Ctx.Preferencias.TemasPorContenido.Remove(clave);
        _main.Ctx.GuardarPreferencias();
        Actualizar(null);
    }

    /// <summary>Asigna el tema elegido a un tipo de contenido (Canciones, Biblia, Textos, Imagenes).</summary>
    [RelayCommand]
    private void UsarPara(string? tipo)
    {
        if (Seleccionado is null || !Enum.TryParse<TipoContenido>(tipo, out var contenido)) return;
        _main.Ctx.Preferencias.TemasPorContenido[contenido] = Seleccionado.Tema.Id;
        _main.Ctx.GuardarPreferencias();
        Actualizar(Seleccionado.Tema.Id);
        _main.MostrarMensaje($"«{Seleccionado.Nombre}» se usará para {tipo!.ToLowerInvariant()}.");
    }

    private void Actualizar(long? seleccionar)
    {
        _main.Proveedor.RecargarTemas();
        Recargar(seleccionar);
    }
}
