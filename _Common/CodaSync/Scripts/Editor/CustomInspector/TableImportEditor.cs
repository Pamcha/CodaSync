using System;
using UnityEditor;
using UnityEngine;
using static Com.Pamcha.CodaSync.ImporterExporter;

namespace Com.Pamcha.CodaSync {
    [CustomEditor(typeof(TableImporter))]
    public class TableImportEditor : Editor {

        TableImporter script;

        private void Awake() {
            script = (TableImporter)target;
        }

        public override void OnInspectorGUI() {
            base.OnInspectorGUI();

            SerializedProperty date = serializedObject.FindProperty("lastSyncDateString");

            script.lastSyncLocalDateString = $"{DateTime.Parse(date.stringValue).ToLocalTime():R}";

            EditorGUILayout.Space(30);


            if (GUILayout.Button("Update Tables list"))
                script.GetTableList(script.OnUpdateTableList);

            if (script.CanDisplayTableSelection && script.tableSelection.Count > 0)
                DrawTableSelection();
        }

        private void DrawTableSelection() {
            GUIStyle listStyle = new GUIStyle();
            GUIStyle headerStyle = new GUIStyle();
            int padding = 5;
            listStyle.margin = new RectOffset(padding, padding, 0, 0);
            headerStyle.margin = new RectOffset(2, 2, 2, 2);

            EditorGUILayout.Space(20);
            Rect group = EditorGUILayout.BeginVertical();
            Rect groupBorder = new Rect(group);
            groupBorder.width += 2;
            groupBorder.height += 2;
            groupBorder.x -= 1;
            groupBorder.y -= 1;
            EditorGUI.DrawRect(groupBorder, new Color(.5f, .5f, .5f));
            EditorGUI.DrawRect(group, new Color(.3f, .3f, .3f));

            Rect headerRect = EditorGUILayout.BeginHorizontal(headerStyle);
            EditorGUI.DrawRect(headerRect, new Color(.18f, .18f, .18f));
            EditorGUILayout.LabelField("Available Tables", EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();

            for (int i = 0; i < script.tableSelection.Count; i++) {
                TableSelection selection = script.tableSelection[i];
                if (ImporterExporter.TypeTables.Contains(selection.tableDescription.name))
                    continue;

                EditorGUILayout.BeginHorizontal(listStyle);
                selection.selected = EditorGUILayout.Toggle(selection.tableDescription.name, selection.selected);
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Select All"))
                SetTableSelectionState(true);
            if (GUILayout.Button("Deselect All"))
                SetTableSelectionState(false);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            if (GUILayout.Button("Import selected Tables")) {
                script.CreateScriptFiles();
            }
        }

        private void SetTableSelectionState(bool state) {
            for (int i = 0; i < script.tableSelection.Count; i++) {
                script.tableSelection[i].selected = state;
            }
        }
    }
}