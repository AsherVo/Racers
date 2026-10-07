
namespace Engine.Tests;

public class YamlParserTests
{
    static YamlMap ParseMap ( string text ) => Assert.IsType< YamlMap >( Yaml.Parse( text ) );

    static string ScalarValue ( YamlNode node ) => Assert.IsType< YamlScalar >( node ).Value;

    static YamlException ParseError ( string text ) => Assert.Throws< YamlException >( () => Yaml.ParseAll( text ) );

    [ Fact ]
    public void ParsesBlockMappingsAndSequences ()
    {
        var map = ParseMap( """
            name: Basic Brain
            variables:
              - TARGET
              - SELF
            components:
            - type: Knob0
              value: 1
            """ );

        Assert.Equal( "Basic Brain", ScalarValue( map[ "name" ] ) );
        var variables = Assert.IsType< YamlList >( map[ "variables" ] );
        Assert.Equal( [ "TARGET", "SELF" ], variables.Select( ScalarValue ) );

        var components = Assert.IsType< YamlList >( map[ "components" ] );
        var knob = Assert.IsType< YamlMap >( Assert.Single( components ) );
        Assert.Equal( "Knob0", ScalarValue( knob[ "type" ] ) );
        Assert.Equal( "1", ScalarValue( knob[ "value" ] ) );
    }

    [ Fact ]
    public void ParsesCompactNestedCollections ()
    {
        var list = Assert.IsType< YamlList >( Yaml.Parse( "- - a\n  - b\n- k: v\n  k2: v2\n" ) );
        Assert.Equal( [ "a", "b" ], Assert.IsType< YamlList >( list[ 0 ] ).Select( ScalarValue ) );
        Assert.Equal( 2, Assert.IsType< YamlMap >( list[ 1 ] ).Count );
    }

    [ Fact ]
    public void ParsesFlowCollections ()
    {
        var map = ParseMap( "a: [1, 'two', \"three\", [4]]\nb: {x: 1, y: 2, z}\nc: []\nd: {}\ne: [k: v]\n" );
        var a = Assert.IsType< YamlList >( map[ "a" ] );
        Assert.Equal( 4, a.Count );
        Assert.Equal( YamlScalarStyle.SingleQuoted, Assert.IsType< YamlScalar >( a[ 1 ] ).Style );
        Assert.True( Assert.IsType< YamlMap >( map[ "b" ] )[ "z" ].IsNull );
        Assert.Empty( Assert.IsType< YamlList >( map[ "c" ] ) );
        Assert.Empty( Assert.IsType< YamlMap >( map[ "d" ] ) );
        var pair = Assert.IsType< YamlMap >( Assert.Single( Assert.IsType< YamlList >( map[ "e" ] ) ) );
        Assert.Equal( "v", ScalarValue( pair[ "k" ] ) );
    }

    [ Fact ]
    public void ParsesMultiLineFlowCollection ()
    {
        var map = ParseMap( "a: [\n  1,  # one\n  2\n]\n" );
        Assert.Equal( 2, Assert.IsType< YamlList >( map[ "a" ] ).Count );
    }

    [ Theory ]
    [ InlineData( "a: plain text", "plain text" ) ]
    [ InlineData( "a: event:/heroes/piercer", "event:/heroes/piercer" ) ]
    [ InlineData( "a: Walk  # comment", "Walk" ) ]
    [ InlineData( "a: no#comment", "no#comment" ) ]
    [ InlineData( "a: 'it''s'", "it's" ) ]
    [ InlineData( "a: \"tab\\there\\n\\u00e9\\x41\"", "tab\there\n\u00e9A" ) ]
    [ InlineData( "a: folded\n  plain\n\n  lines", "folded plain\nlines" ) ]
    [ InlineData( "a: \"folded\n  quoted\"", "folded quoted" ) ]
    [ InlineData( "a: \"escaped\\\n  break\"", "escapedbreak" ) ]
    public void ParsesScalars ( string text, string expected )
    {
        Assert.Equal( expected, ScalarValue( ParseMap( text )[ "a" ] ) );
    }

    [ Theory ]
    [ InlineData( "a: |\n  one\n  two\n", "one\ntwo\n" ) ]
    [ InlineData( "a: |-\n  one\n  two\n\n", "one\ntwo" ) ]
    [ InlineData( "a: |+\n  one\n\n", "one\n\n" ) ]
    [ InlineData( "a: >\n  one\n  two\n\n  three\n", "one two\nthree\n" ) ]
    [ InlineData( "a: >\n  one\n    indented\n  two\n", "one\n  indented\ntwo\n" ) ]
    [ InlineData( "a: |2\n   leading space\n", " leading space\n" ) ]
    [ InlineData( "a: |\n  # not a comment\nb: 1\n", "# not a comment\n" ) ]
    public void ParsesBlockScalars ( string text, string expected )
    {
        Assert.Equal( expected, ScalarValue( ParseMap( text )[ "a" ] ) );
    }

