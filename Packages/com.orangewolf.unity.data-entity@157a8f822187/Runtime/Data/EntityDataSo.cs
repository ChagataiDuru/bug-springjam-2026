using System;
using Newtonsoft.Json;
using UnityEngine;

namespace OrangeWolf.DataEntity.Data
{
    [Serializable]
    public class EntityDataSo : ScriptableObject
    {
        [field: SerializeField, JsonProperty("Id")] public string Id { get; private set; }
        [field: SerializeField, JsonProperty("EntityType")] public EntityType EntityType { get; private set; }
        [field: SerializeField, JsonProperty("Name")] public string Name { get; private set; }
        [field: SerializeField, JsonProperty("Details")] public string Details { get; private set; }
        [field: SerializeField, JsonIgnore()] public Sprite Sprite { get; private set; }

        public void Set(string id, EntityType entityType, string entityName, string details, Sprite sprite)
        {
            Id = id;
            EntityType = entityType;
            Name = entityName;
            Details = details;
            Sprite = sprite;
        }
    }
}