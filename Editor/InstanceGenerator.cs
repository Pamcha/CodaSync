using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Unity.EditorCoroutines.Editor;
using Unity.Plastic.Newtonsoft.Json;
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

        public static void CreateAllInstances(TableStructure[] structures, TableRow[][] tablesRows, string path, ImportReport importReport) {
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
        private static dynamic[] CreateInstances(TableStructure structure, TableRow[] rows, string path, out TableImportState state) {
            ScriptableObject database = ScriptableObject.CreateInstance($"{structure.Name}_DB");
            AssetDatabase.CreateAsset(database, $"{path}/_{structure.Name}_Database.asset");

            allTypes = GetAllTypes();
            Type databaseType = allTypes[$"{TableImporter.CodeNamespace}.{structure.Name}_DB"];
            Type objectType = allTypes[$"{TableImporter.CodeNamespace}.{structure.Name}"];
            Type listType = typeof(List<>).MakeGenericType(objectType);
            dynamic instanceList = Activator.CreateInstance(listType);

            // Track seen names to skip duplicates (duplicates cause asset overwrites and broken lookups)
            HashSet<string> seenNames = new HashSet<string>();
            int created = 0, skipped = 0;

            // Create Assets
            dynamic[] instances = new dynamic[rows.Length];
            // Snapshot of each pre-existing asset BEFORE fields are written. The updated/unchanged
            // verdict can't be made here \u2014 fields (and lookups) are only assigned in pass 2 \u2014 so we
            // record the "before" state now and diff it in SetAllFields. null = created this import.
            string[] beforeSnapshots = new string[rows.Length];
            for (int i = 0; i < rows.Length; i++) {
                if (string.IsNullOrWhiteSpace(rows[i].Name)) {
                    report.warnings.Add($"Table \"{structure.UnmodifiedName}\" \u2192 row #{i + 1} has an empty display column. Skipped.");
                    skipped++;
                    continue;
                }

                if (!seenNames.Add(rows[i].Name)) {
                    report.warnings.Add($"Table \"{structure.UnmodifiedName}\" \u2192 row #{i + 1} \"{rows[i].UnmodifiedName}\" is a duplicate name (resolves to \"{rows[i].Name}\"). Skipped.");
                    skipped++;
                    continue;
                }

                char? invalidChar = GetInvalidFileNameChar(rows[i].Name);
                if (invalidChar != null) {
                    report.warnings.Add($"Table \"{structure.UnmodifiedName}\" \u2192 row #{i + 1} \"{rows[i].UnmodifiedName}\" contains invalid character '{invalidChar}'. Skipped.");
                    skipped++;
                    continue;
                }

                string assetPath = $"{path}/{rows[i].Name}.asset";
                instances[i] = AssetDatabase.LoadAssetAtPath(assetPath, objectType);

                if (instances[i] == null) {
                    instances[i] = ScriptableObject.CreateInstance(objectType);
                    AssetDatabase.CreateAsset(instances[i], assetPath);
                    created++;
                } else {
                    // Capture the on-disk state before pass 2 overwrites the fields.
                    beforeSnapshots[i] = EditorJsonUtility.ToJson(instances[i]);
                }

                instanceList.Add(instances[i]);
            }

            // The report entry (created/updated/unchanged/skipped) is added in SetAllFields, once the
            // real diff is known. created and skipped are final here; carry them forward via state.
            state = new TableImportState {
                created = created,
                skipped = skipped,
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

                SetFields(structure, objectType, instances[i], rows[i].Values);

                // Freshly created this import (no "before" snapshot): always counted as created,
                // and always saved.
                if (state.beforeSnapshots[i] == null) {
                    EditorUtility.SetDirty(instances[i]);
                    AssetDatabase.SaveAssetIfDirty(instances[i]);
                    continue;
                }

                // Pre-existing asset: diff the serialized state to tell updated from unchanged.
                // Only mark dirty / save when something actually changed \u2014 this keeps the consumer's
                // git diff clean and avoids needless asset rewrites.
                // Note: image fields are assigned asynchronously (LoadImage coroutine) after this
                // snapshot, so a row whose only change is an image is currently seen as unchanged.
                string after = EditorJsonUtility.ToJson(instances[i]);
                if (after != state.beforeSnapshots[i]) {
                    updated++;
                    EditorUtility.SetDirty(instances[i]);
                    AssetDatabase.SaveAssetIfDirty(instances[i]);
                } else {
                    unchanged++;
                }
            }

            report.instances.Add(new ImportReport.InstanceInfo {
                tableName = structure.UnmodifiedName,
                created = state.created,
                updated = updated,
                unchanged = unchanged,
                skipped = state.skipped
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
                    field.SetValue(instance, IsFieldEmpty(value) ? default : Convert.ChangeType(value, fieldType));
                    break;
            }
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

            // Fallback to search-based resolution for compatibility
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
        /// Search-based asset resolution (fallback for non-exported assets and SO relations)
        /// </summary>
        private static dynamic FindAssetBySearch(Type assetType, string assetName) {
            string[] searchParam = assetRefs.ContainsKey(assetType) ?
                new[] { GetAssetDirectoryPath(assetType, assetName) } :
                new[] { "Assets" };

            string searchString = $"{assetName} t:{assetType.FullName}";
            searchString = searchString.Replace("UnityEngine.", "");

            string[] resultGUIDS = AssetDatabase.FindAssets(searchString, searchParam);

            for (int i = 0; i < resultGUIDS.Length; i++) {
                string assetPath = AssetDatabase.GUIDToAssetPath(resultGUIDS[i]);
                dynamic asset = AssetDatabase.LoadAssetAtPath(assetPath, assetType);

                string normalizedExpected = CodaSyncUtils.SanitizeName(assetName);

                // Extract just the filename from asset.name
                // (handles case where SO has a 'name' field that shadows UnityEngine.Object.name)
                string actualAssetName = asset?.name ?? "";
                if (actualAssetName.Contains("/")) {
                    actualAssetName = actualAssetName.Substring(actualAssetName.LastIndexOf('/') + 1);
                }

                if (asset != null && actualAssetName == normalizedExpected)
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