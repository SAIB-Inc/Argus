namespace Argus.Sync.Reducers;

[AttributeUsage(AttributeTargets.Class)]
public class DependsOnAttribute(Type dependencyType) : Attribute
{
    public Type DependencyType { get; } = dependencyType ?? throw new ArgumentNullException(nameof(dependencyType));
}
