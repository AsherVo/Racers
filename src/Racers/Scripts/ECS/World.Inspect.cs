using Engine;

namespace MintECS;

public partial class World : IInspectable
{
    public InspectorNode Inspect ( InspectorQuery query )
    {
        string worldKey = $"world-{id}";
        var componentsByEntity = new Dictionary< uint, List< Component > >();
        foreach ( var componentDict in components.Values )
        {
            foreach ( var ( entity, component ) in componentDict )
            {
                if ( !componentsByEntity.TryGetValue( entity, out var list ) )
                {
                    list = new List< Component >();
                    componentsByEntity[ entity ] = list;
                }

                list.Add( component );
            }
        }

        var alive = entities.Where( kvp => kvp.Value ).Select( kvp => kvp.Key ).Order().ToList();
        var root = new InspectorNode( worldKey, $"World {id}" )
        {
            value = $"{alive.Count} entit{( alive.Count == 1 ? "y" : "ies" )}",
            valueIsSummary = true,
            expandable = alive.Count > 0,
        };

        if ( !query.IsExpanded( worldKey ) )
            return root;

        root.children = new List< InspectorNode >( alive.Count );
        foreach ( uint entity in alive )
        {
            var entityComponents = componentsByEntity.GetValueOrDefault( entity ) ?? new List< Component >();
            entityComponents.Sort( ( a, b ) => a is Name ? -1 : b is Name ? 1 : string.CompareOrdinal( a.GetType().Name, b.GetType().Name ) );

            var entityNode = new InspectorNode( entity.ToString(), GetBasicName( entity ) )
            {
                note = IsViewEntity( entity ) ? $"#{entity} · view" : $"#{entity}",
                value = string.Join( " · ", entityComponents.Where( c => c is not Name ).Select( c => c.GetType().Name ) ),
                valueIsSummary = true,
                expandable = entityComponents.Count > 0,
            };
            root.children.Add( entityNode );

            string entityPath = InspectorQuery.Join( worldKey, entityNode.key );
            if ( !query.IsExpanded( entityPath ) )
                continue;

            entityNode.children = new List< InspectorNode >( entityComponents.Count );
            foreach ( var component in entityComponents )
            {
                string typeName = component.GetType().Name;
                string componentPath = InspectorQuery.Join( entityPath, typeName );
                var componentNode = InspectorNode.Describe( typeName, typeName, component, componentPath, query );

                if ( component is Relation relation )
                {
                    // The target, not a summary of the fields: a tool can link to it by its leading id.
                    componentNode.value = $"→ {relation.relation} ({GetName( relation.relation )})";
                    componentNode.valueIsSummary = false;
                    var target = componentNode.children?.Find( child => child.key == nameof( Relation.relation ) );
                    if ( target != null )
                        target.value = $"{relation.relation} ({GetName( relation.relation )})";
                }

                entityNode.children.Add( componentNode );
            }
        }

        return root;
    }
}
