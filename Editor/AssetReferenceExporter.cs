using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Networking;
using static Com.Pamcha.CodaSync.CodaRequester;

namespace Com.Pamcha.CodaSync {
    [CreateAssetMenu(fileName = "AssetReferenceExporter", menuName = "CodaSync/Asset Reference Exporter")]
    public class AssetReferenceExporter : ImporterExporter {
        [SerializeField] private string[] assetFolder;
        [SerializeField] private Object[] assets = new Object[0];

        private const string assetListTableName = "AssetReferences";

        protected override void OnValidate() {
            base.OnValidate();

            LoadAssets();
        }

        public void ExportReferences () {
            EditorUtility.DisplayProgressBar("Coda Table Import", "Fetching Table List", 0);



            GetTableList(OnTableListResponse);
        }

        private void OnTableListResponse (TableDescriptionData[] tables) {
            TableDescriptionData? assetTable = null;

            for (int i = 0; i < tables.Length; i++) {
                if (tables[i].name == assetListTableName) {
                    assetTable = tables[i];
                    break;
                }
            }

            if (assetTable == null) {
                Debug.LogError($"Can't find table {assetListTableName}");
                EditorUtility.ClearProgressBar();
            } else 
                GetTablesStructure(new List<TableDescriptionData>() { assetTable.Value }, OnTableListResponse);
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

            LoadAssets();
        }

        private void OnTableListResponse(TableStructure[] tables)
        {
            if (tables.Length == 0) {
                Debug.LogError($"No tables in API response");
                return;
            }

            ExportReferencesToTable(tables[0]);
        }

        private void ExportReferencesToTable(TableStructure table)
        {
            RowEdit edit = new RowEdit();

            TableColumn? assetNameColumn = GetColumnByName(table, "AssetName");
            TableColumn? assetIdColumn = GetColumnByName(table, "AssetId");
            TableColumn? assetPathColumn = GetColumnByName(table, "AssetPath");
            TableColumn? assetTypeColumn = GetColumnByName(table, "AssetType");

            edit.rows = new Row[assets.Length];
            edit.keyColumns = new[] { assetPathColumn.Value.Id, assetTypeColumn.Value.Id };

            for (int i = 0; i < assets.Length; i++)
            {
                AssetRef assetRef = new AssetRef(assets[i]);

                edit.rows[i].cells = new Cell[4];

                edit.rows[i].cells[0].value = assetRef.AssetName;
                edit.rows[i].cells[0].column = assetNameColumn.Value.Id;

                edit.rows[i].cells[1].value = assetRef.AssetId.ToString();
                edit.rows[i].cells[1].column = assetIdColumn.Value.Id;

                edit.rows[i].cells[2].value = assetRef.AssetPath;
                edit.rows[i].cells[2].column = assetPathColumn.Value.Id;

                string[] typeSplit = assets[i].GetType().ToString().Split('.');
                edit.rows[i].cells[3].value = typeSplit[typeSplit.Length-1];
                edit.rows[i].cells[3].column = assetTypeColumn.Value.Id;
            }

            requester.SetTableRows(documentId, table.Name, edit, OnTableEditResponse);
        }

        private void OnTableEditResponse(UnityWebRequest req) {
            EditorUtility.ClearProgressBar();

            lastSyncDateString = $"{System.DateTime.UtcNow:R}";
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