using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Unity.EditorCoroutines.Editor;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Networking;

namespace Com.Pamcha.CodaSync {
    public class InstanceGenerator {

        private static Dictionary<string, Type> allTypes;
        private static string basePath;
        private static Dictionary<Type, AssetRef[]> assetRefs = new Dictionary<Type, AssetRef[]>();
        private static ImportReport report;
        private static TableStructure[] allStructures;
        private static string currentTableName;
        private static string currentFieldName;
        private static string currentRowName;

        /// <summary>
        /// tablesToRewrite holds the sanitized names of the tables whose generated class no longer
        /// matches the schema their assets were written with (see TableImporter.DiffTableSchemas).
        /// Every asset of those tables is saved even when its row did not change, so the YAML on disk
        /// picks up the added keys and drops the removed ones.
        /// </summary>
        public static void CreateAllInstances(TableStructure[] structures, TableRow[][] tablesRows, string path, ImportReport importReport, HashSet<string> tablesToRewrite = null) {
            report = importReport;
            allStructures = structures;
            LoadAssetRefs(structures, tablesRows);

            Dictionary<TableStructure, dynamic[]> instances = new Dictionary<TableStructure, dynamic[]>();
            Dictionary<TableStructure, TableImportState> states = new Dictionary<TableStructure, TableImportState>();
            basePath = path;

            for (int i = 0; i < structures.Length; i++) {
                if (ImporterExporter.TypeTables.Contains(structures[i].UnmodifiedName))
                    continue;

                string instancePath = $"{path}/{structures[i].Name}";
                if (!Directory.Exists(instancePath))
                    Directory.CreateDirectory(instancePath);

                instances[structures[i]] = CreateInstances(structures[i], tablesRows[i], instancePath, out TableImportState state);
                state.forceRewrite = tablesToRewrite != null && tablesToRewrite.Contains(structures[i].Name);
                states[structures[i]] = state;
            }

            // Flush all newly created assets to disk before resolving lookups in pass 2.
            // No AssetDatabase.Refresh() needed here: assets created via CreateAsset are already
            // indexed in memory, so FindAssets can resolve them. Refresh() would only be needed
            // for files written directly to disk (e.g. File.WriteAllText), and is slow on large projects.
            AssetDatabase.SaveAssets();

            for (int i = 0; i < structures.Length; i++) {
                if (ImporterExporter.TypeTables.Contains(structures[i].UnmodifiedName))
                    continue;

                SetAllFields(structures[i], instances[structures[i]], tablesRows[i], states[structures[i]]);
            }
        }

        /// <summary>
        /// Per-table state carried from pass 1 (asset creation) to pass 2 (field assignment) so the
        /// import report can tell created / updated / unchanged apart. The real created-vs-updated-vs-
        /// unchanged verdict is only known after fields are written in pass 2, so we capture a "before"
        /// snapshot of each pre-existing asset here and diff it once the fields are set.
        /// </summary>
        private class TableImportState {
            public int created;
            public int skipped;
            public int renamed;
            // The table's generated class changed since its assets were last written, so all of them
            // are saved regardless of whether their row changed.
            public bool forceRewrite;
            // Aligned with the instances array. null = the asset was created this import (no diff needed);
            // non-null = EditorJsonUtility snapshot of the pre-existing asset before fields were written.
            public string[] beforeSnapshots;
        }

        #region Assets Loading
        private static void LoadAssetRefs(TableStructure[] structures, TableRow[][] tablesRows) {
            assetRefs.Clear();

            for (int i = 0; i < structures.Length; i++) {
                TableStructure structure = structures[i];

                if (ImporterExporter.TypeTables.Contains(structure.UnmodifiedName)) {
                    assetRefs[GetAssetType(structure.UnmodifiedName)] = GetAssetRefs(structure, tablesRows[i]);
                }
            }
        }

