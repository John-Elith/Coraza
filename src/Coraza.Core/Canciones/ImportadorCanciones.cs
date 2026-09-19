using System.Text.RegularExpressions;
using Coraza.Core.Modelos;

namespace Coraza.Core.Canciones;

/// <summary>
/// Importa canciones desde texto plano (con detección automática de coros y versos),
/// archivos de CCLI SongSelect (.txt) y ChordPro (.cho / .chordpro).
/// </summary>
public static partial class ImportadorCanciones
{
    public static Cancion DesdeTexto(string nombreArchivo, string contenido)
    {
        var texto = (contenido ?? "").Replace("\r\n", "\n").Replace('\r', '\n').TrimStart('﻿');
        var cancion = new Cancion { Titulo = nombreArchivo.Trim() };

        texto = EsChordPro(texto) ? ConvertirChordPro(texto, cancion) : ExtraerMetadatosSongSelect(texto, cancion);

        var resultado = AnalizadorLetra.Analizar(AnalizadorLetra.LimpiarTextoPegado(texto));
        cancion.Secciones = resultado.Secciones;
        cancion.Orden = resultado.Orden;
        if (string.IsNullOrWhiteSpace(cancion.Titulo))
            cancion.Titulo = cancion.Secciones.FirstOrDefault()?.Texto.Split('\n')[0] ?? "Sin título";
        return cancion;
    }

    public static bool EsChordPro(string texto) => DirectivaRegex().IsMatch(texto);

    private static string ConvertirChordPro(string texto, Cancion cancion)
    {
        var salida = new List<string>();
        foreach (var bruta in texto.Split('\n'))
        {
            var linea = bruta.Trim();
            if (linea.StartsWith('#')) continue;
            var m = DirectivaLineaRegex().Match(linea);
            if (m.Success)
            {
                var nombre = m.Groups["n"].Value.ToLowerInvariant();
                var valor = m.Groups["v"].Value.Trim();
                switch (nombre)
                {
                    case "title": case "t": cancion.Titulo = valor; break;
                    case "artist": case "composer": case "subtitle": case "st": cancion.Autor ??= valor; break;
                    case "key": cancion.Tonalidad = valor; break;
                    case "copyright": cancion.Derechos = valor; break;
                    case "ccli": cancion.Ccli = valor; break;
                    case "start_of_chorus": case "soc": salida.Add(""); salida.Add("[Coro]"); break;
                    case "start_of_verse": case "sov": salida.Add(""); salida.Add("[Verso]"); break;
                    case "start_of_bridge": case "sob": salida.Add(""); salida.Add("[Puente]"); break;
                    case "end_of_chorus": case "eoc": case "end_of_verse": case "eov": case "end_of_bridge": case "eob": salida.Add(""); break;
                    case "comment": case "c": case "comment_italic": case "ci":
                        if (CodigosSeccion.IntentarInterpretarEtiqueta(valor, out _, out _))
                        {
                            salida.Add("");
                            salida.Add($"[{valor.TrimEnd(':')}]");
                        }
                        break;
                }
                continue;
            }
            salida.Add(AcordeEnLineaRegex().Replace(bruta, ""));
        }
        return string.Join("\n", salida);
    }

    private static string ExtraerMetadatosSongSelect(string texto, Cancion cancion)
    {
        var lineas = texto.Split('\n').ToList();
        var conservadas = new List<string>();
        foreach (var linea in lineas)
        {
            var t = linea.Trim();
            var ccli = CcliCancionRegex().Match(t);
            if (ccli.Success)
            {
                cancion.Ccli = ccli.Groups[1].Value;
                continue;
            }
            if (t.StartsWith('©') || t.StartsWith("Copyright", StringComparison.OrdinalIgnoreCase))
            {
                cancion.Derechos = t.TrimStart('©').Replace("Copyright", "", StringComparison.OrdinalIgnoreCase).Trim();
                continue;
            }
            if (t.StartsWith("CCLI License", StringComparison.OrdinalIgnoreCase) || t.StartsWith("CCLI Licencia", StringComparison.OrdinalIgnoreCase)
                || t.StartsWith("For use solely", StringComparison.OrdinalIgnoreCase) || t.StartsWith("Note: Reproduction", StringComparison.OrdinalIgnoreCase))
                continue;
            conservadas.Add(linea);
        }

        // Si la primera línea es corta, va seguida de una línea en blanco y no se repite, es el título.
        var indices = conservadas.Select((l, i) => (l.Trim(), i)).Where(x => x.Item1.Length > 0).Take(2).ToList();
        if (indices.Count == 2)
        {
            var (primera, i0) = indices[0];
            var siguienteVacia = i0 + 1 < conservadas.Count && string.IsNullOrWhiteSpace(conservadas[i0 + 1]);
            var repetida = conservadas.Skip(i0 + 1).Any(l => string.Equals(l.Trim(), primera, StringComparison.OrdinalIgnoreCase));
            if (siguienteVacia && !repetida && primera.Length <= 60 && !CodigosSeccion.IntentarInterpretarEtiqueta(primera, out _, out _))
            {
                cancion.Titulo = primera;
                conservadas.RemoveAt(i0);
            }
        }
        return string.Join("\n", conservadas);
    }

    [GeneratedRegex(@"^\s*\{\s*(title|t|soc|start_of_chorus|artist|key|comment|c)\s*[:}]", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex DirectivaRegex();

    [GeneratedRegex(@"^\{\s*(?<n>[a-z_]+)\s*(?::\s*(?<v>[^}]*))?\}$", RegexOptions.IgnoreCase)]
    private static partial Regex DirectivaLineaRegex();

    [GeneratedRegex(@"\[[^\]\n]{1,10}\]")]
    private static partial Regex AcordeEnLineaRegex();

    [GeneratedRegex(@"^CCLI\s+(?:Song|Canci[oó]n)\s*(?:#|No\.?|N[º°])\s*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex CcliCancionRegex();
}
