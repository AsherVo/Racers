using System.Text;
using Engine.Native;

namespace Engine.Tests;

/// <summary>
/// The view model, list, pointer and resize calls of the native Rive bridge, against
/// Rive/fixture.riv loaded headless. The Screen artboard is 200x100: a column of Row artboards
/// (100x20 each) bound to the `rows` list at the top, and a 20x20 Hit box pushed to the bottom-left
/// by the layout. Releasing over the Hit box fires `ping`; releasing over a row fires its `clicked`.
/// </summary>
public sealed unsafe class RiveViewModelTests : IDisposable
{
    const string FIXTURE = "Rive/fixture.riv";
    const float FRAME = 1f / 60f;

    readonly nint file;
    readonly nint screen;

    public RiveViewModelTests ()
    {
        Assert.SkipUnless( NativeAvailable(), "libEngineNative isn't built for this platform (tools/build-native-*.sh)." );

        var bytes = File.ReadAllBytes( Path.Combine( AppContext.BaseDirectory, FIXTURE ) );
        fixed ( byte* p = bytes )
            file = EngineNative.en_rive_file_load( 0, p, bytes.Length );
        Assert.NotEqual( 0, file );

        screen = EngineNative.en_rive_instance_create( file, "Screen", null, out _, out _ );
        Assert.NotEqual( 0, screen );
        Advance();
    }

    public void Dispose ()
    {
        if ( screen != 0 )
            EngineNative.en_rive_instance_destroy( screen );
        if ( file != 0 )
            EngineNative.en_rive_file_destroy( file );
    }

    static bool NativeAvailable ()
    {
        try
        {
            EngineNative.en_rive_backend();
            return true;
        }
        catch ( DllNotFoundException )
        {
            return false;
        }
    }

    void Advance ( int frames = 1 )
    {
        for ( int i = 0; i < frames; i++ )
            EngineNative.en_rive_instance_advance( screen, FRAME );
    }

    void Click ( float x, float y )
    {
        EngineNative.en_rive_instance_pointer( screen, EngineNative.RIVE_POINTER_MOVE, x, y );
        Advance();
        EngineNative.en_rive_instance_pointer( screen, EngineNative.RIVE_POINTER_DOWN, x, y );
        Advance();
        EngineNative.en_rive_instance_pointer( screen, EngineNative.RIVE_POINTER_UP, x, y );
        Advance();
    }

    string? GetString ( string path )
    {
        var buffer = new byte[256];
        fixed ( byte* p = buffer )
        {
            int length = EngineNative.en_rive_instance_get_vm_string( screen, path, p, buffer.Length );
            return length < 0 ? null : Encoding.UTF8.GetString( buffer, 0, length );
        }
    }

    uint TriggerCount ( string path )
    {
        Assert.Equal( 1, EngineNative.en_rive_instance_get_vm_trigger( screen, path, out uint count ) );
        return count;
    }

    int ListCount () => EngineNative.en_rive_instance_vm_list_count( screen, "rows" );

    [Fact]
    public void GettersReadTheDefaultInstance ()
    {
        Assert.Equal( 1, EngineNative.en_rive_instance_get_vm_number( screen, "score", out float score ) );
        Assert.Equal( 7f, score );

        Assert.Equal( 1, EngineNative.en_rive_instance_get_vm_bool( screen, "flag", out int flag ) );
        Assert.Equal( 1, flag );

        Assert.Equal( "Title", GetString( "title" ) );
        Assert.Equal( "Panel", GetString( "panel/caption" ) );
    }

    [Fact]
    public void SettersRoundTripThroughGetters ()
    {
        Assert.Equal( 1, EngineNative.en_rive_instance_set_vm_number( screen, "score", 2.5f ) );
        Assert.Equal( 1, EngineNative.en_rive_instance_set_vm_bool( screen, "flag", 0 ) );
        Assert.Equal( 1, EngineNative.en_rive_instance_set_vm_string( screen, "panel/caption", "Héllo" ) );

        EngineNative.en_rive_instance_get_vm_number( screen, "score", out float score );
        EngineNative.en_rive_instance_get_vm_bool( screen, "flag", out int flag );
        Assert.Equal( 2.5f, score );
        Assert.Equal( 0, flag );
        Assert.Equal( "Héllo", GetString( "panel/caption" ) );
    }

