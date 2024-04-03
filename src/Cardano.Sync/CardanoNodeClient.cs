using Cardano.Sync.Data.Models;
using Cardano.Sync.Data.Models.Experimental;
using PallasDotnet;

namespace Cardano.Sync;

public class CardanoNodeClient
{
    private readonly NodeClient _nodeClient;

    public CardanoNodeClient()
    {
        _nodeClient = new NodeClient();
    }

    public async Task ConnectAsync(string socketPath, ulong magicNumber)
    {
        await _nodeClient.ConnectAsync(socketPath, magicNumber);
    }

    public async Task<UtxosByAddress> GetUtxosByAddressAsync(string address)
    {
        var utxosByAddressCbor = await _nodeClient.GetUtxoByAddressCborAsync(address);
        var utxosByAddress = new List<UtxosByAddress>();

        utxosByAddressCbor.Select(cbor => CborConverter.Deserialize<UtxosByAddress>(cbor)).ToList().ForEach(utxosByAddress.Add);

        // Flatten the list of UtxosByAddress into a single UtxosByAddress object
        var finalUtxosByAddress = new UtxosByAddress(
            utxosByAddress.SelectMany(utxos => utxos.Values).ToDictionary(utxo => utxo.Key, utxo => utxo.Value)
        );

        return finalUtxosByAddress;
    }
}