        private static AssetRef[] GetAssetRefs(TableStructure structure, TableRow[] rows) {
            AssetRef[] refs = new AssetRef[rows.Length];

            for (int i = 0; i < rows.Length; i++) {
                // Keep AssetId parsing for backward compatibility, but AssetPath is now the primary key
                string assetIdString = rows[i].Values[GetColumnIdByName(structure, "AssetId")];
                bool success = int.TryParse(assetIdString, out int assetId);

                if (!success) {
                    report.warnings.Add($"Asset \"{rows[i].Values[GetColumnIdByName(structure, "AssetName")]}\" can't be referenced (invalid AssetId). Check your AssetReferences in Coda.");
                    continue;
                }

                refs[i] = new AssetRef() {
                    AssetId = assetId,
                    AssetName = rows[i].Values[GetColumnIdByName(structure, "AssetName")],
                    AssetPath = rows[i].Values[GetColumnIdByName(structure, "AssetPath")]
                };
            }

            return refs;
        }
        #endregion

        #region Instance Creation
        // Name of the private serialized row-id field emitted on every generated class (see
        // CodeGenerator). The double underscore avoids collisions with sanitized column names,
        // which are only ever prefixed with a single one.
        public const string RowIdFieldName = "__codaRowId";

        private static dynamic[] CreateInstances(TableStructure structure, TableRow[] rows, string path, out TableImportState state) {
            ScriptableObject database = ScriptableObject.CreateInstance($"{structure.Name}_DB");
            AssetDatabase.CreateAsset(database, $"{path}/_{structure.Name}_Database.asset");

            allTypes = GetAllTypes();
            Type databaseType = allTypes[$"{TableImporter.CodeNamespace}.{structure.Name}_DB"];
            Type objectType = allTypes[$"{TableImporter.CodeNamespace}.{structure.Name}"];
            Type listType = typeof(List<>).MakeGenericType(objectType);
            dynamic instanceList = Activator.CreateInstance(listType);

            // Row-id based identity: match existing assets by their stable Coda row id (i-xxxx)
            // instead of their file name, so a row renamed in Coda renames the asset instead of
            // duplicating it. The field is private on the generated class, hence reflection.
            FieldInfo rowIdField = objectType.GetField(RowIdFieldName, BindingFlags.NonPublic | BindingFlags.Instance);

            // Index the table's existing assets: by row id, and by file name for those without an
            // id yet (pre-1.4 assets to migrate, or hand-made ones).
            //
            // The folder is enumerated directly instead of through AssetDatabase.FindAssets("t:Name"):
            // that filter only takes a short type name, so in a project that also holds another class
            // with the same name (say a "Prop" MonoBehaviour alongside the generated "Prop"
            // ScriptableObject) it resolves to the wrong script and returns nothing. Every existing
            // asset would then be missed, its row treated as new, and its re-creation refused by the
            // file-already-exists guard in pass B, leaving the table's database list empty.
            Dictionary<string, ScriptableObject> byId = new Dictionary<string, ScriptableObject>();
            Dictionary<string, ScriptableObject> byName = new Dictionary<string, ScriptableObject>();
            // Flat list of the same assets, kept because neither index covers them all once a name is
            // shared by two files in different subfolders.
            List<ScriptableObject> existingAssets = new List<ScriptableObject>();
            string[] existingFiles = Directory.GetFiles(path, "*.asset", SearchOption.AllDirectories);
            foreach (string file in existingFiles) {
                // GetFiles joins with a backslash on Windows, AssetDatabase only accepts forward slashes
                string existingPath = file.Replace('\\', '/');
                ScriptableObject existing = AssetDatabase.LoadAssetAtPath(existingPath, objectType) as ScriptableObject;
                // Filters out the _X_Database asset and any foreign asset sharing the folder
                if (existing == null || existing.GetType() != objectType)
                    continue;

                existingAssets.Add(existing);

                string existingId = rowIdField?.GetValue(existing) as string;
                if (!string.IsNullOrEmpty(existingId))
                    byId[existingId] = existing;
                else
                    byName[Path.GetFileNameWithoutExtension(existingPath)] = existing;
            }

            int created = 0, skipped = 0, renamed = 0;

            dynamic[] instances = new dynamic[rows.Length];
            // Snapshot of each pre-existing asset BEFORE fields are written. The updated/unchanged
            // verdict can't be made here (fields and lookups are only assigned in pass 2), so we
            // record the "before" state now and diff it in SetAllFields. null = created this import.
            string[] beforeSnapshots = new string[rows.Length];
            // Rows that passed validation but matched no existing asset; created in a second pass,
            // AFTER all renames, so a new row can't collide with a file a rename is about to free.
            List<int> toCreate = new List<int>();
            HashSet<ScriptableObject> matched = new HashSet<ScriptableObject>();

            // Names shared by several rows, counted before anything is touched. Coda does not
            // guarantee the order rows come back in, so importing the first one of a group bound the
            // asset to whichever row happened to arrive first, possibly a duplicate created by
            // mistake, and the asset turned orphan as soon as that row was the one deleted in Coda.
            // Every row of the group is left out instead, so the conflict has no effect on the assets
            // until it is fixed in Coda.
            Dictionary<string, List<int>> rowsByName = new Dictionary<string, List<int>>();
            for (int i = 0; i < rows.Length; i++) {
                if (string.IsNullOrWhiteSpace(rows[i].Name))
                    continue;

                if (!rowsByName.TryGetValue(rows[i].Name, out List<int> sharingRows)) {
                    sharingRows = new List<int>();
                    rowsByName[rows[i].Name] = sharingRows;
                }

                sharingRows.Add(i);
            }

            HashSet<string> duplicateNames = new HashSet<string>();
            for (int i = 0; i < rows.Length; i++) {
                if (string.IsNullOrWhiteSpace(rows[i].Name))
                    continue;

                List<int> sharingRows = rowsByName[rows[i].Name];
                // One warning per name, emitted on its first row, so the warnings follow the order of
                // the table instead of the order of a dictionary.
                if (sharingRows.Count < 2 || sharingRows[0] != i)
                    continue;

                duplicateNames.Add(rows[i].Name);
                string rowNumbers = string.Join(", ", sharingRows.ConvertAll(row => $"#{row + 1}"));
                report.warnings.Add($"Table \"{structure.UnmodifiedName}\" \u2192 \"{rows[i].Name}\" is used by rows {rowNumbers}. None imported until the duplicate is fixed in Coda.");
            }

            // Pass A: validate rows, match existing assets (by id, then by name for migration) and
            // rename assets whose row was renamed in Coda.
            for (int i = 0; i < rows.Length; i++) {
                if (string.IsNullOrWhiteSpace(rows[i].Name)) {
                    report.warnings.Add($"Table \"{structure.UnmodifiedName}\" \u2192 row #{i + 1} has an empty display column. Skipped.");
                    skipped++;
                    continue;
                }

                // The whole group is skipped and already warned about above: no match by id or by
                // name, no rename, no id written, no creation.
                if (duplicateNames.Contains(rows[i].Name)) {
                    skipped++;
                    continue;
                }

                char? invalidChar = GetInvalidFileNameChar(rows[i].Name);
                if (invalidChar != null) {
                    report.warnings.Add($"Table \"{structure.UnmodifiedName}\" \u2192 row #{i + 1} \"{rows[i].UnmodifiedName}\" contains invalid character '{invalidChar}'. Skipped.");
                    skipped++;
                    continue;
                }

                ScriptableObject instance = null;

                if (!string.IsNullOrEmpty(rows[i].Id) && byId.TryGetValue(rows[i].Id, out ScriptableObject byIdMatch)) {
                    instance = byIdMatch;

                    // Row renamed in Coda: rename the asset instead of creating a duplicate
                    string currentPath = AssetDatabase.GetAssetPath(instance);
                    if (Path.GetFileNameWithoutExtension(currentPath) != rows[i].Name) {
                        string error = AssetDatabase.RenameAsset(currentPath, rows[i].Name);
                        if (string.IsNullOrEmpty(error)) {
                            renamed++;
                            report.renames.Add(new ImportReport.RenameInfo {
                                tableName = structure.UnmodifiedName,
                                oldName = Path.GetFileNameWithoutExtension(currentPath),
                                newName = rows[i].Name
                            });
                        } else
                            report.warnings.Add($"Table \"{structure.UnmodifiedName}\" \u2192 could not rename \"{Path.GetFileNameWithoutExtension(currentPath)}\" to \"{rows[i].Name}\": {error}. The asset keeps its old file name.");
                    }
                } else if (byName.TryGetValue(rows[i].Name, out ScriptableObject byNameMatch)) {
                    // Migration fallback: pre-1.4 asset (or hand-made one at the right name) gets
                    // adopted and stamped with the row id below.
                    instance = byNameMatch;
                    byName.Remove(rows[i].Name);
                }

                if (instance != null) {
                    // Snapshot BEFORE stamping the id: on the first post-migration import the stamp
                    // changes the serialization, so the asset is counted (and saved) as updated once.
                    beforeSnapshots[i] = EditorJsonUtility.ToJson(instance);
                    rowIdField?.SetValue(instance, rows[i].Id);
                    matched.Add(instance);
                    instances[i] = instance;
                    instanceList.Add(instances[i]);
                } else {
                    toCreate.Add(i);
                }
            }

            // Pass B: create assets for new rows, now that every rename has been applied.
            foreach (int i in toCreate) {
                string assetPath = $"{path}/{rows[i].Name}.asset";

                // The target file can still be occupied by an asset of this type whose row was
                // deleted in Coda (reported as orphaned below), or by an unrelated asset. Never
                // overwrite it: skip with a warning instead.
                if (AssetDatabase.LoadAssetAtPath(assetPath, typeof(UnityEngine.Object)) != null) {
                    report.warnings.Add($"Table \"{structure.UnmodifiedName}\" \u2192 can't create \"{rows[i].Name}\": a file already exists at {assetPath} (likely an orphaned asset, see the orphan list). Skipped.");
                    skipped++;
                    continue;
                }

                instances[i] = ScriptableObject.CreateInstance(objectType);
                rowIdField?.SetValue(instances[i], rows[i].Id);
                AssetDatabase.CreateAsset(instances[i], assetPath);
                created++;
                instanceList.Add(instances[i]);
            }

            // Ids of ALL current rows, skipped ones included, so a skipped duplicate row doesn't
            // flag its existing asset as orphaned. Read twice below: to tell an asset held by one of
            // the conflicting rows from one whose row was deleted, and by orphan detection itself.
            HashSet<string> currentRowIds = new HashSet<string>();
            for (int i = 0; i < rows.Length; i++) {
                if (!string.IsNullOrEmpty(rows[i].Id))
                    currentRowIds.Add(rows[i].Id);
            }

            // An asset already on disk under a duplicated name keeps everything it has: its row id,
            // its fields, and its place in the table's database list, which is rebuilt from scratch on
            // every import. File names are read here, after pass A, so an asset whose own row still
            // owns it has already been renamed out of the conflict. An asset whose row was deleted in
            // Coda is left to orphan detection below, and stays out of the list.
            HashSet<ScriptableObject> conflicted = new HashSet<ScriptableObject>();
            // Guarded so a table with no name conflict costs exactly what it used to.
            if (duplicateNames.Count > 0) {
                foreach (ScriptableObject existing in existingAssets) {
                    if (matched.Contains(existing))
                        continue;

                    string existingName = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(existing));
                    if (!duplicateNames.Contains(existingName))
                        continue;

                    string existingId = rowIdField?.GetValue(existing) as string;
                    if (!string.IsNullOrEmpty(existingId) && !currentRowIds.Contains(existingId))
                        continue;

                    conflicted.Add(existing);
                    // Cast to dynamic so the call binds on the asset's real generated type: the list
                    // is a List<generated type>, which has no Add(ScriptableObject).
                    instanceList.Add((dynamic)existing);
                }
            }

