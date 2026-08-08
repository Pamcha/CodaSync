using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Com.Pamcha.CodaSync {

    public enum OrphanCategory {
        /// <summary>Asset carrying a row id that no longer exists in Coda: the row was deleted.</summary>
        deletedRow,
        /// <summary>Asset with no row id at all: made by hand, or predating the row-id identity.</summary>
        unmanaged
    }

    public class OrphanEntry {
        public string tableName;
        public string assetName;
        public string assetPath;
        public OrphanCategory category;
        public bool selected;

        /// <summary>Paths of the assets that still point at this one. Filled by ScanReferences.</summary>
        public List<string> referencedBy = new List<string>();
        public bool IsReferenced => referencedBy.Count > 0;
    }

    /// <summary>
    /// Recomputes the orphan verdict outside of an import, from the row ids cached at the last sync,
    /// and finds which assets still reference an orphan before it is offered for deletion.
    ///
    /// The classification mirrors the one the import report does in InstanceGenerator.CreateInstances,
    /// with one difference: the import knows the live rows, this one only knows the cached ids. Keep
    /// the two in sync if the categories ever change.
    /// </summary>
    public static class OrphanScanner {

        /// <summary>
        /// Lists the orphaned and unmanaged assets of every cached table. Purely local: reads the
        /// instance folders and the row ids cached on the importer, never the network.
        /// </summary>
        public static List<OrphanEntry> Scan(TableImporter importer) {
            List<OrphanEntry> entries = new List<OrphanEntry>();
            if (importer == null) return entries;

            string instancesPath = importer.InstancesPath;
            Dictionary<string, Type> generatedTypes = GetGeneratedTypes(importer.CodeNamespaceSetting);

            foreach (TableImporter.SyncedTableRowIds table in importer.SyncedRowIds) {
                string folder = $"{instancesPath}/{table.generatedName}";
                if (!Directory.Exists(folder)) continue;

                // No generated class for this table (never imported since, or the namespace changed):
                // without its type we can't tell its assets apart from anything else in the folder.
                if (!generatedTypes.TryGetValue(table.generatedName, out Type objectType)) continue;

                FieldInfo rowIdField = objectType.GetField(InstanceGenerator.RowIdFieldName, BindingFlags.NonPublic | BindingFlags.Instance);
                HashSet<string> knownRowIds = new HashSet<string>(table.rowIds);

                // The folder is enumerated directly rather than through AssetDatabase.FindAssets("t:Name"):
                // that filter only takes a short type name and resolves to the wrong script when the
                // project holds another class of the same name, returning nothing (the 1.4.1 bug).
                string[] files = Directory.GetFiles(folder, "*.asset", SearchOption.AllDirectories);
                foreach (string file in files) {
                    // GetFiles joins with a backslash on Windows, AssetDatabase only accepts forward slashes
                    string assetPath = file.Replace('\\', '/');
                    ScriptableObject asset = AssetDatabase.LoadAssetAtPath(assetPath, objectType) as ScriptableObject;
                    // Filters out the _X_Database asset and any foreign asset sharing the folder
                    if (asset == null || asset.GetType() != objectType) continue;

                    string rowId = rowIdField?.GetValue(asset) as string;
                    if (!string.IsNullOrEmpty(rowId) && knownRowIds.Contains(rowId)) continue;

                    bool isOrphan = !string.IsNullOrEmpty(rowId);
                    entries.Add(new OrphanEntry {
                        tableName = table.tableName,
                        assetName = Path.GetFileNameWithoutExtension(assetPath),
                        assetPath = assetPath,
                        category = isOrphan ? OrphanCategory.deletedRow : OrphanCategory.unmanaged,
                        // Orphans start ticked, and are unticked by ScanReferences if something
                        // still points at them. Unmanaged assets are never deletable from here.
                        selected = isOrphan
                    });
                }
            }

            return entries;
        }

        /// <summary>
        /// Fills referencedBy for every deleted-row entry: which assets in the project still point at
        /// it. Generated assets aren't meant to be referenced by hand, but if one is, deleting it
        /// would break a scene or a prefab. Walks the whole project (O(number of assets)), so this
        /// only ever runs on an explicit click, never during an import.
        /// Returns false when the user cancelled, leaving the results incomplete.
        /// </summary>
        public static bool ScanReferences(List<OrphanEntry> entries) {
            Dictionary<string, OrphanEntry> candidates = new Dictionary<string, OrphanEntry>();

            foreach (OrphanEntry entry in entries) {
                entry.referencedBy.Clear();
                // Unmanaged assets are never offered for deletion, so there's nothing to protect there
                if (entry.category == OrphanCategory.deletedRow)
                    candidates[entry.assetPath] = entry;
            }

            if (candidates.Count == 0) return true;

            string[] allPaths = AssetDatabase.GetAllAssetPaths();

            try {
                for (int i = 0; i < allPaths.Length; i++) {
                    // Refreshing the bar on every asset costs more than the scan itself on a big project
                    if (i % 50 == 0 && EditorUtility.DisplayCancelableProgressBar(
                            "Coda Sync", $"Scanning references ({i}/{allPaths.Length})...", (float)i / allPaths.Length))
                        return false;

                    // A candidate referencing another candidate must not protect it: they are all up
                    // for deletion together, so references coming from the batch itself are ignored.
                    // Without this, two orphans pointing at each other would both look unsafe forever.
                    if (candidates.ContainsKey(allPaths[i])) continue;

                    string[] dependencies = AssetDatabase.GetDependencies(allPaths[i], false);
                    for (int d = 0; d < dependencies.Length; d++) {
                        if (candidates.TryGetValue(dependencies[d], out OrphanEntry referenced))
                            referenced.referencedBy.Add(allPaths[i]);
                    }
                }
            } finally {
                EditorUtility.ClearProgressBar();
            }

            // A referenced orphan is still an orphan (its row is gone for good), it is just no longer
            // safe to delete blindly: it stays listed, unticked, with whatever points at it.
            foreach (OrphanEntry entry in entries) {
                if (entry.category == OrphanCategory.deletedRow)
                    entry.selected = !entry.IsReferenced;
            }

            return true;
        }

        /// <summary>
        /// Generated classes indexed by short name. Matching on the namespace set on the importer
        /// avoids picking up an unrelated class that happens to share a table's name.
        /// </summary>
        private static Dictionary<string, Type> GetGeneratedTypes(string codeNamespace) {
            Dictionary<string, Type> types = new Dictionary<string, Type>();

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) {
                Type[] assemblyTypes;
                try {
                    assemblyTypes = assembly.GetTypes();
                } catch (ReflectionTypeLoadException) {
                    // An assembly with unresolvable references can't hold our generated classes anyway
                    continue;
                }

                foreach (Type type in assemblyTypes) {
                    if (type.Namespace == codeNamespace)
                        types[type.Name] = type;
                }
            }

            return types;
        }
    }
}
