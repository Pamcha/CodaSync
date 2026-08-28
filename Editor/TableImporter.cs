using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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

        // Tables the user chose to hide from the import list: Coda views, or tables that only exist
        // to feed a select somewhere in the doc. Stored by table id rather than by name, so hiding
        // survives a rename in Coda and lines up with the id-based re-pairing in OnUpdateTableList.
        [SerializeField, HideInInspector] private List<string> ignoredTableIds = new List<string>();
        public IReadOnlyList<string> IgnoredTableIds => ignoredTableIds;

        /// <summary>
        /// Row ids seen in a table at the last sync. An existing asset whose id is missing from this
        /// set belonged to a row that no longer exists in Coda.
        /// </summary>
        [System.Serializable]
        public class SyncedTableRowIds {
            public string tableName;      // Coda table name, as displayed in the doc
            public string generatedName;  // sanitized name: instance folder and generated class
            public List<string> rowIds = new List<string>();
        }

        // Cached so the orphan cleanup window can recompute what is orphaned without hitting the
        // network: it opens on this data, and only calls Coda when the user asks for a re-fetch.
        // Refreshed at the end of every import, for the tables that were part of it.
        [SerializeField, HideInInspector] private List<SyncedTableRowIds> syncedRowIds = new List<SyncedTableRowIds>();
        [SerializeField, HideInInspector] private string rowIdCacheDateString;

        /// <summary>
        /// Field signature of a table's generated class, as of the last import that actually wrote its
        /// assets. Compared against the freshly compiled class to tell whether the assets on disk still
        /// carry the current schema.
        /// </summary>
        [System.Serializable]
        public class SyncedTableSchema {
            public string generatedName;                      // sanitized name: instance folder and generated class
            public List<string> fields = new List<string>();  // "name:Type.FullName", sorted
        }

        // Schema baseline per table, stored on the asset so it is committed with the project and shared
        // by the team. Only advanced once the assets have actually been rewritten (see
        // CacheSyncedSchemas), so an import that dies between code generation and instance creation
        // leaves it alone and the next import redoes the work instead of baking the drift in.
        [SerializeField, HideInInspector] private List<SyncedTableSchema> syncedSchemas = new List<SyncedTableSchema>();

        public IReadOnlyList<SyncedTableRowIds> SyncedRowIds => syncedRowIds;
        public string RowIdCacheDateString => rowIdCacheDateString;
        public string InstancesPath => $"{GetPath()}/Resources";
        public string CodeNamespaceSetting => codeNamespace;

        // Doc id of the last successful table-list fetch. Persisted so OnValidate can tell a real
        // document change apart from the many editor events that also fire it (see OnValidate).
        [SerializeField, HideInInspector] private string lastTableListDocId;

        public static string CodeNamespace { get; private set; }
        private const string editorPrefKeyShouldCreateInstances = "Com.Pamcha.CodaImporter.ShouldCreateInstances";
        private const string editorPrefKeyTablesStructure = "Com.Pamcha.CodaImporter.TablesStructure";

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

        #region RowIdCache
        /// <summary>
        /// Records the row ids of every table that took part in this import, so the orphan cleanup
        /// window can tell a deleted row from a live one without going back to the network. Tables
        /// left out of the import keep the ids of the last one they were in.
        /// </summary>
        private void CacheSyncedRowIds(TableStructure[] structures, TableRow[][] tablesRows) {
            for (int i = 0; i < structures.Length && i < tablesRows.Length; i++) {
                if (TypeTables.Contains(structures[i].UnmodifiedName))
                    continue;

                SyncedTableRowIds cached = syncedRowIds.Find(t => t.generatedName == structures[i].Name);
                if (cached == null) {
                    cached = new SyncedTableRowIds();
                    syncedRowIds.Add(cached);
                }

                cached.tableName = structures[i].UnmodifiedName;
                cached.generatedName = structures[i].Name;
                cached.rowIds.Clear();

                for (int j = 0; j < tablesRows[i].Length; j++) {
                    if (!string.IsNullOrEmpty(tablesRows[i][j].Id))
                        cached.rowIds.Add(tablesRows[i][j].Id);
                }
            }

            rowIdCacheDateString = $"{System.DateTime.UtcNow:R}";
        }

        /// <summary>
        /// Re-fetches the row ids of the already-cached tables from Coda without running an import.
        /// Lets the cleanup window check its verdict against the live document when its cache is old.
        /// </summary>
        public void RefreshRowIdCache(System.Action onDone) {
            documentId = GetDocumentIdFromURL();

            if (requester == null || !docIdFound) {
                EditorUtility.DisplayDialog("Coda Sync", "Can't reach Coda: check the Requester and the document URL on this importer.", "OK");
                onDone?.Invoke();
                return;
            }

            if (syncedRowIds.Count == 0) {
                EditorUtility.DisplayDialog("Coda Sync", "No table cached yet. Run an import first.", "OK");
                onDone?.Invoke();
                return;
            }

            string[] names = new string[syncedRowIds.Count];
            for (int i = 0; i < names.Length; i++) {
                names[i] = syncedRowIds[i].tableName;
            }

            EditorUtility.DisplayProgressBar("Coda Sync", $"Re-fetching row ids ({names.Length} tables)...", 0f);
            requester.GetTablesData(documentId, names, (requests) => OnRowIdRefreshResponse(requests, onDone));
        }

        private void OnRowIdRefreshResponse(UnityWebRequest[] dataRequests, System.Action onDone) {
            EditorUtility.ClearProgressBar();

            // Abort on the first failed response instead of caching a partial answer: a table whose
            // rows didn't come back would have all of its assets flagged as orphaned, which is
            // exactly the mistake this window must never make. The old cache is left untouched.
            for (int i = 0; i < dataRequests.Length; i++) {
                if (!TryGetResponseJson(dataRequests[i], out _)) {
                    string tableName = i < syncedRowIds.Count ? syncedRowIds[i].tableName : "unknown";
                    Debug.LogWarning($"⚠️ <b>[CodaSync]</b> Empty/failed row data response for table \"{tableName}\": {dataRequests[i].error ?? "no content"}. Row ids were left as they were (the table may have been deleted or renamed in Coda).");
                    onDone?.Invoke();
                    return;
                }
            }

            for (int i = 0; i < dataRequests.Length && i < syncedRowIds.Count; i++) {
                TableRow[] rows = JsonConvert.DeserializeObject<TableRowResponse>(dataRequests[i].downloadHandler.text).items;

                syncedRowIds[i].rowIds.Clear();
                for (int j = 0; j < rows.Length; j++) {
                    if (!string.IsNullOrEmpty(rows[j].Id))
                        syncedRowIds[i].rowIds.Add(rows[j].Id);
                }
            }

            rowIdCacheDateString = $"{System.DateTime.UtcNow:R}";
            EditorUtility.SetDirty(this);
            onDone?.Invoke();
        }
        #endregion

        #region HiddenTables
        /// <summary>
        /// True when the table was hidden from the import list by the user.
        /// </summary>
        public bool IsTableIgnored(string tableId) {
            return !string.IsNullOrEmpty(tableId) && ignoredTableIds.Contains(tableId);
        }

        /// <summary>
        /// Hides a table from the import list. Hiding also deselects it, so a table that was ticked
        /// before being hidden can't slip into the next import. Type Tables can't be hidden: they
        /// are already filtered out of the UI and must stay selected to resolve asset references.
        /// </summary>
        public void HideTable(TableSelection selection) {
            if (selection == null || TypeTables.Contains(selection.tableDescription.name))
                return;

            string tableId = selection.tableDescription.id;
            if (string.IsNullOrEmpty(tableId)) return;

            if (!ignoredTableIds.Contains(tableId))
                ignoredTableIds.Add(tableId);

            selection.selected = false;
            EditorUtility.SetDirty(this);
        }

        /// <summary>
        /// Puts a hidden table back in the import list, deselected. Takes an id rather than a
        /// TableSelection so a table hidden then deleted in Coda can still be cleared from the list.
        /// </summary>
        public void ShowTable(string tableId) {
            if (!ignoredTableIds.Remove(tableId)) return;

            EditorUtility.SetDirty(this);
        }
        #endregion

        #region CheckNames
        /// <summary>
        /// Fetches table structures and row data for selected tables, then logs a full name validation report.
        /// </summary>
        public void CheckNames() {
            List<TableDescriptionData> tables = new List<TableDescriptionData>();
            for (int i = 0; i < tableSelection.Count; i++) {
                if (tableSelection[i].selected && !IsTableIgnored(tableSelection[i].tableDescription.id))
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
                // Hiding a table already deselects it; re-checking here means no inconsistent state
                // can ever send a hidden table to import.
                bool isSelected = tableSelection[i].selected && !IsTableIgnored(tableSelection[i].tableDescription.id);
                if (isSelected || isTypeTable) {
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
            HashSet<string> tablesToRewrite = DiffTableSchemas(structures, report);
            InstanceGenerator.CreateAllInstances(structures, tablesRows, instancesPath, report, tablesToRewrite);
            CacheSyncedRowIds(structures, tablesRows);
            CacheSyncedSchemas(structures);

            AssetDatabase.Refresh();

            EditorUtility.ClearProgressBar();
            report.LogToConsole();

            lastSyncDateString = $"{System.DateTime.UtcNow:R}";
            lastSyncLocalDateString = lastSyncDateString;
            EditorUtility.SetDirty(this);
            // Flush now rather than waiting for the user to save the project. The schema baseline is
            // what tells the next import whether the assets on disk still match their class: left in
            // memory, it dies with the editor session and the next import rewrites every table again.
            // The row id cache and the sync dates ride along.
            AssetDatabase.SaveAssetIfDirty(this);
        }

        /// <summary>
        /// Resolves a table's generated class in the currently loaded assemblies. Returns null when the
        /// table has never been generated, or when the namespace setting changed.
        /// </summary>
        private System.Type FindGeneratedType(string sanitizedTableName) {
            string fullTypeName = $"{codeNamespace}.{sanitizedTableName}";

            foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies()) {
                System.Type type = assembly.GetType(fullTypeName);
                if (type != null) return type;
            }

            return null;
        }

        /// <summary>
        /// Signature of everything Unity serializes on a generated class: public fields, plus private
        /// ones carrying [SerializeField], which is how __codaRowId is emitted. Both the name and the
        /// type are included, so a retyped column, which keeps its name, still reads as a schema change.
        /// Sorted, so the comparison does not depend on the order reflection returns fields in.
        /// </summary>
        private static List<string> GetSchemaSignature(System.Type type) {
            List<string> signature = new List<string>();
            FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            foreach (FieldInfo field in fields) {
                if (field.IsNotSerialized)
                    continue;
                if (!field.IsPublic && !field.IsDefined(typeof(SerializeField), true))
                    continue;

                signature.Add($"{field.Name}:{field.FieldType.FullName}");
            }

            signature.Sort(System.StringComparer.Ordinal);
            return signature;
        }

        /// <summary>
        /// Compares each table's freshly compiled class against the schema baseline stored on this
        /// importer, fills the import report, and returns the tables whose assets must ALL be rewritten.
        ///
        /// Why this matters: a key absent from an asset's YAML does not fall back to the Unity default
        /// for its type, it stays at the C# default, so a string field added to the class reads as null
        /// rather than "" on every asset that was not rewritten. The row level diff in
        /// InstanceGenerator.SetAllFields cannot catch that: it compares two in-memory objects under the
        /// same current class, the YAML on disk never enters the comparison.
        ///
        /// The comparison is reflection against reflection, the same vocabulary on both sides, since the
        /// baseline was itself written from a generated type. Comparing the class against the Coda
        /// columns instead would need the column to C# type mapping that lives in CodeGenerator, and
        /// would report the generated __codaRowId as a removed field on every table.
        ///
        /// Neither the mtime nor the hash of the .cs is usable here: a resync can rewrite a generated
        /// file with identical content (mtime bumped, no diff at all), and conversely a class can change
        /// without the sync ever running.
        /// </summary>
        private HashSet<string> DiffTableSchemas(TableStructure[] structures, ImportReport report) {
            HashSet<string> toRewrite = new HashSet<string>();

            foreach (var structure in structures) {
                if (TypeTables.Contains(structure.UnmodifiedName))
                    continue;

                System.Type generatedType = FindGeneratedType(structure.Name);
                if (generatedType == null)
                    continue;

                List<string> current = GetSchemaSignature(generatedType);
                SyncedTableSchema baseline = syncedSchemas.Find(schema => schema.generatedName == structure.Name);

                // No baseline: the assets on disk were written by a version that did not track schemas,
                // or this table has not been synced since. Their schema cannot be trusted, so rewrite the
                // table once. A brand new table lands here too, harmlessly: all of its assets are created
                // anyway, and a created asset is always written.
                if (baseline == null) {
                    toRewrite.Add(structure.Name);
                    report.schemaChanges.Add(new ImportReport.SchemaChangeInfo {
                        tableName = structure.UnmodifiedName,
                        baselineKnown = false
                    });
                    continue;
                }

                HashSet<string> previousFields = new HashSet<string>(baseline.fields);
                HashSet<string> currentFields = new HashSet<string>(current);

                List<string> added = current.Where(field => !previousFields.Contains(field)).ToList();
                List<string> removed = baseline.fields.Where(field => !currentFields.Contains(field)).ToList();

                if (added.Count == 0 && removed.Count == 0)
                    continue;

                toRewrite.Add(structure.Name);
                report.schemaChanges.Add(new ImportReport.SchemaChangeInfo {
                    tableName = structure.UnmodifiedName,
                    baselineKnown = true,
                    added = added,
                    removed = removed
                });
            }

            return toRewrite;
        }

        /// <summary>
        /// Advances the schema baseline for the tables that took part in this import. Called after
        /// CreateAllInstances has returned, never before: the baseline must describe the schema the
        /// assets on disk were actually written with. An import cancelled or crashed between code
        /// generation and instance creation therefore leaves it untouched, and the next import redoes
        /// the rewrite rather than mistaking the drift for an up to date table.
        ///
        /// Tables absent from this import keep their own baseline, stale but truthful: the diff will
        /// fire the day they are imported again.
        /// </summary>
        private void CacheSyncedSchemas(TableStructure[] structures) {
            foreach (var structure in structures) {
                if (TypeTables.Contains(structure.UnmodifiedName))
                    continue;

                System.Type generatedType = FindGeneratedType(structure.Name);
                if (generatedType == null)
                    continue;

                SyncedTableSchema cached = syncedSchemas.Find(schema => schema.generatedName == structure.Name);
                if (cached == null) {
                    cached = new SyncedTableSchema();
                    syncedSchemas.Add(cached);
                }

                cached.generatedName = structure.Name;
                cached.fields = GetSchemaSignature(generatedType);
            }
        }

        private static void CancelImport() {
            isCancelled = true;
            EditorUtility.ClearProgressBar();
            Debug.Log("<color=#E8A838>\u270b <b>[CodaSync]</b> Import cancelled by user.</color>");
        }
        #endregion
    }
}
