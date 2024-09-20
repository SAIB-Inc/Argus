using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Formats.Cbor;
using Argus.Sync.Data.Models;
using Argus.Sync.Data.Models.Datums;
using CborSerialization;

namespace Crashr.Data.Models.Datums;

[CborSerialize(typeof(DictionaryCborConvert<>))]
public class Dictionary<V> : IDatum, IDictionary<ByteArray, V> where V : IDatum
{
    private readonly List<byte[]> _keys = [];
    private readonly List<byte[]> _values = [];

    public V this[ByteArray key]
    {
        get
        {
            byte[] keyBytes = KeyToBytes(key);

            int index = _keys.IndexOf(keyBytes);

            if (index == -1)
            {
                throw new KeyNotFoundException();
            }

            byte[] valueBytes = _values[index];
            return BytesToValue(valueBytes);
        }

        set
        {
            byte[] keyBytes = KeyToBytes(key);
            byte[] valueBytes = ValueToBytes(value);

            int index = _keys.IndexOf(keyBytes);

            if (index == -1)
            {
                Add(key, value);
            }
            else
            {
                _values[index] = valueBytes;
            }
        }
    }

    public ICollection<ByteArray> Keys => _keys.Select(BytesToKey).ToList();

    public ICollection<V> Values => _values.Select(BytesToValue).ToList();

    public int Count => Keys.Count;
    public bool IsReadOnly => false;

    public void Add(ByteArray key, V value)
    {
        if (ContainsKey(key))
        {
            throw new ArgumentException("An item with the same key has already been added.");
        }

        _keys.Add(KeyToBytes(key));
        _values.Add(ValueToBytes(value));
    }

    public void Add(KeyValuePair<ByteArray, V> item)
    {
        byte[] keyBytes = KeyToBytes(item.Key);
        byte[] valueBytes = ValueToBytes(item.Value);

        _keys.Add(keyBytes);
        _values.Add(valueBytes);
    }

    public void Clear()
    {
        _keys.Clear();
        _values.Clear();
    }

    public bool Contains(KeyValuePair<ByteArray, V> item)
    {
        byte[] keyBytes = KeyToBytes(item.Key);
        byte[] valueBytes = ValueToBytes(item.Value);
        int index = _keys.Select((k, i) => (k, i)).FirstOrDefault(t => t.k.SequenceEqual(keyBytes)).i;

        if (index == -1)
        {
            return false;
        }

        return _values[index].SequenceEqual(valueBytes);
    }

    public bool ContainsKey(ByteArray key)
    {
        byte[] keyBytes = KeyToBytes(key);
        return _keys.Any(k => k.SequenceEqual(keyBytes));
    }

    public void CopyTo(KeyValuePair<ByteArray, V>[] array, int arrayIndex)
    {
        throw new NotImplementedException();
    }

    public IEnumerator<KeyValuePair<ByteArray, V>> GetEnumerator()
    {
        for (int i = 0; i < _keys.Count; i++)
        {
            ByteArray key = BytesToKey(_keys[i]);
            V value = BytesToValue(_values[i]);
            yield return new KeyValuePair<ByteArray, V>(key, value);
        }
    }

    public bool Remove(ByteArray key)
    {
        byte[] keyBytes = KeyToBytes(key);

        int index = _keys.Select((k, i) => (k, i)).FirstOrDefault(t => t.k.SequenceEqual(keyBytes)).i;

        if (index == -1)
        {
            return false;
        }

        _keys.RemoveAt(index);
        _values.RemoveAt(index);

        return true;
    }

    public bool Remove(KeyValuePair<ByteArray, V> item)
    {
        byte[] keyBytes = KeyToBytes(item.Key);
        byte[] valueBytes = ValueToBytes(item.Value);

        int index = _keys.Select((k, i) => (k, i)).FirstOrDefault(t => t.k.SequenceEqual(keyBytes)).i;

        if (index == -1)
        {
            return false;
        }

        if (!_values[index].SequenceEqual(valueBytes))
        {
            return false;
        }

        _keys.RemoveAt(index);
        _values.RemoveAt(index);

        return true;
    }

    public bool TryGetValue(ByteArray key, [MaybeNullWhen(false)] out V value)
    {
        byte[] keyBytes = KeyToBytes(key);

        int index = _keys.Select((k, i) => (k, i)).FirstOrDefault(t => t.k.SequenceEqual(keyBytes)).i;

        if (index == -1)
        {
            value = default;
            return false;
        }

        byte[] valueBytes = _values[index];
        value = BytesToValue(valueBytes);
        return true;
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    private static byte[] KeyToBytes(ByteArray key)
    {
        ICborConvertor<ByteArray> converter = (ICborConvertor<ByteArray>)CborConverter.GetConvertor(typeof(ByteArray));
        CborWriter writer = new();
        converter.Write(ref writer, key);
        return writer.Encode();
    }

    private static ByteArray BytesToKey(byte[] bytes)
    {
        ICborConvertor<ByteArray> converter = (ICborConvertor<ByteArray>)CborConverter.GetConvertor(typeof(ByteArray));
        CborReader reader = new(bytes);
        return converter.Read(ref reader);
    }

    private static byte[] ValueToBytes(V value)
    {
        ICborConvertor<V> converter = (ICborConvertor<V>)CborConverter.GetConvertor(typeof(V));
        CborWriter writer = new();
        converter.Write(ref writer, value);
        return writer.Encode();
    }

    private static V BytesToValue(byte[] bytes)
    {
        ICborConvertor<V> converter = (ICborConvertor<V>)CborConverter.GetConvertor(typeof(V));
        CborReader reader = new(bytes);
        return converter.Read(ref reader);
    }
}

public class DictionaryCborConvert<V> : ICborConvertor<Dictionary<V>> where V : IDatum
{
    public Dictionary<V> Read(ref CborReader reader)
    {
        Dictionary<V> dictionary = [];
        int? count = reader.ReadStartMap();
        for(int i = 0; i < count; i++)
        {
            ICborConvertor<ByteArray> keyConverter = (ICborConvertor<ByteArray>)CborConverter.GetConvertor(typeof(ByteArray));
            ByteArray key = keyConverter.Read(ref reader);

            ICborConvertor<V> valueConverter = (ICborConvertor<V>)CborConverter.GetConvertor(typeof(V));
            V value = valueConverter.Read(ref reader);

            dictionary.Add(key, value);
        }
        reader.ReadEndMap();
        return dictionary;
    }

    public void Write(ref CborWriter writer, Dictionary<V> value)
    {
        writer.WriteStartMap(value.Count);
        foreach (KeyValuePair<ByteArray, V> pair in value)
        {
            ICborConvertor<ByteArray> keyConverter = (ICborConvertor<ByteArray>)CborConverter.GetConvertor(typeof(ByteArray));
            keyConverter.Write(ref writer, pair.Key);

            ICborConvertor<V> valueConverter = (ICborConvertor<V>)CborConverter.GetConvertor(typeof(V));
            valueConverter.Write(ref writer, pair.Value);
        }
        writer.WriteEndMap();
    }
}
