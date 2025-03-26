namespace Argus.Sync.Data.Models;

public record ReducerStats(string Name, ReducerState StartState, ReducerState CurrentState)
{
    public string Name { get; } = Name;
    public ReducerState StartState { get; } = StartState;
    public ReducerState CurrentState { get; set; } = CurrentState;
}