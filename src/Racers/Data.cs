using RectangleF = System.Drawing.RectangleF;
using System.Runtime.InteropServices;
using Engine;
using MintECS;

namespace Racers;

public partial class Data
{
    const string DATA_PATH = "data/";
    const string DATA_SUFFIX = ".yaml";

    const string META_BLOCK = "Meta";
    const string ENABLED_KEY = "Enabled";

    Dictionary< string, YamlMap > data;

    public Data ()
        => Setup();

    public void Setup ()
    {
        data = new();
    }

    public void Dispose ()
    {
        data.Clear();
    }

    public YamlMap Load ( string id )
    {
        if ( !data.ContainsKey( id ) )
        {
            var usePath = $"{ DATA_PATH }{ GetGroup( id ) }/{ id }{ DATA_SUFFIX }";
            data[ id ] = ( YamlMap )Yaml.Parse( Toolbox.I.game.content.LoadText( usePath ), usePath );
        }

        return data[ id ];
    }

    public string GetGroup ( string id )
    {
        if ( !GroupsById.TryGetValue( id, out var group ) )
            throw new KeyNotFoundException( $"No data file with ID '{ id }'." );

        return group;
    }

    public bool Exists ( string id )
        => GroupsById.ContainsKey( id );

    public bool IsEnabled ( string id )
    {
        Load( id );

        if ( !data[ id ].TryGetValue( META_BLOCK, out var meta ) )
            return false;

        return GetBool( ( YamlMap )meta, ENABLED_KEY );
    }

    public List< string > GetIds ( string group )
    {
        var result = new List< string >();

        foreach ( var ( id, idGroup ) in GroupsById )
        {
            if ( idGroup == group && Exists( id ) && IsEnabled( id ) )
                result.Add( id );
        }

        return result;
    }

    public bool HasBlock ( string id, string block )
        => Exists( id ) && Load( id ).ContainsKey( block );

    public YamlMap GetBlock ( string id, string block )
    {
        if ( !Exists( id ) )
        {
            Log.Warning( $"Trying to get block '{ block }' from '{ id }', but ID '{ id }' does not exist! Aborting." );
            return null;
        }

        if ( !HasBlock( id, block ) )
        {
            Log.Warning( $"Trying to get block '{ block }' from '{ id }', but block '{ block }' is not present! Aborting." );
            return null;
        }

        data[ id ].TryGetValue( block, out var blockNode );
        return ( YamlMap )blockNode!;
    }

    public bool TryGetBlock ( string id, string block, out YamlMap result )
    {
        result = null;

        if ( !HasBlock( id, block ) )
            return false;

        result = GetBlock( id, block );
        return true;
    }

    public string GetString ( YamlMap block, string key )
        => block.TryGetValue( key, out var node ) ? ( ( YamlScalar )node ).value : null;

    public bool GetBool ( YamlMap block, string key )
        => block.TryGetValue( key, out var node ) && node is YamlScalar scalar && scalar.TryGetBool( out bool value ) && value;
}
