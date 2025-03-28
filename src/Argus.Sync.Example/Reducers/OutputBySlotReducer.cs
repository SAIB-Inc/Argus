using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using Block = Chrysalis.Cbor.Types.Cardano.Core.Block;
using Argus.Sync.Data.Models.Enums;
using Argus.Sync.Extensions;
using Argus.Sync.Utils;
using Chrysalis.Cbor.Extensions.Cardano.Core;
using Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;
using Chrysalis.Cbor.Extensions.Cardano.Core.Header;
using Chrysalis.Wallet.Models.Addresses;
using Chrysalis.Cbor.Extensions.Cardano.Core.Common;
using Argus.Sync.Example.Models;
using Argus.Sync.Reducers;

namespace Argus.Sync.Example.Reducers;

public class OutputBySlotReducer(
    IDbContextFactory<TestDbContext> dbContextFactory
)
{
    public async Task RollBackwardAsync(ulong slot)
    {
        await using TestDbContext dbContext = await dbContextFactory.CreateDbContextAsync();

        List<OutputBySlot> spentOutputs = await dbContext.OutputBySlot
            .Where(o => o.SpentSlot >= slot)
            .ToListAsync();

        if (spentOutputs.Any())
        {
            spentOutputs.ForEach(output =>
            {
                output.SpentSlot = null;
                output.UtxoStatus = UtxoStatus.Unspent;
            });
            dbContext.OutputBySlot.UpdateRange(spentOutputs);
        }

        dbContext.OutputBySlot.RemoveRange(
            dbContext.OutputBySlot
            .AsNoTracking()
            .Where(o => o.Slot >= slot && o.SpentSlot == null)
        );

        await dbContext.SaveChangesAsync();
    }

    public async Task<IEnumerable<OutputBySlot>> ResolveInputsAsync(Block block, TestDbContext dbContext)
    {
        List<(string Id, ulong Index)> inputHashes = [.. block.TransactionBodies()
            .SelectMany(
                txBody =>
                    txBody.Inputs()
                        .Select(
                            input => (Convert.ToHexStringLower(input.TransactionId()), input.Index())
                        )
            )];

        Expression<Func<OutputBySlot, bool>> predicate = PredicateBuilder.False<OutputBySlot>();
        inputHashes.ForEach(input =>
        {
            predicate = predicate.Or(p => p.TxHash == input.Id && p.TxIndex == input.Index);
        });

        return await dbContext.OutputBySlot
            .Where(predicate)
            .ToListAsync();
    }

    public async Task RollForwardAsync(Block block)
    {
        if (!block.TransactionBodies().Any()) return;

        await using TestDbContext dbContext = await dbContextFactory.CreateDbContextAsync();
        IEnumerable<OutputBySlot> existingOutputs = await ResolveInputsAsync(block, dbContext);
        ProcessInputs(block, dbContext, existingOutputs);
        ProcessOutputs(block, dbContext);
        await dbContext.SaveChangesAsync();
        await dbContext.DisposeAsync();
    }

    private static void ProcessInputs(Block block, TestDbContext dbContext, IEnumerable<OutputBySlot> existingOutputs)
    {
        if (!existingOutputs.Any()) return;
        ulong slot = block.Header().HeaderBody().Slot();

        existingOutputs.ToList().ForEach(eo =>
        {
            eo.SpentSlot = slot;
            eo.UtxoStatus = UtxoStatus.Spent;
        });
        dbContext.OutputBySlot.UpdateRange(existingOutputs);
    }

    private static void ProcessOutputs(Block block, TestDbContext dbContext)
    {
        ulong slot = block.Header().HeaderBody().Slot();
        IEnumerable<OutputBySlot> outputEntities = block.TransactionBodies()
            .SelectMany(txBody => txBody.Outputs().Select((output, outputIndex) =>
                new OutputBySlot(
                    txBody.Hash(),
                    (uint)outputIndex,
                    slot,
                    null,
                    new Address(output.Address()).ToBech32(),
                    output.Raw!.Value.ToArray(),
                    [], // TODO: Proper datum extension
                    null,
                    UtxoStatus.Unspent
                )))
            .Where(entity => entity != null);

        dbContext.OutputBySlot.AddRange(outputEntities);
    }
}