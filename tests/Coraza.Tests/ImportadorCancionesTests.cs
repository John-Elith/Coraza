using Coraza.Core.Canciones;
using Coraza.Core.Modelos;

namespace Coraza.Tests;

public class ImportadorCancionesTests
{
    [Fact]
    public void ImportaTextoPlanoConTituloYCoros()
    {
        var c = ImportadorCanciones.DesdeTexto("archivo", """
            Mi canción

            Primera estrofa
            segunda línea

            Este es el coro
            que se repite

            Otra estrofa
            con su línea

            Este es el coro
            que se repite
            """);
        Assert.Equal("Mi canción", c.Titulo);
        Assert.Equal(new[] { "V1", "C", "V2", "C" }, c.Orden);
    }

    [Fact]
    public void ImportaChordPro()
    {
        var c = ImportadorCanciones.DesdeTexto("x", """
            {title: Gracia Sublime}
            {artist: John Newton}
            {key: G}
            {comment: Verso 1}
            [G]Sublime gracia [C]del Se[G]ñor
            {soc}
            [D]Coro de la can[G]ción
            {eoc}
            """);
        Assert.Equal("Gracia Sublime", c.Titulo);
        Assert.Equal("John Newton", c.Autor);
        Assert.Equal("G", c.Tonalidad);
        Assert.Equal(new[] { TipoSeccion.Verso, TipoSeccion.Coro }, c.Secciones.Select(s => s.Tipo));
        Assert.Equal("Sublime gracia del Señor", c.Secciones[0].Texto);
    }

    [Fact]
    public void ImportaOpenLyricsDeOpenLP()
    {
        var c = ImportadorCanciones.DesdeArchivo("x.xml", """
            <?xml version="1.0" encoding="UTF-8"?>
            <song xmlns="http://openlyrics.info/namespace/2009/song" version="0.9">
              <properties>
                <titles><title>Castillo fuerte</title></titles>
                <authors><author>Martín Lutero</author></authors>
                <verseOrder>v1 c v2 c</verseOrder>
                <key>C</key>
              </properties>
              <lyrics>
                <verse name="v1"><lines>Castillo fuerte es nuestro Dios<br/>defensa y buen escudo</lines></verse>
                <verse name="c"><lines><chord name="C"/>Coro de prueba<br/>otra línea</lines></verse>
                <verse name="v2"><lines>Segunda estrofa</lines><lines>Segunda diapositiva</lines></verse>
              </lyrics>
            </song>
            """);
        Assert.Equal("Castillo fuerte", c.Titulo);
        Assert.Equal("Martín Lutero", c.Autor);
        Assert.Equal("C", c.Tonalidad);
        Assert.Equal(new[] { "V1", "C", "V2", "C" }, c.Orden);
        Assert.Equal("Castillo fuerte es nuestro Dios\ndefensa y buen escudo", c.Secciones[0].Texto);
        Assert.Contains("---", c.Secciones[2].Texto);
    }

    [Fact]
    public void ImportaOpenSong()
    {
        var c = ImportadorCanciones.DesdeArchivo("Mi cancion", """
            <?xml version="1.0" encoding="UTF-8"?>
            <song>
              <title>Canción OpenSong</title>
              <author>Autor</author>
              <presentation>V1 C V2 C B C</presentation>
              <lyrics>[V1]
            .G      D
             Primera línea
             segunda línea
            [C]
             Este es el coro||y sigue aquí
            [V2]
             Otra estrofa
            [B]
            ;comentario
             El puente
            </lyrics>
            </song>
            """);
        Assert.Equal("Canción OpenSong", c.Titulo);
        Assert.Equal(new[] { "V1", "C", "V2", "C", "P", "C" }, c.Orden);
        Assert.Equal("Primera línea\nsegunda línea", c.Secciones[0].Texto);
        Assert.Equal(TipoSeccion.Puente, c.Secciones[3].Tipo);
        Assert.Contains("---", c.Secciones[1].Texto);
    }

    [Fact]
    public void ImportaProPresenterConTextoPlanoYRtf()
    {
        static string B64(string s) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(s));
        var rtf = B64(@"{\rtf1\ansi{\fonttbl{\f0 Arial;}}{\colortbl;\red255\green255\blue255;}\f0 Coro en RTF con tilde: canci\'f3n\par segunda l\u237?nea}");
        var c = ImportadorCanciones.DesdeArchivo("pro.pro6", $"""
            <RVPresentationDocument CCLISongTitle="Canción Pro" CCLIAuthor="Autor Pro" CCLISongNumber="123">
              <groups>
                <RVSlideGrouping name="Verse 1"><slides><RVDisplaySlide><displayElements>
                  <RVTextElement><NSString rvXMLIvarName="PlainText">{B64("Primera línea\nsegunda línea")}</NSString></RVTextElement>
                </displayElements></RVDisplaySlide></slides></RVSlideGrouping>
                <RVSlideGrouping name="Chorus"><slides><RVDisplaySlide><displayElements>
                  <RVTextElement RTFData="{rtf}" />
                </displayElements></RVDisplaySlide></slides></RVSlideGrouping>
              </groups>
            </RVPresentationDocument>
            """);
        Assert.Equal("Canción Pro", c.Titulo);
        Assert.Equal("123", c.Ccli);
        Assert.Equal("Primera línea\nsegunda línea", c.Secciones[0].Texto);
        Assert.Equal(TipoSeccion.Coro, c.Secciones[1].Tipo);
        Assert.Equal("Coro en RTF con tilde: canción\nsegunda línea", c.Secciones[1].Texto);
    }

    [Fact]
    public void ImportaSongSelectUsr()
    {
        var c = ImportadorCanciones.DesdeArchivo("x.usr", """
            [File]
            Type=SongSelect Import File
            Version=3.0
            [S A4768151]
            Title=Canción USR
            Author=Autor Uno | Autor Dos
            Copyright=2006 Editorial
            Keys=G
            Fields=Verse 1/tChorus/tMisc 1
            Words=Línea uno/nLínea dos/tCoro uno/nCoro dos/tExtra
            """);
        Assert.Equal("Canción USR", c.Titulo);
        Assert.Equal("Autor Uno, Autor Dos", c.Autor);
        Assert.Equal("4768151", c.Ccli);
        Assert.Equal(new[] { TipoSeccion.Verso, TipoSeccion.Coro, TipoSeccion.Otro }, c.Secciones.Select(s => s.Tipo));
        Assert.Equal("Línea uno\nLínea dos", c.Secciones[0].Texto);
    }

    [Fact]
    public void ExtraeDatosDeSongSelect()
    {
        var c = ImportadorCanciones.DesdeTexto("x", """
            Amazing Grace

            Verse 1
            Amazing grace how sweet the sound

            Chorus
            My chains are gone

            CCLI Song # 4768151
            © 2006 sixsteps Music
            CCLI License # 12345
            """);
        Assert.Equal("Amazing Grace", c.Titulo);
        Assert.Equal("4768151", c.Ccli);
        Assert.Equal("2006 sixsteps Music", c.Derechos);
        Assert.Equal(2, c.Secciones.Count);
    }
}
