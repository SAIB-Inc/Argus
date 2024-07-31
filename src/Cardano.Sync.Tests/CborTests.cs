using Cardano.Sync.Data.Models.Datums;
using Cardano.Sync.Data.Models;
using Cardano.Sync.Data.Models.Experimental;
using System.Formats.Cbor;
using AssetClass = Cardano.Sync.Data.Models.Datums.SundaeSwap.AssetClass;
using Crashr.Data.Models.Datums;

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
        var assetClass = CborConverter.Deserialize<Data.Models.Datums.AssetClass>(
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
                { "name", "hello world" },
            }
        );

        var cip68 = new CIP68<NoDatum>(
            cip68Metadata,
            1,
            new NoDatum()
        );

        var cip68MetadataCborHex = Convert.ToHexString(CborConverter.Serialize(cip68Metadata)).ToLowerInvariant();
        var cip68CborHex = Convert.ToHexString(CborConverter.Serialize(cip68)).ToLowerInvariant();
    }

    [Fact]
    public void UtxosByAddressCborTest()
    {
        var utxosByAddress = CborConverter.Deserialize<UtxosByAddress>(
            Convert.FromHexString(
                "a7825820094d9608d93de0f889fbceef759f599285d479b00ab5a77974f6f27e760a61c80082583901e63022b0f461602484968bb10fd8f872787b862ace2d7e943292a37003ec6a12860ef8c07d4c1a8de7df06acb0f0330a6087ecbe972082a7821a002dc6c0a1581cc27600f3aff3d94043464a33786429b78e6ab9df5e1d23b774acb34ca144434e43541a00100590825820094d9608d93de0f889fbceef759f599285d479b00ab5a77974f6f27e760a61c80182583901e63022b0f461602484968bb10fd8f872787b862ace2d7e943292a37003ec6a12860ef8c07d4c1a8de7df06acb0f0330a6087ecbe972082a71a00103be482582018df76ceb9decacea4b4b35f78a15e478461cc7c7a3b2db6f36a724fc02d530d0082583901e63022b0f461602484968bb10fd8f872787b862ace2d7e943292a37003ec6a12860ef8c07d4c1a8de7df06acb0f0330a6087ecbe972082a7821a01292451a2581c0e378bd85b5c6283a2c214594468d40258b7351f17e76e9613d88f70a144434849501b0005af3107a40000581c30d2ebdb2fec06142ee84e5120c2717b4d68a91bffd924420d94ddeaa144434849501b0005af3107a400008258205aae02fa962693670af84b7ac9aa2929bdba6ae8196162deb015df782890d5000082583901e63022b0f461602484968bb10fd8f872787b862ace2d7e943292a37003ec6a12860ef8c07d4c1a8de7df06acb0f0330a6087ecbe972082a71a004c4b40825820d1dec89008e63c087ce46d481e721d7568c293592cad72e65b8027ba1ff4e0e80282583901e63022b0f461602484968bb10fd8f872787b862ace2d7e943292a37003ec6a12860ef8c07d4c1a8de7df06acb0f0330a6087ecbe972082a7821a0016e360a1581c61b3802ce748ed1fdaad2d6c744b19f104285f7d318172a5d4f06a4ea15820000de140bbc3181d1dde9108563ff2872fd4d2443c3e842879f38da34c6471b601825820e35737928075c3bc18dea861af9f6f44d30111206bb3bcfe5bd22ad380eb9cbb0182583901e63022b0f461602484968bb10fd8f872787b862ace2d7e943292a37003ec6a12860ef8c07d4c1a8de7df06acb0f0330a6087ecbe972082a7821a002ad032a6581c61b3802ce748ed1fdaad2d6c744b19f104285f7d318172a5d4f06a4ea35820000de1400c424d508e014278fa3091562fb7a75464a99fb368a41da083887b94015820000de140222b0ed9cc8232d5089310ad31decc8a18a4dc0ed455c65385c730bb015820000de1409ff47e7fc3c700c38327ec0d589ffeef131b508f159a0a97fc30cdae01581c6f37a98bd0c9ced4e302ec2fb3a2f19ffba1b5c0c2bedee3dac30e56a45148595045534b554c4c535f56545f505f45015148595045534b554c4c535f56545f565f43015248595045534b554c4c535f56545f4d5f4545015348595045534b554c4c535f56545f41435f454501581c9fea1584045acc5f989bd80bf1d380d2bbb1ca9f4ef1a1ef43a7a777a14953505f724d5951353702581cc27600f3aff3d94043464a33786429b78e6ab9df5e1d23b774acb34ca144434e43541a000b6dc8581ccecd82e1ee377c52d81d233d7e81d26d9f01c32c99f9f0ef6dc3dfbba14953505f69436c74593602581cf9c811825adb28f42d82391b900ca6962fa94a1d51739fbaa52f4b06a150434e43545f43455254494649434154451a000f423b825820e35737928075c3bc18dea861af9f6f44d30111206bb3bcfe5bd22ad380eb9cbb0282583901e63022b0f461602484968bb10fd8f872787b862ace2d7e943292a37003ec6a12860ef8c07d4c1a8de7df06acb0f0330a6087ecbe972082a71a00ecc075"
            )
        );

        var utxosByAddressCborHex = Convert.ToHexString(CborConverter.Serialize(utxosByAddress)).ToLowerInvariant();
        Assert.Equal("a7825820094d9608d93de0f889fbceef759f599285d479b00ab5a77974f6f27e760a61c80082583901e63022b0f461602484968bb10fd8f872787b862ace2d7e943292a37003ec6a12860ef8c07d4c1a8de7df06acb0f0330a6087ecbe972082a7821a002dc6c0a1581cc27600f3aff3d94043464a33786429b78e6ab9df5e1d23b774acb34ca144434e43541a00100590825820094d9608d93de0f889fbceef759f599285d479b00ab5a77974f6f27e760a61c80182583901e63022b0f461602484968bb10fd8f872787b862ace2d7e943292a37003ec6a12860ef8c07d4c1a8de7df06acb0f0330a6087ecbe972082a71a00103be482582018df76ceb9decacea4b4b35f78a15e478461cc7c7a3b2db6f36a724fc02d530d0082583901e63022b0f461602484968bb10fd8f872787b862ace2d7e943292a37003ec6a12860ef8c07d4c1a8de7df06acb0f0330a6087ecbe972082a7821a01292451a2581c0e378bd85b5c6283a2c214594468d40258b7351f17e76e9613d88f70a144434849501b0005af3107a40000581c30d2ebdb2fec06142ee84e5120c2717b4d68a91bffd924420d94ddeaa144434849501b0005af3107a400008258205aae02fa962693670af84b7ac9aa2929bdba6ae8196162deb015df782890d5000082583901e63022b0f461602484968bb10fd8f872787b862ace2d7e943292a37003ec6a12860ef8c07d4c1a8de7df06acb0f0330a6087ecbe972082a71a004c4b40825820d1dec89008e63c087ce46d481e721d7568c293592cad72e65b8027ba1ff4e0e80282583901e63022b0f461602484968bb10fd8f872787b862ace2d7e943292a37003ec6a12860ef8c07d4c1a8de7df06acb0f0330a6087ecbe972082a7821a0016e360a1581c61b3802ce748ed1fdaad2d6c744b19f104285f7d318172a5d4f06a4ea15820000de140bbc3181d1dde9108563ff2872fd4d2443c3e842879f38da34c6471b601825820e35737928075c3bc18dea861af9f6f44d30111206bb3bcfe5bd22ad380eb9cbb0182583901e63022b0f461602484968bb10fd8f872787b862ace2d7e943292a37003ec6a12860ef8c07d4c1a8de7df06acb0f0330a6087ecbe972082a7821a002ad032a6581c61b3802ce748ed1fdaad2d6c744b19f104285f7d318172a5d4f06a4ea35820000de1400c424d508e014278fa3091562fb7a75464a99fb368a41da083887b94015820000de140222b0ed9cc8232d5089310ad31decc8a18a4dc0ed455c65385c730bb015820000de1409ff47e7fc3c700c38327ec0d589ffeef131b508f159a0a97fc30cdae01581c6f37a98bd0c9ced4e302ec2fb3a2f19ffba1b5c0c2bedee3dac30e56a45148595045534b554c4c535f56545f505f45015148595045534b554c4c535f56545f565f43015248595045534b554c4c535f56545f4d5f4545015348595045534b554c4c535f56545f41435f454501581c9fea1584045acc5f989bd80bf1d380d2bbb1ca9f4ef1a1ef43a7a777a14953505f724d5951353702581cc27600f3aff3d94043464a33786429b78e6ab9df5e1d23b774acb34ca144434e43541a000b6dc8581ccecd82e1ee377c52d81d233d7e81d26d9f01c32c99f9f0ef6dc3dfbba14953505f69436c74593602581cf9c811825adb28f42d82391b900ca6962fa94a1d51739fbaa52f4b06a150434e43545f43455254494649434154451a000f423b825820e35737928075c3bc18dea861af9f6f44d30111206bb3bcfe5bd22ad380eb9cbb0282583901e63022b0f461602484968bb10fd8f872787b862ace2d7e943292a37003ec6a12860ef8c07d4c1a8de7df06acb0f0330a6087ecbe972082a71a00ecc075", utxosByAddressCborHex);
    }

    [Fact]
    public void ValueCborTest()
    {
        var value = CborConverter.Deserialize<Data.Models.Datums.Value>(
            Convert.FromHexString(
                "821a0385810db5581c09f2d4e4a5c3662f4c1e6a7d9600e9605279dbdcedb22d4507cb6e75a1435350461a0041f4ec581c0e378bd85b5c6283a2c214594468d40258b7351f17e76e9613d88f70a144434849501b0005af3107a40000581c2c569f54737d81fa620c310aba148b00eae6ba2812f9d741b40cca21a1515370665265776172645469636b6574323401581c51f9d9aea568a2e5a25078d4d6252d84c9d7314bc7f9326e5c8dde24a154544553545f504f4f4c5f325f4944454e5449545902581c54648775544512675775bb5132ce6ae25407f5bab7a8dbfe416dc088a14b4144415f544544595f4c501b7fffffffffffffff581c55bfab0754da6936ad8530838b8519291c417bf0e0618b2882b0a367a14b544553545f4144415f4c501b7fffffffffffffff581c5b26e685cc5c9ad630bde3e3cd48c694436671f3d25df53777ca60efa1434e564c1a034c0472581c6f37a98bd0c9ced4e302ec2fb3a2f19ffba1b5c0c2bedee3dac30e56a15148595045534b554c4c535f56545f505f4501581c89267e9a35153a419e1b8ffa23e511ac39ea4e3b00452e9d500f2982a153436176616c6965724b696e67436861726c65731a0104d4ee581c99c2531a16da521d4360f5aebec8123c241bdf3d78ff28c343d0c2a0a14b4144415f544544595f4c501b7fffffffffffffff581c9fea1584045acc5f989bd80bf1d380d2bbb1ca9f4ef1a1ef43a7a777a14953505f724d5951353702581cab182ed76b669b49ee54a37dee0d0064ad4208a859cc4fdf3f906d87a25254656464794265617273436c756232373035015254656464794265617273436c75623430343401581cc27600f3aff3d94043464a33786429b78e6ab9df5e1d23b774acb34ca144434e43541a00032ed8581ccc28bd6957e79e01549c904f8eba6adb08b0f4a83c6413f016607847a154544553545f504f4f4c5f315f4944454e5449545902581ccecd82e1ee377c52d81d233d7e81d26d9f01c32c99f9f0ef6dc3dfbba14953505f69436c74593602581cd26de68106ec5905de8d9771ae3ee6c3236caee843bfc156af35a343a24574544544591b00038d7cdda28b824c4144415f74544544595f4c5019262e581cda3562fad43b7759f679970fb4e0ec07ab5bebe5c703043acda07a3ca25254656464794265617273436c756231363230015254656464794265617273436c75623634383701581ce31b46cd3f495937d42c39385b2d425a19feaeff80554b0871ff73e3a15818434841524c595f4144415f504f4f4c5f4944454e5449545901581ce7ff92046394dc2d84bd8b7b07a5ce4534529a663dc0642325ae7d0da1574f5054494d5f4144415f504f4f4c5f4944454e5449545901581cfb998fe286ea3567e8b23d6bacbf6f56354ccdc2e0873fbdb788405ca14c695553445f534e454b5f4c501b7fffffffffffffff581cfed1c459a47cbff56bd7d29c2dde0de3e9bd15cee02b98622fce82f7a14b43617264616e6f476f6c641a00a6a47b"
            )
        );

        var valueCborHex = Convert.ToHexString(CborConverter.Serialize(value)).ToLowerInvariant();
        Assert.Equal("821a0385810db5581c09f2d4e4a5c3662f4c1e6a7d9600e9605279dbdcedb22d4507cb6e75a1435350461a0041f4ec581c0e378bd85b5c6283a2c214594468d40258b7351f17e76e9613d88f70a144434849501b0005af3107a40000581c2c569f54737d81fa620c310aba148b00eae6ba2812f9d741b40cca21a1515370665265776172645469636b6574323401581c51f9d9aea568a2e5a25078d4d6252d84c9d7314bc7f9326e5c8dde24a154544553545f504f4f4c5f325f4944454e5449545902581c54648775544512675775bb5132ce6ae25407f5bab7a8dbfe416dc088a14b4144415f544544595f4c501b7fffffffffffffff581c55bfab0754da6936ad8530838b8519291c417bf0e0618b2882b0a367a14b544553545f4144415f4c501b7fffffffffffffff581c5b26e685cc5c9ad630bde3e3cd48c694436671f3d25df53777ca60efa1434e564c1a034c0472581c6f37a98bd0c9ced4e302ec2fb3a2f19ffba1b5c0c2bedee3dac30e56a15148595045534b554c4c535f56545f505f4501581c89267e9a35153a419e1b8ffa23e511ac39ea4e3b00452e9d500f2982a153436176616c6965724b696e67436861726c65731a0104d4ee581c99c2531a16da521d4360f5aebec8123c241bdf3d78ff28c343d0c2a0a14b4144415f544544595f4c501b7fffffffffffffff581c9fea1584045acc5f989bd80bf1d380d2bbb1ca9f4ef1a1ef43a7a777a14953505f724d5951353702581cab182ed76b669b49ee54a37dee0d0064ad4208a859cc4fdf3f906d87a25254656464794265617273436c756232373035015254656464794265617273436c75623430343401581cc27600f3aff3d94043464a33786429b78e6ab9df5e1d23b774acb34ca144434e43541a00032ed8581ccc28bd6957e79e01549c904f8eba6adb08b0f4a83c6413f016607847a154544553545f504f4f4c5f315f4944454e5449545902581ccecd82e1ee377c52d81d233d7e81d26d9f01c32c99f9f0ef6dc3dfbba14953505f69436c74593602581cd26de68106ec5905de8d9771ae3ee6c3236caee843bfc156af35a343a24574544544591b00038d7cdda28b824c4144415f74544544595f4c5019262e581cda3562fad43b7759f679970fb4e0ec07ab5bebe5c703043acda07a3ca25254656464794265617273436c756231363230015254656464794265617273436c75623634383701581ce31b46cd3f495937d42c39385b2d425a19feaeff80554b0871ff73e3a15818434841524c595f4144415f504f4f4c5f4944454e5449545901581ce7ff92046394dc2d84bd8b7b07a5ce4534529a663dc0642325ae7d0da1574f5054494d5f4144415f504f4f4c5f4944454e5449545901581cfb998fe286ea3567e8b23d6bacbf6f56354ccdc2e0873fbdb788405ca14c695553445f534e454b5f4c501b7fffffffffffffff581cfed1c459a47cbff56bd7d29c2dde0de3e9bd15cee02b98622fce82f7a14b43617264616e6f476f6c641a00a6a47b", valueCborHex);
    }

    [Fact]
    public void LovelaceCborTest()
    {
        var lovelace = CborConverter.Deserialize<Lovelace>(
            Convert.FromHexString(
                "1b0000001288f7b3e3"
            )
        );

        var lovelaceCborHex = Convert.ToHexString(CborConverter.Serialize(lovelace)).ToLowerInvariant();
        Assert.Equal("1b0000001288f7b3e3", lovelaceCborHex);
    }

    [Theory]
    [InlineData(["9f41ff41ffff", typeof(Data.Models.Datums.Tuple<ByteArray, ByteArray>)])]
    [InlineData(["9f0505ff", typeof(Data.Models.Datums.Tuple<CardanoInt, CardanoInt>)])]
    [InlineData(["9f9f41ff41ffff9f41ff41ffffff", typeof(Data.Models.Datums.Tuple<AssetClass, AssetClass>)])]
    public void TupleCborTest(string cborHex, Type type)
    {
        var tuple = typeof(CborConverter)!
            .GetMethod("Deserialize")!
            .MakeGenericMethod(type)
            .Invoke(null, [Convert.FromHexString(cborHex), CborConformanceMode.Lax, false]);

        var tuplBytes = typeof(CborConverter)!
            .GetMethod("Serialize")!
            .MakeGenericMethod(type)
            .Invoke(null, [tuple, CborConformanceMode.Lax, false, false]) as byte[];

        var tupleHex = Convert.ToHexString(tuplBytes!).ToLowerInvariant();

        Assert.Equal(cborHex, tupleHex);
    }

    [Theory]
    [InlineData("9f41ff41ffff")]
    public void SundaeSwapAssetClassCborTest(string cborHex)
    {
        var sundaeAssetClass = CborConverter.Deserialize<Data.Models.Datums.SundaeSwap.AssetClass>(Convert.FromHexString(cborHex));
        var sundaeBytes = CborConverter.Serialize(sundaeAssetClass);
        var sundaeHex = Convert.ToHexString(sundaeBytes).ToLowerInvariant();
        Assert.Equal(cborHex, sundaeHex);
    }

    [Fact]
    public void DictionaryCborTest()
    {
        var dictionary = CborConverter.Deserialize<Dictionary<Dictionary<CardanoInt>>>(Convert.FromHexString("a141dda341aa0141bb0241cc03"));
        var dictionaryBytes = CborConverter.Serialize(dictionary);
        var dictionaryHex = Convert.ToHexString(dictionaryBytes).ToLowerInvariant();
        Assert.Equal("a141dda341aa0141bb0241cc03", dictionaryHex);
    }
}