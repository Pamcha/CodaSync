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

        private bool showHiddenTables = false;

        // Hiding or unhiding changes how many rows the list draws. The click is recorded here and
        // applied once the whole inspector has been laid out: mutating mid-draw would desync IMGUI's
        // control count between the layout and repaint passes of the same event.
        private TableSelection pendingHide;
        private string pendingUnhideId;
        private bool pendingHideAllViews;

        private void Awake() {
            script = (TableImporter)target;
        }

        private void OnEnable() {
            script = (TableImporter)target;
            // Refresh the table list when the importer is actually being inspected. This replaces
            // the old OnValidate-driven refresh, which also fired on events unrelated to the user
            // looking at the asset (script reload, AssetDatabase.Refresh, focus regain...).
            script.ScheduleTableListRefresh();
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

            // Filter out Type Tables and user-hidden tables, then sort alphabetically
            List<TableSelection> displayedTables = script.tableSelection
                .Where(s => !ImporterExporter.TypeTables.Contains(s.tableDescription.name)
                         && !script.IsTableIgnored(s.tableDescription.id))
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

            GUIContent hideContent = new GUIContent("Hide", "Hide this table from the list. It won't be selectable nor imported until you unhide it");

            for (int i = 0; i < displayedTables.Count; i++) {
                TableSelection selection = displayedTables[i];

                Rect rowRect = EditorGUILayout.BeginHorizontal(listStyle);
                EditorGUI.DrawRect(rowRect, i % 2 == 0 ? rowEven : rowOdd);
                selection.selected = EditorGUILayout.Toggle(selection.tableDescription.name, selection.selected);
                if (GUILayout.Button(hideContent, EditorStyles.miniButton, GUILayout.Width(45)))
                    pendingHide = selection;
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Select All"))
                SetTableSelectionState(true);
            if (GUILayout.Button("Deselect All"))
                SetTableSelectionState(false);
            GUIContent hideViewsContent = new GUIContent("Hide all views", "Hide every Coda view at once, leaving only real tables in the list");
            if (GUILayout.Button(hideViewsContent))
                pendingHideAllViews = true;
            EditorGUILayout.EndHorizontal();

            DrawHiddenTables(listStyle);

            EditorGUILayout.EndVertical();

            // Import + Validate Names buttons, side by side (2/3 + 1/3)
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

            // The sync only ever reports orphaned assets; deleting them is this explicit manual step.
            bool hasCachedRowIds = script.SyncedRowIds.Count > 0;
            using (new EditorGUI.DisabledScope(!hasCachedRowIds)) {
                string tooltip = hasCachedRowIds
                    ? "Review the generated assets whose Coda row was deleted, and delete the ones you choose"
                    : "Run an import first: this needs the row ids of the last sync";

                if (GUILayout.Button(new GUIContent(" Clean orphaned assets...", EditorGUIUtility.IconContent("TreeEditor.Trash").image, tooltip), GUILayout.Height(24)))
                    OrphanedAssetsWindow.Open(script);
            }

            ApplyPendingVisibilityChange();
        }

        /// <summary>
        /// Foldout listing the tables hidden from the import list, each with an unhide button.
        /// </summary>
        private void DrawHiddenTables(GUIStyle listStyle) {
            if (script.IgnoredTableIds.Count == 0) return;

            showHiddenTables = EditorGUILayout.Foldout(showHiddenTables, $"Hidden tables ({script.IgnoredTableIds.Count})", true);
            if (!showHiddenTables) return;

            for (int i = 0; i < script.IgnoredTableIds.Count; i++) {
                string hiddenId = script.IgnoredTableIds[i];

                // Iterating the stored ids rather than the table list keeps a table that was hidden
                // and later deleted in Coda visible here, so its dead id can still be cleared.
                TableSelection hiddenTable = script.tableSelection.Find(s => s.tableDescription.id == hiddenId);
                string label = hiddenTable != null
                    ? hiddenTable.tableDescription.name
                    : $"{hiddenId} (no longer in this document)";

                EditorGUILayout.BeginHorizontal(listStyle);
                EditorGUILayout.LabelField(label);
                if (GUILayout.Button("Unhide", EditorStyles.miniButton, GUILayout.Width(60)))
                    pendingUnhideId = hiddenId;
                EditorGUILayout.EndHorizontal();
            }
        }

        private void ApplyPendingVisibilityChange() {
            if (pendingHide == null && pendingUnhideId == null && !pendingHideAllViews) return;

            if (pendingHide != null) script.HideTable(pendingHide);
            if (pendingUnhideId != null) script.ShowTable(pendingUnhideId);
            if (pendingHideAllViews) HideAllViews();

            pendingHide = null;
            pendingUnhideId = null;
            pendingHideAllViews = false;
            Repaint();
        }

        private void HideAllViews() {
            int hiddenCount = 0;

            for (int i = 0; i < script.tableSelection.Count; i++) {
                TableSelection selection = script.tableSelection[i];
                if (selection.tableDescription.tableType != "view") continue;
                if (script.IsTableIgnored(selection.tableDescription.id)) continue;

                script.HideTable(selection);
                hiddenCount++;
            }

            if (hiddenCount > 0) return;

            // Either the doc really has no view left to hide, or its tables came back without a
            // tableType. Saying so beats a button that silently does nothing.
            EditorUtility.DisplayDialog("Hide all views",
                "No view to hide. Either this document has none, or the Coda API returned its tables without a table type.",
                "OK");
        }

        private void SetTableSelectionState(bool state) {
            for (int i = 0; i < script.tableSelection.Count; i++) {
                TableSelection selection = script.tableSelection[i];

                // Don't deselect Type Tables: they're hidden from the UI and must stay selected
                // so that asset references (Sprite, AudioClip, etc.) are always resolved during import.
                if (!state && ImporterExporter.TypeTables.Contains(selection.tableDescription.name))
                    continue;

                // Hidden tables aren't part of the displayed list, so "Select All" leaves them alone
                if (state && script.IsTableIgnored(selection.tableDescription.id))
                    continue;

                selection.selected = state;
            }
        }
    }
}
