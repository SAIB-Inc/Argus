using Argus.Sync.Example.Data.Enum;
using Argus.Sync.Example.Data.Utils;
using Chrysalis.Cbor.Types.Functional;
using Chrysalis.Plutus.Types.Address;
using CoreAddress = Chrysalis.Cardano.Core.Types.Block.Transaction.Output.Address;
using PlutusAddress = Chrysalis.Plutus.Types.Address.Address;

namespace Argus.Sync.Example.Data.Extensions;

public static class AddressExtensions
{

    public static string? GetBaseAddressBech32(this CoreAddress self) => self.Value.GetAddress();
    /// <summary>
    /// Creates a Shelley-era Bech32 address (base or enterprise) from the PlutusAddress:
    /// - If stake credential exists, produce base address (nibbles 0..3).
    /// - If no stake, produce enterprise address (nibbles 6 or 7).
    /// - Returns null if no payment credential or unhandled credential type.
    /// </summary>
    public static string? GetBaseAddressBech32(this PlutusAddress? self, NetworkType networkType)
    {
        if (self == null)
            return null;

        // Step 1: extract the raw payment/stake bytes from the PlutusAddress
        var (paymentBytes, stakeBytes) = ExtractPlutusCredentials(self);
        if (paymentBytes == null)
        {
            // No payment => can't form enterprise or base address
            return null;
        }

        // Step 2: decide if enterprise or base
        bool hasStake = stakeBytes != null;

        // Step 3: decide nibble for "payment key or script?" and "stake key or script?"
        bool paymentIsScript = self.PaymentCredential is Script;
        bool stakeIsScript = false;

        // If StakeCredential is Some<Inline<Credential>>, check its inner value 
        if (self.StakeCredential is Some<Inline<Credential>> someInline)
        {
            // Now see if that credential is a Script
            if (someInline.Value.Value is Script)
            {
                stakeIsScript = true;
            }
        }

        byte addrTypeNibble;
        if (!hasStake)
        {
            // enterprise: nibble => 6 = key, 7 = script
            addrTypeNibble = paymentIsScript ? (byte)0x07 : (byte)0x06;
        }
        else
        {
            // base address: nibble => 
            // 0 = key-key, 1 = script-key, 2 = key-script, 3 = script-script
            if (!paymentIsScript && !stakeIsScript) addrTypeNibble = 0x00; // base key-key
            else if (paymentIsScript && !stakeIsScript) addrTypeNibble = 0x01; // base script-key
            else if (!paymentIsScript && stakeIsScript) addrTypeNibble = 0x02; // base key-script
            else /* paymentIsScript && stakeIsScript */ addrTypeNibble = 0x03; // base script-script
        }

        // Step 4: network nibble
        // Typically: 1 = mainnet, 0 = testnet
        byte networkNibble = networkType switch
        {
            NetworkType.Mainnet => 0x01,
            NetworkType.Testnet => 0x00,
            _ => 0x00 // fallback
        };

        // Compose the header
        byte header = (byte)((addrTypeNibble << 4) | networkNibble);

        // Step 5: build final address bytes
        byte[] addressBytes;
        if (!hasStake)
        {
            // enterprise => [header, payment]
            addressBytes = new byte[1 + paymentBytes.Length];
            addressBytes[0] = header;
            Buffer.BlockCopy(paymentBytes, 0, addressBytes, 1, paymentBytes.Length);
        }
        else
        {
            // base => [header, payment, stake]
            addressBytes = new byte[1 + paymentBytes.Length + (stakeBytes?.Length ?? 0)];
            addressBytes[0] = header;
            Buffer.BlockCopy(paymentBytes, 0, addressBytes, 1, paymentBytes.Length);
            Buffer.BlockCopy(stakeBytes ?? [], 0, addressBytes, 1 + paymentBytes.Length, stakeBytes?.Length ?? 0);
        }

        // Step 6: get prefix: "addr" or "addr_test" 
        //   (reward would use "stake"/"stake_test", but we aren't doing reward addresses here)
        string prefix = (networkType == NetworkType.Mainnet) ? "addr" : "addr_test";

        // Step 7: encode as Bech32. Reuse your existing method or a library
        return Bech32Codec.Encode(addressBytes, prefix);
    }

    /// <summary>
    /// Extracts the raw byte[] of the payment credential and the stake credential
    /// (if any) from the PlutusAddress.  Returns (null,null) if payment is not recognized.
    /// </summary>
    private static (byte[]? Payment, byte[]? Stake) ExtractPlutusCredentials(PlutusAddress address)
    {
        // Payment
        byte[]? payment = address.PaymentCredential switch
        {
            VerificationKey vk
                => vk.VerificationKeyHash?.Value,
            Script scr
                => scr.ScriptHash?.Value,
            _ => null
        };

        // Stake
        // It's Option<Inline<Credential>> so we do a pattern matching:
        byte[]? stake = address.StakeCredential switch
        {
            // If Some(...), we check if it's a key or script
            Option<Inline<Credential>> oic =>
                oic switch
                {
                    Some<Inline<Credential>> sic => sic.Value.Value switch
                    {
                        VerificationKey vk => vk.VerificationKeyHash.Value,
                        Script sk => sk.ScriptHash.Value,
                        _ => null
                    },
                    _ => null
                },
            _ => null // None or unknown
        };

        return (payment, stake);
    }

