namespace Argus.Sync.Reducers;

[AttributeUsage(AttributeTargets.Class)]
public class ReducerDependsAttribute(params Type[] types) : Attribute
{
    public Type[] Types { get; } = types;
}
