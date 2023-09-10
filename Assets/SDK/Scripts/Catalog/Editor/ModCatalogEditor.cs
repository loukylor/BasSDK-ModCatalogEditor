using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace ThunderRoad
{
    public class CatalogEditor : EditorWindow, ISerializationCallbackReceiver
    {
        public static Dictionary<string, CatalogData> openFiles = new();
        // These 2 fields are only for serialization
        private List<string> openFilesKeys;
        [SerializeReference]
        private List<CatalogData> openFilesValues;

        private MethodInfo guiOutlineMethod;
        [SerializeField]
        private CatalogTreeViewState treeState;
        private static CatalogTreeView treeView;
        [SerializeField]
        private float treeWidth;
        private MethodInfo resizerMethod;

        [SerializeField]
        private Vector2 scrollPos;

        private SerializedObject obj;
        // Needed for the property drawers
        [SerializeField, SerializeReference]
        private CatalogData currentData;
        private SerializedProperty currentDataProp;
        private readonly Dictionary<Type, List<SerializedProperty>> props = new();

        [MenuItem("ThunderRoad (SDK)/Mod Catalog Editor")]
        private static void Open()
        {
            GetWindow<CatalogEditor>();
        }

        private void OnEnable()
        {
            // Look for all jsons
            if (openFiles.Count == 0)
            {
                List<string> paths = new();
                foreach (string file in Directory.GetFiles(Application.dataPath, "*.json", SearchOption.AllDirectories))
                    if (!openFiles.ContainsKey(file))
                        paths.Add(Path.GetRelativePath(Application.dataPath, file));
                AddPaths(paths, false);
            }

            // No clue why this is internal
            guiOutlineMethod ??= typeof(EditorGUI).GetMethod("DrawOutline", BindingFlags.NonPublic | BindingFlags.Static);

            treeState ??= new CatalogTreeViewState();
            treeView = new CatalogTreeView(openFiles, treeState);

            // I have no clue why this method is internal but it works so /shrug
            resizerMethod ??= typeof(EditorGUI).GetMethod("WidthResizer", BindingFlags.NonPublic | BindingFlags.Static, null, new Type[4] { typeof(Rect), typeof(float), typeof(float), typeof(float) }, null);
            treeWidth = EditorPrefs.GetFloat("ModCatalogEditor.treeWidth");

            if (obj == null)
            {
                obj = new SerializedObject(this);
                currentDataProp = obj.FindProperty("currentData");
            }
        }

        private static void AddPaths(List<string> jsonPaths, bool reload = true)
        {
            if (jsonPaths.Count == 0)
                return;

            List<string> jsons = new();
            foreach (string jsonPath in jsonPaths)
            {
                string absolutePath = Path.Combine(Application.dataPath, jsonPath);
                if (File.Exists(absolutePath))
                    jsons.Add(File.ReadAllText(absolutePath));
            }

            // Make sure serializer is able to deserialize properly
            Catalog.GetJsonNetSerializer();

            object[] deserializedObjects = Catalog.DeserializeJsons(jsons, jsonPaths);

            for (int i = 0; i < jsons.Count; i++)
            {
                if (deserializedObjects[i] is CatalogData data)
                {
                    openFiles[jsonPaths[i]] = data;
                    Catalog.LoadJson(deserializedObjects[i], jsons[i], jsonPaths[i], Path.GetFileName(Path.GetDirectoryName(jsonPaths[i])));
                }
            }

            if (reload)
                treeView.Reload();
        }

        private static void RemovePaths(List<string> jsonPaths, bool reload = true)
        {
            foreach (string path in jsonPaths)
                openFiles.Remove(path);

            if (reload)
                treeView.Reload();
        }

        private void OnGUI()
        {
            hasUnsavedChanges = treeView.GetUnsaved().Count > 0;
            
            CatalogTreeViewItem currentItem = null;
            IList<int> selected = treeView.GetSelection();
            if (selected.Count != 0)
                currentItem = treeView.items[selected[0]] as CatalogTreeViewItem;

            // Build toolbar
            Rect toolbarRect = new(0, 0, position.width, GUI.skin.button.CalcHeight(new GUIContent(""), position.width));
            using (new GUILayout.AreaScope(toolbarRect))
            {
                using (new EditorGUILayout.HorizontalScope(GUILayout.ExpandWidth(true)))
                {
                    using (new EditorGUI.DisabledGroupScope(currentItem == null))
                        if (GUILayout.Button(new GUIContent("Save", "Saves current file.")))
                            SaveItem(currentItem);

                    if (GUILayout.Button(new GUIContent("Save All", "Saves all unsaved files.")))
                        SaveChanges();

                    if (GUILayout.Button(new GUIContent("Reload Catalog", "Clears all catalog data then reloads everything."))
                        && (!hasUnsavedChanges || EditorUtility.DisplayDialog("Warning!", "You have unsaved data in this JSON file.", "Proceed", "Cancel")))
                    {
                        openFiles.Clear();
                        treeView.GetUnsaved().Clear();
                        OnEnable();
                        Catalog.EditorLoadAllJson(force: true);
                    }

                    GUIContent dropDownContent = new("New", "Create new catalog JSON.");
                    Rect dropDownRect = GUILayoutUtility.GetRect(dropDownContent, "MiniPullDown");
                    if (EditorGUI.DropdownButton(dropDownRect, dropDownContent, FocusType.Keyboard))
                    {
                        static void newAction(Type type)
                        {
                            string path = EditorUtility.SaveFilePanelInProject("New CatalogData", $"{Catalog.GetCategory(type)}_new", "json", "Enter file name for new CatalogData");
                            if (string.IsNullOrEmpty(path))
                                return;

                            CatalogData data = (CatalogData)Activator.CreateInstance(type);
                            // CatalogData init doesn't like null strings as the id
                            data.id = "";
                            data.version = data.GetCurrentVersion();
                            string jsonString = JsonConvert.SerializeObject(data, Catalog.GetJsonNetSerializerSettings());
                            File.WriteAllText(Path.Combine(Application.dataPath, "../", path), jsonString);
                            AssetDatabase.ImportAsset(path);
                        }
                        GenericMenu menu = new();
                        foreach (Type type in AppDomain.CurrentDomain.GetAssemblies().SelectMany(asm => asm.GetTypes()))
                        {
                            if (type.IsSubclassOf(typeof(CatalogData)))
                            {
                                // This temp var is needed so that the callbak has the right type passed in
                                Type temp = type;
                                menu.AddItem(new GUIContent(type.Name), false, () => newAction(temp));
                            }
                        }
                        menu.DropDown(dropDownRect);
                    }
                }
            }

            Rect mainRect = new(0, toolbarRect.height, position.width, position.height - toolbarRect.height);
            guiOutlineMethod.Invoke(null, new object[3] { mainRect, 2, new Color(0.12f, 0.12f, 0.12f) });
            mainRect = new Rect(mainRect.x + 2, mainRect.y + 2, mainRect.width - 4, mainRect.height - 4);

            // Draw tree and resize bar
            Rect resizeRect = new(treeWidth - 2.5f, toolbarRect.height, 5, position.height - toolbarRect.height);
            treeWidth = (float)resizerMethod.Invoke(null, new object[4] { resizeRect, treeWidth, 16, position.width });
            
            treeView.OnGUI(new Rect(mainRect) { width = treeWidth });
            EditorPrefs.SetFloat("ModCatalogEditor.treeWidth", treeWidth);

            if (currentItem == null)
                return;

            // Draw actual editor part
            float originalLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 200;
            
            using (new GUILayout.AreaScope(new Rect(mainRect) { x = mainRect.x + treeWidth, width = mainRect.width - treeWidth }))
            {
                using (EditorGUILayout.ScrollViewScope scrollView = new(scrollPos))
                {
                    scrollPos = scrollView.scrollPosition;

                    // Prevent loading mismatched data
                    if (currentItem.data.version != currentItem.data.GetCurrentVersion()
                        && !EditorUtility.DisplayDialog("Warning!", "Attempting to load a JSON with an incompatible version. It is recommended to back up before proceeding or data may be lost.", "Proceed", "Cancel"))
                    {
                        treeView.SetSelection(new List<int>());
                        return;
                    }
                    
                    currentData = currentItem.data;
                    obj.Update();

                    Type currentDataType = currentItem.data.GetType();
                    if (!props.ContainsKey(currentDataType))
                    {
                        List<SerializedProperty> dataProps = new();
                        props[currentDataType] = dataProps;
                        foreach (FieldInfo field in currentDataType.GetFields())
                        {
                            switch (field.Name)
                            {
                                case "hashId":
                                case "version":
                                    continue;
                            }

                            SerializedProperty prop = currentDataProp.FindPropertyRelative(field.Name);
                            if (prop != null)
                            {
                                dataProps.Add(prop);
                                DrawPropGUI(prop);
                            }
                        }
                    }
                    else
                        foreach (SerializedProperty prop in props[currentDataType])
                            DrawPropGUI(prop);

                    IList<int> unsaved = treeView.GetUnsaved();
                    if (obj.ApplyModifiedProperties() && !unsaved.Contains(currentItem.id))
                        unsaved.Add(currentItem.id);
                }
            }

            EditorGUIUtility.labelWidth = originalLabelWidth;
        }

        private void DrawPropGUI(SerializedProperty prop)
        {
            // If it's a list type that won't be drawn correctly, draw it ourselves
            if (prop.isArray && prop.propertyType == SerializedPropertyType.Generic)
            {
                // Make first letter uppercase
                GUIContent content = new(prop.name[0].ToString().ToUpper() + prop.name[1..]);
                Rect rect = EditorGUILayout.GetControlRect(true, EditorGUI.GetPropertyHeight(prop, content, true));

                SubclassListDrawer.GetDrawer(prop).Draw(rect, content);
            }
            else
                EditorGUILayout.PropertyField(prop);
        }

        public override void SaveChanges()
        {
            AssetDatabase.StartAssetEditing();
            try
            {
                IList<int> unsaved = treeView.GetUnsaved();
                foreach (int id in unsaved)
                    SaveItem(treeView.items[id] as CatalogTreeViewItem);
                unsaved.Clear();
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            hasUnsavedChanges = false;
        }

        public void SaveItem(CatalogTreeViewItem item)
        {
            string jsonString = JsonConvert.SerializeObject(item.data, Catalog.GetJsonNetSerializerSettings());
            File.WriteAllText(Path.Combine(Application.dataPath, item.path), jsonString);
            AssetDatabase.ImportAsset(Path.Combine("Assets", item.path));
            treeView.GetUnsaved().Remove(item.id);
        }

        public void OnBeforeSerialize()
        {
            openFilesKeys = new List<string>(openFiles.Count);
            openFilesValues = new List<CatalogData>(openFiles.Count);
            foreach (KeyValuePair<string, CatalogData> entry in openFiles)
            {
                openFilesKeys.Add(entry.Key);
                openFilesValues.Add(entry.Value);
            }
        }

        public void OnAfterDeserialize()
        {
            openFiles = new Dictionary<string, CatalogData>();
            for (int i = 0; i < openFilesKeys.Count; i++)
                openFiles[openFilesKeys[i]] = openFilesValues[i];
        }

        private class JsonImportNotifier : AssetPostprocessor
        {   
            private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] _, string[] __)
            {
                List<string> res = new();
                foreach (string path in importedAssets)
                    if (Path.GetExtension(path) == ".json")
                        res.Add(Path.GetRelativePath(Application.dataPath, path));
                AddPaths(res);

                res.Clear();
                foreach (string path in deletedAssets)
                    if (Path.GetExtension(path) == ".json")
                        res.Add(Path.GetRelativePath(Application.dataPath, path));
                RemovePaths(res);
            }
        }
    }
}