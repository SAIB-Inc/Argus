using Microsoft.EntityFrameworkCore;
using PallasDotnet.Models;
using Cardano.Sync.Data;
using TransactionOutputEntity = Cardano.Sync.Data.Models.TransactionOutput;
using ValueEntity = Cardano.Sync.Data.Models.Value;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Cardano.Sync.Reducers;

public class TransactionOutputReducer<T>(
    IDbContextFactory<T> dbContextFactory,
    IConfiguration configuration
) : IReducer<TransactionOutputEntity> where T : CardanoDbContext
{
    private T _dbContext = default!;

    public async Task RollForwardAsync(NextResponse response)
    {
        _dbContext = dbContextFactory.CreateDbContext();
        response.Block.TransactionBodies.ToList().ForEach(txBody =>
        {
            txBody.Outputs.ToList().ForEach(output =>
            {
                _dbContext.TransactionOutputs.Add(Utils.MapTransactionOutputEntity(txBody.Id.ToHex(), response.Block.Slot, output));
            });
        });

        await _dbContext.SaveChangesAsync();
        _dbContext.Dispose();
    }

    public async Task RollBackwardAsync(NextResponse response)
    {
        _dbContext = dbContextFactory.CreateDbContext();
        var rollbackSlot = response.Block.Slot;
        var schema = configuration.GetConnectionString("CardanoContextSchema");
        _dbContext.TransactionOutputs.RemoveRange(
            _dbContext.TransactionOutputs.AsNoTracking().Where(o => o.Slot > rollbackSlot)
        );
        await _dbContext.SaveChangesAsync();
        _dbContext.Dispose();
    }
}