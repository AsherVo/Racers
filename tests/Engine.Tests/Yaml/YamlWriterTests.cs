namespace Engine.Tests;

public class YamlWriterTests
{
    static YamlScalar String ( string value ) => new( value );

    static string WriteValue ( YamlNode value )
    {
        var map = new YamlMap();
        map.Add( "a", value );
        return Yaml.Write( map );
    }

    [Theory]
    [InlineData( "plain words", "a: plain words\n" )]
    [InlineData( "event:/heroes/piercer", "a: event:/heroes/piercer\n" )]
    [InlineData( "5 SPEED", "a: 5 SPEED\n" )]
    [InlineData( "", "a: \"\"\n" )]
    [InlineData( "true", "a: \"true\"\n" )]
    [InlineData( "null", "a: \"null\"\n" )]
    [InlineData( "~", "a: \"~\"\n" )]
    [InlineData( "123", "a: \"123\"\n" )]
    [InlineData( "1.5", "a: \"1.5\"\n" )]
    [InlineData( "yes", "a: \"yes\"\n" )]
    [InlineData( "1_000", "a: \"1_000\"\n" )]
    [InlineData( "key: value", "a: \"key: value\"\n" )]
    [InlineData( "has #hash", "a: \"has #hash\"\n" )]
    [InlineData( "- dash", "a: \"- dash\"\n" )]
    [InlineData( " padded ", "a: \" padded \"\n" )]
    [InlineData( "inner \" and \\ are fine", "a: inner \" and \\ are fine\n" )]
    [InlineData( "\"starts quoted\"", "a: \"\\\"starts quoted\\\"\"\n" )]
    [InlineData( "tab\there", "a: \"tab\\there\"\n" )]
    public void QuotesStringsOnlyWhenNeeded ( string value, string expected )
    {
        Assert.Equal( expected, WriteValue( String( value ) ) );
    }

    [Theory]
    [InlineData( "one\ntwo\n", "a: |\n  one\n  two\n" )]
    [InlineData( "one\ntwo", "a: |-\n  one\n  two\n" )]
    [InlineData( "one\n\n", "a: |+\n  one\n\n" )]
    [InlineData( "  indented\nline\n", "a: |2\n    indented\n  line\n" )]
    public void WritesMultiLineStringsAsLiteralBlocks ( string value, string expected )
    {
        string written = WriteValue( String( value ) );
        Assert.Equal( expected, written );
        Assert.Equal( value, Assert.IsType< YamlScalar >( Assert.IsType< YamlMap >( Yaml.Parse( written ) )["a"] ).Value );
    }

    [Fact]
    public void WritesBlockCollections ()
    {
        var node = Yaml.Parse( "name: Brain\nvariables: [TARGET, SELF]\nroot:\n  type: Sequence\n  children:\n  - type: Wait\n    time: 1\n  - [nested, list]\nempty: []\nnone: {}\nnothing:\n" );
        Assert.Equal( """
            name: Brain
            variables:
            - TARGET
            - SELF
            root:
              type: Sequence
              children:
              - type: Wait
                time: 1
              - - nested
                - list
            empty: []
            none: {}
            nothing:

            """, Yaml.Write( node ) );
    }

    [Fact]
    public void PreservesPlainScalarsSoTypesRoundTrip ()
    {
        string text = "count: 3\nratio: 0.5\nflag: true\nquoted: '3'\n";
        var written = Yaml.Write( Yaml.Parse( text ) );
        Assert.Equal( "count: 3\nratio: 0.5\nflag: true\nquoted: '3'\n", written );
    }

    [Fact]
    public void WritesSharedNodesAsAliases ()
    {
        var shared = new YamlMap();
        shared.Add( "speed", new YamlScalar( "5", YamlScalarStyle.Plain ) );
        var root = new YamlMap();
        root.Add( "first", shared );
        root.Add( "second", shared );

        string written = Yaml.Write( root );
        Assert.Equal( "first: &a1\n  speed: 5\nsecond: *a1\n", written );
        var parsed = Assert.IsType< YamlMap >( Yaml.Parse( written ) );
        Assert.Same( parsed["first"], parsed["second"] );
    }

    [Fact]
    public void WritesTagsAndComplexKeys ()
    {
        var key = new YamlList { new YamlScalar( "x" ) };
        var map = new YamlMap();
        map.Add( key, new YamlScalar( "v" ) );
        map.Add( "tagged", new YamlScalar( "1", YamlScalarStyle.Plain ) { Tag = "tag:yaml.org,2002:str" } );

        string written = Yaml.Write( map );
        Assert.Equal( "? - x\n: v\ntagged: !!str 1\n", written );
        var parsed = Assert.IsType< YamlMap >( Yaml.Parse( written ) );
        Assert.IsType< YamlList >( parsed[0].Key );
        Assert.Equal( "1", Assert.IsType< YamlScalar >( parsed["tagged"] ).Resolve() );
    }

    [Fact]
    public void RejectsCycles ()
    {
        var list = new YamlList();
        list.Add( list );
        Assert.Throws< InvalidOperationException >( () => Yaml.Write( list ) );
    }

    [Fact]
    public void RoundTripsEveryCorpusFile ()
    {
        foreach ( string file in Directory.GetFiles( Path.Combine( AppContext.BaseDirectory, "Yaml", "Corpus" ) ) )
        {
            var original = Yaml.Parse( File.ReadAllText( file ), file );
            string written = Yaml.Write( original );
            Assert.Equal( written, Yaml.Write( Yaml.Parse( written ) ) );
        }
    }
}
