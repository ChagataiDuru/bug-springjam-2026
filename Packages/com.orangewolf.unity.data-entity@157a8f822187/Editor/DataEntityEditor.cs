using System.Collections.Generic;
using OrangeWolf.DataEntity.Data;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace OrangeWolf.DataEntity.Editor
{
    public class DataEntityEditor : EditorWindow
    {
        [SerializeField] private VisualTreeAsset _visualTreeAsset;
        
        private readonly string _dataFolderEntities = "Assets/_Project/Data/Entities/Data";
        private readonly string _dataFolderEntityTypes = "Assets/_Project/Data/Entities/Types";
        
        private ListView _entityList;
        private VisualElement _entityViewer;
        private Label _entityId;
        private Label _entityType;
        private Label _entityName;
        private Label _entityDetails;
        private VisualElement _entitySpriteViewer;
        
        //Generator
        private EntityDataGenerator _entityDataGenerator;
        private ObjectField _objectFieldTsv;
        private Button _btnGenerate;
        
        //Editor
        private List<EntityDataSo> _entityDataList;
        private EntityDataSo _selectedEntity;

        #region Init
        
        [MenuItem("OrangeWolf/Entity Data Editor")]
        public static void ShowWindow()
        {
            DataEntityEditor wnd = GetWindow<DataEntityEditor>();
            wnd.titleContent = new GUIContent("Data Entity Editor");
        }

        public void CreateGUI()
        {
            VisualElement root = rootVisualElement; // Each editor window contains a root VisualElement object. Unity

            // Instantiate UXML
            VisualElement editorUxml = _visualTreeAsset.Instantiate();
            root.Add(editorUxml);
            
            //Get References
            _entityList = root.Q<ListView>("EntityList");
            _entityViewer = root.Q<VisualElement>("EntityViewer");
            _entityViewer.dataSource = this;
            _entityId = root.Q<Label>("EntityId");
            _entityType = root.Q<Label>("EntityType");
            _entityName = root.Q<Label>("EntityName");
            _entityDetails = root.Q<Label>("EntityDetails");
            _entitySpriteViewer = root.Q<VisualElement>("EntitySpriteViewer");

            // Set up ListView
            _entityDataList = FindAllEntities();
            _entityList.itemsSource = _entityDataList;
            _entityList.selectionChanged += OnSelectEntity;
            
            //Generator
            _entityDataGenerator = new EntityDataGenerator(_dataFolderEntities, _dataFolderEntityTypes);
            _objectFieldTsv = root.Q<ObjectField>("TsvObjectField");
            _btnGenerate = root.Q<Button>("GenerateButton");
            _btnGenerate.clicked += OnGenerateButtonClicked;
        }

        private List<EntityDataSo> FindAllEntities()
        {
            List<EntityDataSo> entityList = new();
            var assetGuids = AssetDatabase.FindAssets("t:EntityDataSo", 
                new[] { _dataFolderEntities });
            
            foreach (var assetGuid in assetGuids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(assetGuid);
                var entityData = AssetDatabase.LoadAssetAtPath<EntityDataSo>(assetPath);
                entityList.Add(entityData);
            }
            
            return entityList;
        }
        
        #endregion

        private void OnSelectEntity(object item)
        {
            _selectedEntity = _entityDataList[_entityList.selectedIndex];
            _entityId.text = _selectedEntity.Id;
            _entityType.text = _selectedEntity.EntityType.Name;
            _entityName.text = _selectedEntity.Name;
            _entityDetails.text = _selectedEntity.Details;
            if (_selectedEntity.Sprite)
                _entitySpriteViewer.style.backgroundImage = _selectedEntity.Sprite.texture;
        }
        
        private void OnGenerateButtonClicked()
        {
            _entityDataGenerator.Generate(_objectFieldTsv.value as TextAsset);
            _entityDataList = FindAllEntities();
            _entityList.itemsSource = _entityDataList;
            //_entityList.RefreshItems();
        }
    }
}
