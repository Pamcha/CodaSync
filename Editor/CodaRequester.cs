using System.Collections;
using Unity.EditorCoroutines.Editor;
using Unity.Plastic.Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;
using static System.Text.Encoding;

namespace Com.Pamcha.CodaSync {
    [CreateAssetMenu(fileName = "NewCodaRequester", menuName = "CodaSync/Requester")]
    public class CodaRequester : ScriptableObject {
        [Header("DEBUG")]
        [SerializeField] protected bool logResponses = false;
        [Space(20)]
        [SerializeField] private string _apiBasePath = "https://coda.io/apis/v1";
        [SerializeField] private string _apiToken;

        public string APIBasePath { get => _apiBasePath; }
        public string APIToken { get => _apiToken; }



        public void PerformConnectionTest(System.Action<UnityWebRequest> callback) {
            UnityWebRequest req = CreateBaseGetRequest();
            AddRequestFields(req, "whoami");
            SendRequest(req, callback); // https://coda.io/apis/v1/whoami
        }

        #region GETRequests
        public void GetTableListOfDoc(string documentId, System.Action<UnityWebRequest> callback) {
            UnityWebRequest req = CreateBaseGetRequest();
            AddRequestFields(req, "docs", documentId, "tables");
            SendRequest(req, callback); // https://coda.io/apis/v1/docs/{documentId}/tables 
        }

        public void GetTablesStructure(string documentId, string[] tablesIdOrName, System.Action<UnityWebRequest[]> callback, (string, string) visibleOnlyParam = default) {
            UnityWebRequest[] reqs = new UnityWebRequest[tablesIdOrName.Length];

            for (int i = 0; i < reqs.Length; i++) {
                reqs[i] = CreateBaseGetRequest();
                AddRequestFields(reqs[i], "docs", documentId, "tables", tablesIdOrName[i], "columns");
                if(visibleOnlyParam != default)
                    AddQueryParameters(reqs[i],visibleOnlyParam);
            }
            //Debug.Log(reqs.url);
            SendRequests(reqs, callback); // https://coda.io/apis/v1/docs/{documentId}/tables/{tableIdOrName}/columns
        }

        public void GetTablesData(string documentId, string[] tablesIdOrName, System.Action<UnityWebRequest[]> callback) {
            UnityWebRequest[] reqs = new UnityWebRequest[tablesIdOrName.Length];

            for (int i = 0; i < reqs.Length; i++) {
                reqs[i] = CreateBaseGetRequest();
                AddRequestFields(reqs[i], "docs", documentId, "tables", tablesIdOrName[i], "rows");
            }

            SendRequests(reqs, callback); // https://coda.io/apis/v1/docs/{documentId}/tables/{tableIdOrName}/rows
        }
        #endregion

        #region POSTRequests
        public void SetTableRows (string documentId, string tableIdOrName, RowEdit rowsEdit, System.Action<UnityWebRequest> callback) {
            string param = JsonConvert.SerializeObject(rowsEdit);
            UnityWebRequest req = CreateBasePostRequest(param);

            AddRequestFields(req, "docs", documentId, "tables", tableIdOrName, "rows");

            SendRequest(req, callback);
        }
        #endregion

        private void LogRequestResult(UnityWebRequest req) {
            if (!logResponses)
                return;

            if (req.result == UnityWebRequest.Result.Success) {
                Debug.Log($"Request : {req.url}\nResponse : {req.downloadHandler.text}");
            } else {
                Debug.LogError($"Request : {req.url}\nResponse Code {req.responseCode}\n{req.error}");
            }
        }

        #region REQUEST_CONSTRUCTORS
        private UnityWebRequest CreateBaseGetRequest() {
            UnityWebRequest req = UnityWebRequest.Get(_apiBasePath);
            req.SetRequestHeader("Authorization", $"Bearer {_apiToken}");
            return req;
        }
        private UnityWebRequest CreateBasePostRequest(string data) {
            UnityWebRequest req = UnityWebRequest.PostWwwForm(_apiBasePath, "");
            req.SetRequestHeader("Authorization", $"Bearer {_apiToken}");

            req.uploadHandler = new UploadHandlerRaw(UTF8.GetBytes(data)) {
                contentType = "application/json"
            };

            return req;
        }

        private void AddRequestFields(UnityWebRequest req, params string[] fields) {
            foreach (var field in fields) {
                req.url = $"{req.url}/{field}";
            }
        }

        private void AddQueryParameters(UnityWebRequest req, params (string Key, string Value)[] pairs) {
             req.url = $"{req.url}?";
             for (int i = 0; i < pairs.Length; i++)
             {
                //(string Key, string Value) pair = pairs[i];
                req.url = $"{req.url}{pairs[i].Key}={pairs[i].Value}";
             }
        }

        private void SendRequest(UnityWebRequest req, System.Action<UnityWebRequest> callback) {
            EditorCoroutineUtility.StartCoroutine(WaitRequestResponse(req, callback), this);
        }

        private void SendRequests(UnityWebRequest[] reqs, System.Action<UnityWebRequest[]> callback) {
            EditorCoroutineUtility.StartCoroutine(WaitRequestsResponse(reqs, callback), this);
        }
        #endregion


        private IEnumerator WaitRequestResponse(UnityWebRequest req, System.Action<UnityWebRequest> callback) {
            yield return req.SendWebRequest();
            LogRequestResult(req);
            callback(req);
        }

        private IEnumerator WaitRequestsResponse(UnityWebRequest[] reqs, System.Action<UnityWebRequest[]> callback) {
            foreach (var req in reqs) {
                req.SendWebRequest();
            }

            yield return new WaitUntil(() => AreRequestsDone(reqs));

            foreach (var req in reqs) {
                LogRequestResult(req);
            }

            callback(reqs);
        }

        private bool AreRequestsDone(UnityWebRequest[] reqs) {
            bool completed = true;

            foreach (var req in reqs) {
                completed &= req.isDone;
            }

            return completed;
        }
    }
}
