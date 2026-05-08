using Argus.Sync.Reducers;
using Argus.Sync.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Argus.Sync.Extensions;

/// <summary>
/// Extension methods for registering Argus reducers with the dependency injection container.
/// </summary>
public static class ReducerExtensions
{
    /// <summary>
    /// Registers reducers based on application configuration settings. Scans the consumer's
    /// loaded assemblies for non-abstract <see cref="IReducer"/> implementations and registers
    /// them as singletons. The optional <c>CardanoIndexReducers:ActiveReducers</c> config
    /// list restricts which reducers load (by simple type name).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    public static void AddReducers(
        this IServiceCollection services,
        IConfiguration configuration)
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

        List<Type> reducerTypes = [.. AppDomain.CurrentDomain
            .GetAssemblies()
            .Where(a => !a.FullName!.StartsWith("Argus.Sync,", StringComparison.Ordinal))
            .SelectMany(a => a.GetTypes())
            .Where(t => typeof(IReducer).IsAssignableFrom(t)
                    && !t.IsInterface
                    && !t.IsAbstract)];

        // If no active reducers specified, register all found reducers
        if (!activeReducers.Any())
        {
            foreach (Type reducerType in reducerTypes)
            {
                ValidateReducerDependencies(reducerType);
                RegisterReducer(services, reducerType);
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
            RegisterReducer(services, reducerType);
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

    private static void RegisterReducer(IServiceCollection services, Type reducerType)
    {
        // Generic-typedef reducers are no longer supported with the non-generic IReducer.
        // If a consumer needs to parameterize a reducer over their context type, they
        // construct it themselves and register the closed type.
        if (reducerType.IsGenericTypeDefinition)
        {
            throw new InvalidOperationException(
                $"Generic reducer type definitions ({reducerType.FullName}) are not supported. Register a closed concrete reducer type instead.");
        }

        _ = services.AddSingleton(typeof(IReducer), reducerType);
    }
}
