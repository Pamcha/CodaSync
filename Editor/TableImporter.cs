using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Plastic.Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.Networking;

namespace Com.Pamcha.CodaSync {
    [CreateAssetMenu(fileName = "NewTableImporter", menuName = "CodaSync/Table Importer")]
    public class TableImporter : ImporterExporter {


        [Header("Code Generation")]
        [SerializeField] private string codeNamespace = "Com.DefaultCompany.Table";

        [SerializeField] private bool getVisibleColumnsOnly = true;

        [HideInInspector] public List<TableSelection> tableSelection;
        public bool CanDisplayTableSelection { get => docIdFound && requester != null && tableSelection != null; }

        // Doc id of the last successful table-list fetch. Persisted so OnValidate can tell a real
        // document change apart from the many editor events that also fire it (see OnValidate).
        [SerializeField, HideInInspector] private string lastTableListDocId;

        public static string CodeNamespace { get; private set; }
        private const string editorPrefKeyShouldCreateInstances = "Com.Pamcha.CodaImporter.ShouldCreateInstances";
        private const string editorPrefKeyTablesStructure = "Com.Pamcha.CodaImporter.TablesStructure";
        private const string editorPrefKeyPreviousFields = "Com.Pamcha.CodaImporter.PreviousFields";

        private static bool isCancelled = false;

        protected override void OnValidate() {
            base.OnValidate();

            // OnValidate fires on many events unrelated to a user edit (script reload,
            // AssetDatabase.Refresh, play mode transitions, editor focus regain, the sync itself
            // rewriting lastSyncDateString...), which used to trigger ghost table-list requests.
            // Only auto-refresh here when the target document actually changed; opening the
            // inspector triggers its own refresh (TableImportEditor.OnEnable).
            if (requester != null && docIdFound && documentId != lastTableListDocId)
                ScheduleTableListRefresh();

            if (EditorPrefs.GetBool(editorPrefKeyShouldCreateInstances, true)) {
                EditorPrefs.SetBool(editorPrefKeyShouldCreateInstances, false);
                CreateInstances();
            }
        }

        /// <summary>
        /// Debounced table-list refresh. OnValidate and the inspector can both request a refresh
        /// several times per frame, so every request is routed through a single delayCall: at most
        /// one network call once the current event burst settles.
        /// </summary>
        public void ScheduleTableListRefresh() {
            EditorApplication.delayCall -= DeferredTableListRefresh;
            EditorApplication.delayCall += DeferredTableListRefresh;
        }

        private void DeferredTableListRefresh() {
            EditorApplication.delayCall -= DeferredTableListRefresh;

            // The asset may have been destroyed or reconfigured between the OnValidate burst and now.
            if (this == null || requester == null || !docIdFound)
                return;

            EditorUtility.DisplayProgressBar("Coda Table Import", "Requesting tables list", 0);
            GetTableList(OnUpdateTableList);
        }

