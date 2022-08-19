using System.Collections.Generic;
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
            property.stringValue = EditorGUILayout.DelayedTextField(property.displayName, property.stringValue);

            EditorGUILayout.HelpBox($"{assetList.arraySize} assets found", MessageType.Info);

            EditorGUILayout.Space(20);

            if (GUILayout.Button("Export Assets References"))
                script.ExportReferences();

            serializedObject.ApplyModifiedProperties();
        }
    }

}