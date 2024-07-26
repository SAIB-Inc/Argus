using System.Formats.Cbor;
using CborSerialization;

namespace Cardano.Sync.Data.Models.Datums;

[CborSerialize(typeof(TupleCborConverter<,>))]
public record Tuple<F, S>(F First, S Second) : IDatum where F : IDatum where S : IDatum;


public class TupleCborConverter<F, S> : ICborConvertor<Tuple<F, S>> where F : IDatum where S : IDatum
{
    public Tuple<F, S> Read(ref CborReader reader)
    {
        int? length = reader.ReadStartArray();
        if (length is not null)
        {
            throw new Exception("This is not a tuple");
        }

        ICborConvertor<F> fstConverter = (ICborConvertor<F>)CborConverter.GetConvertor(typeof(F));
        F fst = fstConverter.Read(ref reader);

        ICborConvertor<S> sndConverter = (ICborConvertor<S>)CborConverter.GetConvertor(typeof(S));
        S snd = sndConverter.Read(ref reader);

        return new Tuple<F, S>(fst, snd);
    }

    public void Write(ref CborWriter writer, Tuple<F, S> value)
    {
        writer.WriteStartArray(null);

        ICborConvertor<F> fstConverter = (ICborConvertor<F>)CborConverter.GetConvertor(typeof(F));
        fstConverter.Write(ref writer, value.First);

        ICborConvertor<S> sndConverter = (ICborConvertor<S>)CborConverter.GetConvertor(typeof(S));
        sndConverter.Write(ref writer, value.Second);

        writer.WriteEndArray();
    }
}


public class TupleCborConverter<F, S, T> : ICborConvertor<T> where F : IDatum where S : IDatum where T : Tuple<F, S>
{
    public T Read(ref CborReader reader)
    {
        var converter = new TupleCborConverter<F, S>().Read(ref reader);
        F fst = converter.First;
        S snd = converter.Second;
        return (T)new Tuple<F, S>(fst, snd);
    }

    public void Write(ref CborWriter writer, T value)
    {
        new TupleCborConverter<F, S>().Write(ref writer, value);
    }
}

