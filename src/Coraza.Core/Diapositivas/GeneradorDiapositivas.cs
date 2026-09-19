using System.Text;
using Coraza.Core.Canciones;
using Coraza.Core.Modelos;

namespace Coraza.Core.Diapositivas;

public sealed record OpcionesDivision(int LineasMaximas = 4, int CaracteresMaximos = 240, int VersiculosPorDiapositiva = 1)
{
    public static OpcionesDivision DesdeTema(Tema? tema) => tema is null
        ? new OpcionesDivision()
        : new OpcionesDivision(
            Math.Clamp(tema.LineasMaximas, 1, 12),
            Math.Clamp(tema.CaracteresMaximos, 40, 2000),
            Math.Clamp(tema.VersiculosPorDiapositiva, 1, 10));
}

/// <summary>Convierte canciones, pasajes, textos e imágenes en diapositivas listas para proyectar.</summary>
public static class GeneradorDiapositivas
{
    public static IReadOnlyList<Diapositiva> DeCancion(Cancion cancion, OpcionesDivision opciones, string? licenciaCcli = null)
    {
        var porCodigo = cancion.Secciones
            .GroupBy(s => s.Codigo, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var orden = cancion.Orden.Where(porCodigo.ContainsKey).ToList();
        if (orden.Count == 0) orden = cancion.Secciones.OrderBy(s => s.Posicion).Select(s => s.Codigo).ToList();
        // Una sección escrita que quedó fuera del orden (un «[Final]» agregado después) se proyecta igual, al final.
        orden = AnalizadorLetra.CompletarOrden(orden, cancion.Secciones, out _);

        var diapositivas = new List<Diapositiva>();
        foreach (var codigo in orden)
        {
            var seccion = porCodigo[codigo];
            var partes = DividirSeccion(seccion.Texto, opciones.LineasMaximas);
            for (var i = 0; i < partes.Count; i++)
            {
                diapositivas.Add(new Diapositiva
                {
                    Texto = partes[i],
                    Etiqueta = partes.Count > 1 ? $"{seccion.Nombre} ({i + 1}/{partes.Count})" : seccion.Nombre,
                    CodigoSeccion = seccion.Codigo,
                });
            }
        }

        if (diapositivas.Count == 0)
        {
            diapositivas.Add(new Diapositiva { Texto = cancion.Titulo, Etiqueta = "Título" });
        }

        var creditos = Creditos(cancion, licenciaCcli);
        if (creditos is not null)
        {
            var ultima = diapositivas[^1];
            diapositivas[^1] = new Diapositiva
            {
                Texto = ultima.Texto,
                Etiqueta = ultima.Etiqueta,
                CodigoSeccion = ultima.CodigoSeccion,
                Pie = creditos,
            };
        }
        return diapositivas;
    }

    /// <summary>Créditos que se muestran al final de cada canción (autor, derechos y licencia CCLI).</summary>
    public static string? Creditos(Cancion cancion, string? licenciaCcli)
    {
        var partes = new List<string>();
        if (!string.IsNullOrWhiteSpace(cancion.Autor)) partes.Add(cancion.Autor.Trim());
        if (!string.IsNullOrWhiteSpace(cancion.Derechos))
        {
            var d = cancion.Derechos.Trim();
            partes.Add(d.StartsWith('©') ? d : "© " + d);
        }
        if (!string.IsNullOrWhiteSpace(cancion.Ccli)) partes.Add("CCLI " + cancion.Ccli.Trim());
        if (partes.Count > 0 && !string.IsNullOrWhiteSpace(licenciaCcli)) partes.Add("Licencia CCLI " + licenciaCcli.Trim());
        return partes.Count == 0 ? null : string.Join(" · ", partes);
    }

    public static IReadOnlyList<Diapositiva> DePasaje(
        ReferenciaBiblica referencia,
        IReadOnlyList<Versiculo> versiculos,
        string abreviaturaVersion,
        OpcionesDivision opciones)
    {
        var diapositivas = new List<Diapositiva>();
        var libro = referencia.InfoLibro.Nombre;
        var grupo = new List<Versiculo>();

        void EmitirGrupo()
        {
            if (grupo.Count == 0) return;
            var etiqueta = RangoCorto(grupo[0], grupo[^1]);
            diapositivas.Add(new Diapositiva
            {
                Texto = string.Join(" ", grupo.Select(v => v.Texto)),
                Versiculos = grupo.Select(v => new SegmentoVersiculo(v.Numero, v.Texto)).ToList(),
                Etiqueta = etiqueta,
                Pie = $"{libro} {etiqueta} · {abreviaturaVersion}",
            });
            grupo.Clear();
        }

        foreach (var v in versiculos)
        {
            if (v.Texto.Length > opciones.CaracteresMaximos)
            {
                EmitirGrupo();
                var partes = DividirTextoLargo(v.Texto, opciones.CaracteresMaximos);
                for (var i = 0; i < partes.Count; i++)
                {
                    var etiqueta = $"{v.Capitulo}:{v.Numero}";
                    diapositivas.Add(new Diapositiva
                    {
                        Texto = partes[i],
                        Versiculos = new[] { new SegmentoVersiculo(i == 0 ? v.Numero : 0, partes[i]) },
                        Etiqueta = $"{etiqueta} ({i + 1}/{partes.Count})",
                        Pie = $"{libro} {etiqueta} · {abreviaturaVersion}",
                    });
                }
                continue;
            }

            var largoGrupo = grupo.Sum(x => x.Texto.Length + 1);
            if (grupo.Count >= opciones.VersiculosPorDiapositiva
                || (grupo.Count > 0 && largoGrupo + v.Texto.Length > opciones.CaracteresMaximos)
                || (grupo.Count > 0 && grupo[^1].Capitulo != v.Capitulo))
            {
                EmitirGrupo();
            }
            grupo.Add(v);
        }
        EmitirGrupo();
        return diapositivas;
    }

    public static IReadOnlyList<Diapositiva> DeTexto(TextoLibre texto)
    {
        var bloques = DividirPorBloques(texto.Contenido);
        if (bloques.Count == 0) bloques.Add(texto.Titulo);
        return bloques.Select((b, i) => new Diapositiva
        {
            Texto = FormatoTexto.Listas(b),
            ConFormato = true,
            Etiqueta = bloques.Count > 1 ? $"{texto.Titulo} ({i + 1}/{bloques.Count})" : texto.Titulo,
        }).ToList();
    }

    public static Diapositiva DeCuentaRegresiva(DatosCuentaRegresiva datos)
    {
        TimeSpan? hora = TimeSpan.TryParse(datos.Hora, System.Globalization.CultureInfo.InvariantCulture, out var h) && h > TimeSpan.Zero && h < TimeSpan.FromDays(1)
            ? h
            : null;
        var minutos = Math.Clamp(datos.Minutos ?? 5, 1, 600);
        return new Diapositiva
        {
            Tipo = TipoDiapositiva.CuentaRegresiva,
            Texto = datos.Texto,
            TextoFinal = datos.TextoFinal,
            HoraObjetivo = hora,
            DuracionCuenta = hora is null ? TimeSpan.FromMinutes(minutos) : null,
            Etiqueta = hora is null ? $"Cuenta regresiva · {minutos} min" : $"Cuenta regresiva hasta las {hora:hh\\:mm}",
        };
    }

    public static Diapositiva DeVideo(Medio medio, string rutaAbsoluta, bool bucle) => new()
    {
        Tipo = TipoDiapositiva.Video,
        RutaImagen = rutaAbsoluta,
        Ajuste = medio.Ajuste,
        Etiqueta = medio.Nombre,
        BucleVideo = bucle,
    };

    /// <summary>Cada página de un documento (PDF o presentación) o cada foto de una presentación es una diapositiva.</summary>
    public static IReadOnlyList<Diapositiva> DePaginas(Medio medio, IReadOnlyList<string> paginas) =>
        paginas.Select((ruta, i) => new Diapositiva
        {
            Tipo = TipoDiapositiva.Imagen,
            RutaImagen = ruta,
            Ajuste = medio.Ajuste,
            Etiqueta = medio.Etiquetas == Medio.EtiquetaPresentacion ? $"Foto {i + 1}" : $"Página {i + 1}",
        }).ToList();

    public static Diapositiva DeImagen(Medio medio, string rutaAbsoluta) => new()
    {
        Tipo = TipoDiapositiva.Imagen,
        RutaImagen = rutaAbsoluta,
        Ajuste = medio.Ajuste,
        Etiqueta = medio.Nombre,
    };

    /// <summary>Divide una sección respetando los cortes manuales (línea en blanco o «---») y el máximo de líneas.</summary>
    public static List<string> DividirSeccion(string texto, int lineasMaximas)
    {
        var resultado = new List<string>();
        foreach (var bloque in DividirPorBloques(texto))
        {
            var lineas = bloque.Split('\n');
            resultado.AddRange(DividirLineas(lineas, lineasMaximas));
        }
        return resultado;
    }

    /// <summary>Reparte las líneas en grupos equilibrados (6 líneas con máximo 4 → 3 + 3).</summary>
    public static List<string> DividirLineas(IReadOnlyList<string> lineas, int maximo)
    {
        maximo = Math.Max(1, maximo);
        var resultado = new List<string>();
        if (lineas.Count == 0) return resultado;
        var grupos = (int)Math.Ceiling(lineas.Count / (double)maximo);
        var baseTamano = lineas.Count / grupos;
        var sobrantes = lineas.Count % grupos;
        var indice = 0;
        for (var g = 0; g < grupos; g++)
        {
            var tamano = baseTamano + (g < sobrantes ? 1 : 0);
            resultado.Add(string.Join("\n", lineas.Skip(indice).Take(tamano)));
            indice += tamano;
        }
        return resultado;
    }

    /// <summary>Divide un texto largo en partes de tamaño parecido sin cortar palabras, prefiriendo cortar tras un signo.</summary>
    public static List<string> DividirTextoLargo(string texto, int maximoCaracteres)
    {
        texto = texto.Trim();
        var resultado = new List<string>();
        if (texto.Length <= maximoCaracteres)
        {
            resultado.Add(texto);
            return resultado;
        }

        var partes = (int)Math.Ceiling(texto.Length / (double)maximoCaracteres);
        var objetivo = Math.Min(maximoCaracteres, (int)Math.Ceiling(texto.Length / (double)partes * 1.12));
        var resto = texto;
        while (resto.Length > maximoCaracteres)
        {
            var corte = BuscarCorte(resto, objetivo);
            resultado.Add(resto[..corte].Trim());
            resto = resto[corte..].Trim();
        }
        if (resto.Length > 0) resultado.Add(resto);
        return resultado;
    }

    private static int BuscarCorte(string texto, int limite)
    {
        limite = Math.Min(limite, texto.Length);
        var minimo = (int)(limite * 0.55);
        foreach (var signos in new[] { ".;:?!", "," })
        {
            for (var i = limite - 1; i >= minimo; i--)
            {
                if (signos.Contains(texto[i]) && i + 1 < texto.Length && texto[i + 1] == ' ') return i + 1;
            }
        }
        var espacio = texto.LastIndexOf(' ', limite - 1, limite);
        if (espacio > 0) return espacio;
        var siguiente = texto.IndexOf(' ', limite);
        return siguiente > 0 ? siguiente : texto.Length;
    }

    private static List<string> DividirPorBloques(string texto)
    {
        var bloques = new List<string>();
        var actual = new StringBuilder();
        foreach (var bruta in (texto ?? "").Replace("\r\n", "\n").Split('\n'))
        {
            var linea = bruta.Trim();
            if (linea.Length == 0 || linea == AnalizadorLetra.SeparadorDiapositiva)
            {
                if (actual.Length > 0) bloques.Add(actual.ToString());
                actual.Clear();
                continue;
            }
            if (actual.Length > 0) actual.Append('\n');
            actual.Append(linea);
        }
        if (actual.Length > 0) bloques.Add(actual.ToString());
        return bloques;
    }

    private static string RangoCorto(Versiculo desde, Versiculo hasta)
    {
        if (desde.Capitulo == hasta.Capitulo)
            return desde.Numero == hasta.Numero ? $"{desde.Capitulo}:{desde.Numero}" : $"{desde.Capitulo}:{desde.Numero}-{hasta.Numero}";
        return $"{desde.Capitulo}:{desde.Numero}-{hasta.Capitulo}:{hasta.Numero}";
    }
}
