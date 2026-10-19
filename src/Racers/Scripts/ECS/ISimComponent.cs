namespace MintECS;

/// <summary>
/// Marks a component as simulation state. Only sim systems change it, it's checksummed, and its fields may not
/// hold floats or hash-ordered collections (see <see cref="SimComponentRules"/>). Every other component is view state.
/// </summary>
public interface ISimComponent {}
