using UnityEditor;

namespace Com.Pamcha.CodaSync {
    /// <summary>
    /// The only place that knows where API tokens live: this computer's EditorPrefs, one entry per
    /// Requester, keyed by the GUID of its asset. A token never goes into the asset, so it never reaches
    /// version control and each team member keeps their own. Change this class to store tokens somewhere
    /// else (an OS keychain, for instance) without touching anything else.
    /// </summary>
    internal static class CodaTokenStore {
        private const string KeyPrefix = "Com.Pamcha.CodaSync.ApiToken.";

        /// <summary>False when the Requester isn't saved as an asset yet: there is no GUID to key its token on.</summary>
        public static bool CanStore(CodaRequester requester) {
            return TryGetGuid(requester, out _);
        }

        /// <summary>The token stored on this computer for the Requester, or "" when there is none.</summary>
        public static string Get(CodaRequester requester) {
            return TryGetGuid(requester, out string guid) ? EditorPrefs.GetString(KeyPrefix + guid, "") : "";
        }

        /// <summary>
        /// Stores the token trimmed, since a pasted token often drags a space or a line break along.
        /// An empty token removes the entry.
        /// </summary>
        public static void Set(CodaRequester requester, string token) {
            if (!TryGetGuid(requester, out string guid)) return;

            token = token == null ? "" : token.Trim();
            if (token.Length == 0)
                EditorPrefs.DeleteKey(KeyPrefix + guid);
            else
                EditorPrefs.SetString(KeyPrefix + guid, token);

            CodaTokenStatus.NotifyChanged();
        }

        public static void Clear(CodaRequester requester) {
            Set(requester, "");
        }

        internal static bool TryGetGuid(CodaRequester requester, out string guid) {
            guid = null;
            if (requester == null) return false;

            return AssetDatabase.TryGetGUIDAndLocalFileIdentifier(requester, out guid, out long _) && !string.IsNullOrEmpty(guid);
        }
    }
}
