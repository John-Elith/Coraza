namespace Coraza.Core.Busqueda;

/// <summary>
/// Índice en memoria para la búsqueda instantánea de canciones por título, primera línea
/// o cualquier palabra de la letra, tolerante a tildes y a errores de escritura.
/// </summary>
public sealed class IndiceCanciones
{
    private sealed record Entrada(
        long Id,
        string Titulo,
        string TituloNormal,
        string PrimeraLineaNormal,
        string LetraNormal,
        string[] PalabrasTitulo,
        string[] PalabrasLetra);

    private readonly Dictionary<long, Entrada> _entradas = new();
    private readonly object _candado = new();

    public int Cantidad
    {
        get { lock (_candado) return _entradas.Count; }
    }

    public void Actualizar(long id, string titulo, string letra)
    {
        var tituloNormal = Normalizador.Normalizar(titulo);
        var primera = letra.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
        var letraNormal = Normalizador.Normalizar(letra);
        var entrada = new Entrada(
            id,
            titulo,
            tituloNormal,
            Normalizador.Normalizar(primera),
            letraNormal,
            tituloNormal.Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct().ToArray(),
            letraNormal.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(p => p.Length > 2).Distinct().ToArray());
        lock (_candado) _entradas[id] = entrada;
    }

    public void Quitar(long id)
    {
        lock (_candado) _entradas.Remove(id);
    }

    public void Limpiar()
    {
        lock (_candado) _entradas.Clear();
    }

    /// <summary>Devuelve los ids ordenados por relevancia. Todas las palabras de la consulta deben coincidir.</summary>
    public IReadOnlyList<long> Buscar(string? consulta, int maximo = 200)
    {
        var frase = Normalizador.Normalizar(consulta);
        if (frase.Length == 0) return Array.Empty<long>();
        var palabras = frase.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        Entrada[] entradas;
        lock (_candado) entradas = _entradas.Values.ToArray();

        var resultados = new List<(Entrada Entrada, double Puntaje)>();
        foreach (var e in entradas)
        {
            var puntaje = Puntuar(e, frase, palabras);
            if (puntaje > 0) resultados.Add((e, puntaje));
        }

        return resultados
            .OrderByDescending(r => r.Puntaje)
            .ThenBy(r => r.Entrada.Titulo, StringComparer.CurrentCultureIgnoreCase)
            .Take(maximo)
            .Select(r => r.Entrada.Id)
            .ToList();
    }

    private static double Puntuar(Entrada e, string frase, string[] palabras)
    {
        double total = 0;

        if (e.TituloNormal == frase) total += 60;
        else if (e.TituloNormal.StartsWith(frase, StringComparison.Ordinal)) total += 35;
        else if (e.TituloNormal.Contains(frase, StringComparison.Ordinal)) total += 25;
        if (e.PrimeraLineaNormal.Contains(frase, StringComparison.Ordinal)) total += 12;
        else if (palabras.Length > 1 && e.LetraNormal.Contains(frase, StringComparison.Ordinal)) total += 8;

        foreach (var palabra in palabras)
        {
            var p = PuntuarPalabra(e, palabra);
            if (p <= 0) return 0; // búsqueda tipo «Y»: cada palabra debe aparecer
            total += p;
        }
        return total;
    }

    private static double PuntuarPalabra(Entrada e, string palabra)
    {
        if (e.PalabrasTitulo.Any(t => t.StartsWith(palabra, StringComparison.Ordinal))) return 10;
        if (e.TituloNormal.Contains(palabra, StringComparison.Ordinal)) return 8;
        if (e.PrimeraLineaNormal.Contains(palabra, StringComparison.Ordinal)) return 5;
        if (e.LetraNormal.Contains(palabra, StringComparison.Ordinal)) return 3;

        var tolerancia = Normalizador.Tolerancia(palabra.Length);
        if (tolerancia == 0) return 0;
        if (e.PalabrasTitulo.Any(t => Normalizador.Levenshtein(t, palabra, tolerancia) <= tolerancia)) return 4;
        if (e.PalabrasLetra.Any(t => Normalizador.Levenshtein(t, palabra, tolerancia) <= tolerancia)) return 1.5;
        return 0;
    }
}
