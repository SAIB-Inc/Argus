using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using Argus.Sync.Data;
using Block = Chrysalis.Cardano.Core.Block;
using Argus.Sync.Data.Models.Enums;
using Argus.Sync.Extensions;
using Argus.Sync.Utils;
using Argus.Sync.Data.Models;
using Chrysalis.Utils;

namespace Argus.Sync.Reducers;

public class OutputBySlotReducer<T>(
    IDbContextFactory<T> dbContextFactory
) : IReducer<OutputBySlot> where T : CardanoDbContext, IOutputBySlotDbContext
{

    public async Task RollBackwardAsync(ulong slot)
    {
        await using T dbContext = await dbContextFactory.CreateDbContextAsync();

        List<OutputBySlot> spentOutputs = await dbContext.OutputBySlot
            .Where(o => o.SpentSlot >= slot)
            .ToListAsync();

        if (spentOutputs.Any())
        {
            foreach (OutputBySlot output in spentOutputs)
            {
                output.SpentSlot = null;
                output.UtxoStatus = UtxoStatus.Unspent;
            }
            dbContext.OutputBySlot.UpdateRange(spentOutputs);
        }

        dbContext.OutputBySlot.RemoveRange(
            dbContext.OutputBySlot
            .AsNoTracking()
            .Where(o => o.Slot >= slot && o.SpentSlot == null)
        );

        await dbContext.SaveChangesAsync();
        await dbContext.DisposeAsync();
    }

    public async Task<IEnumerable<OutputBySlot>> ResolveInputsAsync(Block block, T dbContext)
    {
        List<(string, ulong)> inputHashes = block.TransactionBodies()
            .SelectMany(
                txBody =>
                    txBody.Inputs()
                        .Select(
                            input => (input.TransacationId(), input.Index())
                        )
            )
            .ToList();

        Expression<Func<OutputBySlot, bool>> predicate = PredicateBuilder.False<OutputBySlot>();

        foreach ((string id, ulong index) in inputHashes)
        {
            predicate = predicate.Or(p => p.Id == id && p.Index == index);
        }

        return await dbContext.OutputBySlot
            .Where(predicate)
            .ToListAsync();
    }

    public async Task RollForwardAsync(Block block)
    {
        if (block.TransactionBodies().Any())
        {
            await using T dbContext = await dbContextFactory.CreateDbContextAsync();
            IEnumerable<OutputBySlot> existingOutputs = await ResolveInputsAsync(block, dbContext);
            ProcessInputs(block, dbContext, existingOutputs);
            ProcessOutputs(block, dbContext);
            await dbContext.SaveChangesAsync();
            await dbContext.DisposeAsync();
        }
    }

    private void ProcessInputs(Block block, T dbContext, IEnumerable<OutputBySlot> existingOutputs)
    {
        if (existingOutputs.Any())
        {
            existingOutputs.ToList().ForEach(eo =>
            {
                eo.SpentSlot = block.Slot();
                eo.UtxoStatus = UtxoStatus.Spent;
            });
            dbContext.OutputBySlot.UpdateRange(existingOutputs);
        }
    }

    private void ProcessOutputs(Block block, T dbContext)
    {
        List<OutputBySlot> outputEntities = block.TransactionBodies()
            .SelectMany(txBody => txBody.Outputs().Select((output, outputIndex) =>
                DataUtils.MapTransactionOutputEntity(
                    txBody.Id(),
                    (uint)outputIndex,
                    block.Slot(),
                    output,
                    UtxoStatus.Unspent
                )))
            .Where(entity => entity != null)
            .ToList()!;

        dbContext.OutputBySlot.AddRange(outputEntities);
    }

    public async Task<ulong> QueryTip()
    {
        using T dbContext = await dbContextFactory.CreateDbContextAsync();
        ulong maxSlot = await dbContext.OutputBySlot.MaxAsync(x => (ulong?)x.Slot) ?? 0;
        return maxSlot;
    }
}