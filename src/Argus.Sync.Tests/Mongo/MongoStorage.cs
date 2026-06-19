using Argus.Sync.Data.Models;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Argus.Sync.Tests.Mongo;

/// <summary>
/// The storage handle a reducer receives from <c>uow.GetStorage&lt;MongoStorage&gt;()</c>: the database
/// plus the unit-of-work's active session. Reducer writes must pass <see cref="Session"/> so they join
/// the unit-of-work's transaction (the Mongo analogue of EF's <c>GetStorage&lt;DbContext&gt;()</c>).
/// </summary>
public sealed class MongoStorage(IMongoDatabase database, IClientSessionHandle session)
{
    /// <summary>The Mongo database the reducer writes into.</summary>
    public IMongoDatabase Database { get; } = database;

    /// <summary>The active session/transaction; pass it to every write so it participates in the commit.</summary>
    public IClientSessionHandle Session { get; } = session;

    /// <summary>Convenience accessor for a typed collection by name.</summary>
    public IMongoCollection<T> Collection<T>(string name) => Database.GetCollection<T>(name);
}

/// <summary>
/// BSON document the Mongo backend persists for a reducer's checkpoint (collection <c>ReducerStates</c>),
/// keyed by reducer name. Maps to/from the framework's <see cref="ReducerState"/>; the intersection points
/// are kept as the same JSON strings the EF backend uses, so the checkpoint-window math is identical.
/// </summary>
public sealed class MongoReducerStateDoc
{
    /// <summary>Reducer name — the document id.</summary>
    [BsonId]
    public string Name { get; set; } = default!;

    /// <summary>JSON-serialized start intersection point.</summary>
    public string StartIntersectionJson { get; set; } = string.Empty;

    /// <summary>JSON-serialized latest intersection points (the rolling window).</summary>
    public string LatestIntersectionsJson { get; set; } = string.Empty;

    /// <summary>When the checkpoint row was first created.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Maps this document to the framework <see cref="ReducerState"/>.</summary>
    public ReducerState ToReducerState() => new(Name, CreatedAt)
    {
        StartIntersectionJson = StartIntersectionJson,
        LatestIntersectionsJson = LatestIntersectionsJson,
    };

    /// <summary>Builds a document from a framework <see cref="ReducerState"/>.</summary>
    public static MongoReducerStateDoc FromReducerState(ReducerState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new MongoReducerStateDoc
        {
            Name = state.Name,
            StartIntersectionJson = state.StartIntersectionJson,
            LatestIntersectionsJson = state.LatestIntersectionsJson,
            CreatedAt = state.CreatedAt.UtcDateTime,
        };
    }
}