        private void OnCompilation(string s, CompilerMessage[] messages) {
            CompilationPipeline.assemblyCompilationFinished -= OnCompilation;

            if (messages.Count(message => message.type == CompilerMessageType.Error) > 0) {
                EditorUtility.ClearProgressBar();
                EditorPrefs.SetBool(editorPrefKeyShouldCreateInstances, false);
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

            // Remember which document this list belongs to, so OnValidate only re-fetches
            // when the URL actually points somewhere else.
            lastTableListDocId = documentId;
            EditorUtility.SetDirty(this);

            EditorUtility.DisplayProgressBar("Coda Table Import", "Requesting tables list", 1);
            EditorUtility.ClearProgressBar();
        }

        #region CheckNames
        /// <summary>
        /// Fetches table structures and row data for selected tables, then logs a full name validation report.
        /// </summary>
        public void CheckNames() {
            List<TableDescriptionData> tables = new List<TableDescriptionData>();
            for (int i = 0; i < tableSelection.Count; i++) {
                if (tableSelection[i].selected)
                    tables.Add(tableSelection[i].tableDescription);
            }

            if (tables.Count == 0) {
                EditorUtility.DisplayDialog("Check Names", "No tables selected.", "OK");
                return;
            }

            // Use non-cancelable progress bar during async fetch (cancel can't stop an in-flight request)
            EditorUtility.DisplayProgressBar("Coda Sync", $"Fetching table structures ({tables.Count} tables)...", 0f);

            (string, string) visibleOnlyParam = ("visibleOnly", getVisibleColumnsOnly.ToString());
            GetTablesStructure(tables, OnCheckNamesStructureResponse, visibleOnlyParam);
        }

        private void OnCheckNamesStructureResponse(TableStructure[] structures) {
            // Use non-cancelable progress bar during async fetch (cancel can't stop an in-flight request)
            EditorUtility.DisplayProgressBar("Coda Sync", $"Fetching row data ({structures.Length} tables)...", 0.3f);

            // Fetch row data for all tables to validate row names
            string[] names = new string[structures.Length];
            for (int i = 0; i < structures.Length; i++) {
                names[i] = structures[i].UnmodifiedName;
            }

            requester.GetTablesData(documentId, names, (rqs) => OnCheckNamesRowDataResponse(rqs, structures));
        }

        private void OnCheckNamesRowDataResponse(UnityWebRequest[] dataRequests, TableStructure[] structures) {
            // Clear progress bar to reset any pending cancel state from async phases
            EditorUtility.ClearProgressBar();

            TableRow[][] tablesRows = new TableRow[dataRequests.Length][];
            for (int i = 0; i < dataRequests.Length; i++) {
                if (dataRequests[i].result != UnityWebRequest.Result.Success) {
                    Debug.LogWarning($"\u26a0\ufe0f <b>[CodaSync]</b> Failed to fetch rows for table \"{structures[i].UnmodifiedName}\": {dataRequests[i].error}");
                    tablesRows[i] = null;
                    continue;
                }

                try {
                    tablesRows[i] = JsonConvert.DeserializeObject<TableRowResponse>(dataRequests[i].downloadHandler.text).items;
                } catch (System.Exception e) {
                    Debug.LogWarning($"\u26a0\ufe0f <b>[CodaSync]</b> Failed to parse rows for table \"{structures[i].UnmodifiedName}\": {e.Message}");
                    tablesRows[i] = null;
                }
            }

            LogNameValidationReport(structures, tablesRows);
        }

        /// <summary>
        /// Logs a formatted name validation report to the Unity console.
        /// Checks table names, column names, and optionally row names.
        /// Used by both the Validate Names button (with row data) and during import (without).
        /// </summary>
        private static void LogNameValidationReport(TableStructure[] structures, TableRow[][] tablesRows = null, bool duringImport = false) {
            string context = duringImport ? " during import" : "";
            Debug.Log($"<color=#5B9BD5>\ud83d\udd0d <b>[CodaSync]</b> Starting name validation{context}...</color>");

            List<string> allIssues = new List<string>();
            bool hasRowData = tablesRows != null;

            // Count non-type tables for progress calculation
            int tableCount = 0;
            for (int i = 0; i < structures.Length; i++) {
                if (!ImporterExporter.TypeTables.Contains(structures[i].UnmodifiedName))
                    tableCount++;
            }

            int processedTables = 0;

            for (int i = 0; i < structures.Length; i++) {
                if (ImporterExporter.TypeTables.Contains(structures[i].UnmodifiedName))
                    continue;

                // Show per-table progress only when called standalone (not during import)
                if (!duringImport) {
                    float progress = 0.6f + (0.4f * processedTables / Mathf.Max(1, tableCount));
                    if (EditorUtility.DisplayCancelableProgressBar("Coda Sync",
                        $"Checking columns of \"{structures[i].UnmodifiedName}\"...", progress)) {
                        EditorUtility.ClearProgressBar();
                        Debug.Log("<color=#E8A838>\u270b <b>[CodaSync]</b> Name validation cancelled by user.</color>");
                        return;
                    }
                }

                allIssues.AddRange(CodeGenerator.GetColumnNameIssuesForTable(structures[i]));

                if (hasRowData && i < tablesRows.Length && tablesRows[i] != null) {
                    if (!duringImport) {
                        float rowProgress = 0.6f + (0.4f * (processedTables + 0.5f) / Mathf.Max(1, tableCount));
                        if (EditorUtility.DisplayCancelableProgressBar("Coda Sync",
                            $"Checking rows of \"{structures[i].UnmodifiedName}\" ({tablesRows[i].Length} rows)...", rowProgress)) {
                            EditorUtility.ClearProgressBar();
                            Debug.Log("<color=#E8A838>\u270b <b>[CodaSync]</b> Name validation cancelled by user.</color>");
                            return;
                        }
                    }

                    allIssues.AddRange(CodeGenerator.GetRowNameIssuesForTable(structures[i], tablesRows[i]));
                }

                processedTables++;
            }

            EditorUtility.ClearProgressBar();

            if (allIssues.Count == 0) {
                string scope = hasRowData ? "table, column and row" : "table and column";
                Debug.Log($"<color=#6ECB63>\u2705 <b>[CodaSync]</b> All {scope} names are valid C# identifiers.</color>");
            } else {
                // Log duplicates first (red/critical), then other issues (orange/warning)
                foreach (string issue in allIssues) {
                    if (issue.StartsWith("DUPLICATE")) {
                        Debug.Log($"<color=#E85B5B>\u274c <b>[CodaSync]</b> {issue}</color>");
                    } else {
                        Debug.Log($"<color=#E8A838>\u26a0\ufe0f <b>[CodaSync]</b> {issue}</color>");
                    }
                }

                int duplicateCount = allIssues.FindAll(i => i.StartsWith("DUPLICATE")).Count;
                string summary = duplicateCount > 0
                    ? $"Found <b>{allIssues.Count}</b> name issue(s) including <color=#E85B5B><b>{duplicateCount} duplicate(s)</b></color>. Please fix them in your Coda doc."
                    : $"Found <b>{allIssues.Count}</b> name issue(s). Please fix them in your Coda doc.";
                Debug.Log($"<color=#E8A838>\ud83d\udccb <b>[CodaSync]</b> {summary}</color>");
            }
        }
        #endregion

        #region TableStructure
        public void CreateScriptFiles() {
            isCancelled = false;

            List<TableDescriptionData> tables = new List<TableDescriptionData>();
            int typeTableCount = 0;
            for (int i = 0; i < tableSelection.Count; i++) {
                // Always include Type Tables (Sprite, AudioClip, etc.) so that asset references
                // can be resolved even when the user imports only a subset of tables.
                bool isTypeTable = TypeTables.Contains(tableSelection[i].tableDescription.name);
                if (tableSelection[i].selected || isTypeTable) {
                    tables.Add(tableSelection[i].tableDescription);
                    if (isTypeTable) typeTableCount++;
                }
            }

            // Only report the tables the user actually ticked: type tables are hidden from the
            // UI, and counting them here ("8 selected" when the user ticked 3) reads like a bug.
            int selectedCount = tables.Count - typeTableCount;

            if (EditorUtility.DisplayCancelableProgressBar("Coda Table Import", $"Fetching structure for {selectedCount} selected tables...", 0)) {
                CancelImport();
                return;
            }

            (string, string) visibleOnlyParam = ("visibleOnly", getVisibleColumnsOnly.ToString());
            GetTablesStructure(tables, CreateScripts,visibleOnlyParam);
        }

        private void CreateScripts(TableStructure[] tableList) {
            if (isCancelled) return;

            if (EditorUtility.DisplayCancelableProgressBar("Coda Table Import", "Validating names...", .35f)) {
                CancelImport();
                return;
            }

            // Auto-validate names before code generation
            LogNameValidationReport(tableList, duringImport: true);

            CodeNamespace = codeNamespace;

            // Snapshot existing fields before code generation for the import report
            SnapshotExistingFields(tableList);

            CodeFiles[] codes = CodeGenerator.GetCodeFromTableStructures(tableList);

            for (int i = 0; i < tableList.Length; i++) {
                if (TypeTables.Contains(tableList[i].UnmodifiedName))
                    continue;

                float progress = 0.4f + (0.15f * i / tableList.Length);
                if (EditorUtility.DisplayCancelableProgressBar("Coda Table Import", $"Generating code for \"{tableList[i].UnmodifiedName}\"...", progress)) {
                    CancelImport();
                    return;
                }

                CreateSourceFile($"{tableList[i].Name}_DB", codes[i].databaseCode);
                CreateSourceFile(tableList[i].Name, codes[i].classCode);
            }

            JsonSerializerSettings settings = new JsonSerializerSettings();
            EditorPrefs.SetString(editorPrefKeyTablesStructure, JsonConvert.SerializeObject(tableList));

            AssetDatabase.Refresh();

            if (EditorApplication.isCompiling) {
                EditorPrefs.SetBool(editorPrefKeyShouldCreateInstances, true);

                if (EditorUtility.DisplayCancelableProgressBar("Coda Table Import", "Waiting for compilation...", .6f)) {
                    CancelImport();
                    EditorPrefs.SetBool(editorPrefKeyShouldCreateInstances, false);
                    return;
                }

                CompilationPipeline.assemblyCompilationFinished += OnCompilation;
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
            if (isCancelled) return;

            TableStructure[] structures = JsonConvert.DeserializeObject<TableStructure[]>(EditorPrefs.GetString(editorPrefKeyTablesStructure));
            string[] names = new string[structures.Length];

            if (EditorUtility.DisplayCancelableProgressBar("Coda Table Import", $"Fetching row data for {structures.Length} tables...", .7f)) {
                CancelImport();
                return;
            }

            for (int i = 0; i < structures.Length; i++) {
                names[i] = structures[i].UnmodifiedName;
            }
            requester.GetTablesData(documentId, names, (rqs) => OnTablesDataResponse(rqs, structures));
        }

        private void OnTablesDataResponse(UnityWebRequest[] dataRequests, TableStructure[] structures) {
            if (isCancelled) return;

            string basePath = GetPath();
            string instancesPath = $"{basePath}/Resources";
            CodeNamespace = codeNamespace;

            if (!Directory.Exists(instancesPath))
                Directory.CreateDirectory(instancesPath);

            TableRow[][] tablesRows = new TableRow[dataRequests.Length][];

            for (int i = 0; i < dataRequests.Length; i++) {
                // Abort rather than skip: creating instances from a partial row set would leave
                // lookups/databases inconsistent with what's actually in Coda.
                if (!TryGetResponseJson(dataRequests[i], out string json)) {
                    EditorUtility.ClearProgressBar();
                    Debug.LogWarning($"⚠️ <b>[CodaSync]</b> Empty/failed row data response for table \"{structures[i].UnmodifiedName}\": {dataRequests[i].error ?? "no content"}. Import aborted.");
                    return;
                }

                tablesRows[i] = JsonConvert.DeserializeObject<TableRowResponse>(json).items;
            }

            if (EditorUtility.DisplayCancelableProgressBar("Coda Table Import", "Creating ScriptableObject instances...", .85f)) {
                CancelImport();
                return;
            }

            ImportReport report = new ImportReport();
            BuildClassChangeReport(structures, report);
            InstanceGenerator.CreateAllInstances(structures, tablesRows, instancesPath, report);

            AssetDatabase.Refresh();

            EditorUtility.ClearProgressBar();
            report.LogToConsole();

            lastSyncDateString = $"{System.DateTime.UtcNow:R}";
            lastSyncLocalDateString = lastSyncDateString;
            EditorUtility.SetDirty(this);
        }

        /// <summary>
        /// Saves existing field names per table type to EditorPrefs so we can compare after recompilation.
        /// </summary>
        private void SnapshotExistingFields(TableStructure[] tableList) {
            Dictionary<string, List<string>> snapshot = new Dictionary<string, List<string>>();

            foreach (var table in tableList) {
                if (TypeTables.Contains(table.UnmodifiedName))
                    continue;

                string fullTypeName = $"{codeNamespace}.{table.Name}";
                System.Type existingType = null;
                foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies()) {
                    existingType = assembly.GetType(fullTypeName);
                    if (existingType != null) break;
                }

                if (existingType != null) {
                    var fields = existingType.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    List<string> fieldNames = new List<string>();
                    foreach (var f in fields)
                        fieldNames.Add(f.Name);
                    snapshot[table.Name] = fieldNames;
                }
            }

            EditorPrefs.SetString(editorPrefKeyPreviousFields, JsonConvert.SerializeObject(snapshot));
        }

        /// <summary>
        /// Compares new columns against the snapshot taken before code generation,
        /// and adds class change info to the report.
        /// </summary>
        private static void BuildClassChangeReport(TableStructure[] structures, ImportReport report) {
            string json = EditorPrefs.GetString(editorPrefKeyPreviousFields, "{}");
            Dictionary<string, List<string>> previousFields = JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(json);

            foreach (var structure in structures) {
                if (ImporterExporter.TypeTables.Contains(structure.UnmodifiedName))
                    continue;

                // Collect new field names from columns
                HashSet<string> newFields = new HashSet<string>();
                if (structure.Items != null) {
                    foreach (var col in structure.Items)
                        newFields.Add(col.Name);
                }

                if (!previousFields.ContainsKey(structure.Name)) {
                    // New class
                    report.warnings.Add($"New class generated: {structure.Name} ({newFields.Count} fields)");
                } else {
                    HashSet<string> oldFields = new HashSet<string>(previousFields[structure.Name]);
                    List<string> added = new List<string>();
                    List<string> removed = new List<string>();

                    foreach (string f in newFields) {
                        if (!oldFields.Contains(f)) added.Add(f);
                    }
                    foreach (string f in oldFields) {
                        if (!newFields.Contains(f)) removed.Add(f);
                    }

                    if (added.Count > 0 || removed.Count > 0) {
                        string changes = "";
                        if (added.Count > 0) changes += $"+{added.Count} ({string.Join(", ", added)})";
                        if (removed.Count > 0) {
                            if (changes.Length > 0) changes += ", ";
                            changes += $"-{removed.Count} ({string.Join(", ", removed)})";
                        }
                        report.warnings.Add($"Class updated: {structure.Name} \u2192 {changes}");
                    }
                }
            }

            // Cleanup
            EditorPrefs.DeleteKey(editorPrefKeyPreviousFields);
        }

        private static void CancelImport() {
            isCancelled = true;
            EditorUtility.ClearProgressBar();
            Debug.Log("<color=#E8A838>\u270b <b>[CodaSync]</b> Import cancelled by user.</color>");
        }
        #endregion
    }
}
