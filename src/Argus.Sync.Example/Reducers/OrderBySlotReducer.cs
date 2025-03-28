using System.Linq.Expressions;
using Argus.Sync.Example.Extensions;
using Argus.Sync.Example.Models;
using Argus.Sync.Example.Models.Cardano.Common;
using Argus.Sync.Example.Models.Cardano.OrderBook;
using Argus.Sync.Example.Models.Cardano.Sundae;
using Argus.Sync.Example.Models.Enums;
using Argus.Sync.Reducers;
using Argus.Sync.Utils;
using Chrysalis.Cbor.Extensions.Cardano.Core;
using Chrysalis.Cbor.Extensions.Cardano.Core.Header;
using Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types.Cardano.Core;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Wallet.Models.Addresses;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

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

        List<OrderBySlot> spentOutputs = await dbContext.OrdersBySlot
            .Where(o => o.SpentSlot >= slot)
            .ToListAsync();

        if (!spentOutputs.Any()) return;

        List<OrderBySlot> updatedEntries = [.. spentOutputs.Select(existing => existing with
        {
            Status = OrderStatus.Active,
            SpentSlot = null,
            SpentTxHash = null,
            BuyerAddress = null
        })];

        spentOutputs.ForEach(existing => 
        {
            EntityEntry<OrderBySlot>? trackedEntity = dbContext.ChangeTracker.Entries<OrderBySlot>()
                .FirstOrDefault(e =>
                    e.Entity.Slot == existing.Slot &&
                    e.Entity.TxHash == existing.TxHash &&
                    e.Entity.Index == existing.Index);

            if (trackedEntity is not null) trackedEntity.State = EntityState.Detached;
        });

        dbContext.OrdersBySlot.UpdateRange(updatedEntries);
        dbContext.OrdersBySlot.RemoveRange(
            dbContext.OrdersBySlot
            .AsNoTracking()
            .Where(o => o.Slot >= slot && o.SpentSlot == null)
        );

        await dbContext.SaveChangesAsync();
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
            .SelectMany(tx => tx.Inputs(), (tx, input) => (txHash: Convert.ToHexStringLower(input.TransactionId()), index: input.Index))];

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
        ulong slot = block.Header().HeaderBody().Slot();
        string txHash = tx.Hash();

        tx.Outputs().Select((output, index) => new { Output = output, Index = (ulong)index })
            .ToList().ForEach(e =>
            {
                string? outputBech32Addr = new Address(e.Output.Address()).ToBech32();

                if (string.IsNullOrEmpty(outputBech32Addr) || !outputBech32Addr.StartsWith("addr")) return;

                string pkh = Convert.ToHexString(new Address(e.Output.Address()).GetPkh() ?? []).ToLowerInvariant();

                if (pkh != _orderBookScriptHash) return;

                OrderDatum orderDatum = CborSerializer.Deserialize<OrderDatum>(e.Output.Datum());
                List<CborBytes>? asset = orderDatum.Asset.AssetClassValue();

                string policyId = Convert.ToHexStringLower(asset![0].Value);
                string assetName = Convert.ToHexStringLower(asset[1].Value);

                OrderBySlot orderBySlotHistory = new(
                    txHash,
                    e.Index,
                    slot,
                    outputBech32Addr,
                    policyId,
                    assetName,
                    orderDatum.Quantity,
                    null,
                    null,
                    null,
                    tx.Raw!.Value.ToArray(),
                    e.Output.Datum(),
                    OrderStatus.Active
                );

                dbContext.OrdersBySlot.Add(orderBySlotHistory);
            });
    }

    private static void ProcessInputs(Block block, List<OrderBySlot> orderBySlotEntries, TestDbContext dbContext)
    {
        if (!orderBySlotEntries.Any()) return;

        List<TransactionBody> transactions = [.. block.TransactionBodies()];
        IEnumerable<(byte[]? RedeemerRaw, TransactionInput Input, TransactionBody Tx)> inputRedeemers = transactions.GetInputRedeemerTuple(block);
        ulong currentSlot = block.Header().HeaderBody().Slot();

        orderBySlotEntries.ForEach(entry =>
        {
            (byte[]? RedeemerRaw, TransactionInput Input, TransactionBody Tx) = inputRedeemers
                .FirstOrDefault(ir => Convert.ToHexStringLower(ir.Input.TransactionId()) == entry.TxHash && ir.Input.Index == entry.Index);

            bool isSold = IsAcceptOrCancelRedeemer(entry, inputRedeemers);

            OrderBySlot? localEntry = dbContext.OrdersBySlot.Local
                .FirstOrDefault(e => e.TxHash == entry.TxHash && e.Index == entry.Index);

            Address? executorAddress = new(Tx.Outputs().Last().Address()); // TODO
            string executorAddressBech32 = executorAddress?.ToBech32() ?? string.Empty;

            OrderBySlot updatedEntry = entry with
            {
                SpentSlot = currentSlot,
                Status = isSold ? OrderStatus.Sold : OrderStatus.Cancelled,
                BuyerAddress = isSold ? executorAddressBech32 : null,
                SpentTxHash = isSold ? Tx.Hash() : null
            };

            if (localEntry is not null)
                dbContext.Entry(localEntry).CurrentValues.SetValues(updatedEntry);
            else
                dbContext.Attach(updatedEntry).State = EntityState.Modified;
        });
    }

    public static bool IsAcceptOrCancelRedeemer(
        OrderBySlot order, 
        IEnumerable<(byte[]? RedeemerRaw, 
        TransactionInput Input, 
        TransactionBody Tx)> inputRedeemers
    )
    {
        // Get the input that spent this listing
        byte[]? redeemerRaw = inputRedeemers
            .Where(ir => Convert.ToHexStringLower(ir.Input.TransactionId()) == order.TxHash && ir.Input.Index() == order.Index)
            .Select(ir => ir.RedeemerRaw)
            .FirstOrDefault();

        if (redeemerRaw is null) return false;

        try
        {
            AcceptRedeemer? crashrBuyRedeemer = CborSerializer.Deserialize<AcceptRedeemer>(redeemerRaw);
            return true;
        }
        catch { }

        return false;
    }
}