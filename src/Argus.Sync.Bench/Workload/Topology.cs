namespace Argus.Sync.Bench.Workload;

/// <summary>
/// Defines a dependency-graph shape for the bench. Mirrors the real
/// `[DependsOn]`-driven graph in Argus, but constructed declaratively so a
/// single bench class can run all impls against the same topology.
/// </summary>
public sealed class Topology(string name, IReadOnlyList<TopologyNode> roots)
{
    public string Name { get; } = name;
    public IReadOnlyList<TopologyNode> Roots { get; } = roots;

    public IEnumerable<TopologyNode> AllNodes()
    {
        foreach (TopologyNode root in Roots)
        {
            foreach (TopologyNode n in Walk(root))
            {
                yield return n;
            }
        }

        static IEnumerable<TopologyNode> Walk(TopologyNode node)
        {
            yield return node;
            foreach (TopologyNode child in node.Dependents)
            {
                foreach (TopologyNode n in Walk(child))
                {
                    yield return n;
                }
            }
        }
    }

    /// <summary>
    /// Single root reducer with no dependents — measures the producer/consumer
    /// overhead of each pipeline impl in isolation.
    /// </summary>
    public static Topology SingleRoot(WorkProfile profile) =>
        new("SingleRoot", [new TopologyNode("R", profile, [])]);

    /// <summary>
    /// Linear chain `R → A → B`, depth 3. Mirrors the BlockTestReducer chain in
    /// Argus.Sync.Example, which exhibited the 30× slowdown that motivated the
    /// rearchitecture.
    /// </summary>
    public static Topology LinearDepth3(WorkProfile profile)
    {
        TopologyNode b = new("B", profile, []);
        TopologyNode a = new("A", profile, [b]);
        TopologyNode r = new("R", profile, [a]);
        return new("LinearDepth3", [r]);
    }

    /// <summary>
    /// Tree: `R → {A → A1, B}`. Covers both the parallel-sibling forward path
    /// (A and B are siblings under R) and a chain (A → A1).
    /// </summary>
    public static Topology Tree(WorkProfile profile)
    {
        TopologyNode a1 = new("A1", profile, []);
        TopologyNode a = new("A", profile, [a1]);
        TopologyNode b = new("B", profile, []);
        TopologyNode r = new("R", profile, [a, b]);
        return new("Tree", [r]);
    }
}

public sealed class TopologyNode(
    string name,
    WorkProfile profile,
    IReadOnlyList<TopologyNode> dependents)
{
    public string Name { get; } = name;
    public WorkProfile Profile { get; } = profile;
    public IReadOnlyList<TopologyNode> Dependents { get; } = dependents;
}
