using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Coraza.Rendering;

/// <summary>
/// Miniaturas de videos y documentos usando el mismo mecanismo que el Explorador de Windows
/// (IShellItemImageFactory). Se generan en un hilo STA aparte para no congelar la interfaz.
/// </summary>
public static class MiniaturasShell
{
    private const int SIIGBF_BIGGERSIZEOK = 0x1;

    private static readonly Dictionary<string, BitmapSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static Task<BitmapSource?> ObtenerAsync(string? ruta, int lado = 256)
    {
        var tarea = new TaskCompletionSource<BitmapSource?>();
        var hilo = new Thread(() =>
        {
            try { tarea.SetResult(Obtener(ruta, lado)); }
            catch (Exception) { tarea.SetResult(null); }
        })
        { IsBackground = true };
        hilo.SetApartmentState(ApartmentState.STA);
        hilo.Start();
        return tarea.Task;
    }

    /// <summary>Devuelve la miniatura o null si Windows no puede generarla (nunca lanza excepción).</summary>
    public static BitmapSource? Obtener(string? ruta, int lado = 256)
    {
        if (string.IsNullOrWhiteSpace(ruta) || !File.Exists(ruta)) return null;
        var clave = $"{ruta}|{lado}|{File.GetLastWriteTimeUtc(ruta).Ticks}";
        lock (Cache)
        {
            if (Cache.TryGetValue(clave, out var guardada)) return guardada;
        }

        BitmapSource? resultado = null;
        try
        {
            SHCreateItemFromParsingName(Path.GetFullPath(ruta), IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out var fabrica);
            try
            {
                if (fabrica.GetImage(new Tamano { Ancho = lado, Alto = lado }, SIIGBF_BIGGERSIZEOK, out var mapa) == 0 && mapa != IntPtr.Zero)
                {
                    try
                    {
                        var fuente = Imaging.CreateBitmapSourceFromHBitmap(mapa, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        fuente.Freeze();
                        resultado = fuente;
                    }
                    finally
                    {
                        DeleteObject(mapa);
                    }
                }
            }
            finally
            {
                Marshal.ReleaseComObject(fabrica);
            }
        }
        catch (Exception)
        {
            // Sin códec o sin miniatura: se usa un icono genérico.
        }

        lock (Cache)
        {
            Cache[clave] = resultado;
        }
        return resultado;
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(Tamano size, int flags, out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Tamano
    {
        public int Ancho;
        public int Alto;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        string pszPath,
        IntPtr pbc,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
}
