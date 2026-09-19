namespace Coraza.Remoto.Qr;

/// <summary>
/// Genera el código QR con la dirección del control remoto.
///
/// Alcance deliberadamente estrecho: <b>modo byte, corrección de errores nivel M,
/// versiones 1 a 6</b>. Una URL como «http://192.168.101.78:8787/» son 27 caracteres
/// y cabe de sobra, así que no hace falta el resto de la norma. Esa estrechez tiene
/// una ventaja concreta: en las versiones 1 a 6 con nivel M todos los bloques miden
/// lo mismo, lo que elimina el caso más enrevesado del entrelazado.
///
/// Si algún día la URL creciera más allá de 106 caracteres, <see cref="Crear"/> lanza
/// en vez de generar un código silenciosamente ilegible.
/// </summary>
public static class GeneradorQr
{
    // Por versión (índice 1..6): codewords de datos, codewords de corrección por
    // bloque, y número de bloques. Nivel M.
    private static readonly int[] DatosPorVersion = { 0, 16, 28, 44, 64, 86, 108 };
    private static readonly int[] CorreccionPorBloque = { 0, 10, 16, 26, 18, 24, 16 };
    private static readonly int[] Bloques = { 0, 1, 1, 1, 2, 2, 4 };

    /// <summary>Centros de los patrones de alineación. La versión 1 no lleva.</summary>
    private static readonly int[][] Alineacion =
    {
        Array.Empty<int>(), Array.Empty<int>(),
        new[] { 6, 18 }, new[] { 6, 22 }, new[] { 6, 26 }, new[] { 6, 30 }, new[] { 6, 34 },
    };

    /// <summary>
    /// Devuelve la matriz de módulos: <c>true</c> es oscuro. No incluye el margen
    /// blanco; quien la pinte debe dejar al menos 4 módulos de silencio alrededor.
    /// </summary>
    public static bool[,] Crear(string texto) => Construir(texto, null);

    /// <summary>
    /// Igual que <see cref="Crear"/> pero con una máscara concreta. Existe para poder
    /// comparar la salida contra un codificador de referencia en las pruebas: si cada
    /// uno elige una máscara distinta, las dos matrices difieren legítimamente y la
    /// comparación no dice nada.
    /// </summary>
    internal static bool[,] CrearConMascara(string texto, int mascara) => Construir(texto, mascara);

    /// <summary>x es la columna e y la fila, igual que en el resto del archivo.</summary>
    internal static bool MascaraInvierte(int mascara, int x, int y) => mascara switch
    {
        0 => (y + x) % 2 == 0,
        1 => y % 2 == 0,
        2 => x % 3 == 0,
        3 => (y + x) % 3 == 0,
        4 => (y / 2 + x / 3) % 2 == 0,
        5 => y * x % 2 + y * x % 3 == 0,
        6 => (y * x % 2 + y * x % 3) % 2 == 0,
        _ => ((y + x) % 2 + y * x % 3) % 2 == 0,
    };

    /// <summary>Piezas intermedias, para poder compararlas con un codificador de referencia.</summary>
    internal static (byte[] Flujo, bool[,] Reservado, int Lado, int Version) Interior(string texto)
    {
        var datos = System.Text.Encoding.UTF8.GetBytes(texto);
        var version = ElegirVersion(datos.Length);
        var flujo = EntrelazarConCorreccion(Codificar(datos, version), version);
        var lado = 17 + 4 * version;
        var reservado = new bool[lado, lado];
        DibujarPatronesFijos(new bool[lado, lado], reservado, version);
        return (flujo, reservado, lado, version);
    }

    /// <summary>
    /// Inverso de <see cref="ColocarDatos"/>: recorre la matriz en el mismo zigzag y
    /// reconstruye el flujo de codewords. Sirve para leer la matriz de otro codificador
    /// con nuestro propio recorrido y ver si coincide con lo que generamos.
    /// </summary>
    internal static byte[] LeerDatos(bool[,] matriz, bool[,] reservado, int cuantos)
    {
        var lado = matriz.GetLength(0);
        var total = cuantos * 8;
        var bits = new List<bool>(total);
        var arriba = true;

        for (var derecha = lado - 1; derecha >= 1; derecha -= 2)
        {
            if (derecha == 6) derecha = 5;
            for (var paso = 0; paso < lado; paso++)
            {
                var y = arriba ? lado - 1 - paso : paso;
                for (var columna = 0; columna < 2; columna++)
                {
                    var x = derecha - columna;
                    if (reservado[x, y]) continue;
                    if (bits.Count < total) bits.Add(matriz[x, y]);
                }
            }
            arriba = !arriba;
        }

        var salida = new byte[cuantos];
        for (var i = 0; i < bits.Count; i++)
            if (bits[i]) salida[i / 8] |= (byte)(1 << (7 - i % 8));
        return salida;
    }