            // Orphan detection (report only, nothing is ever deleted here).
            foreach (var pair in byId) {
                if (currentRowIds.Contains(pair.Key))
                    continue;

                report.orphans.Add(new ImportReport.OrphanInfo {
                    tableName = structure.UnmodifiedName,
                    assetName = pair.Value.name,
                    assetPath = AssetDatabase.GetAssetPath(pair.Value),
                    reason = ImportReport.OrphanReason.deletedRow
                });
            }

            // Assets without an id that no current row adopted: hand-made / foreign
            foreach (var pair in byName) {
                if (matched.Contains(pair.Value) || conflicted.Contains(pair.Value))
                    continue;

                report.orphans.Add(new ImportReport.OrphanInfo {
                    tableName = structure.UnmodifiedName,
                    assetName = pair.Value.name,
                    assetPath = AssetDatabase.GetAssetPath(pair.Value),
                    reason = ImportReport.OrphanReason.unmanaged
                });
            }

            // The report entry (created/updated/unchanged/skipped) is added in SetAllFields, once the
            // real diff is known. created, skipped and renamed are final here; carry them via state.
            state = new TableImportState {
                created = created,
                skipped = skipped,
                renamed = renamed,
                beforeSnapshots = beforeSnapshots
            };

            FieldInfo instanceListField = databaseType.GetField($"List", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            instanceListField.SetValue(database, instanceList);

            EditorUtility.SetDirty(database);
            AssetDatabase.SaveAssetIfDirty(database);

            return instances;
        }

