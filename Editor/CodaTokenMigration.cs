using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Com.Pamcha.CodaSync {
    /// <summary>
    /// Moves the API tokens that versions before 1.7.0 stored in clear inside Requester assets. Each one
    /// is copied to this computer, unless it already has a token for that Requester, then removed from the
    /// asset, which is saved right away: once the file is committed, the token stops being shared.
    /// Runs over every Requester once per editor session, and again for a single Requester whenever it is
    /// used, to catch an asset brought back mid-session by a pull or a branch switch.
    /// </summary>
    [InitializeOnLoad]
    internal static class CodaTokenMigration {
        private const string SessionKeyProjectScanned = "Com.Pamcha.CodaSync.TokenMigration.ProjectScanned";

        // GUIDs of the Requesters whose cleanup is already scheduled, or failed during this domain (file not editable)
        private static readonly HashSet<string> scheduled = new HashSet<string>();
        private static readonly HashSet<string> failed = new HashSet<string>();

        static CodaTokenMigration() {
            if (!SessionState.GetBool(SessionKeyProjectScanned, false))
                EditorApplication.delayCall += ScanProject;
        }

        private static void ScanProject() {
            SessionState.SetBool(SessionKeyProjectScanned, true);

            foreach (string guid in AssetDatabase.FindAssets($"t:{nameof(CodaRequester)}")) {
                // The t: filter matches a short class name (see 1.4.1): loading by type drops any namesake
                CodaRequester requester = AssetDatabase.LoadAssetAtPath<CodaRequester>(AssetDatabase.GUIDToAssetPath(guid));
                MigrateIfNeeded(requester);
            }
        }

        /// <summary>
        /// Copies the token still stored in the asset to this computer when it has none for this Requester,
        /// then schedules its removal from the asset. The copy is immediate, so the token works right away.
        /// The asset is rewritten on the next editor tick, outside of any GUI or import callback.
        /// </summary>
        public static void MigrateIfNeeded(CodaRequester requester) {
            if (requester == null || string.IsNullOrEmpty(requester.LegacyToken)) return;
            if (!CodaTokenStore.TryGetGuid(requester, out string guid)) return;
            if (scheduled.Contains(guid) || failed.Contains(guid)) return;

            // A token already set up on this computer is never overwritten. Otherwise the copy is what keeps
            // the token alive: Coda only shows a token once, when it is created.
            bool copied = false;
            if (CodaTokenStore.Get(requester).Length == 0) {
                CodaTokenStore.Set(requester, requester.LegacyToken);
                copied = true;
            }

            scheduled.Add(guid);
            EditorApplication.delayCall += () => RemoveFromAsset(requester, guid, copied);
        }

        private static void RemoveFromAsset(CodaRequester requester, string guid, bool copied) {
            scheduled.Remove(guid);
            if (requester == null || string.IsNullOrEmpty(requester.LegacyToken)) return;

            string path = AssetDatabase.GetAssetPath(requester);
            if (!IsEditable(path)) {
                failed.Add(guid);
                Debug.LogWarning($"⚠️ <b>[CodaSync]</b> Couldn't remove the API token from \"{path}\": the file isn't editable. The token was still copied to this computer. Make the file writable and the cleanup will run again.");
                return;
            }

            SerializedObject serializedRequester = new SerializedObject(requester);
            serializedRequester.FindProperty("_apiToken").stringValue = "";
            serializedRequester.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(requester);
            AssetDatabase.SaveAssetIfDirty(requester);

            if (copied)
                Debug.LogWarning($"⚠️ <b>[CodaSync]</b> Moved the API token of \"{requester.name}\" out of \"{path}\": it is now stored in this computer's Unity preferences. Commit \"{path}\" so the token stops being shared. It stayed readable by anyone with access to the project: once everyone has set up their own token, revoke it in Coda.");
            else
                Debug.LogWarning($"⚠️ <b>[CodaSync]</b> Removed the API token stored in \"{path}\": this computer already has its own token for \"{requester.name}\". Commit \"{path}\" so the token stops being shared, and revoke it in Coda once everyone has set up their own.");
        }

        private static bool IsEditable(string path) {
            if (string.IsNullOrEmpty(path)) return false;
            if (!AssetDatabase.IsOpenForEdit(path, out string _, StatusQueryOptions.UseCachedIfPossible)) return false;

            // Registry, git and built-in packages are read-only whatever the file system says
            UnityEditor.PackageManager.PackageInfo package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(path);
            if (package != null
                && package.source != UnityEditor.PackageManager.PackageSource.Embedded
                && package.source != UnityEditor.PackageManager.PackageSource.Local)
                return false;

            string physicalPath = FileUtil.GetPhysicalPath(path);
            return !File.Exists(physicalPath) || (File.GetAttributes(physicalPath) & FileAttributes.ReadOnly) == 0;
        }
    }
}
