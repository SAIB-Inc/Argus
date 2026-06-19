using Argus.Sync.Data;
using Argus.Sync.Data.Models;
using Argus.Sync.Reducers;
using MongoDB.Driver;

namespace Argus.Sync.Tests.Mongo;

/// <summary>
/// MongoDB implementation of <see cref="IBlockUnitOfWorkFactory"/> — the storage-backend seam for Mongo.
/// Creates a per-branch <see cref="MongoBlockUnitOfWork"/> (each owning its own session + open
/// transaction) and reads reducer checkpoints from the <c>ReducerStates</c> collection. The Mongo
/// client must target a replica set (or sharded cluster) so multi-document transactions are available.
/// </summary>
public sealed class MongoBlockUnitOfWorkFactory : IBlockUnitOfWorkFactory
{
    private readonly IMongoClient _client;
    private readonly string _databaseName;
    private readonly int _rollbackBuffer;
    private readonly IMongoCollection<MongoReducerStateDoc> _states;

    /// <summary>Creates a factory bound to a Mongo client + database, with an optional rollback-window size.</summary>
    public MongoBlockUnitOfWorkFactory(
        IMongoClient client,
        string databaseName,
        int rollbackBuffer = ReducerStateCheckpointWindow.DefaultMaxCount)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _databaseName = databaseName;
        _rollbackBuffer = Math.Max(1, rollbackBuffer);
        _states = client.GetDatabase(databaseName).GetCollection<MongoReducerStateDoc>("ReducerStates");
    }

    /// <inheritdoc />
    public async Task<IBlockUnitOfWork> CreateAsync(CancellationToken ct = default)
    {
        IClientSessionHandle session = await _client.StartSessionAsync(cancellationToken: ct).ConfigureAwait(false);
        session.StartTransaction();
        return new MongoBlockUnitOfWork(_client.GetDatabase(_databaseName), session, _rollbackBuffer);
    }

    /// <inheritdoc />
    public async Task<ReducerState?> GetReducerStateAsync(string reducerName, CancellationToken ct = default)
    {
        FilterDefinition<MongoReducerStateDoc> byId = Builders<MongoReducerStateDoc>.Filter.Eq(x => x.Name, reducerName);
        MongoReducerStateDoc? doc = await _states.Find(byId).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return doc?.ToReducerState();
    }
}