        private static void SetAllFields(TableStructure structure, dynamic[] instances, TableRow[] rows, TableImportState state) {
            Type objectType = allTypes[$"{TableImporter.CodeNamespace}.{structure.Name}"];
            currentTableName = structure.UnmodifiedName;

            int updated = 0, unchanged = 0;

            // Set Asset Fields
            for (int i = 0; i < rows.Length; i++) {
                // Skip rows that were not created (empty name or duplicate)
                if (instances[i] == null)
                    continue;

                currentRowName = rows[i].UnmodifiedName;
                SetFields(structure, objectType, instances[i], rows[i].Values);

                // Freshly created this import (no "before" snapshot): always counted as created,
                // and always saved.
                if (state.beforeSnapshots[i] == null) {
                    EditorUtility.SetDirty(instances[i]);
                    AssetDatabase.SaveAssetIfDirty(instances[i]);
                    continue;
                }

                // Pre-existing asset: diff the serialized state to tell updated from unchanged.
                // Only mark dirty / save when something actually changed: this keeps the consumer's
                // git diff clean and avoids needless asset rewrites.
                // Note: image fields are assigned asynchronously (LoadImage coroutine) after this
                // snapshot, so a row whose only change is an image is currently seen as unchanged.
                string after = EditorJsonUtility.ToJson(instances[i]);
                bool dataChanged = after != state.beforeSnapshots[i];

                if (dataChanged) updated++;
                else unchanged++;

                // The count stays driven by the data diff, the save by "data changed OR schema changed".
                // An asset rewritten only because its class gained or lost a field carries the same data
                // as before, so counting it as updated would bring back the over-count fixed in 1.3.0.
                if (dataChanged || state.forceRewrite) {
                    EditorUtility.SetDirty(instances[i]);
                    AssetDatabase.SaveAssetIfDirty(instances[i]);
                }
            }

            if (state.forceRewrite) {
                ImportReport.SchemaChangeInfo schemaChange = report.schemaChanges.Find(change => change.tableName == structure.UnmodifiedName);
                // Every pre-existing asset is saved when the schema changed, and each one lands in
                // exactly one of the two counters, so this is their total. Assets created this import
                // are excluded on purpose: they are always written, schema change or not.
                if (schemaChange != null)
                    schemaChange.rewritten = updated + unchanged;
            }

            report.instances.Add(new ImportReport.InstanceInfo {
                tableName = structure.UnmodifiedName,
                created = state.created,
                updated = updated,
                unchanged = unchanged,
                skipped = state.skipped,
                renamed = state.renamed
            });
        }

