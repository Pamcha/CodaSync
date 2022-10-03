using UnityEngine;

namespace Com.Pamcha.CodaSync {
    public abstract class CodeGenerator {

        private static TableStructure[] allStructures;
        private static TableRow[][] allRows;

        public static CodeFiles[] GetCodeFromTableStructures(TableStructure[] structures, TableRow[][] tablesRows) {
            CodeFiles[] results = new CodeFiles[structures.Length];
            allStructures = structures;
            allRows = tablesRows;

            for (int i = 0; i < structures.Length; i++) {
                results[i] = GetCodeFromTableStructure(structures[i], tablesRows[i]);
            }

            return results;
        }

        private static CodeFiles GetCodeFromTableStructure(TableStructure structure, TableRow[] rows) {
            string databaseCode =
@$"using UnityEngine;
using System.Collections.Generic;
namespace {TableImporter.CodeNamespace} {{
    public class {structure.Name}_DB : ScriptableObject {{
        public static {structure.Name}_DB _instance;
        public static {structure.Name}_DB Instance {{
            get {{
                if (_instance == null)
                    _instance = Resources.Load<{structure.Name}_DB>(typeof({structure.Name}_DB).Name);
                return _instance;
            }}
        }}

        public List<{structure.Name}> List;
    }}
}}";


            string dataCode =
@$"using UnityEngine;
using System.Collections.Generic;

namespace {TableImporter.CodeNamespace} {{
    public class {structure.Name} : ScriptableObject {{{GetFields(structure, rows)}
    }}
}}";

            string enums = "";

            if (HasEnum(structure)) {
                enums =
@$"namespace {TableImporter.CodeNamespace} {{
    
}}";
            }


            return new CodeFiles(databaseCode, dataCode);
        }

        private static string GetFields(TableStructure structure, TableRow[] rows) {
            string fields = "";

            foreach (var item in structure.Items) {
                string type = "";
                string attributes = "";

                (attributes, type) = GetTypeOf(item, rows);

                if (item.Format.IsArray)
                    type = $"{type}[]";
                

                fields = $"{fields}\n        {attributes} public {type} {item.Name};";
            }

            return fields;
        }

        private static string GetEnumCode(TableStructure structure) {
            string code = "";

            foreach (var column in structure.Items) {
                if (column.Format.Type != ColumnType.select)
                    continue;
                ///TODO Lister les valeurs possibles

                //                code = @$"{code}
                //public enum {column.Name}_Enum {{
                //    {}
                //}}";
            }

            return code;
        }

        private static bool HasEnum(TableStructure structure) {
            foreach (var column in structure.Items) {
                if (column.Format.Type == ColumnType.select)
                    return true;
            }

            return false;
        }

        private static (string, string) GetTypeOf(TableColumn column, TableRow[] rows) {
            string type = "";
            string attributes = "";
            ColumnType columnType = column.Format.Type;

            switch (columnType) {
                case ColumnType.canvas:
                    attributes = "[TextArea]";
                    type = "string";
                    break;
                case ColumnType.text:
                case ColumnType.select:
                    type = "string";
                    break;
                case ColumnType.number:
                    type = "float";
                    break;
                case ColumnType.checkbox:
                    type = "bool";
                    break;
                case ColumnType.image:
                    type = "Sprite";
                    break;
                case ColumnType.date:
                    type = "System.DateTime";
                    break;
                case ColumnType.lookup:
                    if (column.Format.Table.Name != ImporterExporter.assetReferencesTableName)
                        type = column.Format.Table.Name;
                    else
                        type = GetTypeFromFirstAssetReferenced(column, rows);
                    break;
                default:
                    type = "object";
                    break;
            }

            return (attributes, type);
        }

        private static string GetTypeFromFirstAssetReferenced (TableColumn column, TableRow[] rows) {
            for (int i = 0; i < rows.Length; i++)
            {
                string assetName = rows[i].Values[column.Id];

                if (!string.IsNullOrEmpty(assetName))
                    return GetReferencedAssetType(assetName);
            }

            return "object";
        }

        private static string GetColumnIdByName(TableStructure structure, string columnName) {
            string columnId = "";

            foreach (TableColumn columnStructure in structure.Items)
            {
                if (columnStructure.Name == columnName)
                {
                    columnId = columnStructure.Id;
                    break;
                }
            }

            return columnId;
        }

        private static string GetReferencedAssetType (string assetName) {
            TableStructure? structure = null;
            TableRow[] assetReferencesRows = null;

            for (int i = 0; i < allStructures.Length; i++) {
                if (allStructures[i].Name == ImporterExporter.assetReferencesTableName) {
                    structure = allStructures[i];
                    assetReferencesRows = allRows[i];
                    break;
                }
            }

            if (structure == null || assetReferencesRows == null) {
                Debug.LogError($"Can't find table {ImporterExporter.assetReferencesTableName}");
                return "object";
            }

            for (int i = 0; i < assetReferencesRows.Length; i++)
            {
                string name = assetReferencesRows[i].Values[GetColumnIdByName(structure.Value, "AssetName")];

                if (assetName == name) {
                    string assetType = assetReferencesRows[i].Values[GetColumnIdByName(structure.Value, "AssetType")];
                    if (!string.IsNullOrEmpty(assetType))
                        return assetType;
                }
            }

            return "object";
        }
    }



    public struct CodeFiles {
        public string databaseCode { get; private set; }
        public string classCode { get; private set; }

        public CodeFiles(string dbCode, string cCode) {
            databaseCode = dbCode;
            classCode = cCode;
        }
    }
}