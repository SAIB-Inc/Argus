using System.Linq.Expressions;
using Argus.Sync.Example.Data;
using Argus.Sync.Example.Data.Enum;
using Argus.Sync.Example.Data.Extensions;
using Argus.Sync.Example.Models;
using Argus.Sync.Extensions;
using Argus.Sync.Reducers;
using Argus.Sync.Utils;
using Chrysalis.Cardano.Core.Extensions;
using Chrysalis.Cardano.Core.Types.Block;
using Chrysalis.Cardano.Core.Types.Block.Transaction.Body;
using Chrysalis.Cardano.Core.Types.Block.Transaction.Input;
using Chrysalis.Cardano.Core.Types.Block.Transaction.Output;
using Chrysalis.Cardano.Sundae.Types.Common;
using Chrysalis.Cbor.Converters;
using Microsoft.EntityFrameworkCore;

namespace Argus.Sync.Example.Reducers;

public class OrderBySlotReducer(
    IDbContextFactory<TestDbContext> dbContextFactory,
    IConfiguration configuration
) : IReducer<OrderBySlot>
{
    private readonly string _orderBookScriptHash = configuration.GetValue("OrderBook", "0f45963b8e895bd46839bbcf34185993440f26e3f07c668bd2026f92");

    public async Task RollBackwardAsync(ulong slot)
    {
        await using TestDbContext dbContext = await dbContextFactory.CreateDbContextAsync();

        await dbContext
            .OrdersBySlot
            .Where(o => o.Slot >= slot)
            .ExecuteDeleteAsync();
    }

    public async Task RollForwardAsync(Block block)
    {
        await using TestDbContext dbContext = await dbContextFactory.CreateDbContextAsync();
        IEnumerable<TransactionBody> transactions = block.TransactionBodies();

        transactions.ToList().ForEach(transaction => ProcessOutputs(
            transaction,
            dbContext,
            block
        ));

        List<(string txHash, ulong index)> inputOutRefs = [.. transactions
            .SelectMany(tx => tx.Inputs(), (tx, input) => (txHash: input.TransactionId(), index: input.Index.Value))];

        Expression<Func<OrderBySlot, bool>> predicate = PredicateBuilder.False<OrderBySlot>();
        inputOutRefs.ForEach(input =>
            predicate = predicate.Or(o => o.TxHash == input.txHash && o.Index == input.index));

        List<OrderBySlot> dbEntries = await dbContext.OrdersBySlot
            .Where(predicate)
            .ToListAsync();

        List<OrderBySlot> localEntries = [.. dbContext.OrdersBySlot.Local.Where(e => inputOutRefs.Any(input => input.txHash == e.TxHash && input.index == e.Index))];

        List<OrderBySlot> allEntries = [.. dbEntries
            .Concat(localEntries)
            .GroupBy(e => (e.TxHash, e.Index))
            .Select(g => g.First())];

        ProcessInputs(block, allEntries, dbContext);

        await dbContext.SaveChangesAsync();
    }

    private void ProcessOutputs(TransactionBody tx, TestDbContext dbContext, Block block)
    {
        ulong slot = block.Slot() ?? 0;

        string txHash = tx.Id();

        tx.Outputs().Select((output, index) => new { Output = output, Index = (ulong)index })
            .ToList().ForEach(e =>
            {
                string? outputBech32Addr = e.Output.Address()?.GetBaseAddressBech32();

                if (string.IsNullOrEmpty(outputBech32Addr) || !outputBech32Addr.StartsWith("addr")) return;

                string pkh = Convert.ToHexString(e.Output.Address()!.GetPublicKeyHash()).ToLowerInvariant();

                if (pkh != _orderBookScriptHash) return;

                OrderDatum orderDatum = CborSerializer.Deserialize<OrderDatum>(e.Output.Datum()!);
                AssetClass asset = orderDatum.Asset;

                string policyId = Convert.ToHexStringLower(asset.Value()[0].Value);
                string assetName = Convert.ToHexStringLower(asset.Value()[1].Value);

                OrderBySlot orderBySlotHistory = new(
                    txHash,
                    e.Index,
                    slot,
                    e.Output.Address()?.GetBaseAddressBech32()!,
                    policyId,
                    assetName,
                    orderDatum.Quantity.Value,
                    null,
                    null,
                    tx.Raw ?? [],
                    e.Output.Datum(),
                    OrderStatus.Active
                );

                dbContext.OrdersBySlot.Add(orderBySlotHistory);
            });
    }

    private static void ProcessInputs(Block block, List<OrderBySlot> orderBySlotEntries, TestDbContext dbContext)
    {
        if (!orderBySlotEntries.Any()) return;

        List<TransactionBody> transactions = block.TransactionBodies().ToList();
        IEnumerable<(byte[]? RedeemerRaw, TransactionInput Input, TransactionBody Tx)> inputRedeemers = transactions.GetInputRedeemerTuple(block);
        ulong currentSlot = block.Slot() ?? 0;

        orderBySlotEntries.ForEach(entry =>
        {
            (byte[]? RedeemerRaw, TransactionInput Input, TransactionBody Tx) redeemerInfo = inputRedeemers
                .FirstOrDefault(ir => ir.Input.TransactionId() == entry.TxHash && ir.Input.Index.Value == entry.Index);

            bool isSold = entry.IsAcceptOrCancelRedeemer(inputRedeemers);

            OrderBySlot? localEntry = dbContext.OrdersBySlot.Local
                .FirstOrDefault(e => e.TxHash == entry.TxHash && e.Index == entry.Index);

            Address? executorAddress = redeemerInfo.Tx.Outputs().Last().Address();
            string executorAddressBech32 = executorAddress?.GetBaseAddressBech32() ?? string.Empty;

            OrderBySlot updatedEntry = entry with
            {
                Slot = currentSlot,
                Status = isSold ? OrderStatus.Sold : OrderStatus.Cancelled,
                BuyerAddress = isSold ? executorAddressBech32 : null,
                SpentTxHash = isSold ? redeemerInfo.Tx.Id() : null
            };

            if (localEntry is not null)
            {
                dbContext.Entry(localEntry).CurrentValues.SetValues(updatedEntry);
            }
            else
            {
                dbContext.Attach(updatedEntry).State = EntityState.Modified;
            }
        });
    }

}