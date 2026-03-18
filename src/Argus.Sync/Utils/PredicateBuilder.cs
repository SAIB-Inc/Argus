using System.Linq.Expressions;

namespace Argus.Sync.Utils;

/// <summary>
/// Utility class for building and combining LINQ predicate expressions.
/// </summary>
public static class PredicateBuilder
{
    /// <summary>
    /// Creates a predicate expression that always evaluates to false.
    /// </summary>
    /// <typeparam name="T">The type of the predicate parameter.</typeparam>
    /// <returns>An expression representing a false predicate.</returns>
    public static Expression<Func<T, bool>> False<T>() => _ => false;

    /// <summary>
    /// Combines two predicate expressions using a logical OR.
    /// </summary>
    /// <typeparam name="T">The type of the predicate parameter.</typeparam>
    /// <param name="expr1">The first predicate expression.</param>
    /// <param name="expr2">The second predicate expression.</param>
    /// <returns>A combined predicate expression.</returns>
    public static Expression<Func<T, bool>> Or<T>(this Expression<Func<T, bool>> expr1, Expression<Func<T, bool>> expr2)
    {
        ArgumentNullException.ThrowIfNull(expr1);
        ArgumentNullException.ThrowIfNull(expr2);
        InvocationExpression invokedExpr = Expression.Invoke(expr2, expr1.Parameters);
        return Expression.Lambda<Func<T, bool>>(Expression.OrElse(expr1.Body, invokedExpr), expr1.Parameters);
    }
}
