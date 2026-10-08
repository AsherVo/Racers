using System.Globalization;

namespace Engine.Tests;

public enum Element
{
    Fire,
    Ice,
    Shock,
}

[Flags]
public enum Targets
{
    None = 0,
    Party = 1,
    Enemies = 2,
    Everyone = Party | Enemies,
}

public struct Point
{
    public float x;
    public float y;
}

public class Weapon
{
    public string name = "";
    public int damage;
    public double range { get; set; } = 1;
    public Element element;
    public Targets targets;
    public Point offset;
    public Point? aim;
    public int[] levels = [];
    public List< string > tags = new();
    public HashSet< Element > resistances = new();
    public Dictionary< string, float > stats = new();
    public Dictionary< Element, List< int > > combos = new();
    public object? extra;
    public YamlNode? raw;

    [YamlIgnore]
    public int cachedValue = 42;

    [YamlMember( "display_name" )]
    public string? displayName;

    [YamlMember]
    int secret = 0;

    [YamlIgnore( Condition = YamlIgnoreCondition.WhenNull )]
    public string? note;

    [YamlIgnore( Condition = YamlIgnoreCondition.WhenDefault )]
    public int bonus;

    public int Secret => secret;

    public int computed => damage * 2;

    public int privateSetter { get; private set; }
}

public class Requirements
{
    [YamlRequired]
    public string id = "";

    public int optional = 7;
}

[YamlPolymorphic]
public abstract class Shape
{
    public string? label;
}

public class Circle : Shape
{
    public float radius;
}

[YamlTypeName( "rect" )]
public class Rectangle : Shape
{
    public float width;
    public float height;
}

public class Square : Rectangle
{
}

[YamlPolymorphic( "kind" )]
public class Effect
{
    public int power;
}

public class Burn : Effect
{
    public float duration;
}

public class Scene
{
    public List< Shape > shapes = new();
    public Shape? focus;
    public Rectangle? frame;
    public Effect? effect;
}

[YamlConverter( typeof( HexColorConverter ) )]
public struct HexColor
{
    public byte r;
    public byte g;
    public byte b;
}

public class HexColorConverter : IYamlConverter
{
    public object? Read ( YamlNode node, Type type, YamlReadContext context )
    {
        string? text = context.ReadString( node );
        if ( text is not { Length: 7 } || text[0] != '#' || !uint.TryParse( text.AsSpan( 1 ), NumberStyles.HexNumber, null, out uint rgb ) )
        {
            context.AddError( node, "Expected a color like #FF8800." );
            return null;
        }

        return new HexColor { r = ( byte )( rgb >> 16 ), g = ( byte )( rgb >> 8 ), b = ( byte )rgb };
    }

    public YamlNode Write ( object value, Type type, YamlWriteContext context )
    {
        var color = ( HexColor )value;
        return new YamlScalar( $"#{color.r:X2}{color.g:X2}{color.b:X2}" );
    }
}

public class UppercaseConverter : IYamlConverter
{
    public object? Read ( YamlNode node, Type type, YamlReadContext context ) => context.ReadString( node )?.ToUpperInvariant();

    public YamlNode Write ( object value, Type type, YamlWriteContext context ) => new YamlScalar( ( ( string )value ).ToLowerInvariant() );
}

public class Theme
{
    public HexColor background;
    public HexColor? accent;

    [YamlConverter( typeof( UppercaseConverter ) )]
    public string code = "";
}

public record Settings
{
    public string title { get; init; } = "";
    public int volume { get; init; }
}

public class NoDefaultConstructor ( int value )
{
    public int value = value;
}

public class UsesInterfaceCollection
{
    public IReadOnlyList< int > values = [];
}

public class ReadonlyMember
{
    [YamlMember]
    public readonly int value;
}

