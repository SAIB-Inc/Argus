using System.Reflection;
namespace Argus.Sync.Reducers;

public static class ReducerDependencyResolver
{
    public static Type[] GetReducerDependencies(Type reducerType)
    {
        var attribute = reducerType.GetCustomAttribute<ReducerDependsAttribute>();

        return attribute?.Types ?? [];
    }
}
