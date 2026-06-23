using OpenAdoration.Plugin.HelloAo;
using OpenAdoration.Plugins.Abstractions;
using Xunit;

namespace OpenAdoration.Plugin.HelloAo.Tests;

public class HelloAoParserTests
{
    [Fact]
    public void ParseTranslations_maps_id_name_language()
    {
        const string json = """
        {"translations":[
          {"id":"spa_r09","name":"Reina Valera 1909","englishName":"Reina Valera 1909","language":"spa"}
        ]}
        """;

        var v = Assert.Single(HelloAoParser.ParseTranslations(json));
        Assert.Equal("spa_r09", v.Id);
        Assert.Equal("Reina Valera 1909", v.Name);
        Assert.Equal("spa_r09", v.Abbreviation);
        Assert.Equal("spa", v.Language);
    }

    [Fact]
    public void ParseComplete_extracts_books_and_verses_with_markup_stripped()
    {
        // Mirrors complete.json: books[].chapters[].chapter.content[]. Verse 2 interleaves a
        // footnote-ref object and a {text} span to prove markup is dropped but text is kept.
        const string json = """
        {
          "translation": {"id":"spa_r09","name":"Reina Valera 1909","englishName":"Reina Valera 1909","language":"spa"},
          "books": [
            {"id":"GEN","commonName":"Génesis","order":1,"numberOfChapters":50,
             "chapters":[
               {"chapter":{"number":1,"content":[
                 {"type":"heading","content":["La Creación"]},
                 {"type":"verse","number":1,"content":["EN el principio crió Dios los cielos y la tierra."]},
                 {"type":"verse","number":2,"content":["Y la tierra estaba",{"noteId":0},{"text":" desordenada y vacía."}]}
               ]}}
             ]},
            {"id":"MAT","commonName":"Mateo","order":40,"numberOfChapters":28,"chapters":[]}
          ]
        }
        """;

        var data = HelloAoParser.ParseComplete(json);

        Assert.Equal("spa_r09", data.Version.Id);
        Assert.Equal(2, data.Books.Count);
        Assert.Equal(PluginTestament.Old, data.Books[0].Testament);
        Assert.Equal(PluginTestament.New, data.Books[1].Testament);   // order 40 → NT
        Assert.Equal("Génesis", data.Books[0].Name);

        Assert.Equal(2, data.Verses.Count);
        Assert.Equal("Génesis", data.Verses[0].Book);                 // book NAME = the DB join key
        Assert.Equal(1, data.Verses[0].Chapter);
        Assert.Equal("EN el principio crió Dios los cielos y la tierra.", data.Verses[0].Text);
        Assert.Equal("Y la tierra estaba desordenada y vacía.", data.Verses[1].Text);  // markup stripped, text kept
    }
}
