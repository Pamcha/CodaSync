namespace Com.Pamcha.CodaSync {
    public abstract class CodeGenerator {

        private static TableStructure[] allStructures;

        public static CodeFiles[] GetCodeFromTableStructures(TableStructure[] structures) {
            CodeFiles[] results = new CodeFiles[structures.Length];
            allStructures = structures;

            for (int i = 0; i < structures.Length; i++) {
                results[i] = GetCodeFromTableStructure(structures[i]);
            }

            return results;
        }

        private static CodeFiles GetCodeFromTableStructure(TableStructure structure) {
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
    public class {structure.Name} : ScriptableObject {{{GetFields(structure)}
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

        private static string GetFields(TableStructure structure) {
            string fields = "";

            foreach (var item in structure.Items) {
                string type = "";
                string attributes = "";

                (attributes, type) = GetTypeOf(item);

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

        private static (string, string) GetTypeOf(TableColumn column) {
            string type = "";
            string attributes = "";
            ColumnType columnType = column.Format.Type;

            switch (columnType) {
                case ColumnType.canvas:
                    type = "string";
                    attributes = "[TextArea]";
                    break;
                case ColumnType.text:
                case ColumnType.select:
                    type = "string";
                    break;
                case ColumnType.slider:
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
                    type = $"{column.Format.Table.Name}";
                    break;
                default:
                    type = "object";
                    break;
            }

            return (attributes, type);
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