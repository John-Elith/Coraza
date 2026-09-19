namespace Coraza.Data;

/// <summary>
/// Carpetas del programa (Documentos\Coraza\…). En la versión portátil (archivo «portable.txt»
/// junto al ejecutable) todo vive en la carpeta «Datos» al lado del programa.
/// </summary>
public sealed class RutasCoraza
{
    public RutasCoraza(string raiz, bool portatil = false)
    {
        Raiz = Path.GetFullPath(raiz);
        Portatil = portatil;
    }

    public string Raiz { get; }
    public bool Portatil { get; }

    public string Biblioteca => Path.Combine(Raiz, "Biblioteca");
    public string ArchivoBaseDatos => Path.Combine(Biblioteca, "coraza.db");
    public string Medios => Path.Combine(Raiz, "Medios");
    public string Imagenes => Path.Combine(Medios, "Imagenes");
    public string Videos => Path.Combine(Medios, "Videos");
    public string Fondos => Path.Combine(Medios, "Fondos");
    public string Audio => Path.Combine(Medios, "Audio");
    public string Documentos => Path.Combine(Medios, "Documentos");
    public string Fuentes => Path.Combine(Raiz, "Fuentes");
    public string Temas => Path.Combine(Raiz, "Temas");
    public string Servicios => Path.Combine(Raiz, "Servicios");
    public string Respaldos => Path.Combine(Raiz, "Respaldos");
    public string Registros => Path.Combine(Raiz, "Registros");

    public static RutasCoraza Predeterminadas(string carpetaPrograma)
    {
        if (File.Exists(Path.Combine(carpetaPrograma, "portable.txt")))
            return new RutasCoraza(Path.Combine(carpetaPrograma, "Datos"), portatil: true);
        var documentos = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return new RutasCoraza(Path.Combine(documentos, "Coraza"));
    }

    public void AsegurarCarpetas()
    {
        foreach (var carpeta in new[] { Biblioteca, Imagenes, Videos, Fondos, Audio, Documentos, Fuentes, Temas, Servicios, Respaldos, Registros })
            Directory.CreateDirectory(carpeta);
    }

    /// <summary>Guarda rutas relativas a la carpeta de datos para que la biblioteca se pueda mover (p. ej. a una USB).</summary>
    public string ARelativa(string ruta)
    {
        var completa = Path.GetFullPath(ruta);
        var raiz = Raiz.EndsWith(Path.DirectorySeparatorChar) ? Raiz : Raiz + Path.DirectorySeparatorChar;
        return completa.StartsWith(raiz, StringComparison.OrdinalIgnoreCase)
            ? Path.GetRelativePath(Raiz, completa)
            : completa;
    }

    public string AAbsoluta(string ruta) => Path.IsPathRooted(ruta) ? ruta : Path.Combine(Raiz, ruta);
}
