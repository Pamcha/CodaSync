using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Com.Pamcha.CodaSync {
    /// <summary>
    /// Where each team member sets up their own API token for this Requester. The token is stored on this
    /// computer (see CodaTokenStore), never in the asset, and checked against Coda as soon as it is pasted.
    /// </summary>
    [CustomEditor(typeof(CodaRequester))]
    public class CodaRequesterEditor : Editor {
        private const string HowToGetTokenUrl = "https://coda.io/@pamcha/coda-sync/find-your-api-key-2";

        // Pasting fires one change, typing fires many: let the field settle before asking Coda
        private const double CheckDelay = 1.0;
        // Past this, a check is given up on, so a request that never answers can't grey out the button for good
        private const double CheckTimeout = 20.0;

        private CodaRequester requester;
        private string tokenField = "";
        private double checkAt = -1;
        private bool checking;
        private double checkStartedAt;
        // Tells the answer of a check apart from a stale one, once a check was given up on
        private int checkCount;
        // Shown under the status, so a check that confirms what was already displayed still shows it happened
        private DateTime lastCheckTime;
        // Why the last check couldn't tell whether the token works (network, rate limit...), if it couldn't
        private string checkProblem;

        private void OnEnable() {
            requester = (CodaRequester)target;
            // Reading the token also moves one still stored in the asset by an older version
            tokenField = requester.APIToken;

            CodaTokenStatus.Changed += Repaint;
            EditorApplication.update += OnEditorUpdate;

            // Once Coda has said whose token it is, SessionState remembers it: one whoami per token per session.
            // A token Coda rejected isn't sent again on its own, "Test connection" is there for that.
            if (requester.HasToken
                && CodaTokenStatus.GetState(requester) != TokenState.Rejected
                && !CodaTokenStatus.TryGetIdentity(requester, out _))
                CheckToken();
        }

        private void OnDisable() {
            CodaTokenStatus.Changed -= Repaint;
            EditorApplication.update -= OnEditorUpdate;
        }

        public override void OnInspectorGUI() {
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "_apiToken");
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space(10);

            if (!CodaTokenStore.CanStore(requester)) {
                EditorGUILayout.HelpBox("Save this Requester as an asset before setting its token.", MessageType.Info);
                return;
            }

            DrawStatus();
            DrawTokenField();
            DrawButtons();
        }

        private void DrawStatus() {
            if (!requester.HasToken) {
                EditorGUILayout.HelpBox("No API token on this machine. Each team member uses their own Coda token: paste yours below. It is stored in this computer's Unity preferences, never in the project.", MessageType.Warning);
                return;
            }

            if (checking) {
                EditorGUILayout.HelpBox("Checking token...", MessageType.None);
                return;
            }

            TokenIdentity identity = null;

            switch (CodaTokenStatus.GetState(requester)) {
                case TokenState.Valid:
                    if (CodaTokenStatus.TryGetIdentity(requester, out identity))
                        EditorGUILayout.HelpBox($"Connected as {identity.name} ({identity.email}), token \"{identity.tokenName}\".", MessageType.Info);
                    else
                        EditorGUILayout.HelpBox("Connected to Coda.", MessageType.Info);
                    break;

                case TokenState.Rejected:
                    EditorGUILayout.HelpBox("Coda rejected this token: invalid, expired or revoked. Paste a new one below.", MessageType.Error);
                    break;

                default:
                    if (!string.IsNullOrEmpty(checkProblem))
                        EditorGUILayout.HelpBox($"Couldn't check the token: {checkProblem}", MessageType.Warning);
                    else if (checkAt > 0)
                        EditorGUILayout.HelpBox("Checking token...", MessageType.None);
                    else
                        EditorGUILayout.HelpBox("Token not checked yet: hit \"Test connection\".", MessageType.None);
                    break;
            }

            if (lastCheckTime != default)
                EditorGUILayout.LabelField($"Last checked at {lastCheckTime:HH:mm:ss}", EditorStyles.miniLabel);

            if (identity != null && !identity.scoped)
                EditorGUILayout.HelpBox("Tip: this token can access all your Coda docs. A token restricted to this game's doc limits the damage if it leaks (read only is enough to import, exporting asset references needs read and write).", MessageType.None);
        }

        private void DrawTokenField() {
            EditorGUI.BeginChangeCheck();
            tokenField = EditorGUILayout.PasswordField(new GUIContent("API token", "Stored in this computer's Unity preferences, never in the project"), tokenField);
            if (!EditorGUI.EndChangeCheck())
                return;

            CodaTokenStore.Set(requester, tokenField);
            ResetCheck();
            checkAt = requester.HasToken ? EditorApplication.timeSinceStartup + CheckDelay : -1;
        }

        private void DrawButtons() {
            using (new EditorGUILayout.HorizontalScope()) {
                using (new EditorGUI.DisabledScope(!requester.HasToken || checking)) {
                    if (GUILayout.Button(checking ? "Testing..." : "Test connection"))
                        CheckToken();
                }

                using (new EditorGUI.DisabledScope(!requester.HasToken)) {
                    if (GUILayout.Button("Remove from this machine")) {
                        CodaTokenStore.Clear(requester);
                        tokenField = "";
                        ResetCheck();
                        // A focused text field keeps showing its own copy of the text
                        GUIUtility.keyboardControl = 0;
                    }
                }
            }

            if (GUILayout.Button("How to get a token"))
                Application.OpenURL(HowToGetTokenUrl);
        }

        private void OnEditorUpdate() {
            double now = EditorApplication.timeSinceStartup;

            if (checkAt > 0 && now >= checkAt)
                CheckToken();

            if (checking && now - checkStartedAt > CheckTimeout) {
                checking = false;
                checkProblem = "no answer from Coda.";
                lastCheckTime = DateTime.Now;
                Repaint();
            }
        }

        private void CheckToken() {
            checkAt = -1;
            if (!requester.HasToken)
                return;

            int check = ++checkCount;
            checking = true;
            checkStartedAt = EditorApplication.timeSinceStartup;
            checkProblem = null;
            requester.PerformConnectionTest((req) => OnCheckResponse(req, check));
            Repaint();
        }

        // Forgets the last check when the token changes: its result belonged to the previous token
        private void ResetCheck() {
            checkAt = -1;
            checking = false;
            checkCount++;
            checkProblem = null;
            lastCheckTime = default;
        }

        private void OnCheckResponse(UnityWebRequest req, int check) {
            // The inspector may have been closed, the token changed, or the check given up on while Coda was answering
            if (this == null || check != checkCount || !checking)
                return;

            checking = false;
            lastCheckTime = DateTime.Now;

            // Valid and rejected tokens are recorded by the Requester itself: only keep why a check told nothing
            CodaApiError.Kind kind = CodaApiError.Classify(req);
            if (kind == CodaApiError.Kind.RateLimited)
                checkProblem = "Coda rate limit reached, try again in a few seconds.";
            else if (kind != CodaApiError.Kind.None && kind != CodaApiError.Kind.Unauthorized)
                checkProblem = string.IsNullOrEmpty(req.error) ? "empty response from Coda." : $"{req.error}.";

            Repaint();
        }
    }
}
