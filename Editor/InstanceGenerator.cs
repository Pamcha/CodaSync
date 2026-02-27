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

        public static void CreateAllInstances(TableStructure[] structures, TableRow[][] tablesRows, string path) {
            LoadAssetRefs(structures, tablesRows);

            Dictionary<TableStructure, dynamic[]> instances = new Dictionary<TableStructure, dynamic[]>();
            basePath = path;

            for (int i = 0; i < structures.Length; i++) {
                if (ImporterExporter.TypeTables.Contains(structures[i].UnmodifiedName))
                    continue;

                string instancePath = $"{path}/{structures[i].Name}";
                if (!Directory.Exists(instancePath))
                    Directory.CreateDirectory(instancePath);

                instances[structures[i]] = CreateInstances(structures[i], tablesRows[i], instancePath);
            }

            // Flush all newly created assets to disk before resolving lookups in pass 2.
            // No AssetDatabase.Refresh() needed here: assets created via CreateAsset are already
            // indexed in memory, so FindAssets can resolve them. Refresh() would only be needed
            // for files written directly to disk (e.g. File.WriteAllText), and is slow on large projects.
            AssetDatabase.SaveAssets();

            for (int i = 0; i < structures.Length; i++) {
                if (ImporterExporter.TypeTables.Contains(structures[i].UnmodifiedName))
                    continue;

                SetAllFields(structures[i], instances[structures[i]], tablesRows[i]);
            }
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
                    Debug.Log($"<color=#E8A838>\u26a0\ufe0f <b>[CodaSync]</b> Asset \"{rows[i].Values[GetColumnIdByName(structure, "AssetName")]}\" can't be referenced (invalid AssetId). Check your AssetReferences in Coda.</color>");
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
        private static dynamic[] CreateInstances(TableStructure structure, TableRow[] rows, string path) {
            ScriptableObject database = ScriptableObject.CreateInstance($"{structure.Name}_DB");
            AssetDatabase.CreateAsset(database, $"{path}/_{structure.Name}_Database.asset");

            allTypes = GetAllTypes();
            Type databaseType = allTypes[$"{TableImporter.CodeNamespace}.{structure.Name}_DB"];
            Type objectType = allTypes[$"{TableImporter.CodeNamespace}.{structure.Name}"];
            Type listType = typeof(List<>).MakeGenericType(objectType);
            dynamic instanceList = Activator.CreateInstance(listType);

            // Track seen names to skip duplicates (duplicates cause asset overwrites and broken lookups)
            HashSet<string> seenNames = new HashSet<string>();

            // Create Assets
            dynamic[] instances = new dynamic[rows.Length];
            for (int i = 0; i < rows.Length; i++) {
                if (string.IsNullOrWhiteSpace(rows[i].Name)) {
                    Debug.Log($"<color=#E8A838>\u26a0\ufe0f <b>[CodaSync]</b> Table \"{structure.UnmodifiedName}\" \u2192 row #{i + 1} has an <b>empty display column</b>. This row will be skipped.</color>");
                    continue;
                }

                if (!seenNames.Add(rows[i].Name)) {
                    Debug.Log($"<color=#E85B5B>\u274c <b>[CodaSync]</b> Table \"{structure.UnmodifiedName}\" \u2192 row #{i + 1} \"{rows[i].UnmodifiedName}\" is a <b>duplicate name</b> (resolves to \"{rows[i].Name}\"). Skipped to avoid asset overwrite.</color>");
                    continue;
                }

                char? invalidChar = GetInvalidFileNameChar(rows[i].Name);
                if (invalidChar != null) {
                    Debug.Log($"<color=#E85B5B>\u274c <b>[CodaSync]</b> Table \"{structure.UnmodifiedName}\" \u2192 row #{i + 1} \"{rows[i].UnmodifiedName}\" contains invalid file name character '<b>{invalidChar}</b>'. This row will be skipped.</color>");
                    continue;
                }

                string assetPath = $"{path}/{rows[i].Name}.asset";
                instances[i] = AssetDatabase.LoadAssetAtPath(assetPath, objectType);

                if (instances[i] == null) {
                    instances[i] = ScriptableObject.CreateInstance(objectType);
                    AssetDatabase.CreateAsset(instances[i], assetPath);
                }

                instanceList.Add(instances[i]);
            }

            FieldInfo instanceListField = databaseType.GetField($"List", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            instanceListField.SetValue(database, instanceList);

            EditorUtility.SetDirty(database);
            AssetDatabase.SaveAssetIfDirty(database);

            return instances;
        }

        private static void SetAllFields(TableStructure structure, dynamic[] instances, TableRow[] rows) {
            Type objectType = allTypes[$"{TableImporter.CodeNamespace}.{structure.Name}"];

            // Set Asset Fields
            for (int i = 0; i < rows.Length; i++) {
                // Skip rows that were not created (empty name or duplicate)
                if (instances[i] == null)
                    continue;

                SetFields(structure, objectType, instances[i], rows[i].Values);
                EditorUtility.SetDirty(instances[i]);
                AssetDatabase.SaveAssetIfDirty(instances[i]);
            }
        }

        private static void SetFields(TableStructure structure, Type instanceType, dynamic instance, Dictionary<string, string> fields) {
            foreach (var key in fields.Keys) {
                TableColumn? column = GetColumnById(structure, key);
                if (column == null) {
                    continue;
                }

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
            return FindAssetBySearch(assetType, assetName);
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
                            Debug.Log($"<color=#E8A838>\u26a0\ufe0f <b>[CodaSync]</b> Asset \"{assetName}\" not found at path: {assetRef.AssetPath}</color>");
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

                string normalizedExpected = assetName.Replace(" ", "_");

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
                    return typeof(float);
                case ColumnType.checkbox:
                    return typeof(bool);
                case ColumnType.image:
                    return typeof(Sprite);
                case ColumnType.date:
                    return typeof(DateTime);
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