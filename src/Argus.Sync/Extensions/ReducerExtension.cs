using Argus.Sync.Data.Models;
using Argus.Sync.Reducers;
using Argus.Sync.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Argus.Sync.Extensions;

/// <summary>
/// Extension methods for registering Argus reducers with the dependency injection container.
/// </summary>
public static class ReducerExtensions
{
    /// <summary>
    /// Registers reducers from an explicit opt-in list of reducer types.
    /// </summary>
    /// <typeparam name="T">The database context type.</typeparam>
    /// <typeparam name="TModel">The reducer model interface type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="optInList">Optional array of reducer types to register.</param>
    public static void AddReducers<T, TModel>(this IServiceCollection services, Type[]? optInList = null)
        where T : DbContext
        where TModel : IReducerModel
    {
        optInList ??= [];

        IEnumerable<string> reducerNames = optInList.Select(ArgusUtil.GetTypeNameWithoutGenerics);
        IEnumerable<string> duplicateNames = [.. reducerNames.GroupBy(x => x)
                                       .Where(g => g.Count() > 1)
                                       .Select(g => g.Key)];

        if (duplicateNames.Any())
        {
            throw new ArgumentException(
                $"Duplicate reducer names found in optInList: {string.Join(", ", duplicateNames)}"
            );
        }

        foreach (Type reducerType in optInList)
        {
            Type? dependency = ReducerDependencyResolver.GetReducerDependency(reducerType);

            if (dependency != null)
            {
                Type? subDependency = ReducerDependencyResolver.GetReducerDependency(dependency);
                if (subDependency == reducerType)
                {
                    throw new ArgumentException(
                        $"Circular dependency detected: {ArgusUtil.GetTypeNameWithoutGenerics(reducerType)} <-> {ArgusUtil.GetTypeNameWithoutGenerics(dependency)}"
                    );
                }
            }
        }

        List<Type> reducerTypes = [.. AppDomain.CurrentDomain.GetAssemblies()
                   .SelectMany(a => a.GetTypes())
                   .Where(t => typeof(IReducer<TModel>).IsAssignableFrom(t)
                           && !t.IsInterface
                           && !t.IsAbstract)];

        if (reducerTypes.Count > 0)
        {
            foreach (Type reducerType in reducerTypes)
            {
                if (optInList.Contains(reducerType))
                {
                    if (reducerType.IsGenericTypeDefinition)
                    {
                        Type closedReducerType = reducerType.MakeGenericType(typeof(T));
                        _ = services.AddSingleton(typeof(IReducer<TModel>), closedReducerType);
                    }
                    else
                    {
                        _ = services.AddSingleton(typeof(IReducer<TModel>), reducerType);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Registers reducers based on application configuration settings.
    /// </summary>
    /// <typeparam name="T">The database context type.</typeparam>
    /// <typeparam name="TModel">The reducer model interface type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    public static void AddReducers<T, TModel>(
        this IServiceCollection services,
        IConfiguration configuration)
        where T : DbContext
        where TModel : IReducerModel
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // Check if we're running migrations
        bool isEfDesignTime = AppDomain.CurrentDomain.GetAssemblies()
            .Any(a => a.GetName().Name == "ef");

        if (isEfDesignTime)
        {
            // During migrations, just add DbContext without reducers
            return;
        }

        IEnumerable<string> activeReducers = configuration
            .GetSection("CardanoIndexReducers:ActiveReducers")
            .Get<IEnumerable<string>>() ?? [];

        // Use AppDomain.CurrentDomain like the first version
        List<Type> reducerTypes = [.. AppDomain.CurrentDomain
            .GetAssemblies()
            .Where(a => !a.FullName!.StartsWith("Argus.Sync,", StringComparison.Ordinal))
            .SelectMany(a => a.GetTypes())
            .Where(t => typeof(IReducer<TModel>).IsAssignableFrom(t)
                    && !t.IsInterface
                    && !t.IsAbstract)];


        // If no active reducers specified, register all found reducers
        if (!activeReducers.Any())
        {
            foreach (Type reducerType in reducerTypes)  // Changed to iterate over reducerTypes
            {
                ValidateReducerDependencies(reducerType);
                RegisterReducer<TModel>(services, reducerType, typeof(T));
            }
            return;
        }

        // Validate reducer names against available types
        List<string> availableReducerNames = [.. reducerTypes.Select(ArgusUtil.GetTypeNameWithoutGenerics)];

        List<string> invalidReducers = [.. activeReducers.Where(r => !availableReducerNames.Contains(r))];

        if (invalidReducers.Count > 0)
        {
            throw new ArgumentException(
                $"Invalid reducer names specified: {string.Join(", ", invalidReducers)}"
            );
        }

        // Check for duplicates in active reducers
        List<string> duplicateNames = [.. activeReducers
            .GroupBy(x => x)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)];

        if (duplicateNames.Count > 0)
        {
            throw new ArgumentException(
                $"Duplicate reducer names specified: {string.Join(", ", duplicateNames)}"
            );
        }

        // Register the specified reducers
        foreach (string reducerName in activeReducers)
        {
            Type reducerType = reducerTypes
                .First(t => ArgusUtil.GetTypeNameWithoutGenerics(t) == reducerName);

            ValidateReducerDependencies(reducerType);
            RegisterReducer<TModel>(services, reducerType, typeof(T));  // Note the <TModel> here
        }
    }

    private static void ValidateReducerDependencies(Type reducerType)
    {
        Type? dependency = ReducerDependencyResolver.GetReducerDependency(reducerType);

        if (dependency != null)
        {
            Type? subDependency = ReducerDependencyResolver.GetReducerDependency(dependency);
            if (subDependency == reducerType)
            {
                throw new ArgumentException(
                    $"Circular dependency detected: {ArgusUtil.GetTypeNameWithoutGenerics(reducerType)} <-> {ArgusUtil.GetTypeNameWithoutGenerics(dependency)}"
                );
            }
        }
    }

    private static void RegisterReducer<TModel>(IServiceCollection services, Type reducerType, Type dbContextType)
        where TModel : IReducerModel
    {
        if (reducerType.IsGenericTypeDefinition)
        {
            Type closedReducerType = reducerType.MakeGenericType(dbContextType);
            _ = services.AddSingleton(typeof(IReducer<TModel>), closedReducerType);
        }
        else
        {
            _ = services.AddSingleton(typeof(IReducer<TModel>), reducerType);
        }
    }
}
