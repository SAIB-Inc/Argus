using System.Reflection;
namespace Argus.Sync.Reducers;

/// <summary>
/// Resolves reducer dependency relationships defined via the <see cref="DependsOnAttribute"/>.
/// </summary>
public static class ReducerDependencyResolver
{
    /// <summary>
    /// Gets the dependency type for a given reducer type, if one is declared.
    /// </summary>
    /// <param name="reducerType">The reducer type to check for dependencies.</param>
    /// <returns>The dependency type, or null if no dependency is declared.</returns>
    public static Type? GetReducerDependency(Type reducerType)
    {
        // Check if the attribute is applied to the reducerType
        DependsOnAttribute? attribute = reducerType.GetCustomAttribute<DependsOnAttribute>();

        // If the attribute exists, return the single dependency type
        return attribute?.DependencyType;
    }
}
