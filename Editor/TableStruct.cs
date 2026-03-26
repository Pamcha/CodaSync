using System.Collections.Generic;
using Unity.Plastic.Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace Com.Pamcha.CodaSync {
    public static class CodaSyncUtils {
        public static string SanitizeName(string name) {
            return string.IsNullOrEmpty(name) ? "" : name.Trim().Replace(' ', '_').Replace('-', '_');
        }
    }

    [System.Serializable]
    public struct TableStructure {
        [JsonProperty]
        private string id;
        [JsonProperty]
        private string type;
        [JsonProperty]
        private string tableType;
        [JsonProperty]
        private string name;
        [JsonProperty]
        private TableColumn[] items;


        public string Id { get => id; set { id = value; } }
        public string Type { get => type; set { type = value; } }
        public string TableType { get => tableType; set { tableType = value; } }
        public string UnmodifiedName { get => name; set => name = value; }
        public string Name { get => CodaSyncUtils.SanitizeName(name); }
        public TableColumn[] Items { get => items; set { items = value; } }
    }

    #region Column
    [System.Serializable]
    public struct TableColumn {
        [JsonProperty]
        private string id;
        [JsonProperty]
        private string name;
        [JsonProperty]
        private ColumnFormat format;

        public string Id { get => id; }
        public string Name { get => CodaSyncUtils.SanitizeName(name); }
        public ColumnFormat Format { get => format; }
    }

    [System.Serializable]
    public struct ColumnFormat {
        [JsonProperty]
        private ColumnType type;
        [JsonProperty]
        private bool isArray;
        [JsonProperty]
        private FormatTable table;

        public ColumnType Type { get => type; }
        public bool IsArray { get => isArray; }
        public FormatTable Table { get => table; }
    }

    public struct FormatTable {
        [JsonProperty]
        private string id;
        [JsonProperty]
        private string name;

        public string Id { get => id; }
        public string Name { get => CodaSyncUtils.SanitizeName(name); }
    }

    public enum ColumnType {
        text,
        number,
        slider,
        checkbox,
        date,
        canvas,
        select,
        lookup,
        person,
        image,
        scale,
        percent,
        currency,
        dateTime,
        time,
        duration,
        email,
        link
    }
    #endregion

    #region Row
    [System.Serializable]
    public struct TableRow {
        [JsonProperty]
        private string id;
        [JsonProperty]
        private string name;
        [JsonProperty]
        private Dictionary<string, string> values;

        public string Id { get => id; }
        public string Name { get => CodaSyncUtils.SanitizeName(name); }
        public string UnmodifiedName { get => name; }
        public Dictionary<string, string> Values { get => values; }
    }
    #endregion

    #region RequestEdits
    public struct RowEdit {
        public Row[] rows;
        public string[] keyColumns;
    }

    public struct Row {
        public Cell[] cells;
    }

    public struct Cell {
        public string column;
        public string value;
    }
    #endregion

    #region Import Report
    public class ImportReport {
        public struct LookupFailure {
            public string tableName;
            public string fieldName;
            public string missingAsset;
            public string referencedTable;
            public bool tableWasImported;
        }

        public struct InstanceInfo {
            public string tableName;
            public int created;
            public int updated;
            public int skipped;
        }

        public List<InstanceInfo> instances = new List<InstanceInfo>();
        public List<LookupFailure> lookupFailures = new List<LookupFailure>();
        public List<string> warnings = new List<string>();

        public void LogToConsole() {
            // Header
            Debug.Log("<color=#5B9BD5>\ud83d\udccb <b>[CodaSync] Import Report</b></color>");

            // Instances summary
            int totalCreated = 0, totalUpdated = 0, totalSkipped = 0;
            foreach (var info in instances) {
                totalCreated += info.created;
                totalUpdated += info.updated;
                totalSkipped += info.skipped;
            }

            string instancesSummary = $"    <b>Assets:</b> {totalCreated} created, {totalUpdated} updated";
            if (totalSkipped > 0) instancesSummary += $", {totalSkipped} skipped";
            Debug.Log($"<color=#6ECB63>{instancesSummary}</color>");

            // Per-table detail
            foreach (var info in instances) {
                string detail = $"      \u2514 {info.tableName}: {info.created} created, {info.updated} updated";
                if (info.skipped > 0) detail += $", {info.skipped} skipped";
                Debug.Log($"<color=#AAAAAA>{detail}</color>");
            }

            // Lookup failures
            if (lookupFailures.Count > 0) {
                Debug.Log($"<color=#E8A838>    <b>Lookup failures:</b> {lookupFailures.Count}</color>");
                foreach (var failure in lookupFailures) {
                    string suggestion = failure.tableWasImported
                        ? $"Check that \"{failure.missingAsset}\" exists in \"{failure.referencedTable}\" in Coda"
                        : $"Make sure table \"{failure.referencedTable}\" is selected for import";
                    Debug.Log($"<color=#E8A838>      \u2514 {failure.tableName}.{failure.fieldName} \u2192 \"{failure.missingAsset}\" not found in {failure.referencedTable}. {suggestion}</color>");
                }
            }

            // Warnings
            if (warnings.Count > 0) {
                Debug.Log($"<color=#E8A838>    <b>Warnings:</b> {warnings.Count}</color>");
                foreach (var warning in warnings) {
                    Debug.Log($"<color=#E8A838>      \u2514 {warning}</color>");
                }
            }

            // Final status
            if (lookupFailures.Count == 0 && warnings.Count == 0) {
                Debug.Log("<color=#6ECB63>\u2705 <b>[CodaSync]</b> Import completed successfully.</color>");
            } else {
                int issueCount = lookupFailures.Count + warnings.Count;
                Debug.Log($"<color=#E8A838>\u26a0\ufe0f <b>[CodaSync]</b> Import completed with {issueCount} issue(s).</color>");
            }
        }
    }
    #endregion

    [System.Serializable]
    struct AssetRef {
        public string AssetName;
        public int AssetId;
        public string AssetPath;

        public AssetRef(Object asset) {
            AssetPath = AssetDatabase.GetAssetPath(asset);
            AssetId = asset.GetInstanceID();

            if (!AssetDatabase.IsMainAsset(asset)) {
                // For sub-assets, check if this is the only one (e.g. single sprite mode).
                // If so, use the file name for a cleaner display in Coda.
                // If multiple sub-assets exist (e.g. sprite sheet), keep asset.name with
                // its suffix (_0, _1, ...) to distinguish each slice.
                Object[] allAtPath = AssetDatabase.LoadAllAssetsAtPath(AssetPath);
                int subAssetCount = 0;
                foreach (var a in allAtPath) {
                    if (!AssetDatabase.IsMainAsset(a)) subAssetCount++;
                }

                AssetName = subAssetCount == 1
                    ? System.IO.Path.GetFileNameWithoutExtension(AssetPath)
                    : asset.name;
            } else {
                AssetName = asset.name;
            }
        }
    }
}