    private static bool[,] Construir(string texto, int? mascaraForzada)
    {
        var datos = System.Text.Encoding.UTF8.GetBytes(texto);
        var version = ElegirVersion(datos.Length);

        var codewords = Codificar(datos, version);
        var completo = EntrelazarConCorreccion(codewords, version);

        var lado = 17 + 4 * version;
        var reservado = new bool[lado, lado];
        var matriz = new bool[lado, lado];

        DibujarPatronesFijos(matriz, reservado, version);
        ColocarDatos(matriz, reservado, completo, lado);

        // Se prueban las ocho máscaras y gana la de menor penalización: es lo que
        // evita manchas y franjas que confunden al lector del teléfono.
        var mejor = mascaraForzada ?? 0;
        if (mascaraForzada is null)
        {
            var mejorPenalizacion = int.MaxValue;
            for (var mascara = 0; mascara < 8; mascara++)
            {
                var candidata = (bool[,])matriz.Clone();
                AplicarMascara(candidata, reservado, mascara, lado);
                EscribirFormato(candidata, mascara, lado);
                var penalizacion = Penalizacion(candidata, lado);
                if (penalizacion >= mejorPenalizacion) continue;
                mejorPenalizacion = penalizacion;
                mejor = mascara;
            }
        }

        AplicarMascara(matriz, reservado, mejor, lado);
        EscribirFormato(matriz, mejor, lado);
        return matriz;
    }

    internal static int ElegirVersion(int bytes)
    {
        for (var v = 1; v <= 6; v++)
            if (bytes + 2 <= DatosPorVersion[v]) return v;   // +2: indicador de modo y longitud
        throw new ArgumentException(
            $"La dirección es demasiado larga para un QR de versión 6 ({bytes} bytes). " +
            "El control remoto solo genera códigos hasta esa versión.");
    }

    /// <summary>Modo byte, longitud en 8 bits, terminador y relleno alterno.</summary>
    private static byte[] Codificar(byte[] datos, int version)
    {
        var capacidad = DatosPorVersion[version];
        var bits = new List<bool>(capacidad * 8);

        foreach (var b in new[] { false, true, false, false }) bits.Add(b);   // 0100 = byte
        for (var i = 7; i >= 0; i--) bits.Add((datos.Length >> i & 1) == 1);
        foreach (var d in datos)
            for (var i = 7; i >= 0; i--) bits.Add((d >> i & 1) == 1);

        var maximo = capacidad * 8;
        for (var i = 0; i < 4 && bits.Count < maximo; i++) bits.Add(false);   // terminador
        while (bits.Count % 8 != 0) bits.Add(false);

        var resultado = new byte[capacidad];
        for (var i = 0; i < bits.Count; i += 8)
        {
            var valor = 0;
            for (var j = 0; j < 8; j++) valor = valor << 1 | (bits[i + j] ? 1 : 0);
            resultado[i / 8] = (byte)valor;
        }

        // Relleno alterno 236 / 17 hasta llenar la capacidad, como manda la norma.
        var relleno = new byte[] { 0xEC, 0x11 };
        for (var i = bits.Count / 8; i < capacidad; i++)
            resultado[i] = relleno[(i - bits.Count / 8) % 2];

        return resultado;
    }

    private static byte[] EntrelazarConCorreccion(byte[] datos, int version)
    {
        var bloques = Bloques[version];
        var porBloque = DatosPorVersion[version] / bloques;
        var ec = CorreccionPorBloque[version];

        var trozos = new byte[bloques][];
        var correcciones = new byte[bloques][];
        for (var i = 0; i < bloques; i++)
        {
            trozos[i] = datos.Skip(i * porBloque).Take(porBloque).ToArray();
            correcciones[i] = ReedSolomon(trozos[i], ec);
        }

        var salida = new List<byte>(datos.Length + ec * bloques);
        for (var i = 0; i < porBloque; i++)
            for (var b = 0; b < bloques; b++) salida.Add(trozos[b][i]);
        for (var i = 0; i < ec; i++)
            for (var b = 0; b < bloques; b++) salida.Add(correcciones[b][i]);
        return salida.ToArray();
    }

    // ---------- Aritmética de Galois GF(256) ----------

    private static readonly byte[] Exp = new byte[512];
    private static readonly byte[] Log = new byte[256];

