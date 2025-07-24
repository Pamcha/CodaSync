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


            for (int i = 0; i < structures.Length; i++) {
                if (ImporterExporter.TypeTables.Contains(structures[i].UnmodifiedName))
                    continue;

                SetAllFields(structures[i], instances[structures[i]], tablesRows[i]);
            }
        }

        #region AssetsLoading
        private static void LoadAssetRefs (TableStructure[] structures, TableRow[][] tablesRows) {
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
                string assetIdString = rows[i].Values[GetColumnIdByName(structure, "AssetId")];
                bool success = int.TryParse(assetIdString, out int assetId);

                if (!success) {
                    Debug.LogWarning($"Coda Sync : Asset {rows[i].Values[GetColumnIdByName(structure, "AssetName")]} can't be referenced, invalid assetId, check your AssetReferences in Coda");
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

        #region InstancesCreation
        private static dynamic[] CreateInstances(TableStructure structure, TableRow[] rows, string path) {
            ScriptableObject database = ScriptableObject.CreateInstance($"{structure.Name}_DB");
            AssetDatabase.CreateAsset(database, $"{path}/_{structure.Name}_Database.asset");

            allTypes = GetAllTypes();
            Type databaseType = allTypes[$"{TableImporter.CodeNamespace}.{structure.Name}_DB"];
            Type objectType = allTypes[$"{TableImporter.CodeNamespace}.{structure.Name}"];
            Type listType = typeof(List<>).MakeGenericType(objectType);
            dynamic instanceList = Activator.CreateInstance(listType);

            //Create Assets
            dynamic[] instances = new dynamic[rows.Length];
            for (int i = 0; i < rows.Length; i++) {

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

            //Set Assets Fields
            for (int i = 0; i < rows.Length; i++) {
                SetFields(structure, objectType, instances[i], rows[i].Values);
                EditorUtility.SetDirty(instances[i]);
                AssetDatabase.SaveAssetIfDirty(instances[i]);
            }
        }

        private static void SetFields(TableStructure structure, Type instanceType, dynamic instance, Dictionary<string, string> fields) {
            FieldInfo instanceField;

            foreach (var key in fields.Keys) {
                TableColumn? column = GetColumnById(structure, key);
                if (column == null) {
                    //Debug.LogWarning($"Coda Sync : Can't find Column structure data for Column {key}");
                    continue;
                }

                instanceField = instanceType.GetField(column.Value.Name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);

                if (!column.Value.Format.IsArray)
                    SetFieldSimpleValue(column.Value, instance, instanceField, fields[key]);
                else {
                    SetFieldArray(column.Value, instance, instanceField, fields[key]);
                }
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

                    break;
                default:
                    field.SetValue(instance, IsFieldEmpty(value) ? default : splitValue);
                    break;
            }
        }

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

            Debug.Log($"Requesting Image {url}");

            yield return req.SendWebRequest();


            Debug.Log($"Result for {url} : {req.result}");

            if (req.result == UnityWebRequest.Result.Success)
                callback(((DownloadHandlerTexture)req.downloadHandler).texture);
            else
                callback(null);
        }
        #endregion

        #region UTILS
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

        private bool DoClassExist(string fullClassname) {
            // Liste de toutes les assembly du projet (je suis pas s�r de ce que c'est une assembly)
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

            for (int i = 0; i < assemblies.Length; i++) {
                // Liste de toutes les types de ton projet
                Type[] types = assemblies[i].GetTypes();

                for (int j = 0; j < types.Length; j++) {

                    // Et l� t'as trouv� ta classe
                    if (types[j].FullName == fullClassname)
                        return true;
                }
            }

            return false;
        }
        private static Type GetType(TableColumn column) {
            Type type = null;
            ColumnType columnType = column.Format.Type;

            switch (columnType) {
                case ColumnType.text:
                case ColumnType.canvas:
                    type = typeof(string);
                    break;
                case ColumnType.slider:
                case ColumnType.number:
                    type = typeof(float);
                    break;
                case ColumnType.checkbox:
                    type = typeof(bool);
                    break;
                case ColumnType.image:
                    type = typeof(Sprite);
                    break;
                case ColumnType.date:
                    type = typeof(DateTime);
                    break;
                case ColumnType.lookup:
                    if (ImporterExporter.TypeTables.Contains(column.Format.Table.Name))
                        type = GetAssetType(column.Format.Table.Name);
                    else
                        type = allTypes[$"{TableImporter.CodeNamespace}.{column.Format.Table.Name}"];
                    break;
                default:
                    type = typeof(object);
                    break;
            }

            return type;
        }

        private static Type GetAssetType (string typeName) {
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


            if (matches.Count == 0)
                return "";
            else {
                return matches[0].Value.Split('.')[0];
            }
        }

        private static string GetPathFromFilePath (string filepath) {
            Regex rx = new Regex(@"^(.+)\/([^\/]+)$");

            MatchCollection matches = rx.Matches(filepath);

            if (matches.Count == 0)
                return "";
            else {
                return matches[0].Groups[1].Value;
            }
        }

        private static TableColumn? GetColumnById(TableStructure structure, string columnId) {
            TableColumn? column = null;

            foreach (TableColumn columnStructure in structure.Items) {
                if (columnStructure.Id == columnId) {
                    column = columnStructure;
                    break;
                }
            }

            return column;
        }

        private static string GetColumnIdByName (TableStructure structure, string columnName) {
            string columnId = "";

            foreach (TableColumn columnStructure in structure.Items) {
                if (columnStructure.Name == columnName) {
                    columnId = columnStructure.Id;
                    break;
                }
            }

            return columnId;
        }

        private static dynamic FindAssetByTypeAndName(Type assetType, string assetName) {
            string[] searchParam = assetRefs.ContainsKey(assetType) ? new[] { GetAssetPath(assetType, assetName) } : new[] { "Assets" };
            string searchString = $"{assetName} t:{assetType.FullName}";
            searchString = searchString.Replace("UnityEngine.", "");

            //Debug.Log(searchParam[0]);

            string[] resultGUIDS = AssetDatabase.FindAssets(searchString, searchParam);

            for (int i = 0; i < resultGUIDS.Length; i++) {
                string assetPath = AssetDatabase.GUIDToAssetPath(resultGUIDS[i]);
                dynamic asset = AssetDatabase.LoadAssetAtPath(assetPath, assetType);

                string normalizedExpected = assetName.Replace(" ", "_");
                // Only return it if the asset name is an exact match
                if (asset != null && asset.name == normalizedExpected)
                    return asset;
            }

            return null;
        }

        private static string GetAssetPath (Type assetType, string assetName) {
            AssetRef[] refs = assetRefs[assetType];

            foreach (var asset in refs) {
                if (asset.AssetName == assetName)
                    return GetPathFromFilePath(asset.AssetPath);
            }

            return "Assets";
        }

        private static bool IsValidUrl(string url) {
            return Uri.TryCreate(url, UriKind.Absolute, out Uri _);
        }
        #endregion
    }
}