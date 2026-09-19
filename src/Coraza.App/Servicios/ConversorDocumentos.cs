using System.IO;
using System.Runtime.InteropServices;
using Serilog;
using Windows.Data.Pdf;
using Windows.Storage;

namespace Coraza.App.Servicios;

/// <summary>
/// Convierte documentos en diapositivas: cada página de un PDF se guarda como imagen PNG usando el motor PDF
/// integrado en Windows (sin internet ni programas extra). Las presentaciones de PowerPoint se exportan a PDF
/// primero si Office está instalado.
/// </summary>
public static class ConversorDocumentos
{
    private static readonly string[] Presentaciones = { ".pptx", ".ppt", ".ppsx", ".pps", ".pptm" };

    public static bool EsPdf(string ruta) => Path.GetExtension(ruta).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    public static bool EsPresentacion(string ruta) => Presentaciones.Contains(Path.GetExtension(ruta).ToLowerInvariant());

    public static bool EsDocumento(string ruta) => EsPdf(ruta) || EsPresentacion(ruta);

    /// <summary>Guarda cada página como «pagina-001.png»… en la carpeta indicada. Devuelve la cantidad de páginas.</summary>
    public static async Task<int> PdfAImagenesAsync(string rutaPdf, string carpeta, IProgress<string>? progreso = null, int ancho = 1920)
    {
        Directory.CreateDirectory(carpeta);
        var archivo = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(rutaPdf));
        var documento = await PdfDocument.LoadFromFileAsync(archivo);
        var destino = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(carpeta));
        for (uint i = 0; i < documento.PageCount; i++)
        {
            progreso?.Report($"Convirtiendo la página {i + 1} de {documento.PageCount}…");
            using var pagina = documento.GetPage(i);
            var escala = ancho / Math.Max(1, pagina.Size.Width);
            var salida = await destino.CreateFileAsync($"pagina-{i + 1:000}.png", CreationCollisionOption.ReplaceExisting);
            using var flujo = await salida.OpenAsync(FileAccessMode.ReadWrite);
            await pagina.RenderToStreamAsync(flujo, new PdfPageRenderOptions
            {
                DestinationWidth = (uint)ancho,
                DestinationHeight = (uint)Math.Max(1, Math.Round(pagina.Size.Height * escala)),
            });
        }
        return (int)documento.PageCount;
    }

    /// <summary>
    /// Exporta una presentación a PDF. Usa PowerPoint si está instalado y, si no, LibreOffice Impress;
    /// ambos trabajan sin internet. Si no hay ninguno, explica cómo exportar el PDF a mano.
    /// </summary>
    public static async Task PresentacionAPdfAsync(string rutaPresentacion, string rutaPdf)
    {
        if (Type.GetTypeFromProgID("PowerPoint.Application") is not null)
        {
            await ConPowerPointAsync(rutaPresentacion, rutaPdf);
            return;
        }
        if (BuscarLibreOffice() is { } soffice)
        {
            await ConLibreOfficeAsync(soffice, rutaPresentacion, rutaPdf);
            return;
        }
        throw new InvalidOperationException(
            "para convertirla hace falta PowerPoint o LibreOffice. También puedes exportarla a PDF "
            + "(Archivo → Exportar → Crear documento PDF) e importar ese PDF");
    }

    /// <summary>Ruta de LibreOffice (soffice.exe) si está instalado.</summary>
    public static string? BuscarLibreOffice()
    {
        var candidatos = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LibreOffice", "program", "soffice.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "LibreOffice", "program", "soffice.exe"),
        };
        return candidatos.FirstOrDefault(File.Exists);
    }

    /// <summary>LibreOffice convierte sin abrir ventanas: «--headless --convert-to pdf».</summary>
    private static async Task ConLibreOfficeAsync(string soffice, string rutaPresentacion, string rutaPdf)
    {
        var salida = Path.GetDirectoryName(Path.GetFullPath(rutaPdf))!;
        var inicio = new System.Diagnostics.ProcessStartInfo(soffice)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argumento in new[] { "--headless", "--norestore", "--convert-to", "pdf", "--outdir", salida, Path.GetFullPath(rutaPresentacion) })
            inicio.ArgumentList.Add(argumento);

        using var proceso = System.Diagnostics.Process.Start(inicio)
                            ?? throw new InvalidOperationException("no se pudo iniciar LibreOffice");
        await proceso.WaitForExitAsync();

        // LibreOffice nombra el PDF como el original: se renombra al destino pedido.
        var generado = Path.Combine(salida, Path.GetFileNameWithoutExtension(rutaPresentacion) + ".pdf");
        if (!File.Exists(generado)) throw new InvalidOperationException("LibreOffice no pudo convertir la presentación");
        if (!string.Equals(generado, Path.GetFullPath(rutaPdf), StringComparison.OrdinalIgnoreCase))
            File.Move(generado, rutaPdf, overwrite: true);
    }

    private static Task ConPowerPointAsync(string rutaPresentacion, string rutaPdf)
    {
        var tarea = new TaskCompletionSource();
        var hilo = new Thread(() =>
        {
            object? aplicacion = null;
            try
            {
                var tipo = Type.GetTypeFromProgID("PowerPoint.Application")
                           ?? throw new InvalidOperationException("PowerPoint no está instalado");
                aplicacion = Activator.CreateInstance(tipo);
                dynamic app = aplicacion!;
                dynamic presentacion = app.Presentations.Open(Path.GetFullPath(rutaPresentacion), -1, 0, 0); // solo lectura, sin ventana
                try
                {
                    presentacion.SaveAs(Path.GetFullPath(rutaPdf), 32); // ppSaveAsPDF
                }
                finally
                {
                    presentacion.Close();
                }
                tarea.SetResult();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "No se pudo exportar la presentación {Ruta}", rutaPresentacion);
                tarea.SetException(ex);
            }
            finally
            {
                if (aplicacion is not null)
                {
                    try { ((dynamic)aplicacion).Quit(); } catch (Exception) { }
                    Marshal.FinalReleaseComObject(aplicacion);
                }
            }
        })
        { IsBackground = true };
        hilo.SetApartmentState(ApartmentState.STA);
        hilo.Start();
        return tarea.Task;
    }
}