    [Fact]
    public void StringGetterReportsTheFullLengthAndTruncates ()
    {
        EngineNative.en_rive_instance_set_vm_string( screen, "title", "abcdef" );

        var buffer = new byte[4];
        fixed ( byte* p = buffer )
        {
            Assert.Equal( 6, EngineNative.en_rive_instance_get_vm_string( screen, "title", p, buffer.Length ) );
            Assert.Equal( 6, EngineNative.en_rive_instance_get_vm_string( screen, "title", null, 0 ) );
        }

        Assert.Equal( "abc\0", Encoding.ASCII.GetString( buffer ) );
    }

    [Theory]
    [InlineData( "" )]
    [InlineData( "nope" )]
    [InlineData( "title/" )]
    [InlineData( "panel/nope" )]
    [InlineData( "nope/caption" )]
    [InlineData( "rows/5/label" )]
    [InlineData( "rows/x/label" )]
    [InlineData( "rows/-1/label" )]
    [InlineData( "score" )]
    public void MissingOrMistypedStringPathsAreNotFound ( string path )
    {
        Assert.Null( GetString( path ) );
        Assert.Equal( 0, EngineNative.en_rive_instance_set_vm_string( screen, path, "x" ) );
    }

    [Fact]
    public void ListPathsAreNotProperties ()
    {
        Assert.Equal( -1, EngineNative.en_rive_instance_vm_list_count( screen, "title" ) );
        Assert.Equal( -1, EngineNative.en_rive_instance_vm_list_count( screen, "rows/0" ) );
        Assert.Equal( 0, EngineNative.en_rive_instance_vm_list_clear( screen, "nope" ) );
    }

    [Fact]
    public void RowsAreReachedByIndex ()
    {
        Assert.Equal( 1, ListCount() );
        Assert.Equal( "Authored", GetString( "rows/0/label" ) );

        Assert.Equal( 1, EngineNative.en_rive_instance_set_vm_string( screen, "rows/0/label", "Changed" ) );
        Assert.Equal( "Changed", GetString( "rows/0/label" ) );
    }

    [Fact]
    public void InsertAddsADefaultInstanceOfTheNamedViewModel ()
    {
        Assert.Equal( 1, EngineNative.en_rive_instance_vm_list_insert( screen, "rows", 0, "Row" ) );
        Assert.Equal( 1, EngineNative.en_rive_instance_vm_list_insert( screen, "rows", 2, "Row" ) );

        Assert.Equal( 3, ListCount() );
        Assert.Equal( "Row", GetString( "rows/0/label" ) );
        Assert.Equal( "Authored", GetString( "rows/1/label" ) );
        Assert.Equal( "Row", GetString( "rows/2/label" ) );
    }

    [Fact]
    public void InsertedRowsHaveTheirOwnValues ()
    {
        EngineNative.en_rive_instance_vm_list_insert( screen, "rows", 1, "Row" );
        EngineNative.en_rive_instance_vm_list_insert( screen, "rows", 2, "Row" );
        EngineNative.en_rive_instance_set_vm_string( screen, "rows/1/label", "One" );
        EngineNative.en_rive_instance_set_vm_string( screen, "rows/2/label", "Two" );

        Assert.Equal( "One", GetString( "rows/1/label" ) );
        Assert.Equal( "Two", GetString( "rows/2/label" ) );
    }

    [Theory]
    [InlineData( -1, "Row" )]
    [InlineData( 2, "Row" )]
    [InlineData( 0, "Nope" )]
    public void InsertRejectsBadIndicesAndViewModels ( int index, string viewModel )
    {
        Assert.Equal( 0, EngineNative.en_rive_instance_vm_list_insert( screen, "rows", index, viewModel ) );
        Assert.Equal( 1, ListCount() );
    }

