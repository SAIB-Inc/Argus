using Argus.Sync.Data;
using Argus.Sync.Data.Models;
using Argus.Sync.Reducers;
using Argus.Sync.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Argus.Sync.Extensions;

public static class ReducerExtensions
{
    public static void AddReducers<T, V>(this IServiceCollection services, Type[]? optInList = null)
        where V : IReducerModel
    {
        optInList ??= [];

        IEnumerable<string> reducerNames = optInList.Select(t => ArgusUtils.GetTypeNameWithoutGenerics(t));
        IEnumerable<string> duplicateNames = reducerNames.GroupBy(x => x)
                                       .Where(g => g.Count() > 1)
                                       .Select(g => g.Key)
                                       .ToList();

        if (duplicateNames.Any())
        {
            throw new ArgumentException(
                $"Duplicate reducer names found in optInList: {string.Join(", ", duplicateNames)}"
            );
        }

        foreach (Type reducerType in optInList)
        {
            IEnumerable<Type> dependencies = ReducerDependencyResolver.GetReducerDependencies(reducerType);

            foreach (Type dependency in dependencies)
            {
                IEnumerable<Type> subDependencies = ReducerDependencyResolver.GetReducerDependencies(dependency);
                if (subDependencies.Contains(reducerType))
                {
                    throw new ArgumentException(
                        $"Circular dependency detected: {ArgusUtils.GetTypeNameWithoutGenerics(reducerType)} <-> {ArgusUtils.GetTypeNameWithoutGenerics(dependency)}"
                    );
                }
            }
        }

        IEnumerable<Type> reducerTypes = AppDomain.CurrentDomain.GetAssemblies()
                   .SelectMany(a => a.GetTypes())
                   .Where(t => typeof(IReducer<V>).IsAssignableFrom(t)
                           && !t.IsInterface
                           && !t.IsAbstract);

        if (reducerTypes.Any())
        {
            foreach (Type reducerType in reducerTypes)
            {
                if (optInList.Contains(reducerType))
                {
                    if (reducerType.IsGenericTypeDefinition)
                    {
                        Type closedReducerType = reducerType.MakeGenericType(typeof(T));
                        services.AddSingleton(typeof(IReducer<V>), closedReducerType);
                    }
                    else
                    {
                        services.AddSingleton(typeof(IReducer<V>), reducerType);
                    }
                }
            }
        }
    }
}