        private static void SetFields(TableStructure structure, Type instanceType, dynamic instance, Dictionary<string, string> fields) {
            foreach (var key in fields.Keys) {
                TableColumn? column = GetColumnById(structure, key);
                if (column == null) {
                    continue;
                }

                currentFieldName = column.Value.Name;
                FieldInfo instanceField = instanceType.GetField(column.Value.Name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);

                if (!column.Value.Format.IsArray)
                    SetFieldSimpleValue(column.Value, instance, instanceField, fields[key]);
                else
                    SetFieldArray(column.Value, instance, instanceField, fields[key]);
            }
        }

        private static void SetFieldSimpleValue(TableColumn column, dynamic instance, FieldInfo field, string value) {
            Type fieldType = GetType(column);

            switch (column.Format.Type) {
                case ColumnType.lookup:
                    field.SetValue(instance, IsFieldEmpty(value) ? default : FindAssetByTypeAndName(fieldType, (string)value));
                    break;
                case ColumnType.image:
                    if (IsValidUrl(value)) {
                        EditorCoroutineUtility.StartCoroutine(LoadImage(value, (Texture2D tex) => {
                            Sprite sprite = CreateSpriteAsset(tex, GetFileNameFromURL(value));
                            field.SetValue(instance, sprite);
                        }), instance);
                    } else {
                        field.SetValue(instance, null);
                    }
                    break;
                default:
                    if (IsFieldEmpty(value)) {
                        field.SetValue(instance, null);
                    } else if (TryConvertValue(column, value, fieldType, out object converted)) {
                        field.SetValue(instance, converted);
                    } else {
                        // One unreadable cell used to throw out of the whole import
                        report.warnings.Add($"Table \"{currentTableName}\" → row \"{currentRowName}\": can't read \"{value}\" as {fieldType.Name} in column \"{currentFieldName}\". Field left unchanged.");
                    }
                    break;
            }
        }

