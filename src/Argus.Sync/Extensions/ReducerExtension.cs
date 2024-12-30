using System.Reflection;
using Argus.Sync.Data;
using Argus.Sync.Data.Models;
using Argus.Sync.Reducers;
using Argus.Sync.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Argus.Sync.Extensions;

public static class ReducerExtensions
{
    public static void AddReducers<T, V>(this IServiceCollection services, Type[]? optInList = null)
        where T : DbContext
        where V : IReducerModel
    {
        optInList ??= [];

        IEnumerable<string> reducerNames = optInList.Select(ArgusUtils.GetTypeNameWithoutGenerics);
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

    public static void AddReducers<T, V>(
        this IServiceCollection services,
        IConfiguration configuration)
        where T : DbContext
        where V : IReducerModel
    {
        IEnumerable<string> activeReducers = configuration.GetSection("CardanoIndexReducers:ActiveReducers").Get<IEnumerable<string>>() ?? [];

        // Convert to list for multiple enumeration
        activeReducers ??= [];
        List<string> activeReducersList = activeReducers.ToList();

        Assembly? entryAssembly = Assembly.GetEntryAssembly();

        List<Type> reducerTypes = entryAssembly!
            .GetTypes()
            .Where(t => typeof(IReducer<V>).IsAssignableFrom(t)
                    && !t.IsInterface
                    && !t.IsAbstract)
            .ToList();

        // If no active reducers specified, register all found reducers
        if (!activeReducersList.Any())
        {
            foreach (Type reducerType in reducerTypes)  // Changed to iterate over reducerTypes
            {
                ValidateReducerDependencies(reducerType);
                RegisterReducer<V>(services, reducerType, typeof(T));
            }
            return;
        }

        // Validate reducer names against available types
        List<string> availableReducerNames = reducerTypes
            .Select(ArgusUtils.GetTypeNameWithoutGenerics)
            .ToList();

        List<string> invalidReducers = activeReducersList
            .Where(r => !availableReducerNames.Contains(r))
            .ToList();

        if (invalidReducers.Any())
        {
            throw new ArgumentException(
                $"Invalid reducer names specified: {string.Join(", ", invalidReducers)}"
            );
        }

        // Check for duplicates in active reducers
        List<string> duplicateNames = activeReducersList
            .GroupBy(x => x)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateNames.Any())
        {
            throw new ArgumentException(
                $"Duplicate reducer names specified: {string.Join(", ", duplicateNames)}"
            );
        }

        // Register the specified reducers
        foreach (string reducerName in activeReducersList)
        {
            Type reducerType = reducerTypes
                .First(t => ArgusUtils.GetTypeNameWithoutGenerics(t) == reducerName);

            ValidateReducerDependencies(reducerType);
            RegisterReducer<V>(services, reducerType, typeof(T));  // Note the <V> here
        }
    }

    private static void ValidateReducerDependencies(Type reducerType)
    {
        Type[] dependencies = ReducerDependencyResolver.GetReducerDependencies(reducerType);

        foreach (Type dependency in dependencies)
        {
            Type[] subDependencies = ReducerDependencyResolver.GetReducerDependencies(dependency);
            if (subDependencies.Contains(reducerType))
            {
                throw new ArgumentException(
                    $"Circular dependency detected: {ArgusUtils.GetTypeNameWithoutGenerics(reducerType)} <-> {ArgusUtils.GetTypeNameWithoutGenerics(dependency)}"
                );
            }
        }
    }

    private static void RegisterReducer<V>(IServiceCollection services, Type reducerType, Type dbContextType)
        where V : IReducerModel
    {
        if (reducerType.IsGenericTypeDefinition)
        {
            Type closedReducerType = reducerType.MakeGenericType(dbContextType);
            services.AddSingleton(typeof(IReducer<V>), closedReducerType);
        }
        else
        {
            services.AddSingleton(typeof(IReducer<V>), reducerType);
        }
    }
}