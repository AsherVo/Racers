using Engine;
using System.Text;
using System.Linq;
using System.Collections.Generic;

namespace MintECS;

public struct RelationFilter
{
	public System.Type relationType;
	public uint[] toEntities;

	public override string ToString ()
	{
		var sb = new StringBuilder();
		sb.Append( relationType.ToString() );
		sb.Append( "->" );
		foreach ( var ent in toEntities )
		{
			sb.Append( ent.ToString() );
			sb.Append( "/" );
		}
		return sb.ToString();
	}
}

public class Filter
{
	public System.Action< uint, World > onAdd;
	public System.Action< uint, World > onRemove;

	public readonly System.Type[] include;
	public readonly System.Type[] exclude;
	public readonly RelationFilter[] relates;
	public readonly RelationFilter[] notRelates;

	public readonly System.Type[] watchedTypes;

	public HashSet< uint > entities = new();
	public bool isBeingModified;

	public Filter ( System.Type[] include, System.Type[] exclude, RelationFilter[] relates, RelationFilter[] notRelates )
	{
		this.include = include;
		this.exclude = exclude;
		this.relates = relates;
		this.notRelates = notRelates;

		watchedTypes = include
			.Concat( exclude )
			.Concat( relates.Select( r => r.relationType ) )
			.Concat( notRelates.Select( r => r.relationType ) )
			.Distinct()
			.ToArray();
	}

	public bool Includes ( System.Type type )
		=> System.Array.IndexOf( include, type ) >= 0;

	public bool Excludes ( System.Type type )
		=> System.Array.IndexOf( exclude, type ) >= 0;

	public bool Relates ( System.Type type )
		=> HasRelationType( relates, type );

	public bool NotRelates ( System.Type type )
		=> HasRelationType( notRelates, type );

	static bool HasRelationType ( RelationFilter[] relationFilters, System.Type type )
	{
		for ( int i = 0; i < relationFilters.Length; i++ )
		{
			if ( relationFilters[ i ].relationType == type )
				return true;
		}

		return false;
	}

	public bool Matches ( uint entity, World world )
	{
		try
		{
            foreach (var includeType in include)
            {
                if ( !world.Has( entity, includeType ) )
                    return false;
            }

            foreach ( var excludeType in exclude )
            {
                if ( world.Has( entity, excludeType ) )
                    return false;
            }

            foreach ( var relation in relates )
            {
                foreach ( var relatedEntity in relation.toEntities )
                {
                    if ( !world.Exists( relatedEntity )
                        || !world.HasRelation( entity, relatedEntity, relation.relationType ) )
                    {
                        return false;
                    }
                }
            }

            foreach ( var relation in notRelates )
            {
                foreach ( var relatedEntity in relation.toEntities )
                {
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

	public List< uint > GetListCopy ()
		=> entities.ToList();

	public uint FirstOrDefault ()
		=> entities.FirstOrDefault();

	public override string ToString ()
	{
		var sb = new StringBuilder();
		sb.Append( "Filter (" );

		if ( include.Length > 0 )
		{
			sb.Append( "[Includes: " );
			foreach ( var inc in include )
			{
				sb.Append( inc.ToString() );
				sb.Append( "," );
			}
			sb.Append( "]" );
		}

		if ( exclude.Length > 0 )
		{
			sb.Append( "[Excludes: " );
			foreach ( var exc in exclude )
			{
				sb.Append( exc.ToString() );
				sb.Append( "," );
			}
			sb.Append( "]" );
		}

		if ( relates.Length > 0 )
		{
			sb.Append( "[Relates: " );
			foreach ( var rel in relates )
			{
				sb.Append( rel.ToString() );
				sb.Append( "," );
			}
			sb.Append( "]" );
		}

		if ( notRelates.Length > 0 )
		{
			sb.Append( "[NotRelates: " );
			foreach ( var rel in notRelates )
			{
				sb.Append( rel.ToString() );
				sb.Append( "," );
			}
			sb.Append( "]" );
		}

		sb.Append( ")" );
		return sb.ToString();
	}
}