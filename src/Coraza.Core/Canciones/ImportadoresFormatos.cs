using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Coraza.Core.Modelos;

namespace Coraza.Core.Canciones;

/// <summary>
/// Formatos de otros programas de proyección: OpenLyrics (OpenLP), OpenSong, ProPresenter 5/6 y CCLI SongSelect (.usr).
/// </summary>
public static partial class ImportadorCanciones
{
    /// <summary>Detecta el formato por el contenido y crea la canción.</summary>
    public static Cancion DesdeArchivo(string nombreArchivo, string contenido)
    {
        var inicio = (contenido ?? "").TrimStart('﻿', ' ', '\r', '\n', '\t');
        if (inicio.StartsWith('<'))
        {
            try
            {
                var raiz = XDocument.Parse(inicio).Root;
                if (raiz is not null)
                {
                    if (raiz.Name.LocalName == "song" && raiz.Name.NamespaceName.Contains("openlyrics", StringComparison.OrdinalIgnoreCase))
                        return DesdeOpenLyrics(raiz, nombreArchivo);
                    if (raiz.Name.LocalName == "song") return DesdeOpenSong(raiz, nombreArchivo);
                    if (raiz.Name.LocalName.StartsWith("RVPresentationDocument", StringComparison.Ordinal))
                        return DesdeProPresenter(raiz, nombreArchivo);
                }
            }
            catch (XmlException)
            {
                // No es XML válido: se trata como texto.
            }
        }
        if (inicio.StartsWith("[File]", StringComparison.OrdinalIgnoreCase) || SeccionUsrRegex().IsMatch(inicio))
            return DesdeSongSelectUsr(inicio, nombreArchivo);
        return DesdeTexto(nombreArchivo, contenido ?? "");
    }

    // ------------------------------------------------------------------ OpenLyrics (OpenLP)

    private static Cancion DesdeOpenLyrics(XElement raiz, string nombre)
    {
        var ns = raiz.Name.Namespace;
        var p = raiz.Element(ns + "properties");
        var autores = p?.Element(ns + "authors")?.Elements(ns + "author").Select(a => a.Value.Trim()).Where(a => a.Length > 0).ToList() ?? new List<string>();
        var cancion = new Cancion
        {
            Titulo = Valor(p?.Element(ns + "titles")?.Elements(ns + "title").FirstOrDefault()) ?? nombre,
            Autor = autores.Count > 0 ? string.Join(", ", autores) : null,
            Derechos = Valor(p?.Element(ns + "copyright")),
            Ccli = Valor(p?.Element(ns + "ccliNo")),
            Tonalidad = Valor(p?.Element(ns + "key")),
            Tempo = int.TryParse(Valor(p?.Element(ns + "tempo")), out var tempo) ? tempo : null,
        };

        var texto = new StringBuilder();
        foreach (var verso in raiz.Element(ns + "lyrics")?.Elements(ns + "verse") ?? Enumerable.Empty<XElement>())
        {
            var bloques = verso.Elements(ns + "lines").Select(LineasOpenLyrics).Where(b => b.Length > 0).ToList();
            if (bloques.Count == 0) continue;
            AgregarSeccion(texto, EtiquetaOpenLyrics((string?)verso.Attribute("name") ?? "v"), string.Join("\n---\n", bloques));
        }
        return Completar(cancion, texto.ToString(), Valor(p?.Element(ns + "verseOrder")), EtiquetaOpenLyrics);
    }

