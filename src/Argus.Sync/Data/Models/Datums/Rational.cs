using System.Formats.Cbor;
using CborSerialization;

namespace Argus.Sync.Data.Models.Datums;

// 121_0([_ 5, 100_0])
[CborSerialize(typeof(RationalCborConvert))]
public record Rational(ulong Numerator, ulong Denominator) : IDatum
{
    public static Rational operator +(Rational a, Rational b)
    {
        if (a.Denominator == b.Denominator)
        {
            return new Rational(a.Numerator + b.Numerator, a.Denominator);
        }
        else
        {
            checked
            {
                ulong newNumerator = a.Numerator * b.Denominator + b.Numerator * a.Denominator;
                ulong newDenominator = a.Denominator * b.Denominator;
                return new Rational(newNumerator, newDenominator);
            }
        }
    }

    public static Rational operator *(Rational a, Rational b)
    {
        checked
        {
            return new Rational(a.Numerator * b.Numerator, a.Denominator * b.Denominator);
        }
    }

    public static ulong operator /(Rational a, Rational b)
    {
        if (b.Numerator == 0)
        {
            throw new DivideByZeroException("Denominator cannot be zero.");
        }
        return a.Numerator / b.Numerator;
    }

    public ulong Floor()
    {
        return this / new Rational(1, 1);
    }
}

public class RationalCborConvert : ICborConvertor<Rational>
{
    public Rational Read(ref CborReader reader)
    {
        var tag = reader.ReadTag();
        if ((int)tag != 121)
        {
            throw new Exception("Invalid tag");
        }
        reader.ReadStartArray();
        var numerator = reader.ReadUInt64();
        var denominator = reader.ReadUInt64();
        reader.ReadEndArray();
        return new Rational(numerator, denominator);
    }

    public void Write(ref CborWriter writer, Rational value)
    {
        writer.WriteTag((CborTag)121);
        writer.WriteStartArray(null);
        writer.WriteUInt64(value.Numerator);
        writer.WriteUInt64(value.Denominator);
        writer.WriteEndArray();
    }
}