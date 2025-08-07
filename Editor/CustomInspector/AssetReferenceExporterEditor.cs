using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Com.Pamcha.CodaSync {
    [CustomEditor(typeof(AssetReferenceExporter))]
    public class AssetReferenceExporterEditor : Editor {
        AssetReferenceExporter script;

        private void Awake() {
            script = (AssetReferenceExporter)target;
        }

        public override void OnInspectorGUI() {
            serializedObject.Update();

            Editor.DrawPropertiesExcluding(serializedObject, "assetFolder", "assets");


            EditorGUILayout.Space(30);

            if (GUILayout.Button("Update Asset List"))
                script.LoadAssets();

            SerializedProperty property = serializedObject.FindProperty("assetFolder");
            SerializedProperty assetList = serializedObject.FindProperty("assets");


            EditorGUILayout.Space(50);

            if (GUILayout.Button("+", GUILayout.Width(30), GUILayout.Height(30)))
                OpenFolderSelection();

            for (int i = property.arraySize-1; i >= 0; i--) {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("-", GUILayout.Width(20), GUILayout.Height(20))) {
                    RemoveFolder(i);
                    continue;
                }
                EditorGUILayout.LabelField(property.GetArrayElementAtIndex(i).stringValue);
                EditorGUILayout.EndHorizontal();
            }

            //EditorGUILayout.PropertyField(property, new GUIContent("Folders : "));
            EditorGUILayout.HelpBox($"{assetList.arraySize} assets found", MessageType.Info);

            EditorGUILayout.Space(20);

            if (GUILayout.Button("Export Assets References"))
                script.ExportReferences();

             if (GUILayout.Button("Check for Duplicate Asset Paths"))
                script.CheckForDuplicateAssetPaths(); 

            serializedObject.ApplyModifiedProperties();
        }


        private void OpenFolderSelection () {
            string folder = AssetDatabase.GetAssetPath(script);

            List<string> pathParts = folder.Split("/").ToList();
            pathParts.RemoveAt(pathParts.Count - 1);
            folder = string.Join('/', pathParts);

            string path = EditorUtility.OpenFolderPanel("Select Folder", folder, "Test");

            if (!string.IsNullOrEmpty(path))
                AddFolder(path);
        }

        private void AddFolder (string path) {
            string projectPath = Application.dataPath;

            if (path.Contains(projectPath)) {
                string[] paths = path.Split(projectPath);

                script.AddFolder($"Assets{paths[1]}");
            } else {
                Debug.LogError($"Selected folder can't be outside of current Unity project");
            }
        }

        private void RemoveFolder (int index) {
            SerializedProperty property = serializedObject.FindProperty("assetFolder");
            property.DeleteArrayElementAtIndex(index);
        }
    }

}