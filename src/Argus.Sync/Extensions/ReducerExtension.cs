using Argus.Sync.Data.Models;
using Argus.Sync.Reducers;
using Argus.Sync.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace Argus.Sync.Extensions;

public static class ReducerExtensions
{
    public static void AddReducers<TContext, V>(this IServiceCollection services, string[]? optInList = null)
        where V : IReducerModel
    {
        optInList ??= [];

        IEnumerable<Type> reducerTypes = AppDomain.CurrentDomain.GetAssemblies()
                   .SelectMany(a => a.GetTypes())
                   .Where(t => typeof(IReducer<V>).IsAssignableFrom(t)
                           && !t.IsInterface
                           && !t.IsAbstract);

        if (reducerTypes.Any())
        {
            foreach (Type reducerType in reducerTypes)
            {
                string typeName = ArgusUtils.GetTypeNameWithoutGenerics(reducerType);

                if (optInList.Contains(typeName))
                {
                    if (reducerType.IsGenericTypeDefinition)
                    {
                        Type closedType = reducerType.MakeGenericType(typeof(TContext));
                        services.AddSingleton(typeof(IReducer<V>), closedType);
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