#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OrangeWolf.DataEntity.Data;
using OrangeWolf.DataEntity.Editor.Editor;
using OrangeWolf.Generic;
using UnityEditor;
using UnityEngine;

namespace OrangeWolf.DataEntity.Editor
{
    public sealed class EntityDataGenerator
    {
        private readonly string _dataFolderEntities;
        private readonly string _dataFolderEntityTypes;

        public EntityDataGenerator(string dataFolderEntities, string dataFolderEntityTypes)
        {
            _dataFolderEntities = dataFolderEntities;
            _dataFolderEntityTypes = dataFolderEntityTypes;
        }

        public void Generate(TextAsset entityDataTsv)
        {
            if (!entityDataTsv)
            {
                Debug.LogError($"Entity Data TSV is null");
                return;
            }
                
            SaveData(new TsvParser().Parse<EntityTsvData>(entityDataTsv.text));
        }
        
        #region Save

        private void SaveData(List<EntityTsvData> entityTsvData)
        {
            if (!Directory.Exists(_dataFolderEntities))
                Directory.CreateDirectory(_dataFolderEntities);
            
            if (!Directory.Exists(_dataFolderEntityTypes))
                Directory.CreateDirectory(_dataFolderEntityTypes);
            
            //Get and Save Entity Types
            var entityTypes = GetEntityTypes(entityTsvData);
            foreach (var entityType in entityTypes)
            {
                var path = $"{_dataFolderEntityTypes}/{entityType.Name}.asset";
                var entityTypeData = AssetDatabase.LoadAssetAtPath<EntityType>(path);
                
                if (!entityTypeData) //not found asset at path
                {
                    AssetDatabase.CreateAsset(entityType, path);
                }
                else
                {
                    entityTypeData.Set(entityType.Name);
                    EditorUtility.SetDirty(entityTypeData);
                }
            }
            
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var entityTypeDatas = new List<EntityType>();
            foreach (var entityType in entityTypes)
            {
                var path = $"{_dataFolderEntityTypes}/{entityType.Name}.asset";
                var entityTypeData = AssetDatabase.LoadAssetAtPath<EntityType>(path);
                entityTypeDatas.Add(entityTypeData);
            }

            //Create or Set Entity Data
            foreach (var entityTsvLine in entityTsvData)
            {
                var entityType = entityTypeDatas.FirstOrDefault(x => x.Name == entityTsvLine.EntityType);
                if (entityType == null)
                    throw new Exception($"Entity Type Not Found: {entityTsvLine.EntityType}");
                
                var path = $"{_dataFolderEntities}/{entityTsvLine.Id}.asset";
                var entityData = AssetDatabase.LoadAssetAtPath<EntityDataSo>(path);
                Sprite sprite = null;
                
                if (!entityData) //new asset create process
                {
                    entityData = ScriptableObject.CreateInstance<EntityDataSo>();
                    entityData.Set(entityTsvLine.Id, entityType, entityTsvLine.Name, entityTsvLine.Details, sprite);
                    AssetDatabase.CreateAsset(entityData, path);
                }
                else
                {
                    entityData.Set(entityTsvLine.Id, entityType, entityTsvLine.Name, entityTsvLine.Details, sprite);
                    EditorUtility.SetDirty(entityData);
                }
            }
            
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
        
        private EntityType[] GetEntityTypes(List<EntityTsvData> entityTsvData)
        {
            var entityTypes = new HashSet<EntityType>();
            foreach (var entityTsvLine in entityTsvData)
            {
                if (entityTypes.All(x => x.Name != entityTsvLine.EntityType))
                {
                    var entityType = ScriptableObject.CreateInstance<EntityType>();
                    entityType.Set(entityTsvLine.EntityType);
                    entityTypes.Add(entityType);
                }
            }

            return entityTypes.ToArray();
        }
        
        private Sprite FindSprite(string spriteName)
        {
            if (string.IsNullOrEmpty(spriteName))
                return null;
            
            var sprites = AssetDatabase.FindAssets("t:Sprite", new[] {"Assets/_Project/Sprites"});
            foreach (var sprite in sprites)
            {
                var path = AssetDatabase.GUIDToAssetPath(sprite);
                if (path.Contains(spriteName))
                {
                    return AssetDatabase.LoadAssetAtPath<Sprite>(path);
                }
            }

            return null;
        }

        #endregion
    }
}
#endif
