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
        // Where versions before 1.7.0 stored the API token, in clear inside the asset. Only read by
        // CodaTokenMigration, which moves the token to this computer and empties the field. It keeps this
        // name so the migration can still read an asset saved by any older version.
        [SerializeField, HideInInspector] private string _apiToken;

        private const string BearerPrefix = "Bearer ";

        public string APIBasePath { get => _apiBasePath; }

        /// <summary>
        /// The API token stored on this computer for this Requester, or "" when there is none. Each team
        /// member sets up their own: the token never goes into the asset.
        /// </summary>
        public string APIToken {
            get {
                CodaTokenMigration.MigrateIfNeeded(this);
                return CodaTokenStore.Get(this);
            }
        }

        public bool HasToken { get => APIToken.Length > 0; }

        internal string LegacyToken { get => _apiToken; }



        /// <summary>
        /// Asks Coda who the token belongs to. On success, the answer (name, email, token name, whether the
        /// token is restricted) is recorded for the Requester's inspector.
        /// </summary>
        public void PerformConnectionTest(System.Action<UnityWebRequest> callback) {
            UnityWebRequest req = CreateBaseGetRequest();
            AddRequestFields(req, "whoami");
            string token = CodaTokenStore.Get(this);
            SendRequest(req, (response) => {
                RecordIdentity(response, token);
                callback(response);
            }); // https://coda.io/apis/v1/whoami
        }

        private void RecordIdentity(UnityWebRequest req, string token) {
            if (req.result != UnityWebRequest.Result.Success || string.IsNullOrEmpty(req.downloadHandler.text))
                return;

            try {
                WhoAmIResponse whoAmI = JsonConvert.DeserializeObject<WhoAmIResponse>(req.downloadHandler.text);
                CodaTokenStatus.RecordValid(this, token, new TokenIdentity {
                    name = whoAmI.name,
                    email = whoAmI.loginId,
                    tokenName = whoAmI.tokenName,
                    scoped = whoAmI.scoped
                });
            } catch (JsonException) {
                // The token still works, the inspector just can't say whose it is
            }
        }

        private struct WhoAmIResponse {
            public string name;
            public string loginId;
            public string tokenName;
            public bool scoped;
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
            req.SetRequestHeader("Authorization", BearerPrefix + APIToken);
            // Pin encodings libcurl can decode: without this, Coda's CDN may answer in brotli (br),
            // which Unity's libcurl doesn't support → "Curl error 61" and an empty response body.
            req.SetRequestHeader("Accept-Encoding", "gzip, deflate");
            return req;
        }
        private UnityWebRequest CreateBasePostRequest(string data) {
            UnityWebRequest req = UnityWebRequest.PostWwwForm(_apiBasePath, "");
            req.SetRequestHeader("Authorization", BearerPrefix + APIToken);
            // Same brotli pinning as CreateBaseGetRequest (Curl error 61)
            req.SetRequestHeader("Accept-Encoding", "gzip, deflate");

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

        // Requests are built and sent within the same call, so the token stored right now is the one their
        // Authorization header carries. It travels with the request to record what Coda says about it.
        private void SendRequest(UnityWebRequest req, System.Action<UnityWebRequest> callback) {
            EditorCoroutineUtility.StartCoroutine(WaitRequestResponse(req, CodaTokenStore.Get(this), callback), this);
        }

        private void SendRequests(UnityWebRequest[] reqs, System.Action<UnityWebRequest[]> callback) {
            EditorCoroutineUtility.StartCoroutine(WaitRequestsResponse(reqs, CodaTokenStore.Get(this), callback), this);
        }
        #endregion


        private IEnumerator WaitRequestResponse(UnityWebRequest req, string token, System.Action<UnityWebRequest> callback) {
            yield return req.SendWebRequest();
            LogRequestResult(req);
            RecordTokenStatus(req, token);
            callback(req);
        }

        private IEnumerator WaitRequestsResponse(UnityWebRequest[] reqs, string token, System.Action<UnityWebRequest[]> callback) {
            foreach (var req in reqs) {
                req.SendWebRequest();
            }

            yield return new WaitUntil(() => AreRequestsDone(reqs));

            foreach (var req in reqs) {
                LogRequestResult(req);
                RecordTokenStatus(req, token);
            }

            callback(reqs);
        }

        /// <summary>
        /// Records what a response says about the token it was sent with: a 401 means Coda rejected it, any
        /// success means it works. Recorded against that token rather than the current one, which the user
        /// may have replaced while the request was on its way.
        /// </summary>
        private void RecordTokenStatus(UnityWebRequest req, string token) {
            if (token.Length == 0)
                return;

            if (req.responseCode == 401)
                CodaTokenStatus.RecordRejected(this, token);
            else if (req.result == UnityWebRequest.Result.Success)
                CodaTokenStatus.RecordValid(this, token);
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
