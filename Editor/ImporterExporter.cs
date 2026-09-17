using Com.Pamcha.Common.ReadOnlyField;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Unity.Plastic.Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Com.Pamcha.CodaSync {
    public abstract class ImporterExporter : ScriptableObject {
        [SerializeField] protected CodaRequester requester;
        [SerializeField] protected string documentURL;
        [SerializeField, ReadOnly] protected string documentId;
        [Space]
        [SerializeField, HideInInspector] protected string lastSyncDateString;
        [SerializeField, ReadOnly] public string lastSyncLocalDateString;

        protected bool docIdFound { get; private set; } = false;

        public CodaRequester Requester { get => requester; }

        // How the last table-list request failed, if it did. Lets the inspector explain a document the
        // token can't reach. Only describes what Coda answered during this session.
        private CodaApiError.Kind lastTableListError = CodaApiError.Kind.None;
        internal CodaApiError.Kind LastTableListError { get => lastTableListError; }


        public static readonly List<string> TypeTables = new List<string>{
            "AudioClip",
            "Sprite",
            "Material",
            "AnimatorController",
            "Animation",
            "GameObject"
        };

        protected virtual void OnValidate() {
            documentId = GetDocumentIdFromURL();
        }

        #region GETs
        /// <param name="userInitiated">
        /// False for the refresh that runs on its own when an importer is inspected: it opens no dialog,
        /// and doesn't send a token Coda already rejected.
        /// </param>
        public void GetTableList(Action<TableDescriptionData[]> callback, bool userInitiated = true) {
            if (!CanReachCoda(userInitiated))
                return;

            documentId = GetDocumentIdFromURL();
            requester.GetTableListOfDoc(documentId, (req) => OnTableListResponse(req, callback, userInitiated));
        }

        private void OnTableListResponse(UnityWebRequest req, Action<TableDescriptionData[]> callback, bool userInitiated) {
            if (!TryGetResponseJson(req, out string jsonString)) {
                EditorUtility.ClearProgressBar();
                lastTableListError = CodaApiError.Classify(req);
                CodaApiError.Report(req, requester, "this document", "", showDialog: userInitiated);
                return;
            }

            lastTableListError = CodaApiError.Kind.None;
            callback(JsonConvert.DeserializeObject<TableListResponse>(jsonString).items);
        }

        /// <summary>
        /// Checks what a request to Coda needs before sending one: a document id, a Requester, and an API
        /// token on this computer. A request that fires on its own also stops at a token Coda already
        /// rejected instead of sending it again. Dialogs only open when the user clicked for the request.
        /// Call it before showing a progress bar, so a failed check never leaves one on screen.
        /// </summary>
        protected bool CanReachCoda(bool userInitiated) {
            if (!docIdFound) {
                if (userInitiated)
                    EditorUtility.DisplayDialog("Import setup", "Can't find documentID. Check your Coda document URL field", "OK");
                return false;
            }

            if (requester == null) {
                if (userInitiated)
                    EditorUtility.DisplayDialog("Import setup", "No Requester setup", "OK");
                return false;
            }

            if (!requester.HasToken) {
                if (userInitiated && EditorUtility.DisplayDialog("Coda Sync", $"No Coda API token on this machine for \"{requester.name}\". Each team member sets up their own token, in the Requester.", "Set up token", "Cancel"))
                    CodaSyncGUI.SelectRequester(requester);
                return false;
            }

            if (!userInitiated && CodaTokenStatus.GetState(requester) == TokenState.Rejected)
                return false;

            return true;
        }

        /// <summary>
        /// Guard shared by every response handler: response structs are non-nullable, so
        /// DeserializeObject throws on an empty body instead of returning null. An empty/failed
        /// response can happen on rate-limit (429), timeout, token cooldown, a network hiccup,
        /// or an unsupported content encoding (Curl error 61).
        /// </summary>
        protected static bool TryGetResponseJson(UnityWebRequest req, out string json) {
            if (req.result != UnityWebRequest.Result.Success || string.IsNullOrEmpty(req.downloadHandler.text)) {
                json = null;
                return false;
            }

            json = req.downloadHandler.text;
            return true;
        }


        public void GetTablesStructure(List<TableDescriptionData> tables, Action<TableStructure[]> response,  (string, string) visibleOnlyParam = default) {
            if (tables.Count == 0) {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Table selection", "There is no table selected to import", "OK");
                return;
            }

            string[] tablesName = new string[tables.Count];

            for (int i = 0; i < tablesName.Length; i++) {
                tablesName[i] = tables[i].name;
            }

            requester.GetTablesStructure(documentId, tablesName, (req) => OnTableStructureResponse(req, tables.ToArray(), response), visibleOnlyParam);
        }

        private void OnTableStructureResponse(UnityWebRequest[] tableRequests, TableDescriptionData[] tableList, Action<TableStructure[]> callback) {
            // Abort on the first failed/empty response rather than skipping the table: generating
            // classes/instances from a partial table set could leave lookups pointing at nothing.
            for (int i = 0; i < tableRequests.Length; i++) {
                if (!TryGetResponseJson(tableRequests[i], out _)) {
                    EditorUtility.ClearProgressBar();
                    CodaApiError.Report(tableRequests[i], requester, $"table \"{tableList[i].name}\"", "Operation aborted.", showDialog: true);
                    return;
                }
            }

            TableStructure[] structures = new TableStructure[tableRequests.Length];

            for (int i = 0; i < structures.Length; i++) {
                structures[i] = JsonConvert.DeserializeObject<TableStructure>(tableRequests[i].downloadHandler.text);

                structures[i].Id = tableList[i].id;
                structures[i].UnmodifiedName = tableList[i].name;
                structures[i].Type = tableList[i].type;
                structures[i].TableType = tableList[i].tableType;

            }

            callback(structures);
        }
        #endregion


        #region UTILS
        protected string GetDocumentIdFromURL() {
            Regex rx = new Regex(@"_d([\w-]*)\/");
            MatchCollection matches = rx.Matches(documentURL);


            if (matches.Count == 0 || matches[0].Groups.Count <= 1) {
                docIdFound = false;
                return "Can't find DocId";
            } else {
                GroupCollection groups = matches[0].Groups;
                docIdFound = true;
                return groups[1].Value;
            }
        }

        protected string GetPath() {
            Regex rx = new Regex(@".*(?=\/)");
            MatchCollection matches = rx.Matches(AssetDatabase.GetAssetPath(this));


            if (matches.Count == 0)
                return "";
            else {
                return matches[0].Value;
            }
        }
        #endregion


        #region ResponsesStructure
        [System.Serializable]
        public struct TableListResponse {
            public TableDescriptionData[] items;
            public string href;
        }
        [System.Serializable]
        public struct TableDescriptionData {
            public string id;
            public string type;
            public string tableType;
            public string name;
        }
        [System.Serializable]
        public struct TableRowResponse {
            public TableRow[] items;
        }

        [System.Serializable]
        public class TableSelection {
            public TableDescriptionData tableDescription;
            public bool selected = false;
        }
        #endregion
    }

}