    static GeneradorQr()
    {
        var x = 1;
        for (var i = 0; i < 255; i++)
        {
            Exp[i] = (byte)x;
            Log[x] = (byte)i;
            x <<= 1;
            if (x >= 256) x ^= 0x11D;   // polinomio generador del campo
        }
        for (var i = 255; i < 512; i++) Exp[i] = Exp[i - 255];
    }

    private static byte Multiplicar(byte a, byte b) =>
        a == 0 || b == 0 ? (byte)0 : Exp[Log[a] + Log[b]];

    private static byte[] ReedSolomon(byte[] datos, int grados)
    {
        var generador = new byte[] { 1 };
        for (var i = 0; i < grados; i++)
            generador = MultiplicarPolinomios(generador, new byte[] { 1, Exp[i] });

        var resto = new byte[datos.Length + grados];
        Array.Copy(datos, resto, datos.Length);

        for (var i = 0; i < datos.Length; i++)
        {
            var coeficiente = resto[i];
            if (coeficiente == 0) continue;
            for (var j = 0; j < generador.Length; j++)
                resto[i + j] ^= Multiplicar(generador[j], coeficiente);
        }

        return resto.Skip(datos.Length).ToArray();
    }

    private static byte[] MultiplicarPolinomios(byte[] a, byte[] b)
    {
        var resultado = new byte[a.Length + b.Length - 1];
        for (var i = 0; i < a.Length; i++)
            for (var j = 0; j < b.Length; j++)
                resultado[i + j] ^= Multiplicar(a[i], b[j]);
        return resultado;
    }

    // ---------- Patrones fijos ----------

    private static void DibujarPatronesFijos(bool[,] matriz, bool[,] reservado, int version)
    {
        var lado = matriz.GetLength(0);

        DibujarBuscador(matriz, reservado, 0, 0, lado);
        DibujarBuscador(matriz, reservado, lado - 7, 0, lado);
        DibujarBuscador(matriz, reservado, 0, lado - 7, lado);

        // Temporización: la fila y la columna 6 alternan oscuro/claro.
        for (var i = 8; i < lado - 8; i++)
        {
            var oscuro = i % 2 == 0;
            matriz[i, 6] = oscuro; reservado[i, 6] = true;
            matriz[6, i] = oscuro; reservado[6, i] = true;
        }

        foreach (var cx in Alineacion[version])
            foreach (var cy in Alineacion[version])
            {
                // No van encima de los buscadores.
                if (cx == 6 && cy == 6) continue;
                if (cx == 6 && cy == Alineacion[version][^1]) continue;
                if (cy == 6 && cx == Alineacion[version][^1]) continue;
                DibujarAlineacion(matriz, reservado, cx, cy);
            }

        // Módulo oscuro fijo, siempre en la misma posición.
        matriz[8, lado - 8] = true;
        reservado[8, lado - 8] = true;

        // Espacio de la información de formato: se rellena después de elegir máscara.
        for (var i = 0; i < 9; i++)
        {
            if (!reservado[i, 8]) reservado[i, 8] = true;
            if (!reservado[8, i]) reservado[8, i] = true;
        }
        for (var i = 0; i < 8; i++)
        {
            reservado[lado - 1 - i, 8] = true;
            reservado[8, lado - 1 - i] = true;
        }
    }

    private static void DibujarBuscador(bool[,] matriz, bool[,] reservado, int x0, int y0, int lado)
    {
        for (var dx = -1; dx <= 7; dx++)
            for (var dy = -1; dy <= 7; dy++)
            {
                var x = x0 + dx;
                var y = y0 + dy;
                if (x < 0 || y < 0 || x >= lado || y >= lado) continue;
                var borde = dx is >= 0 and <= 6 && dy is >= 0 and <= 6;
                var anillo = dx is 0 or 6 || dy is 0 or 6;
                var centro = dx is >= 2 and <= 4 && dy is >= 2 and <= 4;
                matriz[x, y] = borde && (anillo || centro);
                reservado[x, y] = true;
            }
    }

    private static void DibujarAlineacion(bool[,] matriz, bool[,] reservado, int cx, int cy)
    {
        for (var dx = -2; dx <= 2; dx++)
            for (var dy = -2; dy <= 2; dy++)
            {
                matriz[cx + dx, cy + dy] = Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1;
                reservado[cx + dx, cy + dy] = true;
            }
    }

    // ---------- Datos, máscara y formato ----------

