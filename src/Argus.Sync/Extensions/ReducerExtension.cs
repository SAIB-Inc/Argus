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

        // Skip reducer registration when invoked by the `dotnet ef` design-time tools (migrations,
        // scaffolding): the tool runs as an assembly literally named "ef", and we don't want the indexer's
        // reducers + hosted worker spun up during a migration command. We check the ENTRY assembly only —
        // not every loaded assembly — so a referenced/transitive assembly that merely happens to be named
        // "ef" can't trip it.
        if (IsEfDesignTime(System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name))
        {
            // If a real application's entry assembly is genuinely named "ef", this would otherwise register
            // ZERO reducers and the indexer would silently do nothing — so surface the reason.
            Console.Error.WriteLine(
                "[Argus] Entry assembly is named 'ef' — assuming a `dotnet ef` design-time invocation and " +
                "skipping reducer registration. If this is your application (not the EF CLI), rename the " +
                "startup project; a project named 'ef' collides with the EF tool's assembly name.");
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

    /// <summary>
    /// True when the process entry assembly is the <c>dotnet ef</c> design-time tool (assembly name "ef"),
    /// used to skip reducer/worker registration during migrations. Entry-assembly only and case-sensitive,
    /// so a referenced assembly named "ef" or a differently-cased project name does not match.
    /// </summary>
    internal static bool IsEfDesignTime(string? entryAssemblyName) =>
        string.Equals(entryAssemblyName, "ef", StringComparison.Ordinal);

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
