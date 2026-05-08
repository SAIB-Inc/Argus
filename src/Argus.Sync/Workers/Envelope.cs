using Argus.Sync.Data.Models;
using Argus.Sync.Reducers;

namespace Argus.Sync.Workers;

/// <summary>
/// Internal envelope passed between reducer pipelines via bounded channels.
/// Carries one chain-sync message plus the unit-of-work for the current
/// dependency-graph branch. Branch-root pipelines (chain root or any child
/// of a fork point) create the UoW and pass it down; branch-interior
/// pipelines forward the same UoW; branch-leaf pipelines commit it.
/// </summary>
/// <param name="Response">The chain provider's NextResponse for this block.</param>
/// <param name="BranchUow">
/// The UoW for this branch. Null when the chain consumer pushes to a root
/// pipeline (the root creates the UoW); non-null when one pipeline forwards
/// to its dependents within the same branch.
/// </param>
internal sealed record Envelope(NextResponse Response, IBlockUnitOfWork? BranchUow);
