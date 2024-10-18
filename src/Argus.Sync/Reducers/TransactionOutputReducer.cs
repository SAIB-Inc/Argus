using Microsoft.EntityFrameworkCore;
using TransactionOutputEntity = Argus.Sync.Data.Models.TransactionOutput;
using System.Linq.Expressions;
using Argus.Sync.Data;
using Block = Chrysalis.Cardano.Models.Core.Block.Block;
using Argus.Sync.Data.Models.Enums;
using Argus.Sync.Extensions.Chrysalis;
using Argus.Sync.Extensions;
using Argus.Sync.Utils;
using Chrysalis.Cbor;

namespace Argus.Sync.Reducers;

public class TransactionOutputReducer<T>(
    IDbContextFactory<T> dbContextFactory
) : IReducer<TransactionOutputEntity> where T : CardanoDbContext
{
    private T _dbContext = default!;

    public async Task RollBackwardAsync(ulong slot)
    {
        _dbContext = dbContextFactory.CreateDbContext();

        List<TransactionOutputEntity> spentOutputs = await _dbContext.TransactionOutputs
            .Where(o => o.SpentSlot > slot)
            .ToListAsync();

        if (spentOutputs.Any())
        {
            foreach (TransactionOutputEntity output in spentOutputs)
            {
                output.SpentSlot = null;
                output.UtxoStatus = UtxoStatus.Unspent;
            }
            _dbContext.TransactionOutputs.UpdateRange(spentOutputs);
        }

        _dbContext.TransactionOutputs.RemoveRange(
            _dbContext.TransactionOutputs.AsNoTracking().Where(o => o.Slot > slot && o.SpentSlot == null)
        );

        await _dbContext.SaveChangesAsync();
        _dbContext.Dispose();
    }

    public async Task RollForwardAsync(Block block)
    {
        var value = block.TransactionBodies();
        if (block.TransactionBodies().Any())
        {
            _dbContext = dbContextFactory.CreateDbContext();

            await ProcessInputsAsync(block);
            ProcessOutputs(block);

            await _dbContext.SaveChangesAsync();
            _dbContext.Dispose();
        }
    }

    private async Task ProcessInputsAsync(Block block)
    {
        List<(string, string)> inputHashes = block.TransactionBodies()
            .SelectMany(txBody => txBody.Inputs().Select(input => (Convert.ToHexString(input.TransactionId.Value).ToLowerInvariant(), input.Index.Value.ToString())))
            .ToList();

        Expression<Func<TransactionOutputEntity, bool>> predicate = PredicateBuilder.False<TransactionOutputEntity>();

        foreach ((string id, string index) in inputHashes)
        {
            predicate = predicate.Or(p => p.Id == id && p.Index.ToString() == index);
        }

        List<TransactionOutputEntity> existingOutputs = await _dbContext.TransactionOutputs
            .Where(predicate)
            .ToListAsync();

        if (existingOutputs.Any())
        {
            existingOutputs.ForEach(eo =>
            {
                eo.SpentSlot = block.Slot();
                eo.UtxoStatus = UtxoStatus.Spent;
            });
            _dbContext.TransactionOutputs.UpdateRange(existingOutputs);
        }
    }

    private void ProcessOutputs(Block block)
    {
        List<TransactionOutputEntity> outputEntities = block.TransactionBodies()
            .SelectMany(txBody => txBody.Outputs().Select((output, outputIndex) =>
                DataUtils.MapTransactionOutputEntity(
                    txBody.TransactionId(),
                    (uint)outputIndex,
                    block.Slot(),
                    output,
                    UtxoStatus.Unspent
                )))
            .Where(entity => entity != null)
            .ToList()!;

        _dbContext.TransactionOutputs.AddRange(outputEntities);
    }
}