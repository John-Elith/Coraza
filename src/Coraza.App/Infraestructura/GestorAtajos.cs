using System.Windows.Input;

namespace Coraza.App.Infraestructura;

public enum AccionAtajo
{
    Siguiente,
    Anterior,
    SiguienteElemento,
    ElementoAnterior,
    EnviarAlVivo,
    PantallaNegra,
    PantallaLogo,
    OcultarTexto,
    Limpiar,
    Buscar,
    IrBiblia,
    NuevaCancion,
    AlternarProyeccion,
    ModoConcentracion,
    IrCoro,
    IrVerso,
    IrPuente,
    IrPreCoro,
    IrFinal,
    PatronPrueba,
}

public readonly record struct Gesto(Key Tecla, ModifierKeys Modificadores)
{
    public override string ToString()
    {
        var partes = new List<string>();
        if (Modificadores.HasFlag(ModifierKeys.Control)) partes.Add("Ctrl");
        if (Modificadores.HasFlag(ModifierKeys.Shift)) partes.Add("Shift");
        if (Modificadores.HasFlag(ModifierKeys.Alt)) partes.Add("Alt");
        partes.Add(Tecla.ToString());
        return string.Join("+", partes);
    }

    /// <summary>Texto amigable en español: «→», «Espacio», «Av Pág», «Ctrl + K».</summary>
    public string Legible()
    {
        var partes = new List<string>();
        if (Modificadores.HasFlag(ModifierKeys.Control)) partes.Add("Ctrl");
        if (Modificadores.HasFlag(ModifierKeys.Shift)) partes.Add("Mayús");
        if (Modificadores.HasFlag(ModifierKeys.Alt)) partes.Add("Alt");
        partes.Add(Tecla switch
        {
            Key.Right => "→",
            Key.Left => "←",
            Key.Up => "↑",
            Key.Down => "↓",
            Key.Space => "Espacio",
            Key.PageDown => "Av Pág",
            Key.PageUp => "Re Pág",
            Key.Escape => "Esc",
            Key.Enter => "Enter",
            Key.OemPeriod => ".",
            Key.Oem2 => "/",
            _ => Tecla.ToString(),
        });
        return string.Join(" + ", partes);
    }

    public static bool IntentarParsear(string texto, out Gesto gesto)
    {
        gesto = default;
        var modificadores = ModifierKeys.None;
        Key? tecla = null;
        foreach (var parte in texto.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (parte.ToLowerInvariant())
            {
                case "ctrl": case "control": modificadores |= ModifierKeys.Control; break;
                case "shift": case "mayús": case "mayus": modificadores |= ModifierKeys.Shift; break;
                case "alt": modificadores |= ModifierKeys.Alt; break;
                default:
                    if (Enum.TryParse<Key>(parte, true, out var k)) tecla = k;
                    else return false;
                    break;
            }
        }
        if (tecla is null) return false;
        gesto = new Gesto(tecla.Value, modificadores);
        return true;
    }
}

/// <summary>Atajos de teclado personalizables. Acepta presentadores inalámbricos (envían Av Pág / Re Pág / B / .).</summary>
public sealed class GestorAtajos
{
    public static readonly IReadOnlyDictionary<AccionAtajo, string> Nombres = new Dictionary<AccionAtajo, string>
    {
        [AccionAtajo.Siguiente] = "Siguiente diapositiva",
        [AccionAtajo.Anterior] = "Diapositiva anterior",
        [AccionAtajo.SiguienteElemento] = "Siguiente elemento del servicio",
        [AccionAtajo.ElementoAnterior] = "Elemento anterior del servicio",
        [AccionAtajo.EnviarAlVivo] = "Enviar la vista previa al vivo",
        [AccionAtajo.PantallaNegra] = "Pantalla negra",
        [AccionAtajo.PantallaLogo] = "Pantalla de logotipo",
        [AccionAtajo.OcultarTexto] = "Ocultar solo el texto",
        [AccionAtajo.Limpiar] = "Limpiar todo (en pantalla completa, Esc sale de ella)",
        [AccionAtajo.Buscar] = "Búsqueda universal",
        [AccionAtajo.IrBiblia] = "Ir al módulo Biblia",
        [AccionAtajo.NuevaCancion] = "Nueva canción",
        [AccionAtajo.AlternarProyeccion] = "Iniciar o detener la proyección",
        [AccionAtajo.ModoConcentracion] = "Modo concentración",
        [AccionAtajo.IrCoro] = "Saltar al coro",
        [AccionAtajo.IrVerso] = "Saltar a un verso (V y el número)",
        [AccionAtajo.IrPuente] = "Saltar al puente",
        [AccionAtajo.IrPreCoro] = "Saltar al pre-coro",
        [AccionAtajo.IrFinal] = "Saltar al final",
        [AccionAtajo.PatronPrueba] = "Patrón de prueba",
    };

