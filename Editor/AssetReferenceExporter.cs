using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using static Com.Pamcha.CodaSync.CodaRequester;

namespace Com.Pamcha.CodaSync {
    [CreateAssetMenu(fileName = "AssetReferenceExporter", menuName = "CodaSync/Asset Reference Exporter")]
    public class AssetReferenceExporter : ImporterExporter {
        [SerializeField] private string[] assetFolder;
        [SerializeField] private Object[] assets = new Object[0];


        protected override void OnValidate() {
            base.OnValidate();

            LoadAssets();
        }

        public void ExportReferences () {
            EditorUtility.DisplayProgressBar("Coda Table Import", "Fetching Table List", 0);
            GetTableList((tables) => GetTablesStructure(tables.ToList(), OnTableListResponse));
        }

        public void AddFolder(string path) {
            string[] newFolders = new string[assetFolder.Length + 1];

            for (int i = 0; i < newFolders.Length; i++) {
                if (i < newFolders.Length - 1)
                    newFolders[i] = assetFolder[i];
                else
                    newFolders[i] = path;
            }

            assetFolder = newFolders;

            EditorUtility.SetDirty(this); // so that the folder list is saved

            LoadAssets();
        }

        private void OnTableListResponse(TableStructure[] tableList) {
            EditorUtility.DisplayProgressBar("Coda Table Import", "Exporting Assets references", .5f);
            for (int i = 0; i < tableList.Length; i++) {
                if (TypeTables.Contains(tableList[i].Name)) {
                    System.Type assetType;
                    switch (tableList[i].Name)
                    {
                        case "AudioClip":
                            assetType = typeof(AudioClip);
                            break;
                        case "Sprite":
                            assetType = typeof(Sprite);
                            break;
                        case "Texture2D":
                            assetType = typeof(Texture2D);
                            break;
                        case "GameObject":
                            assetType = typeof(GameObject);
                            break;
                        default:
                            assetType = typeof(Object);
                            break;
                    }

                    //List<AssetRef> refs = GetRefs(assetType);
                    //Debug.Log($"Got {refs.Count} asset of type {assetType}");
                    ExportReferencesToTable(tableList[i], GetRefs(assetType));
                }
            }
        }

        private void ExportReferencesToTable (TableStructure table, List<AssetRef> refs) {
            RowEdit edit = new RowEdit();

            TableColumn? assetNameColumn = GetColumnByName(table, "AssetName");
            TableColumn? assetIdColumn = GetColumnByName(table, "AssetId");
            TableColumn? assetPathColumn = GetColumnByName(table, "AssetPath");

            edit.rows = new Row[refs.Count];
            edit.keyColumns = new[] { assetIdColumn.Value.Id };

            for (int i = 0; i < refs.Count; i++) {
                edit.rows[i].cells = new Cell[3];

                edit.rows[i].cells[0].value = refs[i].AssetName;
                edit.rows[i].cells[0].column = assetNameColumn.Value.Id;

                edit.rows[i].cells[1].value = refs[i].AssetId.ToString();
                edit.rows[i].cells[1].column = assetIdColumn.Value.Id;

                edit.rows[i].cells[2].value = refs[i].AssetPath;
                edit.rows[i].cells[2].column = assetPathColumn.Value.Id;
            }

            requester.SetTableRows(documentId, table.Name, edit, OnTableEditResponse);
        }

        private void OnTableEditResponse(UnityWebRequest req) {
            EditorUtility.ClearProgressBar();

            lastSyncDateString = $"{System.DateTime.UtcNow:R}";
            lastSyncLocalDateString = lastSyncDateString;

            //Debug.Log("last sync "+lastSyncDateString + " - " +lastSyncLocalDateString);
            EditorUtility.SetDirty(this);
        }

        private List<AssetRef> GetRefs(System.Type type) {
            List<AssetRef> refs = new List<AssetRef>();

            for (int i = 0; i < assets.Length; i++) {
                if (assets[i].GetType() == type)
                    refs.Add(new AssetRef(assets[i]));
            }

            return refs;
        }
        private TableColumn? GetColumnByName(TableStructure structure, string columnName) {
            TableColumn? column = null;

            foreach (TableColumn columnStructure in structure.Items) {
                if (columnStructure.Name == columnName) {
                    column = columnStructure;
                    break;
                }
            }

            return column;
        }


        #region AssetLoading
        public void LoadAssets () {
            assets = FindAssetsAt(assetFolder);
        }

        public static Object[] FindAssetsAt (string[] folders) {
            List<Object> assets = new List<Object>();

            foreach (string folder in folders) {
                if (string.IsNullOrEmpty(folder))
                    continue;

                string[] guids = AssetDatabase.FindAssets($"", new[] { folder });

                for (int i = 0; i < guids.Length; i++) {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                    //if asset is a game object we just add the main asset at path so we don't explore recursively the game object and export each one of its children
                    if(AssetDatabase.LoadMainAssetAtPath(assetPath).GetType() == typeof(GameObject))
                        assets.Add(AssetDatabase.LoadMainAssetAtPath(assetPath));
                    else
                        assets.AddRange(AssetDatabase.LoadAllAssetsAtPath(assetPath));
                }
            }
            
            return assets.ToArray();
        }

        public static List<T> FindAssetsByType<T>(string folder) where T : UnityEngine.Object {
            List<T> assets = new List<T>();
            string[] classPath = typeof(T).ToString().Split('.');
            string type = classPath[classPath.Length-1];
            string[] guids = AssetDatabase.FindAssets($"t:{type}", new[] { folder });

            for (int i = 0; i < guids.Length; i++) {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                T asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
                if (asset != null) {
                    assets.Add(asset);
                }
            }
            return assets;
        }
        #endregion
    }
}