        private static bool TryConvertValue(TableColumn column, string value, Type fieldType, out object converted) {
            converted = null;

            if (fieldType == typeof(float)) {
                if (!TryParseCodaNumber(value, out float number))
                    return false;

                // Coda sends "25 %" for a percent cell whose value is 0.25, the number its formulas use
                if (column.Format.Type == ColumnType.percent && !float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                    number /= 100f;

                converted = number;
                return true;
            }

            try {
                converted = Convert.ChangeType(value, fieldType, CultureInfo.InvariantCulture);
                return true;
            } catch (Exception e) when (e is FormatException || e is InvalidCastException || e is OverflowException) {
                return false;
            }
        }

        /// <summary>
        /// Reads a number Coda may have formatted with the doc's locale: "12.5", "25 %", "€12,50", "1 234,50 €",
        /// "$1,234.50". With both separators, the last one is the decimal point. With a single one, it is a
        /// thousands separator when it repeats or has exactly three digits after it, the decimal point otherwise.
        /// </summary>
        internal static bool TryParseCodaNumber(string value, out float number) {
            if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                return true;

            StringBuilder kept = new StringBuilder();
            foreach (char c in value) {
                if (char.IsDigit(c) || c == '.' || c == ',' || c == '-')
                    kept.Append(c);
            }

            string s = kept.ToString();
            int lastDot = s.LastIndexOf('.');
            int lastComma = s.LastIndexOf(',');

            if (lastDot >= 0 && lastComma >= 0) {
                char thousands = lastDot > lastComma ? ',' : '.';
                s = s.Replace(thousands.ToString(), "").Replace(',', '.');
            } else if (lastDot >= 0 || lastComma >= 0) {
                char separator = lastDot >= 0 ? '.' : ',';
                int last = Math.Max(lastDot, lastComma);
                bool repeats = s.IndexOf(separator) != last;
                bool thousands = repeats || s.Length - last - 1 == 3;
                s = thousands ? s.Replace(separator.ToString(), "") : s.Replace(',', '.');
            }

            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
        }

        private static void SetFieldArray(TableColumn column, dynamic instance, FieldInfo field, string value) {
            Type fieldType = GetType(column);
            string[] splitValue = value.Split(',');

            switch (column.Format.Type) {
                case ColumnType.lookup:
                    Type listType = typeof(List<>).MakeGenericType(fieldType);
                    dynamic list = Activator.CreateInstance(listType);

                    for (int i = 0; i < splitValue.Length; i++) {
                        list.Add(FindAssetByTypeAndName(fieldType, splitValue[i]));
                    }

                    field.SetValue(instance, IsFieldEmpty(value) ? default : list.ToArray());
                    break;
                case ColumnType.image:
                    // TODO: Implement image array handling if needed
                    break;
                default:
                    field.SetValue(instance, IsFieldEmpty(value) ? default : splitValue);
                    break;
            }
        }
        #endregion

        #region Asset Resolution
        /// <summary>
        /// Main method to find assets - now uses AssetPath as primary resolution method
        /// </summary>
        private static dynamic FindAssetByTypeAndName(Type assetType, string assetName) {
            // Try path-based resolution first (faster and more reliable)
            dynamic asset = FindAssetByPath(assetType, assetName);
            if (asset != null) return asset;

            asset = FindAssetInTableFolder(assetType, assetName);
            if (asset != null) return asset;

            // Fallback for a target table written by another importer, in another folder
            asset = FindAssetBySearch(assetType, assetName);

            if (asset == null && !IsFieldEmpty(assetName)) {
                // Determine which table this lookup targets
                string referencedTable = assetType.Name;
                bool wasImported = false;
                if (allStructures != null) {
                    foreach (var s in allStructures) {
                        if (s.Name == referencedTable || s.UnmodifiedName == referencedTable) {
                            wasImported = true;
                            referencedTable = s.UnmodifiedName;
                            break;
                        }
                    }
                }

                report.lookupFailures.Add(new ImportReport.LookupFailure {
                    tableName = currentTableName,
                    fieldName = currentFieldName,
                    missingAsset = assetName,
                    referencedTable = referencedTable,
                    tableWasImported = wasImported
                });
            }

            return asset;
        }

        /// <summary>
        /// Direct path-based asset resolution using exported asset references
        /// </summary>
        private static dynamic FindAssetByPath(Type assetType, string assetName) {
            if (!assetRefs.ContainsKey(assetType)) return null;

            AssetRef[] refs = assetRefs[assetType];
            foreach (var assetRef in refs) {
                if (assetRef.AssetName == assetName) {
                    if (!string.IsNullOrEmpty(assetRef.AssetPath)) {
                        dynamic asset = AssetDatabase.LoadAssetAtPath(assetRef.AssetPath, assetType);
                        if (asset != null) {
                            return asset;
                        } else {
                            report.warnings.Add($"Asset \"{assetName}\" not found at path: {assetRef.AssetPath}");
                        }
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// A data table writes each row at {basePath}/{Table}/{sanitized row name}.asset, and its class is named
        /// after the table: the lookup value gives the path directly, with no search index involved.
        /// </summary>
        private static UnityEngine.Object FindAssetInTableFolder(Type assetType, string assetName) {
            if (string.IsNullOrEmpty(basePath) || IsFieldEmpty(assetName))
                return null;

            string assetPath = $"{basePath}/{assetType.Name}/{CodaSyncUtils.SanitizeName(assetName)}.asset";
            return AssetDatabase.LoadAssetAtPath(assetPath, assetType);
        }

        /// <summary>
        /// Search-based asset resolution (fallback for non-exported assets and SO relations)
        /// </summary>
        private static dynamic FindAssetBySearch(Type assetType, string assetName) {
            string[] searchParam = assetRefs.ContainsKey(assetType) ?
                new[] { GetAssetDirectoryPath(assetType, assetName) } :
                new[] { "Assets" };

            string normalizedExpected = CodaSyncUtils.SanitizeName(assetName);

            // The file is named after the sanitized value: a raw "Demir - Base" makes Unity's search require the word "-"
            string searchString = $"{normalizedExpected} t:{assetType.FullName}";
            searchString = searchString.Replace("UnityEngine.", "");

            string[] resultGUIDS = AssetDatabase.FindAssets(searchString, searchParam);

            for (int i = 0; i < resultGUIDS.Length; i++) {
                string assetPath = AssetDatabase.GUIDToAssetPath(resultGUIDS[i]);

                // Compare the file name: on a dynamic, asset.name reads a generated 'name' field when the table has one
                if (Path.GetFileNameWithoutExtension(assetPath) != normalizedExpected)
                    continue;

                UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath(assetPath, assetType);
                if (asset != null)
                    return asset;
            }

            return null;
        }

        /// <summary>
        /// Get directory path for search fallback
        /// </summary>
        private static string GetAssetDirectoryPath(Type assetType, string assetName) {
            if (!assetRefs.ContainsKey(assetType)) return "Assets";
            
            AssetRef[] refs = assetRefs[assetType];
            foreach (var asset in refs) {
                if (asset.AssetName == assetName)
                    return GetPathFromFilePath(asset.AssetPath);
            }
            return "Assets";
        }
        #endregion

        #region Image Handling
        private static Sprite CreateSpriteAsset(Texture2D texture, string assetName) {
            string assetPath = $"{basePath}/Images/{assetName}.png";

            if (!Directory.Exists($"{basePath}/Images"))
                Directory.CreateDirectory($"{basePath}/Images");

            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);

            if (sprite == null) {
                File.WriteAllBytes(assetPath, texture.EncodeToPNG());
                AssetDatabase.Refresh();
                sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            }

            return sprite;
        }

        private static IEnumerator LoadImage(string url, Action<Texture2D> callback) {
            UnityWebRequest req = UnityWebRequestTexture.GetTexture(url);
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success) {
                callback(((DownloadHandlerTexture)req.downloadHandler).texture);
            } else {
                Debug.Log($"<color=#E85B5B>\u274c <b>[CodaSync]</b> Failed to load image from {url}: {req.error}</color>");
                callback(null);
            }
        }
        #endregion

        #region Utility Methods
        private static bool IsFieldEmpty(object fieldValue) {
            return fieldValue.GetType() == typeof(string) && string.IsNullOrEmpty((string)fieldValue);
        }

        private static Dictionary<string, Type> GetAllTypes() {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            Dictionary<string, Type> allTypes = new Dictionary<string, Type>();

            foreach (var assembly in assemblies) {
                Type[] types = assembly.GetTypes();
                foreach (var type in types) {
                    allTypes[type.FullName] = type;
                }
            }

            return allTypes;
        }

        private static Type GetType(TableColumn column) {
            ColumnType columnType = column.Format.Type;

            switch (columnType) {
                case ColumnType.text:
                case ColumnType.canvas:
                    return typeof(string);
                case ColumnType.slider:
                case ColumnType.number:
                case ColumnType.scale:
                case ColumnType.percent:
                case ColumnType.currency:
                    return typeof(float);
                case ColumnType.checkbox:
                    return typeof(bool);
                case ColumnType.image:
                    return typeof(Sprite);
                case ColumnType.date:
                case ColumnType.dateTime:
                    return typeof(DateTime);
                case ColumnType.time:
                case ColumnType.duration:
                case ColumnType.email:
                case ColumnType.link:
                    return typeof(string);
                case ColumnType.lookup:
                    if (ImporterExporter.TypeTables.Contains(column.Format.Table.Name))
                        return GetAssetType(column.Format.Table.Name);
                    else
                        return allTypes[$"{TableImporter.CodeNamespace}.{column.Format.Table.Name}"];
                default:
                    return typeof(object);
            }
        }

        private static Type GetAssetType(string typeName) {
            switch (typeName) {
                case "AudioClip":
                    return typeof(AudioClip);
                case "Texture2D":
                    return typeof(Texture2D);
                case "Sprite":
                    return typeof(Sprite);
                case "Material":
                    return typeof(Material);
                case "AnimatorController":
                    return typeof(AnimatorController);
                case "Animation":
                    return typeof(Animation);
                case "GameObject":
                    return typeof(GameObject);
                default:
                    return typeof(UnityEngine.Object);
            }
        }

        private static string GetFileNameFromURL(string url) {
            Regex rx = new Regex(@"[^\/\\&\?]+\.\w{3,4}(?=([\?&].*$|$))");
            MatchCollection matches = rx.Matches(url);
            return matches.Count == 0 ? "" : matches[0].Value.Split('.')[0];
        }

        private static string GetPathFromFilePath(string filepath) {
            Regex rx = new Regex(@"^(.+)\/([^\/]+)$");
            MatchCollection matches = rx.Matches(filepath);
            return matches.Count == 0 ? "" : matches[0].Groups[1].Value;
        }

        private static TableColumn? GetColumnById(TableStructure structure, string columnId) {
            foreach (TableColumn columnStructure in structure.Items) {
                if (columnStructure.Id == columnId) {
                    return columnStructure;
                }
            }
            return null;
        }

        private static string GetColumnIdByName(TableStructure structure, string columnName) {
            foreach (TableColumn columnStructure in structure.Items) {
                if (columnStructure.Name == columnName) {
                    return columnStructure.Id;
                }
            }
            return "";
        }

        private static bool IsValidUrl(string url) {
            return Uri.TryCreate(url, UriKind.Absolute, out Uri _);
        }

        // Characters that Unity rejects in asset file names (cross-platform).
        // Path.GetInvalidFileNameChars() is OS-specific (macOS only blocks \0 and /),
        // so we enforce the full Windows set to match Unity's AssetDatabase behavior.
        private static readonly HashSet<char> InvalidAssetNameChars = new HashSet<char>(
            new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' }
        );

        /// <summary>
        /// Checks if a name contains characters that Unity rejects in asset file names.
        /// Returns the first invalid character found, or null if the name is valid.
        /// </summary>
        private static char? GetInvalidFileNameChar(string name) {
            for (int i = 0; i < name.Length; i++) {
                if (InvalidAssetNameChars.Contains(name[i]) || char.IsControl(name[i]))
                    return name[i];
            }
            return null;
        }

        #endregion
    }
}