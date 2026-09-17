using UnityEditor;
using UnityEngine;

namespace Com.Pamcha.CodaSync {
    /// <summary>
    /// Connection status shared by the Table Importer and Asset Reference Exporter inspectors: tells the
    /// user what stands between the importer and Coda, and greys out what can't work without a token.
    /// </summary>
    internal static class CodaSyncGUI {
        public const string NoTokenTooltip = "Set up the Coda API token of the Requester first";

        /// <summary>
        /// Selects and pings the Requester, so its inspector shows the token field. Deferred: changing the
        /// selection in the middle of an inspector's OnGUI breaks that inspector's layout.
        /// </summary>
        public static void SelectRequester(CodaRequester requester) {
            if (requester == null) return;

            EditorApplication.delayCall += () => {
                if (requester == null) return;
                Selection.activeObject = requester;
                EditorGUIUtility.PingObject(requester);
            };
        }

        /// <summary>True when the importer can send requests: a Requester with a token on this computer.</summary>
        public static bool CanSendRequests(ImporterExporter importer) {
            return importer.Requester != null && importer.Requester.HasToken;
        }

        /// <summary>Why the buttons that call Coda are greyed out, or "" when they aren't.</summary>
        public static string DisabledRequestTooltip(ImporterExporter importer) {
            if (importer.Requester == null) return "Assign a Requester first";
            if (!importer.Requester.HasToken) return NoTokenTooltip;
            return "";
        }

        /// <summary>
        /// Draws what stands between the importer and Coda, if anything: no Requester, no token on this
        /// computer, a token Coda rejected, or a document the token can't reach. Draws nothing when all is well.
        /// </summary>
        public static void DrawConnectionStatus(ImporterExporter importer) {
            CodaRequester requester = importer.Requester;

            if (requester == null) {
                EditorGUILayout.HelpBox("No Requester assigned.", MessageType.Warning);
                return;
            }

            if (!requester.HasToken) {
                DrawHelpBoxWithSetupButton($"No Coda API token on this machine for \"{requester.name}\". Each team member sets up their own.", MessageType.Warning, requester);
                return;
            }

            if (CodaTokenStatus.GetState(requester) == TokenState.Rejected) {
                DrawHelpBoxWithSetupButton($"Coda rejected the API token of \"{requester.name}\".", MessageType.Error, requester);
                return;
            }

            CodaApiError.Kind lastError = importer.LastTableListError;
            if (lastError == CodaApiError.Kind.Forbidden || lastError == CodaApiError.Kind.NotFound)
                EditorGUILayout.HelpBox($"The API token of \"{requester.name}\" can't access this document: it may be restricted to another doc, or the document URL is wrong.", MessageType.Warning);
        }

        private static void DrawHelpBoxWithSetupButton(string message, MessageType type, CodaRequester requester) {
            EditorGUILayout.HelpBox(message, type);

            using (new EditorGUILayout.HorizontalScope()) {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Set up token", GUILayout.Width(110)))
                    SelectRequester(requester);
            }
        }
    }
}
