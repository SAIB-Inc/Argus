using Argus.Sync.Data;
using Argus.Sync.Data.Models;
using Argus.Sync.Reducers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Argus.Sync.Extensions;

public static class ReducerExtensions
{
    public static void AddReducers<T, V>(this IServiceCollection services, Type[]? optInList = null)
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