using Engine;
using MintECS;

namespace Racers;

public class PrefabSystem : WorldManipulator, ISystem
{
    public void StartSystem ( World world )
    {
        this.world = world;
    }

    public void StopSystem ( World world )
    {
        this.world = null;
    }

    public uint CreateFromData ( string id, string name = null )
    {
        var data = Toolbox.I.data;

        if ( !data.Exists( id ) )
        {
            Log.Warning( $"Trying to create actor from data, but '{ id }` does not exist! Aborting." );
            return 0;
        }

        var entity = Create( name == null ? id : name );
        Add( entity, new DataId { id = id } );

        if ( data.TryGetBlock( id, "Unit", out var unitBlock ) )
        {
            if ( data.GetBool( unitBlock, "IsUnit" ) )
                Add( entity, new Unit {} );
        }

        if ( data.TryGetBlock( id, "Stats", out var statsBlock ) )
        {
            var resources = Get< ResourceSystem >();

            var health = data.GetInt( statsBlock, "Health" );
            if ( health > 0 )
                resources.Add( entity, "Health", startValue: health );
        }

        return entity;
    }
}
