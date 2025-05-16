using Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using WalletAddress = Chrysalis.Wallet.Models.Addresses.Address;

namespace Argus.Sync.Example.Utils;

public static class DatumUtils
{
    public static bool TryGetBech32Address(TransactionOutput output, out string bech32Address)
    {
        bech32Address = string.Empty;

        try
        {
            byte[] address = output.Address();
            if (address == null) return false;

            WalletAddress walletAddress = new(address);

            bech32Address = walletAddress.ToBech32();
            return !string.IsNullOrEmpty(bech32Address);
        }
        catch
        {
            return false;
        }
    }

    public static bool TryGetWalletAddress(TransactionOutput output, out WalletAddress walletAddress)
    {
        walletAddress = default!;

        try
        {
            byte[] address = output.Address();
            if (address == null) return false;

            WalletAddress wallet = new(address);

            walletAddress = wallet;
            return true;
        }
        catch
        {
            return false;
        }
    }
}