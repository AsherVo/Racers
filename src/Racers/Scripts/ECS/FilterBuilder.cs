using Engine;
using System.Linq;
using System.Collections.Generic;

namespace MintECS;

public class FilterBuilder
{
	List< System.Type > includes = new();
    List< System.Type > excludes = new();
    List< RelationFilter > relates = new();
    List< RelationFilter > notRelates = new();

	public FilterBuilder Include< T > () where T : Component
	{
		includes.Add( typeof( T ) );
		return this;
	}

	public FilterBuilder Include ( System.Type type )
	{
		includes.Add( type );
		return this;
	}

	public FilterBuilder Exclude< T > () where T : Component
	{
		excludes.Add( typeof( T ) );
		return this;
	}

	public FilterBuilder Exclude ( System.Type type )
	{
		excludes.Add( type );
		return this;
	}

	public FilterBuilder Related< T > ( params uint[] entities ) where T : Relation
	{
		relates.Add( new RelationFilter { relationType = typeof( T ), toEntities = entities } );
		return this;
	}

	public FilterBuilder NotRelated< T > ( params uint[] entities ) where T : Relation
	{
		notRelates.Add( new RelationFilter { relationType = typeof( T ), toEntities = entities } );
		return this;
	}
    
    public bool Matches ( uint entity, World world )
    {
        try
        {
            for ( int i = 0; i < includes.Count; i++ )
            {
                var includeType = includes [ i ];
                if ( !world.Has( entity, includeType ) )
                    return false;
            }

            for ( int i = 0; i < excludes.Count; i++ )
            {
                var excludeType = excludes [ i ];
                if ( world.Has( entity, excludeType ) )
                    return false;
            }

            for ( int i = 0; i < relates.Count; i++ )
            {
                var relation = relates [ i ];
                for ( int k = 0; k < relation.toEntities.Length; k++ )
                {
                    uint relatedEntity = relation.toEntities [ k ];
                    if ( !world.Exists( relatedEntity )
                        || !world.HasRelation( entity, relatedEntity, relation.relationType ) )
                    {
                        return false;
                    }
                }
            }

            for ( int i = 0; i < notRelates.Count; i++ )
            {
                var relation = notRelates [ i ];
                for ( int k = 0; k < relation.toEntities.Length; k++ )
                {
                    uint relatedEntity = relation.toEntities [ k ];
                    if ( world.Exists( relatedEntity )
                        && world.HasRelation( entity, relatedEntity, relation.relationType ) )
                    {
                        return false;
                    }
                }
            }

            return true;
        } 
        catch ( System.Exception e )
        {
            Log.Error( $"Failed at checking match for { world.GetName( entity ) }\n{ e }" );
            return false;
        }
    }

	public Filter ToFilter ()
		=> new Filter( 
			include: includes.ToArray(), 
			exclude: excludes.ToArray(),
			relates: relates.ToArray(),
			notRelates: notRelates.ToArray()
		);
}