public class YamlSerializerTests
{
    const string WEAPON = """
        name: Laser
        damage: 12
        range: 2.5
        element: Shock
        targets: Party, Enemies
        offset: {x: 1, y: -2}
        aim: {x: 0.5, y: 0.5}
        levels: [1, 2, 3]
        tags: [hot, bright]
        resistances: [Fire, Ice]
        stats: {speed: 1.5, weight: 3}
        combos:
          Fire: [1, 2]
          Ice: []
        extra: {notes: [1, two, 3.5, true, null]}
        raw: [anything, {goes: here}]
        display_name: Laser Beam
        secret: 99
        note: hello
        bonus: 3
        """;

    [Fact]
    public void ReadsEverySupportedMemberType ()
    {
        var weapon = Yaml.Deserialize< Weapon >( WEAPON );

        Assert.Equal( "Laser", weapon.name );
        Assert.Equal( 12, weapon.damage );
        Assert.Equal( 2.5, weapon.range );
        Assert.Equal( Element.Shock, weapon.element );
        Assert.Equal( Targets.Everyone, weapon.targets );
        Assert.Equal( -2f, weapon.offset.y );
        Assert.Equal( 0.5f, weapon.aim?.x );
        Assert.Equal( [1, 2, 3], weapon.levels );
        Assert.Equal( ["hot", "bright"], weapon.tags );
        Assert.Equal( [Element.Fire, Element.Ice], weapon.resistances.Order() );
        Assert.Equal( 1.5f, weapon.stats["speed"] );
        Assert.Equal( [1, 2], weapon.combos[Element.Fire] );
        Assert.Empty( weapon.combos[Element.Ice] );
        var extra = Assert.IsType< Dictionary< string, object? > >( weapon.extra );
        Assert.Equal( [1L, "two", 3.5, true, null], Assert.IsType< List< object? > >( extra["notes"] ) );
        Assert.IsType< YamlList >( weapon.raw );
        Assert.Equal( "Laser Beam", weapon.displayName );
        Assert.Equal( 99, weapon.Secret );
        Assert.Equal( 42, weapon.cachedValue );
        Assert.Equal( "hello", weapon.note );
        Assert.Equal( 3, weapon.bonus );
    }

    [Fact]
    public void WritesAndReadsBackTheSameValues ()
    {
        var weapon = Yaml.Deserialize< Weapon >( WEAPON );
        string written = Yaml.Serialize( weapon );
        var again = Yaml.Deserialize< Weapon >( written );

        Assert.Equal( written, Yaml.Serialize( again ) );
        Assert.Equal( weapon.targets, again.targets );
        Assert.Equal( weapon.combos[Element.Fire], again.combos[Element.Fire] );
        Assert.Contains( "targets: Party, Enemies\n", written.Replace( "Everyone", "Party, Enemies" ) );
        Assert.Contains( "display_name: Laser Beam\n", written );
        Assert.DoesNotContain( "cachedValue", written );
        Assert.DoesNotContain( "computed", written );
        Assert.DoesNotContain( "privateSetter", written );
    }

    [Fact]
    public void WritesMembersInDeclarationOrder ()
    {
        string written = Yaml.Serialize( new Weapon() );
        var keys = Assert.IsType< YamlMap >( Yaml.Parse( written ) ).Keys.Select( key => ( ( YamlScalar )key ).Value );
        Assert.Equal( [ "name", "damage", "element", "targets", "offset", "aim", "levels", "tags", "resistances", "stats", "combos", "extra", "raw",
            "display_name", "secret", "range" ], keys );
    }

    [Fact]
    public void IgnoreConditionsOmitNullAndDefaultValues ()
    {
        string written = Yaml.Serialize( new Weapon { note = null, bonus = 0 } );
        Assert.DoesNotContain( "note", written );
        Assert.DoesNotContain( "bonus", written );

        written = Yaml.Serialize( new Weapon { note = "x", bonus = 1 } );
        Assert.Contains( "note: x\n", written );
        Assert.Contains( "bonus: 1\n", written );
    }

    [Fact]
    public void MissingKeysKeepInitialValues ()
    {
        var weapon = Yaml.Deserialize< Weapon >( "name: Stick\n" );
        Assert.Equal( 1, weapon.range );
        Assert.Empty( weapon.tags );
    }

