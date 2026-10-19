using System;

namespace ArgentSea.ShardKeys;




/// <summary>
/// A GhostShardKey is for situations where you have ShardKey’s serialzied data, but you do not know the original generics used when it was serialize, and therefore cannot create an instance to deserialize it.
/// </summary>
/// <remarks>
/// A key with an enum-typed part cannot be reconstructed this way. The serialized metadata records only that a part is an
/// enum of a given underlying width (int, byte, short or long), not which enum type, so <see cref="GetInstance"/> can only
/// select the underlying primitive; the typed key it then constructs rejects the stored enum code with
/// <see cref="InvalidShardKeyMetadataException"/>. Deserialize such keys with the original <c>ShardKey&lt;...&gt;</c> type instead.
/// </remarks>
public class GhostShardKey
{
    private ReadOnlyMemory<byte> _serialization;

    /// <summary>
    /// Delibertately lightweight constructor.
    /// Enables high-performance logging with very limited overhead if template is not invoked.
    /// </summary>
    /// <param name="serialization"></param>
    public GhostShardKey(ReadOnlyMemory<byte> serialization)
    {
        _serialization = serialization;
    }

    /// <summary>
    /// Create an instance of the ShareKey from the serialization or null.
    /// </summary>
    /// <returns>The ShardKey,ShardChild, ShardGrandChild, ShardGreatGrandChild, or null.</returns>
    /// <exception cref="InvalidShardKeyMetadataException">Thrown when a key part is an enum type, which the metadata cannot name (see the class remarks).</exception>
    public dynamic? GetInstance()
    {
        var span = _serialization.Span;
        var isUtf8 = ((span[0] & 128) != 128);
        if (isUtf8) // utf8 encoding chars do not use high bits, so it's safe to put.
        {
            span = StringExtensions.Decode(span).Span;
        }
        if (_serialization.Length < 5)
        {
            return null;
        }
        var typeSize = ((int)span[0]) & 3;
        uint intValues = 0;
        switch (typeSize)
        {
            case 1:
                intValues = BitConverter.ToUInt32(new byte[4] { (byte)0, (byte)0, span[1], (byte)0 });
                return MakeShardKeyInstance(intValues);
            case 2:
                intValues = BitConverter.ToUInt32(new byte[4] { (byte)0, span[2], span[1], (byte)0 });
                return MakeShardChildKeyInstance(intValues);
            case 3:
                intValues = BitConverter.ToUInt32(new byte[4] { span[3], span[2], span[1], (byte)0 });
                if ((intValues & 63) == 0)
                {
                    return MakeShardGrandChildKeyInstance(intValues);
                }
                else
                {
                    return MakeShardGreatGrandChildKeyInstance(intValues);

                }
            default:
                return null;
        }
    }
    #region Make Instances
    private static Type GetKnownArgType(KeyDataType dataType)
        => ShardKeySerialization.GetArgType(dataType) ?? throw new InvalidShardKeyMetadataException();

    private dynamic? MakeShardKeyInstance(uint typeInfo)
    {
        var generic = typeof(ShardKey<>);
        var shardDataType = (KeyDataType)((typeInfo >> 18) & 63);
        var types = new Type[1] { GetKnownArgType(shardDataType) };
        var shardDefinition = generic.MakeGenericType(types);
        return Activator.CreateInstance(shardDefinition, _serialization);
    }
    private dynamic? MakeShardChildKeyInstance(uint typeInfo)
    {
        var generic = typeof(ShardKey<,>);
        var shardDataType = (KeyDataType)((typeInfo >> 18) & 63);
        var shardChildDataType = (KeyDataType)((typeInfo >> 12) & 63);
        var types = new Type[2] { GetKnownArgType(shardDataType), GetKnownArgType(shardChildDataType) };
        var shardDefinition = generic.MakeGenericType(types);
        return Activator.CreateInstance(shardDefinition, _serialization);
    }
    private dynamic? MakeShardGrandChildKeyInstance(uint typeInfo)
    {
        var generic = typeof(ShardKey<,,>);
        var shardDataType = (KeyDataType)((typeInfo >> 18) & 63);
        var shardChildDataType = (KeyDataType)((typeInfo >> 12) & 63);
        var shardGrandChildDataType = (KeyDataType)((typeInfo >> 6) & 63);
        var types = new Type[3] { GetKnownArgType(shardDataType), GetKnownArgType(shardChildDataType), GetKnownArgType(shardGrandChildDataType) };
        var shardDefinition = generic.MakeGenericType(types);
        return Activator.CreateInstance(shardDefinition, _serialization);
    }

    private dynamic? MakeShardGreatGrandChildKeyInstance(uint typeInfo)
    {
        var generic = typeof(ShardKey<,,,>);
        var shardDataType = (KeyDataType)((typeInfo >> 18) & 63);
        var shardChildDataType = (KeyDataType)((typeInfo >> 12) & 63);
        var shardGrandChildDataType = (KeyDataType)((typeInfo >> 6) & 63);
        var shardGreatGrandChildDataType = (KeyDataType)(typeInfo & 63);
        var types = new Type[4] { GetKnownArgType(shardDataType), GetKnownArgType(shardChildDataType), GetKnownArgType(shardGrandChildDataType), GetKnownArgType(shardGreatGrandChildDataType) };
        var shardDefinition = generic.MakeGenericType(types);
        return Activator.CreateInstance(shardDefinition, _serialization);
    }

    #endregion
    public override string ToString()
    {
        var obj = GetInstance();
        if (obj is null)
        {
            return "{ Not a ShardKey }";
        }
        return obj.ToString();
    }

    public short GetShardId()
    {
        if (_serialization.Length < 5)
        {
            throw new InvalidShardKeyMetadataException();
        }
        var span = _serialization.Span;
        var isUtf8 = ((span[0] & 128) != 128);
        if (isUtf8)
        {
            span = StringExtensions.Decode(span).Span;
        }
        var orgnLen = span[0] & 3;
        var pos = orgnLen + 1;
        return BitConverter.ToInt16(span.Slice(pos));
    }
}
