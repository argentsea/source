using Orleans.Storage;

namespace ArgentSea.Orleans;

/// <summary>
/// ArgentSea grain storage maps grain state to and from relational columns and never serializes it,
/// but Orleans requires storage options (<see cref="IStorageProviderSerializerOptions"/>) to carry an <see cref="IGrainStorageSerializer"/>.
/// This no-op serializer is the default for <see cref="OrleansDbPersistenceOptions"/> and <see cref="OrleansShardPersistenceOptions"/>,
/// so the options do not construct an unused JSON serializer.
/// </summary>
public class FakeGrainStorageSerializer : IGrainStorageSerializer
{
    public T Deserialize<T>(BinaryData input) => default!;
    public BinaryData Serialize<T>(T? input) => BinaryData.Empty;
}
