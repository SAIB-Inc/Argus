using System.Formats.Cbor;
using CborSerialization;

namespace Argus.Sync.Data.Models.Datums;

/*
With Stake Credentials
121_0([_
    121_0([_
        h'cb84310092f8c3dae1ebf0ac456114e487297d3fe684d3236588d5b3',
    ]),
    121_0([_
        121_0([_
            121_0([_
                h'cb84310092f8c3dae1ebf0ac456114e487297d3fe684d3236588d5b3',
            ]),
        ]),
    ]),
])

Without Stake Credentials
121_0([_
    121_0([_
        h'cb84310092f8c3dae1ebf0ac456114e487297d3fe684d3236588d5b3',
    ]),
    122_0([]),
])
*/
[CborSerialize(typeof(AddressCborConvert))]
public record Address(Credential Credential, StakeCredential? StakeCredential) : IDatum;

public class AddressCborConvert : ICborConvertor<Address>
{
    public Address Read(ref CborReader reader)
    {
        // Read the outermost tag and start array
        var outerTag = reader.ReadTag();
        if ((int)outerTag != 121)
        {
            throw new Exception("Invalid outer tag for Address");
        }
        reader.ReadStartArray();

        // Read payment credential
        var paymentCredential = new CredentialCborConvert().Read(ref reader);

        // Read the next tag to determine if it's a stake credential or an empty structure
        var innerTag = reader.ReadTag();
        StakeCredential? stakeCredential = null;
        if ((int)innerTag == 121) // With Stake Credentials
        {
            reader.ReadStartArray();
            stakeCredential = new StakeCredentialCborConvert().Read(ref reader);
            reader.ReadEndArray();
        }
        else if ((int)innerTag == 122) // Without Stake Credentials
        {
            reader.ReadStartArray();
            reader.ReadEndArray();
        }

        reader.ReadEndArray(); // End of Address

        return new Address(paymentCredential, stakeCredential);
    }

    public void Write(ref CborWriter writer, Address value)
    {
        writer.WriteTag((CborTag)121); // Outermost tag for Address
        writer.WriteStartArray(null);

        // Write payment credential
        new CredentialCborConvert().Write(ref writer, value.Credential);

        // Write stake credential or an empty array if null
        if (value.StakeCredential != null)
        {
            writer.WriteTag((CborTag)121); // Tag for StakeCredential
            writer.WriteStartArray(null); // StakeCredential array has one element
            new StakeCredentialCborConvert().Write(ref writer, value.StakeCredential);
            writer.WriteEndArray();
        }
        else
        {
            writer.WriteTag((CborTag)122); // Tag for empty array
            writer.WriteStartArray(0); // Empty array
            writer.WriteEndArray();
        }

        writer.WriteEndArray(); // End of Address
    }
}