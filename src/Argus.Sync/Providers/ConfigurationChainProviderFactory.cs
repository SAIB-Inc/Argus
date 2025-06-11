using Microsoft.Extensions.Configuration;

namespace Argus.Sync.Providers;

/// <summary>
/// Configuration-based implementation of IChainProviderFactory.
/// Creates ICardanoChainProvider instances based on application configuration settings.
/// Supports UnixSocket (N2C), gRPC (U5C), and TCP (N2N) connection types.
/// </summary>
public class ConfigurationChainProviderFactory(IConfiguration configuration) : IChainProviderFactory

{
    public ICardanoChainProvider CreateProvider()
    {
        var connectionType = configuration.GetValue<string>("CardanoNodeConnection:ConnectionType") 
            ?? throw new Exception("Connection type not configured.");
        
        return connectionType switch
        {
            "UnixSocket" => CreateN2CProvider(),
            "TCP" => throw new NotImplementedException("TCP connection type is not yet implemented."),
            "gRPC" => CreateU5CProvider(),
            _ => throw new Exception($"Invalid chain provider connection type: {connectionType}")
        };
    }

    private N2CProvider CreateN2CProvider()
    {
        var socketPath = configuration.GetValue<string?>("CardanoNodeConnection:UnixSocket:Path")
            ?? throw new InvalidOperationException("Socket path is not configured for UnixSocket connection type.");
        
        return new N2CProvider(socketPath);
    }

    private U5CProvider CreateU5CProvider()
    {
        var gRPCEndpoint = configuration.GetValue<string?>("CardanoNodeConnection:gRPC:Endpoint")
            ?? throw new Exception("gRPC endpoint is not configured for gRPC connection type.");
        
        var apiKey = configuration.GetValue<string?>("CardanoNodeConnection:gRPC:ApiKey")
            ?? throw new Exception("Demeter API key is missing for gRPC connection type.");
        
        var headers = new Dictionary<string, string>
        {
            { "dmtr-api-key", apiKey }
        };
        
        return new U5CProvider(gRPCEndpoint, headers);
    }
}