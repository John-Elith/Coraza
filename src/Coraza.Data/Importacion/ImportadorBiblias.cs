using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Coraza.Core.Biblia;
using Microsoft.Data.Sqlite;

namespace Coraza.Data.Importacion;

public sealed record VersiculoImportado(int Libro, int Capitulo, int Versiculo, string Texto);

public sealed record BibliaImportada(string? Nombre, string? Abreviatura, string? Idioma, IReadOnlyList<VersiculoImportado> Versiculos);

/// <summary>
/// Lee Biblias en los formatos más comunes para que el usuario importe las versiones de las que tiene licencia:
/// VPL (eBible.org, BibleWorks), Zefania XML, OSIS XML, USFM (archivos sueltos o en ZIP),
/// e-Sword (.bblx), MySword (.bbl.mybible) y MyBible (.SQLite3). Acepta archivos comprimidos con gzip.
/// </summary>
public static partial class ImportadorBiblias
{
    public static BibliaImportada Leer(string ruta)
    {
        if (EsSqlite(ruta)) return LeerSqlite(ruta);
        if (ruta.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return LeerZip(ruta);
        using var archivo = File.OpenRead(ruta);
        Stream flujo = ruta.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
            ? new GZipStream(archivo, CompressionMode.Decompress)
            : archivo;
        using (flujo)
        {
            return Leer(flujo);
        }
    }

    public static BibliaImportada Leer(Stream flujo)
    {
        using var memoria = new MemoryStream();
        flujo.CopyTo(memoria);
        var bytes = memoria.ToArray();
        var inicio = Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 4096));
        if (inicio.Contains("<XMLBIBLE", StringComparison.OrdinalIgnoreCase)) return LeerZefania(new MemoryStream(bytes));
        if (inicio.Contains("<osis", StringComparison.OrdinalIgnoreCase)) return LeerOsis(new MemoryStream(bytes));
        var texto = Decodificar(bytes);
        if (UsfmIdRegex().IsMatch(texto)) return LeerUsfm(new[] { texto });
        return LeerVpl(new StringReader(texto));
    }

    // ------------------------------------------------------------------ VPL

    public static BibliaImportada LeerVpl(TextReader lector)
    {
        var versiculos = new List<VersiculoImportado>();
        string? linea;
        while ((linea = lector.ReadLine()) is not null)
        {
            var m = VplRegex().Match(linea);
            if (!m.Success) continue;
            var libro = LibrosBiblia.PorCodigo(m.Groups["libro"].Value);
            if (libro is null) continue;
            var texto = LimpiarTexto(m.Groups["texto"].Value);
            if (texto.Length == 0) continue;
            versiculos.Add(new VersiculoImportado(libro.Numero, int.Parse(m.Groups["cap"].Value), int.Parse(m.Groups["ver"].Value), texto));
        }
        if (versiculos.Count == 0) throw new FormatException("El archivo no contiene versículos en un formato reconocido.");
        return new BibliaImportada(null, null, null, versiculos);
    }

    // ------------------------------------------------------------------ Zefania

    public static BibliaImportada LeerZefania(Stream flujo)
    {
        var doc = XDocument.Load(flujo, LoadOptions.None);
        var raiz = doc.Root ?? throw new FormatException("XML vacío.");
        var info = raiz.Element("INFORMATION");
        var nombre = (string?)raiz.Attribute("biblename") ?? info?.Element("title")?.Value;
        var abreviatura = info?.Element("identifier")?.Value;
        var idioma = info?.Element("language")?.Value;

        var versiculos = new List<VersiculoImportado>();
        foreach (var libro in raiz.Elements("BIBLEBOOK"))
        {
            if (!int.TryParse((string?)libro.Attribute("bnumber"), out var numeroLibro) || numeroLibro is < 1 or > 66) continue;
            foreach (var capitulo in libro.Elements("CHAPTER"))
            {
                if (!int.TryParse((string?)capitulo.Attribute("cnumber"), out var numeroCapitulo)) continue;
                foreach (var vers in capitulo.Elements("VERS"))
                {
                    if (!int.TryParse((string?)vers.Attribute("vnumber"), out var numeroVersiculo)) continue;
                    var texto = LimpiarTexto(TextoSinNotas(vers));
                    if (texto.Length > 0) versiculos.Add(new VersiculoImportado(numeroLibro, numeroCapitulo, numeroVersiculo, texto));
                }
            }
        }
        if (versiculos.Count == 0) throw new FormatException("El archivo Zefania no contiene versículos.");
        return new BibliaImportada(nombre, abreviatura, idioma, versiculos);
    }

    // ------------------------------------------------------------------ OSIS

    /// <summary>OSIS XML con versículos contenedores (&lt;verse osisID&gt;texto&lt;/verse&gt;) o hitos (sID / eID).</summary>
    public static BibliaImportada LeerOsis(Stream flujo)
    {
        var ajustes = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, IgnoreComments = true, IgnoreProcessingInstructions = true };
        using var lector = XmlReader.Create(flujo, ajustes);
        var versiculos = new List<VersiculoImportado>();
        var texto = new StringBuilder();
        string? actual = null;
        string? nombre = null;
        string? idioma = null;

        void Emitir()
        {
            if (actual is not null && ReferenciaOsis(actual) is var (libro, capitulo, versiculo) && libro > 0)
            {
                var limpio = LimpiarTexto(texto.ToString());
                if (limpio.Length > 0) versiculos.Add(new VersiculoImportado(libro, capitulo, versiculo, limpio));
            }
            texto.Clear();
        }

        lector.MoveToContent();
        while (!lector.EOF)
        {
            switch (lector.NodeType)
            {
                case XmlNodeType.Element when lector.LocalName == "osisText":
                    idioma = lector.GetAttribute("xml:lang");
                    break;
                case XmlNodeType.Element when lector.LocalName == "verse":
                {
                    var fin = lector.GetAttribute("eID");
                    var id = lector.GetAttribute("osisID");
                    if (fin is not null)
                    {
                        Emitir();
                        actual = null;
                    }
                    else if (id is not null)
                    {
                        Emitir();
                        actual = id;
                    }
                    break;
                }
                case XmlNodeType.Element when lector.LocalName == "title":
                    if (nombre is null && actual is null && !lector.IsEmptyElement)
                    {
                        nombre = EtiquetasRegex().Replace(lector.ReadInnerXml(), "").Trim();
                        continue;
                    }
                    lector.Skip();
                    continue;
                case XmlNodeType.Element when lector.LocalName is "note" or "rdg" or "reference" && actual is null:
                case XmlNodeType.Element when lector.LocalName is "note" or "rdg":
                    lector.Skip();
                    continue;
                case XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace when actual is not null:
                    texto.Append(lector.Value);
                    break;
                case XmlNodeType.EndElement when lector.LocalName == "verse" && actual is not null:
                    Emitir();
                    actual = null;
                    break;
            }
            lector.Read();
        }
        Emitir();
        if (versiculos.Count == 0) throw new FormatException("El archivo OSIS no contiene versículos.");
        return new BibliaImportada(string.IsNullOrWhiteSpace(nombre) ? null : nombre, null, idioma, versiculos);
    }

    private static (int Libro, int Capitulo, int Versiculo)? ReferenciaOsis(string osisId)
    {
        var partes = osisId.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0].Split('!')[0].Split('.');
        if (partes.Length < 3) return null;
        var libro = LibrosBiblia.PorCodigo(partes[0]);
        if (libro is null || !int.TryParse(partes[1], out var c) || !int.TryParse(partes[2], out var v)) return null;
        return (libro.Numero, c, v);
    }

    // ------------------------------------------------------------------ USFM

    /// <summary>USFM: un archivo por libro (\id GEN, \c 1, \v 1 …). Se quitan notas, referencias cruzadas y títulos.</summary>
    public static BibliaImportada LeerUsfm(IEnumerable<string> archivos)
    {
        var versiculos = new List<VersiculoImportado>();
        foreach (var contenido in archivos)
        {
            int? libro = null;
            var capitulo = 0;
            int? versiculo = null;
            var texto = new StringBuilder();

            void Emitir()
            {
                if (libro is int l && versiculo is int v && capitulo > 0)
                {
                    var limpio = LimpiarTexto(LimpiarUsfm(texto.ToString()));
                    if (limpio.Length > 0) versiculos.Add(new VersiculoImportado(l, capitulo, v, limpio));
                }
                texto.Clear();
                versiculo = null;
            }

            foreach (var bruta in contenido.Replace("\r", "").Split('\n'))
            {
                var linea = bruta.Trim();
                if (linea.Length == 0) continue;
                var marcador = MarcadorUsfmRegex().Match(linea);
                var codigo = marcador.Success ? marcador.Groups[1].Value : "";
                var resto = marcador.Success ? marcador.Groups[2].Value : linea;
                switch (codigo)
                {
                    case "id":
                        Emitir();
                        libro = LibrosBiblia.PorCodigo(resto.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "")?.Numero;
                        capitulo = 0;
                        continue;
                    case "c":
                        Emitir();
                        capitulo = int.TryParse(resto.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(), out var c) ? c : 0;
                        continue;
                    case "v":
                    case "":
                        break;
                    case "p" or "m" or "pi" or "pi1" or "pi2" or "q" or "q1" or "q2" or "q3" or "q4" or "li" or "li1" or "li2"
                        or "nb" or "pc" or "pm" or "mi" or "pmo" or "qm" or "qm1" or "qm2":
                        linea = resto;
                        break;
                    default:
                        continue; // títulos, encabezados, introducciones
                }

                var partes = VersoUsfmRegex().Split(linea);
                if (partes[0].Trim().Length > 0 && versiculo is not null) texto.Append(' ').Append(partes[0]);
                for (var i = 1; i + 1 < partes.Length; i += 2)
                {
                    Emitir();
                    versiculo = int.Parse(partes[i]);
                    texto.Append(partes[i + 1]);
                }
            }
            Emitir();
        }
        if (versiculos.Count == 0) throw new FormatException("Los archivos USFM no contienen versículos.");
        return new BibliaImportada(null, null, null, versiculos);
    }

    private static string LimpiarUsfm(string texto)
    {
        var t = NotaUsfmRegex().Replace(texto, "");
        t = PalabraUsfmRegex().Replace(t, "$1");
        t = MarcaUsfmRegex().Replace(t, "");
        return t;
    }

    private static BibliaImportada LeerZip(string ruta)
    {
        using var zip = ZipFile.OpenRead(ruta);
        var usfm = zip.Entries
            .Where(e => e.Name.EndsWith(".usfm", StringComparison.OrdinalIgnoreCase) || e.Name.EndsWith(".sfm", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.FullName)
            .ToList();
        if (usfm.Count > 0)
        {
            var textos = usfm.Select(e =>
            {
                using var lector = new StreamReader(e.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                return lector.ReadToEnd();
            }).ToList();
            return LeerUsfm(textos);
        }
        var candidato = zip.Entries.FirstOrDefault(e => e.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) || e.Name.EndsWith(".osis", StringComparison.OrdinalIgnoreCase))
                        ?? zip.Entries.FirstOrDefault(e => e.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));
        if (candidato is null) throw new FormatException("El ZIP no contiene una Biblia en un formato reconocido.");
        using var flujo = candidato.Open();
        return Leer(flujo);
    }

    // ------------------------------------------------------------------ e-Sword, MySword y MyBible (SQLite)

    private static bool EsSqlite(string ruta)
    {
        try
        {
            using var archivo = File.OpenRead(ruta);
            var cabecera = new byte[16];
            return archivo.Read(cabecera, 0, 16) == 16 && Encoding.ASCII.GetString(cabecera, 0, 15) == "SQLite format 3";
        }
        catch (IOException)
        {
            return false;
        }
    }

    public static BibliaImportada LeerSqlite(string ruta)
    {
        try
        {
            var cadena = new SqliteConnectionStringBuilder { DataSource = ruta, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
            using var c = new SqliteConnection(cadena);
            c.Open();
            var tablas = Consultar(c, "SELECT name FROM sqlite_master WHERE type = 'table'", r => r.GetString(0))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var versiculos = new List<VersiculoImportado>();
            string? nombre = null, abreviatura = null, idioma = null;

            if (tablas.Contains("verses"))
            {
                // MyBible
                foreach (var (b, cap, ver, txt) in Consultar(c, "SELECT book_number, chapter, verse, text FROM verses ORDER BY book_number, chapter, verse",
                             r => (r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.IsDBNull(3) ? "" : r.GetString(3))))
                {
                    var libro = LibrosBiblia.PorNumeroMyBible(b);
                    if (libro is null) continue;
                    var limpio = LimpiarTexto(LimpiarMarcado(txt));
                    if (limpio.Length > 0) versiculos.Add(new VersiculoImportado(libro.Numero, cap, ver, limpio));
                }
                if (tablas.Contains("info"))
                {
                    foreach (var (clave, valor) in Consultar(c, "SELECT name, value FROM info", r => (r.GetString(0), r.IsDBNull(1) ? "" : r.GetString(1))))
                    {
                        if (clave.Equals("description", StringComparison.OrdinalIgnoreCase)) nombre = valor;
                        else if (clave.Equals("language", StringComparison.OrdinalIgnoreCase)) idioma = valor;
                    }
                }
            }
            else if (tablas.Contains("Bible"))
            {
                // e-Sword (.bblx, texto RTF) y MySword (.bbl.mybible, texto GBF)
                foreach (var (b, cap, ver, txt) in Consultar(c, "SELECT Book, Chapter, Verse, Scripture FROM Bible ORDER BY Book, Chapter, Verse",
                             r => (r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.IsDBNull(3) ? "" : r.GetString(3))))
                {
                    if (b is < 1 or > 66) continue;
                    var limpio = LimpiarTexto(LimpiarMarcado(txt));
                    if (limpio.Length > 0) versiculos.Add(new VersiculoImportado(b, cap, ver, limpio));
                }
                if (tablas.Contains("Details"))
                {
                    using var cmd = c.CreateCommand();
                    cmd.CommandText = "SELECT * FROM Details LIMIT 1";
                    using var r = cmd.ExecuteReader();
                    if (r.Read())
                    {
                        for (var i = 0; i < r.FieldCount; i++)
                        {
                            if (r.IsDBNull(i)) continue;
                            var valor = Convert.ToString(r.GetValue(i))?.Trim();
                            switch (r.GetName(i).ToLowerInvariant())
                            {
                                case "title": nombre = valor; break;
                                case "description": nombre ??= valor; break;
                                case "abbreviation": abreviatura = valor; break;
                                case "language": idioma = valor; break;
                            }
                        }
                    }
                }
            }
            else
            {
                throw new FormatException("La base de datos no tiene el formato de e-Sword, MySword ni MyBible.");
            }

            if (versiculos.Count == 0) throw new FormatException("El archivo no contiene versículos.");
            return new BibliaImportada(string.IsNullOrWhiteSpace(nombre) ? null : nombre, string.IsNullOrWhiteSpace(abreviatura) ? null : abreviatura, idioma, versiculos);
        }
        catch (SqliteException ex)
        {
            throw new FormatException("No se pudo leer el módulo: puede estar cifrado o dañado.", ex);
        }
    }

    private static List<T> Consultar<T>(SqliteConnection c, string sql, Func<SqliteDataReader, T> leer)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        using var r = cmd.ExecuteReader();
        var lista = new List<T>();
        while (r.Read()) lista.Add(leer(r));
        return lista;
    }

    /// <summary>Quita el marcado de los módulos: notas y números Strong (GBF de MySword, etiquetas de MyBible) y RTF (e-Sword).</summary>
    public static string LimpiarMarcado(string texto)
    {
        if (string.IsNullOrEmpty(texto)) return "";
        var t = NotasGbfRegex().Replace(texto, " ");
        t = ContenidoOcultoRegex().Replace(t, " ");
        t = SaltosRegex().Replace(t, " ");
        t = EtiquetasRegex().Replace(t, "");
        if (t.Contains('\\'))
        {
            t = RtfUnicodeRegex().Replace(t, m => ((char)(ushort)(short)int.Parse(m.Groups[1].Value)).ToString());
            t = RtfHexRegex().Replace(t, m => Encoding.Latin1.GetString(new[] { Convert.ToByte(m.Groups[1].Value, 16) }));
            t = RtfControlRegex().Replace(t, "");
            t = t.Replace("{", "").Replace("}", "");
        }
        return WebUtility.HtmlDecode(t);
    }

    // ------------------------------------------------------------------ Utilidades

    /// <summary>Quita corchetes de palabras añadidas, espacios dobles y la mayúscula capitular («EN el principio» → «En el principio»).</summary>
    public static string LimpiarTexto(string texto)
    {
        var t = texto.Replace("[", "").Replace("]", "").Replace("¶", "").Trim();
        t = EspaciosRegex().Replace(t, " ");
        t = CapitularRegex().Replace(t, m => m.Value[0] + m.Value[1..].ToLowerInvariant());
        return t;
    }

    private static string Decodificar(byte[] bytes)
    {
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes).TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    private static string TextoSinNotas(XElement elemento)
    {
        var sb = new StringBuilder();
        foreach (var nodo in elemento.Nodes())
        {
            switch (nodo)
            {
                case XText texto:
                    sb.Append(texto.Value);
                    break;
                case XElement hijo when !hijo.Name.LocalName.Equals("NOTE", StringComparison.OrdinalIgnoreCase)
                                        && !hijo.Name.LocalName.Equals("DIV", StringComparison.OrdinalIgnoreCase):
                    sb.Append(TextoSinNotas(hijo));
                    break;
            }
        }
        return sb.ToString();
    }

    [GeneratedRegex(@"^\s*(?<libro>[1-3]?[A-Z]{2,3})\s+(?<cap>\d+):(?<ver>\d+)\s+(?<texto>.*)$")]
    private static partial Regex VplRegex();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex EspaciosRegex();

    [GeneratedRegex(@"^[A-ZÁÉÍÓÚÑÜ]{2,}(?=[\s,;:.]+[a-záéíóúñü])")]
    private static partial Regex CapitularRegex();

    [GeneratedRegex(@"^\s*\\id\s+\w{3}", RegexOptions.Multiline)]
    private static partial Regex UsfmIdRegex();

    [GeneratedRegex(@"^\\([a-z]+\d*)\*?\s*(.*)$")]
    private static partial Regex MarcadorUsfmRegex();

    [GeneratedRegex(@"\\v\s+(\d+)[\-\d\w]*\s*")]
    private static partial Regex VersoUsfmRegex();

    [GeneratedRegex(@"\\(f|fe|x|ef|ex)\s.*?\\\1\*", RegexOptions.Singleline)]
    private static partial Regex NotaUsfmRegex();

    [GeneratedRegex(@"\\\+?w\s+([^|\\]*?)(\|[^\\]*)?\\\+?w\*")]
    private static partial Regex PalabraUsfmRegex();

    [GeneratedRegex(@"\\\+?[a-z]+\d*\*?")]
    private static partial Regex MarcaUsfmRegex();

    [GeneratedRegex(@"<RF[^>]*>.*?<Rf>|<TS>.*?<Ts>", RegexOptions.Singleline)]
    private static partial Regex NotasGbfRegex();

    [GeneratedRegex(@"<(S|m|n|f|h)>.*?</\1>", RegexOptions.Singleline)]
    private static partial Regex ContenidoOcultoRegex();

    [GeneratedRegex(@"<(br|pb|CM|CL|CI)\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex SaltosRegex();

    [GeneratedRegex(@"<[^<>]{1,60}>")]
    private static partial Regex EtiquetasRegex();

    [GeneratedRegex(@"\\u(-?\d+)\??")]
    private static partial Regex RtfUnicodeRegex();

    [GeneratedRegex(@"\\'([0-9a-fA-F]{2})")]
    private static partial Regex RtfHexRegex();

    [GeneratedRegex(@"\\[a-zA-Z]+-?\d* ?")]
    private static partial Regex RtfControlRegex();
}
