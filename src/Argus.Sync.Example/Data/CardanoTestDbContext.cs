using Argus.Sync.Data;
using Microsoft.EntityFrameworkCore;

namespace Argus.Sync.Example.Data;

public class CardanoTestDbContext
(
    DbContextOptions<CardanoTestDbContext> options,
    IConfiguration configuration
) : OutputBySlotDbContext(options, configuration)
{ }