    [Fact]
    public void ReportsEveryErrorWithPathAndPosition ()
    {
        bool ok = Yaml.TryDeserialize< Weapon >( """
            name: [not, a, string]
            damage: lots
            element: Water
            levels: [1, 2.5]
            stats: {speed: fast}
            offset: {x: 1, z: 2}
            nmae: typo
            """, out _, out var errors, source: "weapon.yaml" );

        Assert.False( ok );
        Assert.Equal( [
            "weapon.yaml:1:7: name: Expected a string, found a list.",
            "weapon.yaml:2:9: damage: Expected an integer, found 'lots'.",
            "weapon.yaml:3:10: element: 'Water' is not a Element. Expected one of: Fire, Ice, Shock.",
            "weapon.yaml:4:13: levels[1]: Expected an integer, found '2.5'.",
            "weapon.yaml:5:16: stats.speed: Expected a number, found 'fast'.",
            "weapon.yaml:6:16: offset.z: Unknown key 'z' for Point.",
            "weapon.yaml:7:1: nmae: Unknown key 'nmae' for Weapon.",
        ], errors.Select( error => error.ToString() ) );
    }

    [Fact]
    public void DeserializeThrowsWithAllErrors ()
    {
        var exception = Assert.Throws< YamlException >( () => Yaml.Deserialize< Weapon >( "damage: x\nrange: y\n" ) );
        Assert.Equal( 2, exception.Errors.Count );
    }

    [Fact]
    public void UnknownKeysCanBeAllowed ()
    {
        var weapon = Yaml.Deserialize< Weapon >( "name: Axe\nunused: 1\n", new YamlReadOptions { AllowUnknownKeys = true } );
        Assert.Equal( "Axe", weapon.name );
    }

    [Fact]
    public void RequiredMembersMustBePresent ()
    {
        Assert.False( Yaml.TryDeserialize< Requirements >( "optional: 1\n", out _, out var errors ) );
        Assert.Contains( "Missing required key 'id'", Assert.Single( errors ).Message );
        Assert.Equal( 7, Yaml.Deserialize< Requirements >( "id: a\n" ).optional );
    }

    [Theory]
    [InlineData( "damage: 2147483648", "out of range" )]
    [InlineData( "damage:", "Expected an integer, found null" )]
    [InlineData( "offset: 3", "Expected a map for Point" )]
    [InlineData( "targets: Party, Allies", "'Allies' is not a Targets" )]
    [InlineData( "resistances: [Fire, Fire]", "Duplicate item" )]
    [InlineData( "levels: {a: 1}", "Expected a list" )]
    public void ReportsValueErrors ( string text, string message )
    {
        Assert.False( Yaml.TryDeserialize< Weapon >( text, out _, out var errors ) );
        Assert.Contains( message, Assert.Single( errors ).Message );
    }

    [Fact]
    public void EmptyDocumentIsAnError ()
    {
        Assert.False( Yaml.TryDeserialize< Weapon >( "# nothing\n", out _, out var errors ) );
        Assert.Contains( "document is empty", Assert.Single( errors ).Message );
    }

    [Fact]
    public void SyntaxErrorsAreReportedByTryDeserialize ()
    {
        Assert.False( Yaml.TryDeserialize< Weapon >( "name: [", out _, out var errors ) );
        Assert.Contains( "Unterminated", Assert.Single( errors ).Message );
    }

    [Fact]
    public void ReadsPolymorphicTypesByDiscriminator ()
    {
        var scene = Yaml.Deserialize< Scene >( """
            shapes:
            - type: Circle
              radius: 2
            - type: rect
              width: 3
              height: 4
              label: box
            - type: Square
              width: 1
            focus: {type: Circle}
            frame: {width: 10}
            effect: {kind: Burn, power: 2, duration: 1.5}
            """ );

        Assert.Equal( 2f, Assert.IsType< Circle >( scene.shapes[0] ).radius );
        Assert.Equal( "box", Assert.IsType< Rectangle >( scene.shapes[1] ).label );
        Assert.IsType< Square >( scene.shapes[2] );
        Assert.IsType< Circle >( scene.focus );
        Assert.Equal( 10f, Assert.IsType< Rectangle >( scene.frame ).width );
        Assert.Equal( 1.5f, Assert.IsType< Burn >( scene.effect ).duration );
    }

