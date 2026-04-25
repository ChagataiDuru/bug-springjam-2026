#if UNITY_EDITOR
using System;
using Newtonsoft.Json;

namespace OrangeWolf.DataEntity.Editor.Editor
{
    [Serializable]
    public sealed class EntityTsvData //excel table structure
    {
        [JsonProperty("Id")] public string Id { get; set; }
        [JsonProperty("EntityType")] public string EntityType { get; private set; }
        [JsonProperty("Name")] public string Name { get; private set; }
        [JsonProperty("Details")] public string Details { get; private set; }
    }
}
#endif
