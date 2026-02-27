using System;
using System.Collections.Generic;
using System.Linq;
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

            if (string.IsNullOrEmpty(date.stringValue))
                script.lastSyncLocalDateString = "Never";
            else
                script.lastSyncLocalDateString = $"{DateTime.Parse(date.stringValue).ToLocalTime():R}";

            EditorGUILayout.Space(30);


            GUIContent updateContent = new GUIContent(" Update Tables list", EditorGUIUtility.IconContent("Refresh").image);
            if (GUILayout.Button(updateContent))
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

            // Filter out Type Tables and sort alphabetically
            List<TableSelection> displayedTables = script.tableSelection
                .Where(s => !ImporterExporter.TypeTables.Contains(s.tableDescription.name))
                .OrderBy(s => s.tableDescription.name)
                .ToList();

            // Count selected tables
            int selectedCount = displayedTables.Count(s => s.selected);
            int totalCount = displayedTables.Count;

            EditorGUILayout.Space(20);
            Rect group = EditorGUILayout.BeginVertical();
            Rect groupBorder = new Rect(group);
            groupBorder.width += 2;
            groupBorder.height += 2;
            groupBorder.x -= 1;
            groupBorder.y -= 1;
            EditorGUI.DrawRect(groupBorder, new Color(.5f, .5f, .5f));
            EditorGUI.DrawRect(group, new Color(.3f, .3f, .3f));

            // Header with selection counter
            Rect headerRect = EditorGUILayout.BeginHorizontal(headerStyle);
            EditorGUI.DrawRect(headerRect, new Color(.18f, .18f, .18f));
            EditorGUILayout.LabelField($"Available Tables ({selectedCount}/{totalCount} selected)", EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();

            // Sorted table list with zebra striping
            Color rowEven = new Color(.3f, .3f, .3f);
            Color rowOdd = new Color(.26f, .26f, .26f);

            for (int i = 0; i < displayedTables.Count; i++) {
                TableSelection selection = displayedTables[i];

                Rect rowRect = EditorGUILayout.BeginHorizontal(listStyle);
                EditorGUI.DrawRect(rowRect, i % 2 == 0 ? rowEven : rowOdd);
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

            // Import + Validate Names buttons — side by side (2/3 + 1/3)
            EditorGUILayout.Space(15);

            Color previousBg = GUI.backgroundColor;

            GUIStyle importButtonStyle = new GUIStyle(GUI.skin.button) {
                fontStyle = FontStyle.Bold,
                fontSize = 13,
                fixedHeight = 35
            };

            EditorGUILayout.BeginHorizontal();

            // Import button (2/3 width)
            GUI.backgroundColor = new Color(0.4f, 0.8f, 0.4f);
            GUIContent importContent = new GUIContent(" Import selected Tables", EditorGUIUtility.IconContent("Download-Available").image);
            if (GUILayout.Button(importContent, importButtonStyle, GUILayout.ExpandWidth(true))) {
                script.CreateScriptFiles();
            }

            // Validate Names button (1/3 width)
            GUI.backgroundColor = previousBg;
            GUIContent validateNamesContent = new GUIContent(" Validate Names", EditorGUIUtility.IconContent("Search Icon").image,
                "Check table and column names for C# compatibility issues");
            if (GUILayout.Button(validateNamesContent, GUILayout.Width(EditorGUIUtility.currentViewWidth * 0.3f), GUILayout.Height(35))) {
                script.CheckNames();
            }

            EditorGUILayout.EndHorizontal();

            GUI.backgroundColor = previousBg;
        }

        private void SetTableSelectionState(bool state) {
            for (int i = 0; i < script.tableSelection.Count; i++) {
                script.tableSelection[i].selected = state;
            }
        }
    }
}