    [Fact]
    public void SwapRemoveAndClear ()
    {
        EngineNative.en_rive_instance_vm_list_insert( screen, "rows", 1, "Row" );
        EngineNative.en_rive_instance_set_vm_string( screen, "rows/1/label", "Second" );

        Assert.Equal( 1, EngineNative.en_rive_instance_vm_list_swap( screen, "rows", 0, 1 ) );
        Assert.Equal( "Second", GetString( "rows/0/label" ) );
        Assert.Equal( "Authored", GetString( "rows/1/label" ) );
        Assert.Equal( 0, EngineNative.en_rive_instance_vm_list_swap( screen, "rows", 0, 2 ) );

        Assert.Equal( 1, EngineNative.en_rive_instance_vm_list_remove( screen, "rows", 0 ) );
        Assert.Equal( 1, ListCount() );
        Assert.Equal( "Authored", GetString( "rows/0/label" ) );
        Assert.Equal( 0, EngineNative.en_rive_instance_vm_list_remove( screen, "rows", 1 ) );

        Assert.Equal( 1, EngineNative.en_rive_instance_vm_list_clear( screen, "rows" ) );
        Assert.Equal( 0, ListCount() );
        Assert.Equal( 0, EngineNative.en_rive_instance_vm_list_remove( screen, "rows", 0 ) );
    }

    [Fact]
    public void TriggerCountGoesUpByOnePerFire ()
    {
        uint before = TriggerCount( "ping" );

        EngineNative.en_rive_instance_fire_vm_trigger( screen, "ping" );
        EngineNative.en_rive_instance_fire_vm_trigger( screen, "ping" );
        Advance();

        Assert.Equal( before + 2, TriggerCount( "ping" ) );
        Assert.Equal( 0, EngineNative.en_rive_instance_get_vm_trigger( screen, "title", out _ ) );
    }

    [Fact]
    public void ReleasingOverAListenerFiresItsTrigger ()
    {
        uint before = TriggerCount( "ping" );

        Click( 10, 90 );
        Assert.Equal( before + 1, TriggerCount( "ping" ) );

        Click( 150, 50 );
        Assert.Equal( before + 1, TriggerCount( "ping" ) );
    }

    [Fact]
    public void ClickingARowFiresOnlyThatRowsTrigger ()
    {
        EngineNative.en_rive_instance_vm_list_clear( screen, "rows" );
        EngineNative.en_rive_instance_vm_list_insert( screen, "rows", 0, "Row" );
        EngineNative.en_rive_instance_vm_list_insert( screen, "rows", 1, "Row" );
        Advance();

        uint first = TriggerCount( "rows/0/clicked" );
        uint second = TriggerCount( "rows/1/clicked" );

        Click( 50, 30 );

        Assert.Equal( first, TriggerCount( "rows/0/clicked" ) );
        Assert.Equal( second + 1, TriggerCount( "rows/1/clicked" ) );
    }

    [Fact]
    public void PointerExitIsAccepted ()
    {
        EngineNative.en_rive_instance_pointer( screen, EngineNative.RIVE_POINTER_MOVE, 10, 90 );
        EngineNative.en_rive_instance_pointer( screen, EngineNative.RIVE_POINTER_EXIT, 250, 90 );
        Assert.Equal( 0, EngineNative.en_rive_instance_pointer( screen, 99, 10, 90 ) );
    }

    [Fact]
    public void ResizingReflowsTheLayout ()
    {
        EngineNative.en_rive_instance_resize( screen, 200, 200 );
        Advance();
        uint before = TriggerCount( "ping" );

        Click( 10, 90 );
        Assert.Equal( before, TriggerCount( "ping" ) );

        Click( 10, 190 );
        Assert.Equal( before + 1, TriggerCount( "ping" ) );
    }
}