    /// <summary>
    /// Returns a human-readable address string if <paramref name="addressBytes"/>
    /// represents a valid Shelley-era address (Bech32) or Byron bootstrap address (Base58).
    /// Otherwise returns null.
    /// </summary>
    public static string? GetAddress(this byte[] addressBytes)
    {
        if (addressBytes == null || addressBytes.Length < 1)
            return null;

        byte header = addressBytes[0];
        // high nibble => address type
        byte addrTypeNibble = (byte)((header & 0xF0) >> 4);
        // low nibble  => network ID or Byron indicator
        byte networkNibble = (byte)(header & 0x0F);

        // Byron detection: CIP-19 states 8..11 nibble => Byron (0x8..0xB).
        if (addrTypeNibble >= 8 && addrTypeNibble <= 11)
        {
            return null;
        }
        else
        {
            // Attempt Shelley (Bech32) route
            // Convert network nibble => (Mainnet, Testnet, or others)
            NetworkType networkType;
            try
            {
                networkType = ParseNetwork(networkNibble);
            }
            catch
            {
                return null; // Unknown network
            }

            // Convert address-type nibble => (Base, Pointer, Enterprise, Reward) + script flags
            AddressType shelleyType = TranslateAddressType(addrTypeNibble,
                out _, out _);

            // Build the prefix: "addr", "stake" plus "_test" if testnet
            string prefix = Bech32Prefix(shelleyType, networkType);

            // Quick check: If you want to do additional length validation, parse credentials, etc.
            // For an example, we just encode the raw bytes as Bech32 with that prefix.
            try
            {
                return Bech32Codec.Encode(addressBytes, prefix);
            }
            catch
            {
                return null;
            }
        }
    }

    // ---------------------------------------------------------------------------------
    // "Shelley" Helpers
    // ---------------------------------------------------------------------------------

    private static AddressType TranslateAddressType(
        byte addrTypeNibble,
        out bool isScriptPayment,
        out bool isScriptStake)
    {
        isScriptPayment = false;
        isScriptStake = false;

        // CIP-19 table for Shelley addresses (0..7, 14..15)
        //  0 => base key-key
        //  1 => base script-key
        //  2 => base key-script
        //  3 => base script-script
        //  4 => pointer key
        //  5 => pointer script
        //  6 => enterprise key
        //  7 => enterprise script
        // 14 => stake (reward) key
        // 15 => stake (reward) script
        // 8..11 => Byron (handled above), 12..13 => reserved, etc.

        return addrTypeNibble switch
        {
            0 => AddressType.Base,
            1 => (isScriptPayment = true) switch { _ => AddressType.Base },
            2 => (isScriptStake = true) switch { _ => AddressType.Base },
            3 => (isScriptPayment = true) switch
            { _ => (isScriptStake = true) switch { _ => AddressType.Base } },

            4 => AddressType.Pointer,
            5 => (isScriptPayment = true) switch { _ => AddressType.Pointer },

            6 => AddressType.Enterprise,
            7 => (isScriptPayment = true) switch { _ => AddressType.Enterprise },

            14 => AddressType.Reward,
            15 => (isScriptPayment = true) switch { _ => AddressType.Reward },

            _ => throw new Exception("Unsupported or reserved nibble for Shelley")
        };
    }

    private static NetworkType ParseNetwork(byte networkNibble)
    {
        return networkNibble switch
        {
            0x00 => NetworkType.Testnet,
            0x01 => NetworkType.Mainnet,
            // For other networks (2..15?), handle them or throw
            _ => throw new Exception($"Unknown network nibble = 0x{networkNibble:X2}")
        };
    }

    private static string Bech32Prefix(AddressType addressType, NetworkType networkType)
    {
        // "stake" prefix if reward, else "addr"
        string prefixCore = (addressType == AddressType.Reward) ? "stake" : "addr";
        // Usually "_test" suffix if testnet, else no suffix
        string netSuffix = (networkType == NetworkType.Testnet) ? "_test" : "";

        return prefixCore + netSuffix;
    }

