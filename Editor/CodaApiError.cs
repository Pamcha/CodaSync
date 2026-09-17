using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Com.Pamcha.CodaSync {
    /// <summary>
    /// Turns a failed Coda response into a message that says what to fix. A rejected token, a token
    /// restricted to another doc and a network hiccup used to surface as the same raw warning.
    /// </summary>
    internal static class CodaApiError {
        public enum Kind { None, EmptyResponse, Unauthorized, Forbidden, NotFound, RateLimited, Network, Other }

        public static Kind Classify(UnityWebRequest req) {
            if (req.result == UnityWebRequest.Result.Success)
                return req.downloadHandler == null || string.IsNullOrEmpty(req.downloadHandler.text) ? Kind.EmptyResponse : Kind.None;

            switch (req.responseCode) {
                case 401: return Kind.Unauthorized;
                case 403: return Kind.Forbidden;
                case 404:
                case 410: return Kind.NotFound;
                case 429: return Kind.RateLimited;
            }

            // No HTTP status at all (DNS, timeout, Curl error 61...) or a failure on Coda's side
            if (req.responseCode == 0 || req.responseCode >= 500)
                return Kind.Network;

            return Kind.Other;
        }

        /// <summary>Problems the user fixes outside the console: in the Requester, in Coda, or in the document URL.</summary>
        public static bool IsAccessProblem(Kind kind) {
            return kind == Kind.Unauthorized || kind == Kind.Forbidden || kind == Kind.NotFound;
        }

        /// <summary>
        /// Plain-text explanation of a failed response. target names what was requested, "this document"
        /// or table "Name". Never includes the token.
        /// </summary>
        public static string Describe(Kind kind, UnityWebRequest req, CodaRequester requester, string target) {
            string requesterName = requester != null ? requester.name : "the Requester";

            switch (kind) {
                case Kind.Unauthorized:
                    return $"Coda rejected the API token of \"{requesterName}\" (invalid, expired or revoked). Select the Requester to paste a new one.";
                case Kind.Forbidden:
                    return $"The API token of \"{requesterName}\" doesn't grant access to {target}. It may be restricted to another doc or table, or be read only (exporting needs read and write).";
                case Kind.NotFound:
                    return $"Coda couldn't find {target} with the API token of \"{requesterName}\": check the document URL, whether the table was renamed or deleted in Coda, or whether the token is restricted to another doc.";
                case Kind.RateLimited:
                    return "Coda rate limit reached. Wait a few seconds and try again.";
                case Kind.Network:
                    return $"Couldn't reach Coda for {target} ({req.error}).";
                case Kind.EmptyResponse:
                    return $"Empty response from Coda for {target}.";
                default:
                    return $"Coda refused the request for {target} ({req.error}).";
            }
        }

        /// <summary>
        /// Logs a failed response once, followed by what happens next (outcome, e.g. "Import aborted.").
        /// With showDialog, a token or access problem also opens a dialog: those are fixed outside the
        /// console, and interrupting the user is only fair when they clicked for this request.
        /// </summary>
        public static void Report(UnityWebRequest req, CodaRequester requester, string target, string outcome, bool showDialog) {
            Kind kind = Classify(req);
            string message = Describe(kind, req, requester, target);
            if (!string.IsNullOrEmpty(outcome))
                message = $"{message} {outcome}";

            Debug.LogWarning($"⚠️ <b>[CodaSync]</b> {message}");

            if (!showDialog || !IsAccessProblem(kind))
                return;

            if (kind == Kind.Unauthorized && requester != null) {
                if (EditorUtility.DisplayDialog("Coda Sync", message, "Set up token", "Close"))
                    CodaSyncGUI.SelectRequester(requester);
            } else {
                EditorUtility.DisplayDialog("Coda Sync", message, "OK");
            }
        }
    }
}
