using Microsoft.Extensions.Configuration;

namespace Argus.Sync.Providers;

/// <summary>
/// Configuration-based implementation of IChainProviderFactory.
/// Creates ICardanoChainProvider instances based on application configuration settings.
/// Supports UnixSocket (N2C), gRPC (U5C), and TCP (N2N) connection types.
/// </summary>
public class ConfigurationChainProviderFactory(IConfiguration configuration) : IChainProviderFactory
{
    /// <inheritdoc />
    public ICardanoChainProvider CreateProvider()
    {
        string connectionType = configuration.GetValue<string>("CardanoNodeConnection:ConnectionType")
            ?? throw new InvalidOperationException("Connection type not configured.");

        return connectionType switch
        {
            "UnixSocket" => CreateN2CProvider(),
            "TCP" => throw new NotImplementedException("TCP connection type is not yet implemented."),
            "gRPC" => CreateU5CProvider(),
            _ => throw new InvalidOperationException($"Invalid chain provider connection type: {connectionType}")
        };
    }

    private N2CProvider CreateN2CProvider()
    {
        string socketPath = configuration.GetValue<string?>("CardanoNodeConnection:UnixSocket:Path")
            ?? throw new InvalidOperationException("Socket path is not configured for UnixSocket connection type.");

        return new N2CProvider(socketPath);
    }

    private U5CProvider CreateU5CProvider()
    {
        string gRPCEndpoint = configuration.GetValue<string?>("CardanoNodeConnection:gRPC:Endpoint")
            ?? throw new InvalidOperationException("gRPC endpoint is not configured for gRPC connection type.");

        string apiKey = configuration.GetValue<string?>("CardanoNodeConnection:gRPC:ApiKey")
            ?? throw new InvalidOperationException("Demeter API key is missing for gRPC connection type.");

        Dictionary<string, string> headers = new()
        {
            { "dmtr-api-key", apiKey }
        };

        return new U5CProvider(gRPCEndpoint, headers);
    }
}