    /// <summary>
    /// Extract the 28-byte Payment Key Hash (PKH) from a Shelley key-based address
    /// (base/pointer/enterprise with a KEY credential in the *payment* field).
    /// Returns null if the payment credential is script-based, Byron, reward, or otherwise absent.
    /// </summary>
    public static byte[]? GetPkh(this byte[] addressBytes)
    {
        // Basic sanity check
        if (addressBytes == null || addressBytes.Length < 2)
            return null;

        // Nibbles
        byte header = addressBytes[0];
        byte addrTypeNibble = (byte)((header & 0xF0) >> 4);

        // Switch on CIP-19 nibble:
        //   0 => base key-key
        //   1 => base script-key        [payment=script => NO PKH]
        //   2 => base key-script        [payment=key => PKH, stake=script]
        //   3 => base script-script     [NO PKH]
        //   4 => pointer key           [payment=key => PKH]
        //   5 => pointer script        [NO PKH]
        //   6 => enterprise key        [payment=key => PKH]
        //   7 => enterprise script     [NO PKH]
        //   14 => reward key
        //   15 => reward script
        //   8..11 => Byron, etc.
        // If the payment credential is "key", we typically expect the 28-byte PKH
        // to start at offset=1. The total length can vary:
        //   - Base:  57 bytes (1 + 28 + 28)
        //   - Enterprise: 29 bytes (1 + 28)
        //   - Pointer: >= 29 (1 + 28 + pointer info)
        // We'll check minimal length below.

        switch (addrTypeNibble)
        {
            case 0: // base key-key
                {
                    if (addressBytes.Length < 1 + 28 + 28) return null;
                    // Payment key-hash is at offset=1, length=28
                    byte[] pkh = new byte[28];
                    Buffer.BlockCopy(addressBytes, 1, pkh, 0, 28);
                    return pkh;
                }
            case 1: // base script-key => payment=script => NO pkh
                return null;

            case 2: // base key-script => payment=key
                {
                    if (addressBytes.Length < 1 + 28 + 28) return null;
                    byte[] pkh = new byte[28];
                    Buffer.BlockCopy(addressBytes, 1, pkh, 0, 28);
                    return pkh;
                }
            case 3: // base script-script
                return null;

            case 4: // pointer key => payment=key
                {
                    // Must have at least 29 bytes: 1 + 28 + (some pointer)
                    // If you want to parse the pointer fully, you’d do that too. 
                    if (addressBytes.Length < 29) return null;
                    byte[] pkh = new byte[28];
                    Buffer.BlockCopy(addressBytes, 1, pkh, 0, 28);
                    return pkh;
                }
            case 5: // pointer script
                return null;

            case 6: // enterprise key => payment=key
                {
                    // Must have exactly 1+28=29 bytes for enterprise key
                    if (addressBytes.Length < 29) return null;
                    byte[] pkh = new byte[28];
                    Buffer.BlockCopy(addressBytes, 1, pkh, 0, 28);
                    return pkh;
                }
            case 7: // enterprise script
                return null;

            // Byron or reward -> no payment key to extract
            default:
                return null;
        }
    }

    /// <summary>
    /// Extract the 28-byte Stake Key Hash (SKH) from a *base address* that has a key-based stake credential.
    /// Returns null if not a base address, stake is script-based, pointer, enterprise, reward, Byron, etc.
    /// </summary>
    public static byte[]? GetSkh(this byte[] addressBytes)
    {
        // If it's a base address with a key stake part,
        // typically we have nibble = 0 => key-key, or nibble = 1 => script-key (stake=key).
        // nibble = 2 => key-script => stake=script => no SKH
        // nibble = 3 => script-script => no SKH
        // pointer/enterprise => no stake part to extract
        // reward => single credential, not a "stake" in the sense of base address
        // Byron => none
        // Also, the total length must be at least 57 bytes for base addresses:
        //   1 + 28 (payment) + 28 (stake) = 57.

        if (addressBytes == null || addressBytes.Length < 57)
            return null;

        byte header = addressBytes[0];
        byte addrTypeNibble = (byte)((header & 0xF0) >> 4);

        switch (addrTypeNibble)
        {
            case 0: // base key-key => stake=key => offset=29..28 bytes
                {
                    // offset = 1..28 is payment, offset = 29..28 is stake
                    byte[] skh = new byte[28];
                    Buffer.BlockCopy(addressBytes, 1 + 28, skh, 0, 28);
                    return skh;
                }
            case 1: // base script-key => stake=key => offset=29..28 bytes
                {
                    byte[] skh = new byte[28];
                    Buffer.BlockCopy(addressBytes, 1 + 28, skh, 0, 28);
                    return skh;
                }
            // If nibble=2 => base key-script => stake=script => no SKH
            // If nibble=3 => base script-script => stake=script => no SKH
            // 4..7 => pointer/enterprise => no stake
            // 8..11 => Byron => no stake
            // 14..15 => reward => single credential => not “stake” in base sense
            default:
                return null;
        }
    }
}