    [Fact]
    public void WritesPolymorphicDiscriminatorFirst ()
    {
        var scene = new Scene { shapes = [new Circle { radius = 1 }, new Rectangle { width = 2, label = "r" }], effect = new Effect { power = 1 } };
        string written = Yaml.Serialize( scene );

        Assert.Contains( "- type: Circle\n  label: null\n  radius: 1.0\n", written );
        Assert.Contains( "- type: rect\n  label: r\n  width: 2.0\n", written );
        Assert.Contains( "effect:\n  kind: Effect\n  power: 1\n", written );
        Assert.Equal( written, Yaml.Serialize( Yaml.Deserialize< Scene >( written ) ) );
    }

    [Theory]
    [InlineData( "focus: {radius: 1}", "Missing 'type' to choose which Shape to create. Expected one of: Circle, rect, Square." )]
    [InlineData( "focus: {type: Triangle}", "Unknown Shape type 'Triangle'. Expected one of: Circle, rect, Square." )]
    [InlineData( "frame: {type: Circle}", "Unknown Rectangle type 'Circle'. Expected one of: rect, Square." )]
    [InlineData( "focus: {type: [x]}", "Expected a Shape type name, found a list." )]
    public void ReportsPolymorphicErrors ( string text, string message )
    {
        Assert.False( Yaml.TryDeserialize< Scene >( text, out _, out var errors ) );
        Assert.Equal( message, Assert.Single( errors ).Message );
    }

    [Fact]
    public void UsesConvertersForTypesAndMembers ()
    {
        var theme = Yaml.Deserialize< Theme >( "background: '#FF8800'\naccent: '#000010'\ncode: abc\n" );
        Assert.Equal( 0x88, theme.background.g );
        Assert.Equal( ( byte )0x10, theme.accent?.b );
        Assert.Equal( "ABC", theme.code );
        Assert.Equal( "background: '#FF8800'\naccent: '#000010'\ncode: abc\n".Replace( "'", "\"" ), Yaml.Serialize( theme ) );

        Assert.False( Yaml.TryDeserialize< Theme >( "background: red\n", out _, out var errors ) );
        Assert.Equal( "background: Expected a color like #FF8800.", Assert.Single( errors ).ToString()["<yaml>:1:13: ".Length..] );
    }

    [Fact]
    public void SupportsInitOnlyProperties ()
    {
        var settings = Yaml.Deserialize< Settings >( "title: Racers\nvolume: 8\n" );
        Assert.Equal( new Settings { title = "Racers", volume = 8 }, settings );
    }

    [Fact]
    public void ExplainsUnsupportedTypes ()
    {
        var noConstructor = Assert.Throws< InvalidOperationException >( () => Yaml.Deserialize< NoDefaultConstructor >( "value: 1\n" ) );
        Assert.Contains( "parameterless constructor", noConstructor.Message );

        var interfaceCollection = Assert.Throws< InvalidOperationException >( () => Yaml.Deserialize< UsesInterfaceCollection >( "values: []\n" ) );
        Assert.Contains( "UsesInterfaceCollection.values", interfaceCollection.Message );
        Assert.Contains( "List<T>", interfaceCollection.Message );

        var readonlyMember = Assert.Throws< InvalidOperationException >( () => Yaml.Deserialize< ReadonlyMember >( "value: 1\n" ) );
        Assert.Contains( "readonly", readonlyMember.Message );
    }

    [Fact]
    public void RejectsCyclicObjects ()
    {
        var shape = new Scene();
        var holder = new Holder();
        holder.self = holder;
        Assert.Throws< InvalidOperationException >( () => Yaml.Serialize( holder ) );
        Assert.NotNull( Yaml.Serialize( shape ) );
    }

    public class Holder
    {
        public Holder? self;
    }
}
