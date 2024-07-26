

using System.Formats.Cbor;
using Cardano.Sync.Data.Models.Datums;
using CborSerialization;

namespace Cardano.Sync.Data.Models;

public static class CborConverter
{
    public static byte[] Serialize<T>(T cborObject, CborConformanceMode cborConformanceMode = CborConformanceMode.Lax, bool convertIndefiniteLengthEncodings = false, bool allowMultipleRootLevelValues = false)
    {
        var convertor = GetConvertor(typeof(T));
        CborWriter writer = new(cborConformanceMode, convertIndefiniteLengthEncodings, allowMultipleRootLevelValues);
        ((ICborConvertor<T>)convertor).Write(ref writer, cborObject);
        return writer.Encode();
    }

    public static T Deserialize<T>(byte[] cborData, CborConformanceMode cborConformanceMode = CborConformanceMode.Lax, bool allowMultipleRootLevelValues = false)
    {
        var convertor = GetConvertor(typeof(T));
        CborReader reader = new(cborData, cborConformanceMode, allowMultipleRootLevelValues);
        return ((ICborConvertor<T>)convertor).Read(ref reader);
    }

    public static object GetConvertor(Type type)
    {
        if (type.IsGenericType)
        {
            var genericTypeDef = type.GetGenericTypeDefinition();
            if (genericTypeDef.GetCustomAttributes(inherit: true).FirstOrDefault(a => a.GetType() == typeof(CborSerialize)) is CborSerialize attribute)
            {
                var genericArgs = type.GetGenericArguments(); // Handles any number of generic arguments
                var converterType = attribute.ConvertorType.MakeGenericType(genericArgs); // Applies those arguments to the converter type
                return Activator.CreateInstance(converterType)!;
            }
        }

        // Fallback for non-generic types or types without the CborSerialize attribute
        if (type.GetCustomAttributes(inherit: true).Where(a => a.GetType() == typeof(CborSerialize)).FirstOrDefault() is CborSerialize nonGenericAttribute)
        {
            return Activator.CreateInstance(nonGenericAttribute.ConvertorType)!;
        }

        throw new CborException($"Type {type.Name} does not have a CborSerialize attribute.");
    }
}
