using CborSerialization;
using Cardano.Sync.Data.Models.Datums;

namespace Cardano.Sync.Tests;

public class CborTests
{
    [Fact]
    public void SignatureCborTest()
    {
        var signature = CborConverter.Deserialize<Signature>(Convert.FromHexString("d8799f581c0c61f135f652bc17994a5411d0a256de478ea24dbc19759d2ba14f03ff"));
        var signatureCborHex = Convert.ToHexString(CborConverter.Serialize(signature)).ToLowerInvariant();
        Assert.Equal("d8799f581c0c61f135f652bc17994a5411d0a256de478ea24dbc19759d2ba14f03ff", signatureCborHex);
    }

    [Fact]
    public void RationalCborTest()
    {
        var rational = CborConverter.Deserialize<Rational>(Convert.FromHexString("d8799f051864ff"));
        var rationalCborHex = Convert.ToHexString(CborConverter.Serialize(rational)).ToLowerInvariant();
        Assert.Equal("d8799f051864ff", rationalCborHex);
    }

    [Fact]
    public void RewardSettingCborTest()
    {
        var rewardSetting = CborConverter.Deserialize<RewardSetting>(Convert.FromHexString("d8799f1864d8799f051864ffff"));
        var rewardSettingCborHex = Convert.ToHexString(CborConverter.Serialize(rewardSetting)).ToLowerInvariant();
        Assert.Equal("d8799f1864d8799f051864ffff", rewardSettingCborHex);
    }

    [Fact]
    public void RationalMathTest()
    {
        var a = new Rational(1, 2);
        var b = new Rational(1, 2);
        var c = a + b;
        var d = a * b;

        Assert.Equal(2ul, c.Numerator);
        Assert.Equal(2ul, c.Denominator);
        Assert.Equal(1ul, d.Numerator);
        Assert.Equal(4ul, d.Denominator);
    }

    [Fact]
    public void CredentialCborTest()
    {
        var credential = CborConverter.Deserialize<Credential>(Convert.FromHexString("d8799f581ccb84310092f8c3dae1ebf0ac456114e487297d3fe684d3236588d5b3ff"));
        var credentialCborHex = Convert.ToHexString(CborConverter.Serialize(credential)).ToLowerInvariant();
        Assert.Equal("d8799f581ccb84310092f8c3dae1ebf0ac456114e487297d3fe684d3236588d5b3ff", credentialCborHex);
    }

    [Fact]
    public void StakeCredentialCborTest()
    {
        var stakeCredential = CborConverter.Deserialize<StakeCredential>(Convert.FromHexString("d8799fd8799f581ccb84310092f8c3dae1ebf0ac456114e487297d3fe684d3236588d5b3ffff"));
        var stakeCredentialCborHex = Convert.ToHexString(CborConverter.Serialize(stakeCredential)).ToLowerInvariant();
        Assert.Equal("d8799fd8799f581ccb84310092f8c3dae1ebf0ac456114e487297d3fe684d3236588d5b3ffff", stakeCredentialCborHex);
    }

    [Fact]
    public void AddressWithStakeCredentialCborTest()
    {
        var address = CborConverter.Deserialize<Address>(Convert.FromHexString("d8799fd8799f581ccb84310092f8c3dae1ebf0ac456114e487297d3fe684d3236588d5b3ffd8799fd8799fd8799f581ccb84310092f8c3dae1ebf0ac456114e487297d3fe684d3236588d5b3ffffffff"));
        var addressCborHex = Convert.ToHexString(CborConverter.Serialize(address)).ToLowerInvariant();
        Assert.Equal("d8799fd8799f581ccb84310092f8c3dae1ebf0ac456114e487297d3fe684d3236588d5b3ffd8799fd8799fd8799f581ccb84310092f8c3dae1ebf0ac456114e487297d3fe684d3236588d5b3ffffffff", addressCborHex);
    }