    public static readonly IReadOnlyDictionary<AccionAtajo, string> Predeterminados = new Dictionary<AccionAtajo, string>
    {
        [AccionAtajo.Siguiente] = "Right, Space, PageDown",
        [AccionAtajo.Anterior] = "Left, PageUp",
        [AccionAtajo.SiguienteElemento] = "Down",
        [AccionAtajo.ElementoAnterior] = "Up",
        [AccionAtajo.EnviarAlVivo] = "Enter",
        [AccionAtajo.PantallaNegra] = "B, OemPeriod",
        [AccionAtajo.PantallaLogo] = "L",
        [AccionAtajo.OcultarTexto] = "T",
        [AccionAtajo.Limpiar] = "Escape, Shift+Escape",
        [AccionAtajo.Buscar] = "Ctrl+K, Oem2",
        [AccionAtajo.IrBiblia] = "Ctrl+B",
        [AccionAtajo.NuevaCancion] = "Ctrl+N",
        [AccionAtajo.AlternarProyeccion] = "F5",
        [AccionAtajo.ModoConcentracion] = "F11",
        [AccionAtajo.IrCoro] = "C",
        [AccionAtajo.IrVerso] = "V",
        [AccionAtajo.IrPuente] = "P",
        [AccionAtajo.IrPreCoro] = "R",
        [AccionAtajo.IrFinal] = "F",
        [AccionAtajo.PatronPrueba] = "Ctrl+Shift+P",
    };

    private readonly Dictionary<Gesto, AccionAtajo> _mapa = new();
    private readonly Dictionary<AccionAtajo, List<Gesto>> _porAccion = new();

    public GestorAtajos(IReadOnlyDictionary<string, string>? personalizados)
    {
        foreach (var accion in Enum.GetValues<AccionAtajo>())
        {
            var texto = personalizados is not null && personalizados.TryGetValue(accion.ToString(), out var p)
                ? p
                : Predeterminados.GetValueOrDefault(accion, "");
            var gestos = Parsear(texto);
            _porAccion[accion] = gestos;
            foreach (var g in gestos) _mapa.TryAdd(g, accion);
        }
    }

    public AccionAtajo? Buscar(Key tecla, ModifierKeys modificadores) =>
        _mapa.TryGetValue(new Gesto(tecla, modificadores), out var accion) ? accion : null;

    public IReadOnlyList<Gesto> GestosDe(AccionAtajo accion) => _porAccion.GetValueOrDefault(accion) ?? new List<Gesto>();

    public string TextoLegible(AccionAtajo accion) => string.Join(" / ", GestosDe(accion).Select(g => g.Legible()));

    public static List<Gesto> Parsear(string? texto)
    {
        var lista = new List<Gesto>();
        foreach (var parte in (texto ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Gesto.IntentarParsear(parte, out var g)) lista.Add(g);
        }
        return lista;
    }

    public static string Serializar(IEnumerable<Gesto> gestos) => string.Join(", ", gestos.Select(g => g.ToString()));

    /// <summary>¿Es una tecla que un cuadro de texto necesita para escribir (letras, espacio, flechas…)?</summary>
    public static bool TeclaDeEscritura(Key tecla, ModifierKeys modificadores)
    {
        if (modificadores.HasFlag(ModifierKeys.Control) || modificadores.HasFlag(ModifierKeys.Alt)) return false;
        return tecla is >= Key.A and <= Key.Z
            or >= Key.D0 and <= Key.D9
            or >= Key.NumPad0 and <= Key.NumPad9
            or Key.Space or Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End
            or Key.PageUp or Key.PageDown or Key.Back or Key.Delete or Key.Enter or Key.Escape or Key.Tab
            or >= Key.Oem1 and <= Key.Oem102;
    }
}
