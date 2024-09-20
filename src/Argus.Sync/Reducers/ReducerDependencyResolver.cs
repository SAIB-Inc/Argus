using System.Reflection;
namespace Argus.Sync.Reducers;

public static class ReducerDependencyResolver
{
    public static Type[] GetReducerDependencies(Type reducerType)
    {
        // Check if the attribute is applied to the reducerType
        var attribute = reducerType.GetCustomAttribute<ReducerDependsAttribute>();

        // If the attribute exists, return the types specified in it
        return attribute?.Types ?? [];
    }
}