    private static void ColocarDatos(bool[,] matriz, bool[,] reservado, byte[] datos, int lado)
    {
        var bit = 0;
        var total = datos.Length * 8;
        var arriba = true;

        for (var derecha = lado - 1; derecha >= 1; derecha -= 2)
        {
            if (derecha == 6) derecha = 5;   // la columna 6 es de temporización

            for (var paso = 0; paso < lado; paso++)
            {
                var y = arriba ? lado - 1 - paso : paso;
                for (var columna = 0; columna < 2; columna++)
                {
                    var x = derecha - columna;
                    if (reservado[x, y]) continue;
                    if (bit < total)
                        matriz[x, y] = (datos[bit / 8] >> (7 - bit % 8) & 1) == 1;
                    bit++;
                }
            }
            arriba = !arriba;
        }
    }

    private static void AplicarMascara(bool[,] matriz, bool[,] reservado, int mascara, int lado)
    {
        for (var x = 0; x < lado; x++)
            for (var y = 0; y < lado; y++)
            {
                if (reservado[x, y]) continue;
                if (MascaraInvierte(mascara, x, y)) matriz[x, y] = !matriz[x, y];
            }
    }

    private static void EscribirFormato(bool[,] matriz, int mascara, int lado)
    {
        // Nivel M = 00, más los tres bits de la máscara; luego BCH(15,5) y XOR final.
        var datos = 0b00 << 3 | mascara;
        var resto = datos << 10;
        for (var i = 4; i >= 0; i--)
            if ((resto >> (i + 10) & 1) == 1)
                resto ^= 0b10100110111 << i;
        var formato = (datos << 10 | resto) ^ 0b101010000010010;

        // El bit i ocupa una posición concreta de la norma, y el orden importa: el
        // bit 0 va en (8,0) y el 14 en (0,8). Ponerlos al revés, o intercambiar las
        // dos franjas de la segunda copia, produce un código de aspecto impecable
        // que ningún lector decodifica.
        for (var i = 0; i < 15; i++)
        {
            var bit = (formato >> i & 1) == 1;

            // Primera copia, rodeando el buscador superior izquierdo.
            if (i < 6) matriz[8, i] = bit;
            else if (i == 6) matriz[8, 7] = bit;
            else if (i == 7) matriz[8, 8] = bit;
            else if (i == 8) matriz[7, 8] = bit;
            else matriz[14 - i, 8] = bit;

            // Segunda copia: bits 0-7 en la fila 8 por la derecha, bits 8-14 en la
            // columna 8 por abajo.
            if (i < 8) matriz[lado - 1 - i, 8] = bit;
            else matriz[8, lado - 7 + (i - 8)] = bit;
        }
    }

    /// <summary>Las cuatro reglas de penalización de la norma; gana la máscara con menos.</summary>
    private static int Penalizacion(bool[,] m, int lado)
    {
        var total = 0;

        // 1. Rachas de cinco o más del mismo color.
        for (var i = 0; i < lado; i++)
        {
            total += PenalizarRacha(j => m[i, j], lado);
            total += PenalizarRacha(j => m[j, i], lado);
        }

        // 2. Bloques de 2x2 del mismo color.
        for (var x = 0; x < lado - 1; x++)
            for (var y = 0; y < lado - 1; y++)
                if (m[x, y] == m[x + 1, y] && m[x, y] == m[x, y + 1] && m[x, y] == m[x + 1, y + 1])
                    total += 3;

        // 3. Secuencias que imitan a un patrón de búsqueda.
        for (var i = 0; i < lado; i++)
            for (var j = 0; j < lado - 6; j++)
            {
                if (ImitaBuscador(k => m[i, k], j)) total += 40;
                if (ImitaBuscador(k => m[k, i], j)) total += 40;
            }

        // 4. Desequilibrio entre claros y oscuros.
        var oscuros = 0;
        foreach (var celda in m) if (celda) oscuros++;
        var porcentaje = oscuros * 100 / (lado * lado);
        total += Math.Abs(porcentaje - 50) / 5 * 10;

        return total;
    }

    private static int PenalizarRacha(Func<int, bool> leer, int lado)
    {
        var total = 0;
        var racha = 1;
        for (var i = 1; i < lado; i++)
        {
            if (leer(i) == leer(i - 1)) racha++;
            else
            {
                if (racha >= 5) total += racha - 2;
                racha = 1;
            }
        }
        return racha >= 5 ? total + racha - 2 : total;
    }

    private static bool ImitaBuscador(Func<int, bool> leer, int desde)
    {
        // 1:1:3:1:1 oscuro-claro-oscuro-claro-oscuro
        return leer(desde) && !leer(desde + 1) && leer(desde + 2) && leer(desde + 3)
            && leer(desde + 4) && !leer(desde + 5) && leer(desde + 6);
    }
}
