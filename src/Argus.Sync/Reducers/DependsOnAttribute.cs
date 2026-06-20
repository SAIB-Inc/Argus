namespace Argus.Sync.Reducers;

/// <summary>
/// Specifies that a reducer depends on another reducer for block processing order.
/// </summary>
/// <param name="dependencyType">The type of the reducer this reducer depends on.</param>
[AttributeUsage(AttributeTargets.Class)]
public sealed class DependsOnAttribute(Type dependencyType) : Attribute
{
    /// <summary>Gets the type of the reducer this reducer depends on.</summary>
    public Type DependencyType { get; } = dependencyType ?? throw new ArgumentNullException(nameof(dependencyType));
}
