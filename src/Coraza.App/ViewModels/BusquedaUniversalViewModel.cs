using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coraza.App.Servicios;
using Coraza.Core.Biblia;
using Coraza.Core.Busqueda;

namespace Coraza.App.ViewModels;

public sealed record ResultadoBusqueda(string Grupo, string Titulo, string? Detalle, string ClaveIcono, Action Ejecutar)
{
    public Geometry? Icono => Application.Current.TryFindResource(ClaveIcono) as Geometry;
}

/// <summary>
/// Buscador único (Ctrl + K): encuentra canciones, versículos, medios, textos, servicios y comandos.
/// «juan 3 16» muestra el versículo, «cuan grande» la canción y «negro» el comando de pantalla negra.
/// </summary>
public sealed partial class BusquedaUniversalViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    [ObservableProperty] private bool _abierta;
    [ObservableProperty] private string _consulta = "";
    [ObservableProperty] private ResultadoBusqueda? _seleccionado;

    public BusquedaUniversalViewModel(MainViewModel main) => _main = main;

    public ObservableCollection<ResultadoBusqueda> Resultados { get; } = new();

    public void Abrir()
    {
        Consulta = "";
        Actualizar();
        Abierta = true;
    }

    [RelayCommand]
    private void Cerrar() => Abierta = false;

    partial void OnConsultaChanged(string value) => Actualizar();

    public void Mover(int delta)
    {
        if (Resultados.Count == 0) return;
        var indice = Seleccionado is null ? -1 : Resultados.IndexOf(Seleccionado);
        Seleccionado = Resultados[Math.Clamp(indice + delta, 0, Resultados.Count - 1)];
    }

    [RelayCommand]
    private void Ejecutar(ResultadoBusqueda? resultado)
    {
        resultado ??= Seleccionado;
        if (resultado is null) return;
        Abierta = false;
        resultado.Ejecutar();
    }

    private void Actualizar()
    {
        Resultados.Clear();
        var consulta = Consulta.Trim();
        var palabras = Normalizador.Palabras(consulta);

        var version = _main.Biblia.Version;
        if (consulta.Length > 0 && version is not null && AnalizadorReferencias.Analizar(consulta) is { } referencia)
        {
            var versiculos = _main.Ctx.Biblias.ObtenerPasaje(version.Id, referencia);
            if (versiculos.Count > 0)
            {
                Resultados.Add(new ResultadoBusqueda("Biblia", $"{referencia} · {version.Abreviatura}", Recortar(versiculos[0].Texto),
                    "Ico.Biblia", () => _main.MostrarElemento(FabricaElementos.DePasaje(version, referencia))));
            }
        }

        foreach (var comando in Comandos())
        {
            if (palabras.Length == 0 || palabras.All(p => comando.Claves.Any(c => c.StartsWith(p, StringComparison.Ordinal))))
                Resultados.Add(comando.Resultado);
        }

        if (palabras.Length > 0)
        {
            foreach (var id in _main.Ctx.IndiceCanciones.Buscar(consulta, 8))
            {
                if (_main.Canciones.PorId(id) is not { } cancion) continue;
                Resultados.Add(new ResultadoBusqueda("Canción", cancion.Titulo, cancion.PrimeraLinea, "Ico.Musica",
                    () => _main.MostrarElemento(FabricaElementos.DeCancion(cancion))));
            }

            var normal = string.Join(" ", palabras);
            foreach (var medio in _main.Medios.Medios.Where(m => Normalizador.Normalizar(m.Nombre).Contains(normal)).Take(5))
            {
                Resultados.Add(new ResultadoBusqueda("Imagen", medio.Nombre, medio.Detalle, "Ico.Imagen",
                    () => _main.MostrarElemento(FabricaElementos.DeMedio(medio.Modelo))));
            }
            foreach (var texto in _main.Textos.Textos.Where(t => Normalizador.Normalizar(t.Titulo).Contains(normal)).Take(5))
            {
                Resultados.Add(new ResultadoBusqueda("Texto", texto.Titulo, Recortar(texto.Contenido), "Ico.Texto",
                    () => _main.MostrarElemento(FabricaElementos.DeTexto(texto))));
            }
            foreach (var servicio in _main.Ctx.Servicios.Listar().Where(s => Normalizador.Normalizar(s.Nombre).Contains(normal)).Take(4))
            {
                var id = servicio.Id;
                Resultados.Add(new ResultadoBusqueda("Servicio", servicio.Nombre, servicio.Fecha.ToString("d"), "Ico.Lista", () =>
                {
                    if (_main.Ctx.Servicios.Obtener(id) is { } s) _main.AbrirServicio(s);
                }));
            }

            if (version is not null && consulta.Length >= 4 && !AnalizadorReferencias.PareceReferencia(consulta))
            {
                foreach (var v in _main.Ctx.Biblias.Buscar(version.Id, consulta, limite: 5))
                {
                    var r = new Coraza.Core.Modelos.ReferenciaBiblica(v.Libro, v.Capitulo, v.Numero);
                    Resultados.Add(new ResultadoBusqueda("Versículo", $"{r} · {version.Abreviatura}", Recortar(v.Texto), "Ico.Biblia",
                        () => _main.MostrarElemento(FabricaElementos.DePasaje(version, r))));
                }
            }
        }

        Seleccionado = Resultados.FirstOrDefault();
    }

    private sealed record Comando(string[] Claves, ResultadoBusqueda Resultado);

    private IEnumerable<Comando> Comandos()
    {
        Comando C(string titulo, string claves, string icono, string atajo, Action accion) =>
            new(Normalizador.Palabras(titulo + " " + claves), new ResultadoBusqueda("Comando", titulo, atajo, icono, accion));

        yield return C("Pantalla negra", "negro oscuro apagar panico", "Ico.MonitorApagado", "B", () => _main.AlternarNegroCommand.Execute(null));
        yield return C("Pantalla de logotipo", "logo escudo", "Ico.Escudo", "L", () => _main.AlternarLogoCommand.Execute(null));
        yield return C("Ocultar solo el texto", "texto letra ocultar", "Ico.OjoCerrado", "T", () => _main.AlternarTextoCommand.Execute(null));
        yield return C("Limpiar pantalla", "limpiar borrar quitar", "Ico.Cerrar", "Esc", () => _main.LimpiarCommand.Execute(null));
        yield return C(_main.Proyeccion.Proyectando ? "Detener la proyección" : "Iniciar la proyección", "proyectar video beam proyector salida",
            "Ico.Monitor", "F5", () => _main.AlternarProyeccion());
        yield return C("Nueva canción", "crear cancion", "Ico.Mas", "Ctrl+N", () => _main.Canciones.NuevaCommand.Execute(null));
        yield return C("Marquesina", "marquesina anuncio texto desplazar cinta", "Ico.Texto", "", () => _main.EditarMarquesinaCommand.Execute(null));
        yield return C("Mensaje urgente", "mensaje urgente alerta aviso padres guarderia", "Ico.Rayo", "", () => _main.EnviarMensajeUrgenteCommand.Execute(null));
        yield return C("Mostrar u ocultar el reloj", "reloj hora", "Ico.Reloj", "", () => _main.AlternarRelojCommand.Execute(null));
        yield return C("Agregar una cuenta regresiva", "cuenta regresiva temporizador inicio espera", "Ico.Reloj", "", () => _main.AgregarCuentaRegresivaCommand.Execute(null));
        yield return C("Pegar letra desde el portapapeles", "pegar letra web", "Ico.Pegar", "", () => _main.Canciones.PegarLetraCommand.Execute(null));
        yield return C("Patrón de prueba", "patron prueba cuadricula margenes proyector", "Ico.Rejilla", "", () => _main.AlternarPatronPruebaCommand.Execute(null));
        yield return C("Nuevo servicio", "servicio crear orden", "Ico.Lista", "", () => _main.NuevoServicioCommand.Execute(null));
        yield return C("Abrir otro servicio", "servicio abrir", "Ico.Carpeta", "", () => _main.AbrirOtroServicioCommand.Execute(null));
        yield return C("Configuración", "ajustes opciones preferencias atajos logo", "Ico.Ajustes", "", () => _main.AbrirConfiguracionCommand.Execute(null));
        yield return C("Cambiar la apariencia (oscuro / claro)", "apariencia modo oscuro claro grafito vino lino colores paleta", "Ico.Luna", "", () => _main.AlternarAparienciaCommand.Execute(null));
        yield return C("Cambiar Windows a modo Extender", "extender duplicar pantallas", "Ico.Monitor", "Win+P", () => _main.CambiarAExtenderCommand.Execute(null));
    }

    private static string Recortar(string texto, int maximo = 90)
    {
        var plano = texto.Replace('\n', ' ');
        return plano.Length <= maximo ? plano : plano[..maximo].TrimEnd() + "…";
    }
}
