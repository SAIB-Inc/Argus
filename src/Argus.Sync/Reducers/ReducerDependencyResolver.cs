using System.Reflection;
namespace Argus.Sync.Reducers;

public static class ReducerDependencyResolver
{
    public static Type? GetReducerDependency(Type reducerType)
    {
        // Check if the attribute is applied to the reducerType
        DependsOnAttribute? attribute = reducerType.GetCustomAttribute<DependsOnAttribute>();

        // If the attribute exists, return the single dependency type
        return attribute?.DependencyType;
    }
}