    [Fact]
    public void AddressWithoutStakeCredentialCborTest()
    {
        var address = CborConverter.Deserialize<Address>(Convert.FromHexString("d8799fd8799f581ccb84310092f8c3dae1ebf0ac456114e487297d3fe684d3236588d5b3ffd87a80ff"));
        var addressCborHex = Convert.ToHexString(CborConverter.Serialize(address)).ToLowerInvariant();
        Assert.Equal("d8799fd8799f581ccb84310092f8c3dae1ebf0ac456114e487297d3fe684d3236588d5b3ffd87a80ff", addressCborHex);
    }

    [Fact]
    public void OutputDatumCborTest()
    {
        var inlineDatum = CborConverter.Deserialize<InlineDatum<Credential>>(
            Convert.FromHexString("d87b9fd8799f581ccb84310092f8c3dae1ebf0ac456114e487297d3fe684d3236588d5b3ffff")
        );

        var inlineDatumCborHex = Convert.ToHexString(CborConverter.Serialize(inlineDatum)).ToLowerInvariant();
        Assert.Equal("d87b9fd8799f581ccb84310092f8c3dae1ebf0ac456114e487297d3fe684d3236588d5b3ffff", inlineDatumCborHex);

        var inlineDatumWithAddress = CborConverter.Deserialize<InlineDatum<Address>>(
            Convert.FromHexString("d87b9fd8799fd8799f581ccb84310092f8c3dae1ebf0ac456114e487297d3fe684d3236588d5b3ffd8799fd8799fd8799f581ccb84310092f8c3dae1ebf0ac456114e487297d3fe684d3236588d5b3ffffffffff")
        );

        var inlineDatumWithAddressCborHex = Convert.ToHexString(CborConverter.Serialize(inlineDatumWithAddress)).ToLowerInvariant();
        Assert.Equal("d87b9fd8799fd8799f581ccb84310092f8c3dae1ebf0ac456114e487297d3fe684d3236588d5b3ffd8799fd8799fd8799f581ccb84310092f8c3dae1ebf0ac456114e487297d3fe684d3236588d5b3ffffffffff", inlineDatumWithAddressCborHex);
    }

    [Fact]
    public void DestinationCborTest()
    {
        var destination = CborConverter.Deserialize<Destination<InlineDatum<Credential>>>(
            Convert.FromHexString(
                "d8799fd8799fd8799f581ccb84310092f8c3dae1ebf0ac456114e487297d3fe684d3236588d5b3ffd8799fd8799fd8799f581ccb84310092f8c3dae1ebf0ac456114e487297d3fe684d3236588d5b3ffffffffd87b9fd8799f581ccb84310092f8c3dae1ebf0ac456114e487297d3fe684d3236588d5b3ffffff"
            )
        );

        var destinationCborHex = Convert.ToHexString(CborConverter.Serialize(destination)).ToLowerInvariant();
        Assert.Equal("d8799fd8799fd8799f581ccb84310092f8c3dae1ebf0ac456114e487297d3fe684d3236588d5b3ffd8799fd8799fd8799f581ccb84310092f8c3dae1ebf0ac456114e487297d3fe684d3236588d5b3ffffffffd87b9fd8799f581ccb84310092f8c3dae1ebf0ac456114e487297d3fe684d3236588d5b3ffffff", destinationCborHex);
    }


    [Fact]
    public void CIP68MetdataCborTest()
    {
        var timelockMetadata = CborConverter.Deserialize<CIP68Metdata>(
            Convert.FromHexString(
                "a24d6c6f636b65645f616d6f756e744431303030446e616d65581a5374616b65204e465420314b20434e4354202d20323430313233"
            )
        );

        var timelockMetadataCborHex = Convert.ToHexString(CborConverter.Serialize(timelockMetadata)).ToLowerInvariant();
        Assert.Equal("a24d6c6f636b65645f616d6f756e744431303030446e616d65581a5374616b65204e465420314b20434e4354202d20323430313233", timelockMetadataCborHex);
    }

    // [Fact]
    // public void CIP68TimelockCborTest()
    // {
    //     var timelock = CborConverter.Deserialize<CIP68<Timelock>>(
    //         Convert.FromHexString(
    //             "d8799fa24d6c6f636b65645f616d6f756e744431303030446e616d65581a5374616b65204e465420314b20434e4354202d2032343031323301d8799f1903e858206c00ac8ecdbfad86c9287b2aec257f2e3875b572de8d8df27fd94dd650671c94ffff"
    //         )
    //     );

