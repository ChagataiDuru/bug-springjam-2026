using Newtonsoft.Json;
using UnityEngine;

namespace OrangeWolf.DataEntity.Data
{
    public class EntityType : ScriptableObject
    {
        [field: SerializeField, JsonProperty("EntityName")] public string Name { get; private set; }

        public void Set(string entityType)
        {
            Name = entityType;
        }
    }
}