    [ Fact ]
    public void ResolvesCoreSchemaTypes ()
    {
        var map = ParseMap( "n1:\nn2: ~\nb: true\ni: -12\nh: 0x1F\nf: 1.5e3\ninf: -.inf\ns: '12'\nstr: !!str true\n" );
        Assert.Null( Assert.IsType< YamlScalar >( map[ "n1" ] ).Resolve() );
        Assert.Null( Assert.IsType< YamlScalar >( map[ "n2" ] ).Resolve() );
        Assert.Equal( true, Assert.IsType< YamlScalar >( map[ "b" ] ).Resolve() );
        Assert.Equal( -12L, Assert.IsType< YamlScalar >( map[ "i" ] ).Resolve() );
        Assert.Equal( 31L, Assert.IsType< YamlScalar >( map[ "h" ] ).Resolve() );
        Assert.Equal( 1500.0, Assert.IsType< YamlScalar >( map[ "f" ] ).Resolve() );
        Assert.Equal( double.NegativeInfinity, Assert.IsType< YamlScalar >( map[ "inf" ] ).Resolve() );
        Assert.Equal( "12", Assert.IsType< YamlScalar >( map[ "s" ] ).Resolve() );
        Assert.Equal( "true", Assert.IsType< YamlScalar >( map[ "str" ] ).Resolve() );
    }

    [ Fact ]
    public void AliasesShareTheAnchoredNode ()
    {
        var map = ParseMap( "base: &b {speed: 5}\ncopy: *b\n" );
        Assert.Same( map[ "base" ], map[ "copy" ] );
        Assert.Equal( "b", map[ "base" ].Anchor );
    }

    [ Fact ]
    public void ParsesTagsAndDirectives ()
    {
        var map = ParseMap( "%TAG !e! tag:example.com,2026:\n---\na: !e!point {x: 1}\nb: !local x\nc: !!int 3\n" );
        Assert.Equal( "tag:example.com,2026:point", map[ "a" ].Tag );
        Assert.Equal( "!local", map[ "b" ].Tag );
        Assert.Equal( "tag:yaml.org,2002:int", map[ "c" ].Tag );
    }

    [ Fact ]
    public void ParsesMultipleDocuments ()
    {
        var documents = Yaml.ParseAll( "a: 1\n---\nb: 2\n...\n---\n- 3\n" );
        Assert.Equal( 3, documents.Count );
        Assert.IsType< YamlList >( documents[ 2 ] );
        Assert.Throws< YamlException >( () => Yaml.Parse( "a: 1\n---\nb: 2\n" ) );
    }

    [ Fact ]
    public void EmptyInputIsNull ()
    {
        Assert.True( Yaml.Parse( "" ).IsNull );
        Assert.True( Yaml.Parse( "# just a comment\n" ).IsNull );
        Assert.Empty( Yaml.ParseAll( "" ) );
    }

    [ Fact ]
    public void HandlesWindowsLineEndingsAndByteOrderMark ()
    {
        var map = ParseMap( "\uFEFFa: 1\r\nb: |\r\n  x\r\n  y\r\n" );
        Assert.Equal( "x\ny\n", ScalarValue( map[ "b" ] ) );
    }

    [ Fact ]
    public void RecordsPositions ()
    {
        var map = ParseMap( "a: 1\nb:\n  c: [x, y]\n" );
        var c = Assert.IsType< YamlMap >( map[ "b" ] )[ "c" ];
        Assert.Equal( 3, c.Line );
        Assert.Equal( 6, c.Column );
    }

    [ Theory ]
    [ InlineData( "a: 1\na: 2\n", 2, 1, "Duplicate key 'a'" ) ]
    [ InlineData( "a: 1\n  b: 2\n", 2, 4, "Mapping values are not allowed" ) ]
    [ InlineData( "a:\n  b: 1\n c: 2\n", 3, 2, "Bad indentation" ) ]
    [ InlineData( "a: \"open\n", 1, 4, "Unterminated" ) ]
    [ InlineData( "a: [1, 2\n", 1, 4, "Unterminated flow collection" ) ]
    [ InlineData( "a: *missing\n", 1, 4, "Undefined alias" ) ]
    [ InlineData( "a:\n\tb: 1\n", 2, 1, "Tabs" ) ]
    [ InlineData( "a: 1\njust text\n", 2, 1, "Expected ':'" ) ]
    [ InlineData( "a: \"\\q\"\n", 1, 5, "Invalid escape" ) ]
    [ InlineData( "a: b: c\n", 1, 5, "Mapping values are not allowed" ) ]
    [ InlineData( "a: 1\u0007\n", 1, 5, "Invalid character" ) ]
    public void ReportsSyntaxErrorsWithPositions ( string text, int line, int column, string message )
    {
        var error = Assert.Single( ParseError( text ).Errors );
        Assert.Contains( message, error.Message );
        Assert.Equal( (line, column), (error.Line, error.Column) );
    }

    [ Fact ]
    public void ErrorsNameTheSource ()
    {
        var exception = Assert.Throws< YamlException >( () => Yaml.Parse( "a: [", "items/sword.yaml" ) );
        Assert.StartsWith( "items/sword.yaml:1:4:", exception.Message );
    }

    [ Fact ]
    public void RejectsExcessiveNesting ()
    {
        string deep = new string( '[', 1000 ) + new string( ']', 1000 );
        Assert.Contains( "Nesting", ParseError( deep ).Message );
    }
}
