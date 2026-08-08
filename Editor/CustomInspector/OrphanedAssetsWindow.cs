using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Com.Pamcha.CodaSync {
    /// <summary>
    /// Lets the user delete the assets whose Coda row was deleted, one by one. The sync itself never
    /// deletes anything: it only reports orphans, and this window is the explicit manual step.
    /// </summary>
    public class OrphanedAssetsWindow : EditorWindow {

        private const int CacheStaleAfterDays = 7;

        private TableImporter importer;
        private List<OrphanEntry> entries = new List<OrphanEntry>();
        private HashSet<OrphanEntry> expandedReferences = new HashSet<OrphanEntry>();
        private Vector2 scroll;
        private bool referencesScanned;

        public static void Open(TableImporter importer) {
            OrphanedAssetsWindow window = GetWindow<OrphanedAssetsWindow>(true, "Coda Sync - Orphaned assets");
            window.importer = importer;
            window.minSize = new Vector2(520, 360);
            window.Rescan(scanReferences: true);
            window.Show();
        }

        private void Rescan(bool scanReferences) {
            // Re-fetching from Coda is asynchronous: the window can be closed before the answer
            // comes back, and the callback would then run against a destroyed window.
            if (this == null || importer == null) return;

            entries = OrphanScanner.Scan(importer);
            expandedReferences.Clear();

            // Nothing to protect and nothing to walk the project for
            if (!scanReferences || entries.Count == 0) {
                referencesScanned = entries.Count == 0;
                Repaint();
                return;
            }

            referencesScanned = OrphanScanner.ScanReferences(entries);
            Repaint();
        }

        private void OnGUI() {
            if (importer == null) {
                EditorGUILayout.HelpBox("The Table Importer this window was opened from no longer exists.", MessageType.Warning);
                return;
            }

            DrawToolbar();
            DrawCacheState();

            // A partial reference scan can leave a referenced asset looking safe, which is the one
            // mistake that turns a cleanup into a broken scene
            if (!referencesScanned)
                EditorGUILayout.HelpBox("The reference scan was cancelled, so referenced assets may still show as safe to delete. Hit \"Rescan references\" before deleting.", MessageType.Warning);

            scroll = EditorGUILayout.BeginScrollView(scroll);

            List<OrphanEntry> safe = new List<OrphanEntry>();
            List<OrphanEntry> referenced = new List<OrphanEntry>();
            List<OrphanEntry> unmanaged = new List<OrphanEntry>();

            foreach (OrphanEntry entry in entries) {
                if (entry.category == OrphanCategory.unmanaged) unmanaged.Add(entry);
                else if (entry.IsReferenced) referenced.Add(entry);
                else safe.Add(entry);
            }

            if (entries.Count == 0)
                EditorGUILayout.HelpBox("No orphaned asset: every generated asset matches a row that still exists in Coda.", MessageType.Info);

            DrawCategory($"🗑 Safe to delete ({safe.Count})",
                "Their row was deleted in Coda and nothing in the project references them.", safe, selectable: true);

            DrawCategory($"⚠ Deleted but referenced ({referenced.Count})",
                "Their row was deleted in Coda, but something still points at them. Deleting one breaks whatever references it.", referenced, selectable: true);

            DrawCategory($"❓ Unmanaged ({unmanaged.Count})",
                "No Coda row id: made by hand, or generated before row-id identity existed. Never offered for deletion, clean them up yourself if needed.", unmanaged, selectable: false);

            EditorGUILayout.EndScrollView();

            DrawFooter();
        }

        private void DrawToolbar() {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button(new GUIContent("Rescan references", "Re-check which assets point at the orphans. Local, no network call"), EditorStyles.toolbarButton, GUILayout.Width(130)))
                Rescan(scanReferences: true);

            if (GUILayout.Button(new GUIContent("Re-fetch from Coda", "Ask Coda for the current row ids, to check this list against the live document"), EditorStyles.toolbarButton, GUILayout.Width(130)))
                importer.RefreshRowIdCache(() => Rescan(scanReferences: true));

            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(importer.name, EditorStyles.miniLabel, GUILayout.Width(150));
            EditorGUILayout.EndHorizontal();
        }

        private void DrawCacheState() {
            string cacheDate = importer.RowIdCacheDateString;

            if (string.IsNullOrEmpty(cacheDate)) {
                EditorGUILayout.HelpBox("No row id cached yet. Run an import (or hit \"Re-fetch from Coda\") so this window knows which rows still exist.", MessageType.Warning);
                return;
            }

            // Never let a date parse throw from OnGUI: the stored string is RFC1123 (English month
            // and day names), which some cultures refuse to parse.
            if (!DateTime.TryParse(cacheDate, out DateTime syncDate)) {
                EditorGUILayout.LabelField($"Row ids last fetched: {cacheDate}", EditorStyles.miniLabel);
                return;
            }

            EditorGUILayout.LabelField($"Row ids last fetched: {syncDate.ToLocalTime():R}", EditorStyles.miniLabel);

            // An old cache can show a row deleted long ago as still alive, or the other way around
            if ((DateTime.UtcNow - syncDate.ToUniversalTime()).TotalDays >= CacheStaleAfterDays)
                EditorGUILayout.HelpBox("These row ids are more than a week old. Hit \"Re-fetch from Coda\" before deleting anything.", MessageType.Warning);
        }

        private void DrawCategory(string title, string help, List<OrphanEntry> categoryEntries, bool selectable) {
            if (categoryEntries.Count == 0) return;

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(help, EditorStyles.wordWrappedMiniLabel);

            foreach (OrphanEntry entry in categoryEntries) {
                EditorGUILayout.BeginHorizontal();

                if (selectable)
                    entry.selected = EditorGUILayout.Toggle(entry.selected, GUILayout.Width(18));
                else
                    GUILayout.Space(22);

                EditorGUILayout.LabelField(new GUIContent($"{entry.assetName}", entry.assetPath), GUILayout.MinWidth(120));
                EditorGUILayout.LabelField(entry.tableName, EditorStyles.miniLabel, GUILayout.Width(120));

                if (GUILayout.Button(new GUIContent("Ping", "Select this asset in the Project window"), EditorStyles.miniButton, GUILayout.Width(40)))
                    EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(entry.assetPath));

                EditorGUILayout.EndHorizontal();

                if (entry.IsReferenced)
                    DrawReferences(entry);
            }
        }

        private void DrawReferences(OrphanEntry entry) {
            EditorGUI.indentLevel += 2;

            bool expanded = expandedReferences.Contains(entry);
            bool nowExpanded = EditorGUILayout.Foldout(expanded, $"Referenced by {entry.referencedBy.Count} asset(s)", true);

            if (nowExpanded && !expanded) expandedReferences.Add(entry);
            else if (!nowExpanded && expanded) expandedReferences.Remove(entry);

            if (nowExpanded) {
                foreach (string referencingPath in entry.referencedBy) {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(referencingPath, EditorStyles.miniLabel);
                    if (GUILayout.Button("Ping", EditorStyles.miniButton, GUILayout.Width(40)))
                        EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(referencingPath));
                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUI.indentLevel -= 2;
        }

        private void DrawFooter() {
            List<OrphanEntry> toDelete = GetSelected();

            EditorGUILayout.Space(5);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            using (new EditorGUI.DisabledScope(toDelete.Count == 0)) {
                Color previousBg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.9f, 0.45f, 0.45f);

                if (GUILayout.Button($"Delete selected ({toDelete.Count})", GUILayout.Height(28), GUILayout.Width(180)))
                    DeleteSelected(toDelete);

                GUI.backgroundColor = previousBg;
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(5);
        }

        private List<OrphanEntry> GetSelected() {
            List<OrphanEntry> selected = new List<OrphanEntry>();

            foreach (OrphanEntry entry in entries) {
                // Belt and braces: unmanaged entries have no checkbox, and must never be deletable
                if (entry.selected && entry.category == OrphanCategory.deletedRow)
                    selected.Add(entry);
            }

            return selected;
        }

        private void DeleteSelected(List<OrphanEntry> toDelete) {
            if (toDelete.Count == 0) return;

            // AssetDatabase.DeleteAsset can't be undone, so the confirmation names the exact files
            StringBuilder message = new StringBuilder();
            message.AppendLine($"Permanently delete {toDelete.Count} asset(s)? This can't be undone from the editor.");
            message.AppendLine();

            int previewCount = Mathf.Min(toDelete.Count, 15);
            for (int i = 0; i < previewCount; i++) {
                message.AppendLine(toDelete[i].assetPath);
            }
            if (toDelete.Count > previewCount)
                message.AppendLine($"... and {toDelete.Count - previewCount} more");

            int referencedCount = toDelete.FindAll(e => e.IsReferenced).Count;
            if (referencedCount > 0) {
                message.AppendLine();
                message.AppendLine($"{referencedCount} of them are still referenced elsewhere in the project. Deleting those will break the references.");
            }

            if (!EditorUtility.DisplayDialog("Delete orphaned assets", message.ToString(), "Delete", "Cancel"))
                return;

            int deleted = 0;
            foreach (OrphanEntry entry in toDelete) {
                if (AssetDatabase.DeleteAsset(entry.assetPath))
                    deleted++;
                else
                    Debug.LogWarning($"⚠️ <b>[CodaSync]</b> Could not delete \"{entry.assetPath}\".");
            }

            AssetDatabase.Refresh();
            Debug.Log($"<color=#7FD17F>🗑 <b>[CodaSync]</b> Deleted {deleted} orphaned asset(s).</color>");

            Rescan(scanReferences: true);
        }
    }
}
