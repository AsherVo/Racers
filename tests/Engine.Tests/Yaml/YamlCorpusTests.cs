namespace Engine.Tests.Corpus;

public class Meta
{
    [YamlRequired]
    public string name = "";

    public string type = "";
    public string author = "";
}

public enum Side
{
    Party,
    Enemies,
}

public class ItemData
{
    public Meta meta = new();
    public Side type;
    public List< Component > components = new();
    public BehaviorNode? onAttackedByLaser;
    public BehaviorNode? onEnter;
}

[YamlPolymorphic]
public abstract class Component
{
}

public class Knob0 : Component
{
    public string name = "";
    public float value;
    public string? display;
    public string? units;
}

public class AttackBonusPerStunned : Component
{
    public string addMultDamageKnob = "";
}

public class BrainData
{
    public Meta meta = new();
    public List< string > variables = new();
    public BehaviorNode? rootNode;
}

[YamlPolymorphic]
public abstract class BehaviorNode
{
    public string? name { get; set; }
    public List< BehaviorNode > children { get; set; } = new();
}

public class Sequence : BehaviorNode { }
public class Selector : BehaviorNode { }
public class PulseItem : BehaviorNode { }
public class AreaAttack : BehaviorNode { }
public class DidCollide : BehaviorNode { }
public class Die : BehaviorNode { }
public class GainBubbleShield : BehaviorNode { public string amount { get; set; } = ""; }
public class DebugLog : BehaviorNode { public string log { get; set; } = ""; }
public class FindClosestEnemy : BehaviorNode { public string save_entity { get; set; } = ""; }
public class SetAnimation : BehaviorNode { public string anim { get; set; } = ""; }
public class Rotate : BehaviorNode { public float amount { get; set; } }
public class DoesNotHaveComponent : BehaviorNode { public string component { get; set; } = ""; }
public class HasComponent : BehaviorNode { public string component { get; set; } = ""; }
public class DoesOverlap : BehaviorNode { public string entity { get; set; } = ""; }
public class SetHomingTarget : BehaviorNode { public string target { get; set; } = ""; public float speed { get; set; } }

public class MoveTo : BehaviorNode
{
    public string entity { get; set; } = "";
    public float accel { get; set; }
    public float maxSpeed { get; set; }
    public float distance { get; set; }
}

public class Wander : BehaviorNode
{
    public float accel { get; set; }
    public float maxSpeed { get; set; }
    public float stopDist { get; set; }
    public float minSpeed { get; set; }
}

public class YamlCorpusTests
{
    static string Read ( string name ) => File.ReadAllText( Path.Combine( AppContext.BaseDirectory, "Yaml", "Corpus", name ) );

    [Fact]
    public void ReadsItemWithKnobsAndBehaviorTree ()
    {
        var item = Yaml.Deserialize< ItemData >( Read( "AntiLaserBubble_L1.yaml" ) );
        Assert.Equal( "AntiLaserBubble", item.meta.name );
        Assert.Equal( Side.Party, item.type );
        var knob = Assert.IsType< Knob0 >( Assert.Single( item.components ) );
        Assert.Equal( ( "X", 1f, "BubbleShields" ), ( knob.name, knob.value, knob.units ) );
        var sequence = Assert.IsType< Sequence >( item.onAttackedByLaser );
        Assert.Equal( "Knob.X", Assert.IsType< GainBubbleShield >( sequence.children[0] ).amount );
        Assert.IsType< PulseItem >( sequence.children[1] );
    }

    [Fact]
    public void ReadsItemWithSeveralComponents ()
    {
        var item = Yaml.Deserialize< ItemData >( Read( "StunBoost_L2_SubItem.yaml" ) );
        Assert.Equal( Side.Enemies, item.type );
        Assert.Equal( "X", Assert.IsType< AttackBonusPerStunned >( item.components[1] ).addMultDamageKnob );

        var test = Yaml.Deserialize< ItemData >( Read( "_TestItem_L2.yaml" ) );
        Assert.Equal( "Test!", Assert.IsType< DebugLog >( Assert.Single( test.onEnter!.children ) ).log );
    }

    [Fact]
    public void ReadsBrainsWithCommentsAndBlankLines ()
    {
        var basic = Yaml.Deserialize< BrainData >( Read( "BasicBrain.yaml" ) );
        Assert.Equal( ["TARGET"], basic.variables );
        var selector = Assert.IsType< Selector >( basic.rootNode );
        Assert.Equal( "Melee Attack", selector.children[0].name );
        Assert.Equal( 50f, Assert.IsType< MoveTo >( selector.children[0].children[1] ).distance );
        Assert.Equal( "Walk", Assert.IsType< SetAnimation >( selector.children[1].children[0] ).anim );

        var boomerang = Yaml.Deserialize< BrainData >( Read( "BoomerangBrain.yaml" ) );
        var root = Assert.IsType< Sequence >( boomerang.rootNode );
        Assert.Equal( 360f, Assert.IsType< Rotate >( root.children[0] ).amount );
        Assert.IsType< Die >( root.children[2].children[1].children[^1] );
    }

    [Fact]
    public void CorpusRoundTripsThroughObjects ()
    {
        var brain = Yaml.Deserialize< BrainData >( Read( "BoomerangBrain.yaml" ) );
        string written = Yaml.Serialize( brain );
        Assert.Equal( written, Yaml.Serialize( Yaml.Deserialize< BrainData >( written ) ) );
    }

    [Fact]
    public void ReportsMistakesInGameData ()
    {
        string broken = Read( "AntiLaserBubble_L1.yaml" )
            .Replace( "type: GainBubbleShield", "type: GainBubbleSheild" )
            .Replace( "value: 1", "valeu: 1" );

        Assert.False( Yaml.TryDeserialize< ItemData >( broken, out _, out var errors, source: "AntiLaserBubble_L1.yaml" ) );
        Assert.Equal( 2, errors.Count );
        Assert.Equal( "AntiLaserBubble_L1.yaml:11:3: components[0].valeu: Unknown key 'valeu' for Knob0.", errors[0].ToString() );
        Assert.StartsWith( "AntiLaserBubble_L1.yaml:18:11: onAttackedByLaser.children[0].type: Unknown BehaviorNode type 'GainBubbleSheild'.", errors[1].ToString() );
    }
}
