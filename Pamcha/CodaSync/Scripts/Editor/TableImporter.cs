using System.Collections.Generic;
using System.IO;
using Unity.Plastic.Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Com.Pamcha.CodaSync {
    [CreateAssetMenu(fileName = "NewTableImporter", menuName = "CodaSync/Table Importer")]
    public class TableImporter : ImporterExporter {


        [Header("Code Generation")]
        [SerializeField] private string codeNamespace = "Com.DefaultCompany.Table";
        [HideInInspector] public List<TableSelection> tableSelection;
        public bool CanDisplayTableSelection { get => docIdFound && requester != null && tableSelection != null; }

        public static string CodeNamespace { get; private set; }
        private const string editorPrefKeyShouldCreateInstances = "Com.Pamcha.CodaImporter.ShouldCreateInstances";
        private const string editorPrefKeyTablesStructure = "Com.Pamcha.CodaImporter.TablesStructure";

        protected override void OnValidate() {
            base.OnValidate();

            if (requester != null && docIdFound) {
                EditorUtility.DisplayProgressBar("Coda Table Import", "Requesting tables list", 0);
                GetTableList(OnUpdateTableList);
            }

            /// Try create instances after a recompilation
            if (EditorPrefs.GetBool(editorPrefKeyShouldCreateInstances)) {
                EditorPrefs.SetBool(editorPrefKeyShouldCreateInstances, false);
                CreateInstances();
            }
        }
        public void OnUpdateTableList(TableDescriptionData[] tableList) {
            List<TableSelection> newTableSelection = new List<TableSelection>();

            for (int i = 0; i < tableList.Length; i++) {
                TableSelection selection = new TableSelection();
                newTableSelection.Add(selection);
                selection.tableDescription = tableList[i];

                if (TypeTables.Contains(tableList[i].name))
                    selection.selected = true;
                else {
                    TableSelection prevSelection = tableSelection.Find(b => b.tableDescription.id == selection.tableDescription.id);
                    if (prevSelection != null)
                        selection.selected = prevSelection.selected;
                }
            }

            tableSelection = newTableSelection;

            EditorUtility.DisplayProgressBar("Coda Table Import", "Requesting tables list", 1);
            EditorUtility.ClearProgressBar();
        }

        #region TableStructure
        public void CreateScriptFiles() {
            List<TableDescriptionData> tables = new List<TableDescriptionData>();
            for (int i = 0; i < tableSelection.Count; i++) {
                if (tableSelection[i].selected)
                    tables.Add(tableSelection[i].tableDescription);
            }

            EditorUtility.DisplayProgressBar("Coda Table Import", "Requesting tables structure", 0);
            GetTablesStructure(tables, CreateScripts);
        }

        private void CreateScripts(TableStructure[] tableList) {
            EditorUtility.DisplayProgressBar("Coda Table Import", "Generating code", .5f);
            CodeNamespace = codeNamespace;
            CodeFiles[] codes = CodeGenerator.GetCodeFromTableStructures(tableList);

            for (int i = 0; i < tableList.Length; i++) {
                if (TypeTables.Contains(tableList[i].UnmodifiedName))
                    continue;
                CreateSourceFile($"{tableList[i].Name}_DB", codes[i].databaseCode);
                CreateSourceFile(tableList[i].Name, codes[i].classCode);
            }

            JsonSerializerSettings settings = new JsonSerializerSettings();
            EditorPrefs.SetString(editorPrefKeyTablesStructure, JsonConvert.SerializeObject(tableList));

            AssetDatabase.Refresh();

            if (EditorApplication.isCompiling) {
                EditorPrefs.SetBool(editorPrefKeyShouldCreateInstances, true);
                EditorUtility.DisplayProgressBar("Coda Table Import", "Waiting compilation", .6f);
            } else
                CreateInstances();
        }

        private void CreateSourceFile(string filename, string code) {
            string basePath = GetPath();
            string scriptsPath = $"{basePath}/Scripts";

            if (!Directory.Exists(scriptsPath))
                Directory.CreateDirectory(scriptsPath);

            File.WriteAllText($"{scriptsPath}/{filename}.cs", code);
        }
        #endregion


        #region TableData
        private void CreateInstances() {
            TableStructure[] structures = JsonConvert.DeserializeObject<TableStructure[]>(EditorPrefs.GetString(editorPrefKeyTablesStructure));
            string[] names = new string[structures.Length];
            EditorUtility.DisplayProgressBar("Coda Table Import", "Requesting tables data", .7f);

            for (int i = 0; i < structures.Length; i++) {
                names[i] = structures[i].UnmodifiedName;
            }
            requester.GetTablesData(documentId, names, (rqs) => OnTablesDataResponse(rqs, structures));
        }

        private void OnTablesDataResponse(UnityWebRequest[] dataRequests, TableStructure[] structures) {
            string basePath = GetPath();
            string instancesPath = $"{basePath}/Resources";
            CodeNamespace = codeNamespace;

            if (!Directory.Exists(instancesPath))
                Directory.CreateDirectory(instancesPath);

            TableRow[][] tablesRows = new TableRow[dataRequests.Length][];

            for (int i = 0; i < dataRequests.Length; i++) {
                tablesRows[i] = JsonConvert.DeserializeObject<TableRowResponse>(dataRequests[i].downloadHandler.text).items;
            }

            EditorUtility.DisplayProgressBar("Coda Table Import", "Requesting tables data", .85f);
            InstanceGenerator.CreateAllInstances(structures, tablesRows, instancesPath);

            AssetDatabase.Refresh();

            EditorUtility.DisplayProgressBar("Coda Table Import", "Requesting tables data", 1);
            EditorUtility.ClearProgressBar();

            lastSyncDateString = $"{System.DateTime.UtcNow:R}";
        }

        #endregion
    }
}
