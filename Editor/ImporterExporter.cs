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
        public void GetTableList(Action<TableDescriptionData[]> callback) {
            if (!docIdFound) {
                EditorUtility.DisplayDialog("Import setup", "Can't find documentID. Check your Coda document URL field", "OK");
                return;
            }

            if (requester == null) {
                EditorUtility.DisplayDialog("Import setup", "No Requester setup", "OK");
                return;
            }

            documentId = GetDocumentIdFromURL();
            requester.GetTableListOfDoc(documentId, (req) => OnTableListResponse(req, callback));
        }

        private void OnTableListResponse(UnityWebRequest req, Action<TableDescriptionData[]> callback) {
            string jsonString = req.downloadHandler.text;
            callback(JsonConvert.DeserializeObject<TableListResponse>(jsonString).items);
        }


        public void GetTablesStructure(List<TableDescriptionData> tables, Action<TableStructure[]> response,  (string, string) visibleOnlyParam = default) {
            if (tables.Count == 0) {
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
