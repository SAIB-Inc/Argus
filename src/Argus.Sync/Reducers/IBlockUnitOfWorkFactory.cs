namespace Argus.Sync.Reducers;

/// <summary>
/// Factory for per-block, per-branch <see cref="IBlockUnitOfWork"/> instances.
/// The framework calls <see cref="CreateAsync"/> once at the start of each
/// branch's processing for each block, and disposes the UoW (commit or rollback)
/// when the branch completes.
/// </summary>
public interface IBlockUnitOfWorkFactory
{
    /// <summary>Creates a fresh unit of work for one block-branch.</summary>
    Task<IBlockUnitOfWork> CreateAsync(CancellationToken ct = default);
}
