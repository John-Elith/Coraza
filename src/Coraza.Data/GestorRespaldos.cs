namespace Coraza.Data;

/// <summary>Copias de seguridad automáticas diarias de la base de datos (se conservan las últimas 30).</summary>
public static class GestorRespaldos
{
    private const string Prefijo = "coraza-";

    /// <summary>Crea el respaldo del día si todavía no existe. Devuelve la ruta creada o null.</summary>
    public static string? RespaldoDiario(BaseDatos db, string carpeta, int conservar = 30)
    {
        Directory.CreateDirectory(carpeta);
        var destino = Path.Combine(carpeta, $"{Prefijo}{DateTime.Today:yyyy-MM-dd}.db");
        if (File.Exists(destino)) return null;
        db.CopiarA(destino);
        Depurar(carpeta, conservar);
        return destino;
    }

    public static string RespaldoManual(BaseDatos db, string carpeta)
    {
        var destino = Path.Combine(carpeta, $"{Prefijo}{DateTime.Now:yyyy-MM-dd_HHmmss}-manual.db");
        db.CopiarA(destino);
        return destino;
    }

    private static void Depurar(string carpeta, int conservar)
    {
        var sobrantes = new DirectoryInfo(carpeta)
            .GetFiles($"{Prefijo}*.db")
            .Where(f => !f.Name.EndsWith("-manual.db", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.Name)
            .Skip(conservar);
        foreach (var archivo in sobrantes)
        {
            try { archivo.Delete(); }
            catch (IOException) { /* se reintenta en el próximo inicio */ }
        }
    }
}
