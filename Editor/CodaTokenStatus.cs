using System;
using System.Security.Cryptography;
using System.Text;
using Unity.Plastic.Newtonsoft.Json;
using UnityEditor;

namespace Com.Pamcha.CodaSync {
    internal enum TokenState { Unknown, Valid, Rejected }

    /// <summary>Who a token belongs to, as answered by Coda's whoami endpoint.</summary>
    internal class TokenIdentity {
        public string name;
        public string email;
        public string tokenName;
        /// <summary>True when the token is restricted to some docs or tables.</summary>
        public bool scoped;
    }

    /// <summary>
    /// What Coda last said about each Requester's token: valid, rejected, or nothing yet. Kept in
    /// SessionState, so it survives the domain reload in the middle of an import but not an editor
    /// restart. Each entry is recorded against a fingerprint of the token it was sent with: once the
    /// token changes, the entry no longer applies and the state reads Unknown again.
    /// </summary>
    internal static class CodaTokenStatus {
        private const string KeyPrefix = "Com.Pamcha.CodaSync.TokenStatus.";

        /// <summary>Raised when a token, or what Coda said about it, changes. Inspectors repaint on it.</summary>
        public static event Action Changed;

        private class Entry {
            public string fingerprint;
            public TokenState state;
            public bool hasIdentity;
            public TokenIdentity identity;
        }

        // OnGUI asks for the state several times per frame: remember the last fingerprint computed
        private static string lastToken;
        private static string lastFingerprint;

        public static TokenState GetState(CodaRequester requester) {
            Entry entry = GetCurrentEntry(requester);
            return entry != null ? entry.state : TokenState.Unknown;
        }

        public static bool TryGetIdentity(CodaRequester requester, out TokenIdentity identity) {
            Entry entry = GetCurrentEntry(requester);
            identity = entry != null && entry.state == TokenState.Valid && entry.hasIdentity ? entry.identity : null;
            return identity != null;
        }

        /// <summary>Records a successful answer to a request sent with this token, keeping the identity already known for it.</summary>
        public static void RecordValid(CodaRequester requester, string token) {
            Entry previous = GetEntry(requester);
            if (previous != null && previous.state == TokenState.Valid && previous.fingerprint == Fingerprint(token))
                return;

            Save(requester, new Entry { fingerprint = Fingerprint(token), state = TokenState.Valid });
        }

        public static void RecordValid(CodaRequester requester, string token, TokenIdentity identity) {
            Save(requester, new Entry { fingerprint = Fingerprint(token), state = TokenState.Valid, hasIdentity = identity != null, identity = identity });
        }

        public static void RecordRejected(CodaRequester requester, string token) {
            Save(requester, new Entry { fingerprint = Fingerprint(token), state = TokenState.Rejected });
        }

        public static void NotifyChanged() {
            Changed?.Invoke();
        }

        private static Entry GetCurrentEntry(CodaRequester requester) {
            Entry entry = GetEntry(requester);
            if (entry == null) return null;

            string token = CodaTokenStore.Get(requester);
            return token.Length > 0 && entry.fingerprint == Fingerprint(token) ? entry : null;
        }

        private static Entry GetEntry(CodaRequester requester) {
            if (!CodaTokenStore.TryGetGuid(requester, out string guid)) return null;

            string json = SessionState.GetString(KeyPrefix + guid, "");
            if (json.Length == 0) return null;

            try {
                return JsonConvert.DeserializeObject<Entry>(json);
            } catch (JsonException) {
                // Written by another version of this class: forget it, the next answer from Coda rewrites it
                return null;
            }
        }

        private static void Save(CodaRequester requester, Entry entry) {
            if (!CodaTokenStore.TryGetGuid(requester, out string guid)) return;

            SessionState.SetString(KeyPrefix + guid, JsonConvert.SerializeObject(entry));
            NotifyChanged();
        }

        // Only a short hash of the token is kept, never the token itself
        private static string Fingerprint(string token) {
            token = token ?? "";
            if (token == lastToken) return lastFingerprint;

            using (SHA256 sha = SHA256.Create()) {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(token));
                StringBuilder hex = new StringBuilder();
                for (int i = 0; i < 8; i++)
                    hex.Append(hash[i].ToString("x2"));

                lastToken = token;
                lastFingerprint = hex.ToString();
            }

            return lastFingerprint;
        }
    }
}