    private static string LineasOpenLyrics(XElement lineas)
    {
        var sb = new StringBuilder();
        foreach (var nodo in lineas.DescendantNodes())
        {
            switch (nodo)
            {
                case XElement { Name.LocalName: "br" }:
                    sb.Append('\n');
                    break;
                case XElement { Name.LocalName: "line" } when sb.Length > 0:
                    sb.Append('\n');
                    break;
                case XText texto when texto.Parent?.Name.LocalName != "comment":
                    sb.Append(texto.Value);
                    break;
            }
        }
        return string.Join("\n", sb.ToString().Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));
    }

    private static string EtiquetaOpenLyrics(string nombre)
    {
        var m = CodigoLetraNumeroRegex().Match(nombre.Trim().ToLowerInvariant());
        var tipo = (m.Success ? m.Groups[1].Value : "v") switch
        {
            "c" => "Coro",
            "b" => "Puente",
            "p" => "Pre-coro",
            "e" => "Final",
            "i" => "Intro",
            "o" => "Otro",
            _ => "Verso",
        };
        return m.Success && m.Groups[2].Value.Length > 0 ? $"{tipo} {m.Groups[2].Value}" : tipo;
    }

    // ------------------------------------------------------------------ OpenSong

    private static Cancion DesdeOpenSong(XElement raiz, string nombre)
    {
        var cancion = new Cancion
        {
            Titulo = Valor(raiz.Element("title")) ?? nombre,
            Autor = Valor(raiz.Element("author")),
            Derechos = Valor(raiz.Element("copyright")),
            Ccli = Valor(raiz.Element("ccli")),
            Tonalidad = Valor(raiz.Element("key")),
        };
        var texto = new StringBuilder();
        foreach (var bruta in (raiz.Element("lyrics")?.Value ?? "").Replace("\r", "").Split('\n'))
        {
            if (bruta.StartsWith('.') || bruta.StartsWith(';')) continue; // acordes y comentarios
            var m = EncabezadoOpenSongRegex().Match(bruta.Trim());
            if (m.Success)
            {
                texto.Append("\n[").Append(EtiquetaOpenSong(m.Groups[1].Value + m.Groups[2].Value)).Append("]\n");
                continue;
            }
            // «||» fuerza un cambio de diapositiva y «|» separa líneas.
            texto.Append(bruta.Trim().Replace("||", "\n---\n").Replace("|", "\n")).Append('\n');
        }
        return Completar(cancion, texto.ToString(), Valor(raiz.Element("presentation")), EtiquetaOpenSong);
    }

    private static string EtiquetaOpenSong(string codigo)
    {
        var m = CodigoLetraNumeroRegex().Match(codigo.Trim().ToLowerInvariant());
        var tipo = (m.Success ? m.Groups[1].Value : "v") switch
        {
            "c" => "Coro",
            "b" => "Puente",
            "p" => "Pre-coro",
            "t" => "Otro",
            "e" => "Final",
            "i" => "Intro",
            _ => "Verso",
        };
        return m.Success && m.Groups[2].Value.Length > 0 ? $"{tipo} {m.Groups[2].Value}" : tipo;
    }

    // ------------------------------------------------------------------ ProPresenter 5 / 6

    private static Cancion DesdeProPresenter(XElement raiz, string nombre)
    {
        string? Atributo(string n) => ((string?)raiz.Attribute(n))?.Trim() is { Length: > 0 } s ? s : null;
        var editorial = Atributo("CCLIPublisher");
        var cancion = new Cancion
        {
            Titulo = Atributo("CCLISongTitle") ?? nombre,
            Autor = Atributo("CCLIAuthor") ?? Atributo("CCLIArtistCredits"),
            Ccli = Atributo("CCLISongNumber"),
            Derechos = editorial is null ? null : $"{Atributo("CCLICopyrightYear")} {editorial}".Trim(),
        };
        var texto = new StringBuilder();
        foreach (var grupo in raiz.Descendants().Where(e => e.Name.LocalName == "RVSlideGrouping"))
        {
            var diapositivas = grupo.Descendants().Where(e => e.Name.LocalName == "RVDisplaySlide").Select(TextoDiapositivaPro).Where(t => t.Length > 0).ToList();
            if (diapositivas.Count == 0) continue;
            AgregarSeccion(texto, (string?)grupo.Attribute("name") ?? "Verso", string.Join("\n---\n", diapositivas));
        }
        return Completar(cancion, texto.ToString(), null, e => e);
    }

    private static string TextoDiapositivaPro(XElement diapositiva)
    {
        foreach (var elemento in diapositiva.Descendants().Where(e => e.Name.LocalName == "RVTextElement"))
        {
            var plano = elemento.Elements().FirstOrDefault(e => (string?)e.Attribute("rvXMLIvarName") == "PlainText")?.Value;
            if (DesdeBase64(plano) is { Length: > 0 } t) return LimpiarLineas(t);
            var rtf = (string?)elemento.Attribute("RTFData")
                      ?? elemento.Elements().FirstOrDefault(e => (string?)e.Attribute("rvXMLIvarName") == "RTFData")?.Value;
            if (DesdeBase64(rtf) is { Length: > 0 } r) return LimpiarLineas(QuitarRtf(r));
        }
        return "";
    }

    private static string? DesdeBase64(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return null;
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(valor.Trim()));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>Extrae el texto de un documento RTF (se ignoran tablas de fuentes y colores).</summary>
    internal static string QuitarRtf(string rtf)
    {
        var sb = new StringBuilder();
        var pila = new Stack<bool>();
        var ignorar = false;
        var i = 0;
        while (i < rtf.Length)
        {
            var c = rtf[i];
            if (c == '{')
            {
                pila.Push(ignorar);
                i++;
                if (i + 1 < rtf.Length && rtf[i] == '\\')
                {
                    var m = RtfPalabraRegex().Match(rtf, i);
                    if (rtf[i + 1] == '*' || (m.Success && m.Groups[1].Value is "fonttbl" or "colortbl" or "stylesheet" or "info" or "pict"))
                        ignorar = true;
                }
                continue;
            }
            if (c == '}')
            {
                ignorar = pila.Count > 0 && pila.Pop();
                i++;
                continue;
            }
            if (c == '\\' && i + 1 < rtf.Length)
            {
                var siguiente = rtf[i + 1];
                if (siguiente == '\'' && i + 3 < rtf.Length)
                {
                    if (!ignorar) sb.Append(Encoding.Latin1.GetString(new[] { Convert.ToByte(rtf.Substring(i + 2, 2), 16) }));
                    i += 4;
                    continue;
                }
                if (siguiente is '\\' or '{' or '}')
                {
                    if (!ignorar) sb.Append(siguiente);
                    i += 2;
                    continue;
                }
                var palabra = RtfPalabraRegex().Match(rtf, i);
                if (palabra.Success)
                {
                    i += palabra.Length;
                    if (ignorar) continue;
                    switch (palabra.Groups[1].Value)
                    {
                        case "par" or "line":
                            sb.Append('\n');
                            break;
                        case "tab":
                            sb.Append(' ');
                            break;
                        case "u" when int.TryParse(palabra.Groups[2].Value, out var punto):
                            sb.Append((char)(ushort)(short)punto);
                            if (i < rtf.Length && rtf[i] == '?') i++;
                            break;
                    }
                    continue;
                }
                i++;
                continue;
            }
            if (!ignorar && c != '\r' && c != '\n') sb.Append(c);
            i++;
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------------ CCLI SongSelect (.usr)

    private static Cancion DesdeSongSelectUsr(string contenido, string nombre)
    {
        var valores = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? ccli = null;
        foreach (var linea in contenido.Replace("\r", "").Split('\n'))
        {
            var m = SeccionUsrRegex().Match(linea);
            if (m.Success)
            {
                ccli = m.Groups[1].Value;
                continue;
            }
            var igual = linea.IndexOf('=');
            if (igual > 0) valores[linea[..igual].Trim()] = linea[(igual + 1)..];
        }
        string? Campo(string clave) => valores.TryGetValue(clave, out var v) && v.Trim().Length > 0 ? v.Replace(" | ", ", ").Trim() : null;
        var cancion = new Cancion
        {
            Titulo = Campo("Title") ?? nombre,
            Autor = Campo("Author"),
            Derechos = Campo("Copyright"),
            Ccli = ccli,
            Tonalidad = Campo("Keys"),
        };
        var etiquetas = (Campo("Fields") ?? "").Split("/t");
        var palabras = (valores.GetValueOrDefault("Words") ?? "").Split("/t");
        var texto = new StringBuilder();
        for (var i = 0; i < palabras.Length; i++)
        {
            var letra = palabras[i].Replace("/n", "\n").Trim();
            if (letra.Length == 0) continue;
            AgregarSeccion(texto, i < etiquetas.Length && etiquetas[i].Trim().Length > 0 ? etiquetas[i].Trim() : "Verse", letra);
        }
        return Completar(cancion, texto.ToString(), null, e => e);
    }

    // ------------------------------------------------------------------ Utilidades

    private static void AgregarSeccion(StringBuilder texto, string etiqueta, string letra)
    {
        // Las etiquetas desconocidas («Misc 1») se guardan como «Otro» para no confundirlas con la letra.
        var valida = CodigosSeccion.IntentarInterpretarEtiqueta($"[{etiqueta}]", out _, out _) ? etiqueta : "Otro";
        texto.Append('[').Append(valida).Append("]\n").Append(letra.Trim()).Append("\n\n");
    }

    private static Cancion Completar(Cancion cancion, string texto, string? orden, Func<string, string> etiqueta)
    {
        var resultado = AnalizadorLetra.Analizar(texto);
        cancion.Secciones = resultado.Secciones;
        cancion.Orden = resultado.Orden;
        if (!string.IsNullOrWhiteSpace(orden))
        {
            var codigos = orden.Split(new[] { ' ', ',', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => CodigosSeccion.IntentarInterpretarEtiqueta($"[{etiqueta(t)}]", out var tipo, out var n) ? CodigosSeccion.Codigo(tipo, n) : t);
            var validos = AnalizadorLetra.InterpretarOrden(string.Join(" ", codigos), resultado.Secciones).Validos;
            if (validos.Count > 0) cancion.Orden = validos;
        }
        if (string.IsNullOrWhiteSpace(cancion.Titulo)) cancion.Titulo = "Sin título";
        return cancion;
    }

    private static string? Valor(XElement? elemento) => elemento?.Value.Trim() is { Length: > 0 } v ? v : null;

    private static string LimpiarLineas(string texto) =>
        string.Join("\n", texto.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));

    [GeneratedRegex(@"^([a-z]+)(\d*)[a-z]?$")]
    private static partial Regex CodigoLetraNumeroRegex();

    [GeneratedRegex(@"^\[([A-Za-z])(\d*)\]$")]
    private static partial Regex EncabezadoOpenSongRegex();

    [GeneratedRegex(@"^\[S\s*A?(\d+)\]", RegexOptions.Multiline)]
    private static partial Regex SeccionUsrRegex();

    [GeneratedRegex(@"\G\\([a-zA-Z]+)(-?\d+)? ?")]
    private static partial Regex RtfPalabraRegex();
}