    //     var timelockCborHex = Convert.ToHexString(CborConverter.Serialize(timelock)).ToLowerInvariant();
    //     Assert.Equal("d8799fa24d6c6f636b65645f616d6f756e744431303030446e616d65581a5374616b65204e465420314b20434e4354202d2032343031323301d8799f1903e858206c00ac8ecdbfad86c9287b2aec257f2e3875b572de8d8df27fd94dd650671c94ffff", timelockCborHex);
    // }

    [Fact]
    public void AssetClassCborTest()
    {
        var assetClass = CborConverter.Deserialize<AssetClass>(
            Convert.FromHexString(
                "d8799f581c1f164eea5c242f53cb2df2150fa5ab7ba126350e904ddbcc65226e1857634e4554415f4144415f504f4f4c5f4944454e54495459ff"
            )
        );

        var assetClassCborHex = Convert.ToHexString(CborConverter.Serialize(assetClass)).ToLowerInvariant();
        Assert.Equal("d8799f581c1f164eea5c242f53cb2df2150fa5ab7ba126350e904ddbcc65226e1857634e4554415f4144415f504f4f4c5f4944454e54495459ff", assetClassCborHex);
    }

    [Fact]
    public void SpectrumLiquidityPoolCborTest()
    {
        var spectrumLiquidityPool = CborConverter.Deserialize<SpectrumLiquidityPool>(
            Convert.FromHexString(
                "d8799fd8799f581c1f164eea5c242f53cb2df2150fa5ab7ba126350e904ddbcc65226e1857634e4554415f4144415f504f4f4c5f4944454e54495459ffd8799f4040ffd8799f581cb34b3ea80060ace9427bda98690a73d33840e27aaa8d6edb7f0c757a45634e455441ffd8799f581c1f164eea5c242f53cb2df2150fa5ab7ba126350e904ddbcc65226e184c4144415f634e4554415f4c50ff1903e59f581cd26de68106ec5905de8d9771ae3ee6c3236caee843bfc156af35a343ff00ff"
            )
        );

        var spectrumLiquidityPoolCborHex = Convert.ToHexString(CborConverter.Serialize(spectrumLiquidityPool)).ToLowerInvariant();

        Assert.Equal(
            "d8799fd8799f581c1f164eea5c242f53cb2df2150fa5ab7ba126350e904ddbcc65226e1857634e4554415f4144415f504f4f4c5f4944454e54495459ffd8799f4040ffd8799f581cb34b3ea80060ace9427bda98690a73d33840e27aaa8d6edb7f0c757a45634e455441ffd8799f581c1f164eea5c242f53cb2df2150fa5ab7ba126350e904ddbcc65226e184c4144415f634e4554415f4c50ff1903e59f581cd26de68106ec5905de8d9771ae3ee6c3236caee843bfc156af35a343ff00ff",
            spectrumLiquidityPoolCborHex
        );
    }

    [Fact]
    public void OutputReferenceCborTest()
    {
        var outputReference = CborConverter.Deserialize<OutputReference>(
            Convert.FromHexString(
                "d8799fd8799f5820cfa1c305723466348efa5dd76e77dba8687b0bf3427f1b1371425dc39775cf27ff00ff"
            )
        );

        var outputReferenceCborHex = Convert.ToHexString(CborConverter.Serialize(outputReference)).ToLowerInvariant();
        Assert.Equal("d8799fd8799f5820cfa1c305723466348efa5dd76e77dba8687b0bf3427f1b1371425dc39775cf27ff00ff", outputReferenceCborHex);
    }

    [Fact]
    public void CIP68MetataLongValuesCborTest()
    {
        var cip68Metadata = new CIP68Metdata(
            new Dictionary<string, string>
            {
                { "locked_assets", "[(8b05e87a51c1d4a0fa888d2bb14dbc25e8c343ea379a171b63aa84a0,434e4354,1050)]" },
            }
        );

        var cip68MetadataCborHex = Convert.ToHexString(CborConverter.Serialize(cip68Metadata)).ToLowerInvariant();
    }
}