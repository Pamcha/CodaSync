using System.Collections.Generic;
using Unity.Plastic.Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace Com.Pamcha.CodaSync {
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
        public string Name { get => string.IsNullOrEmpty(name) ? "" : name.Replace(' ', '_'); }
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
        public string Name { get => string.IsNullOrEmpty(name) ? "" : name.Replace(' ', '_'); }
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
        public string Name { get => string.IsNullOrEmpty(name) ? "" : name.Replace(' ', '_'); }
    }

    public enum ColumnType {
        text,
        number,
        checkbox,
        date,
        canvas,
        select,
        lookup,
        person,
        image
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
        public string Name { get => string.IsNullOrEmpty(name) ? "" : name.Replace(' ', '_'); }
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

    [System.Serializable]
    struct AssetRef {
        public string AssetName;
        public int AssetId;
        public string AssetPath;

        public AssetRef(Object asset) {
            AssetName = asset.name;
            AssetId = asset.GetInstanceID();
            AssetPath = AssetDatabase.GetAssetPath(asset);
        }
    }
}