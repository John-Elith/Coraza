namespace Coraza.Core.Modelos;

public enum TipoDiapositiva { Texto, Imagen, Vacia, CuentaRegresiva, Video }

/// <summary>Datos de una cuenta regresiva: minutos o una hora de inicio («10:00»), texto y mensaje final.</summary>
public sealed record DatosCuentaRegresiva(int? Minutos, string? Hora, string Texto, string TextoFinal);

/// <summary>Fragmento de un pasaje bíblico: número de versículo (0 = continuación) y su texto.</summary>
public sealed record SegmentoVersiculo(int Numero, string Texto);

/// <summary>Una pantalla lista para proyectar. Es inmutable: se genera a partir de canciones, pasajes, textos o imágenes.</summary>
public sealed class Diapositiva
{
    public TipoDiapositiva Tipo { get; init; } = TipoDiapositiva.Texto;

    /// <summary>Texto principal; las líneas se separan con '\n'.</summary>
    public string Texto { get; init; } = "";

    /// <summary>Si existe, el texto se dibuja por versículos (con número en superíndice).</summary>
    public IReadOnlyList<SegmentoVersiculo>? Versiculos { get; init; }

    /// <summary>Pie de la diapositiva: referencia bíblica o créditos de la canción.</summary>
    public string? Pie { get; init; }

    /// <summary>Etiqueta para el operador: «Coro», «Verso 1 (1/2)», «3:16».</summary>
    public string? Etiqueta { get; init; }

    /// <summary>Código de sección para saltos rápidos (C, V1, P…).</summary>
    public string? CodigoSeccion { get; init; }

    public string? RutaImagen { get; init; }

    public ModoAjuste Ajuste { get; init; } = ModoAjuste.Ajustar;

    /// <summary>Para videos: volver a empezar al terminar.</summary>
    public bool BucleVideo { get; init; }

    /// <summary>El texto admite formato sencillo (**negrita**, *cursiva*, ==resaltado==, {coral}color{/}).</summary>
    public bool ConFormato { get; init; }

    /// <summary>Cuenta regresiva por duración (p. ej. 5 minutos).</summary>
    public TimeSpan? DuracionCuenta { get; init; }

    /// <summary>Cuenta regresiva hasta una hora del día (p. ej. 10:00).</summary>
    public TimeSpan? HoraObjetivo { get; init; }

    /// <summary>Momento exacto en que termina; se fija al enviarla al vivo para que todas las salidas coincidan.</summary>
    public DateTime? FinCuenta { get; init; }

    /// <summary>Texto que se muestra cuando la cuenta llega a cero.</summary>
    public string? TextoFinal { get; init; }

    public DateTime CalcularFinCuenta(DateTime ahora) =>
        FinCuenta ?? (HoraObjetivo is { } hora ? ahora.Date + hora : ahora + (DuracionCuenta ?? TimeSpan.FromMinutes(5)));

    public Diapositiva ConFinCuenta(DateTime fin) => new()
    {
        Tipo = Tipo,
        Texto = Texto,
        Versiculos = Versiculos,
        Pie = Pie,
        Etiqueta = Etiqueta,
        CodigoSeccion = CodigoSeccion,
        RutaImagen = RutaImagen,
        Ajuste = Ajuste,
        DuracionCuenta = DuracionCuenta,
        HoraObjetivo = HoraObjetivo,
        TextoFinal = TextoFinal,
        FinCuenta = fin,
    };

    public static Diapositiva Vacia { get; } = new() { Tipo = TipoDiapositiva.Vacia };
}
