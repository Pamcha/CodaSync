using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Com.Pamcha.CodaSync {
    public abstract class CodeGenerator {

        private static TableStructure[] allStructures;

        // C# reserved keywords that cannot be used as identifiers
        private static readonly HashSet<string> CSharpKeywords = new HashSet<string> {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char",
            "checked", "class", "const", "continue", "decimal", "default", "delegate", "do",
            "double", "else", "enum", "event", "explicit", "extern", "false", "finally",
            "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int",
            "interface", "internal", "is", "lock", "long", "namespace", "new", "null",
            "object", "operator", "out", "override", "params", "private", "protected",
            "public", "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof",
            "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
            "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using",
            "virtual", "void", "volatile", "while"
        };

        /// <summary>
        /// Sanitizes a name into a valid C# identifier.
        /// Strips invalid characters, handles leading digits, and checks for reserved keywords.
        /// </summary>
        public static string SanitizeIdentifier(string name) {
            if (string.IsNullOrEmpty(name))
                return "_empty";

            // Replace spaces and hyphens with underscores (existing behavior)
            string sanitized = name.Trim().Replace(' ', '_').Replace('-', '_');

            // Remove all remaining invalid characters
            sanitized = Regex.Replace(sanitized, @"[^a-zA-Z0-9_]", "");

            // Handle empty result after stripping
            if (string.IsNullOrEmpty(sanitized))
                return "_invalid";

            // Prefix with underscore if starts with a digit
            if (char.IsDigit(sanitized[0]))
                sanitized = "_" + sanitized;

            // Prefix with underscore if it's a C# keyword
            if (CSharpKeywords.Contains(sanitized))
                sanitized = "_" + sanitized;

            return sanitized;
        }

        /// <summary>
        /// Analyzes table and column names for C# identifier issues.
        /// Returns a list of human-readable issue descriptions.
        /// </summary>
        public static List<string> GetNameIssuesReport(TableStructure[] structures) {
            List<string> allIssues = new List<string>();
            foreach (var structure in structures) {
                allIssues.AddRange(GetColumnNameIssuesForTable(structure));
            }
            return allIssues;
        }

        /// <summary>
        /// Returns column and table name issues for a single table.
        /// </summary>
        public static List<string> GetColumnNameIssuesForTable(TableStructure structure) {
            List<string> issues = new List<string>();

            if (ImporterExporter.TypeTables.Contains(structure.UnmodifiedName))
                return issues;

            // Check table name
            string sanitizedTable = SanitizeIdentifier(structure.UnmodifiedName);
            if (sanitizedTable != structure.Name) {
                issues.Add($"Table \"{structure.UnmodifiedName}\" contains invalid characters. Suggested: \"{sanitizedTable}\"");
            }

            if (structure.Items == null)
                return issues;

            // Check column names
            foreach (var column in structure.Items) {
                string rawName = column.Name;
                string sanitizedColumn = SanitizeIdentifier(rawName);
                if (sanitizedColumn != rawName) {
                    issues.Add($"Table \"{structure.UnmodifiedName}\" \u2192 column \"{rawName}\" contains invalid characters. Suggested: \"{sanitizedColumn}\"");
                }
            }

            return issues;
        }

        /// <summary>
        /// Returns row name issues for a single table's rows.
        /// Checks for empty display columns and problematic characters.
        /// </summary>
        public static List<string> GetRowNameIssuesForTable(TableStructure structure, TableRow[] rows) {
            List<string> issues = new List<string>();

            if (ImporterExporter.TypeTables.Contains(structure.UnmodifiedName))
                return issues;

            if (rows == null)
                return issues;

            // Track row names (after basic sanitization) to detect duplicates
            Dictionary<string, List<int>> nameOccurrences = new Dictionary<string, List<int>>();

            for (int r = 0; r < rows.Length; r++) {
                string rawName = rows[r].UnmodifiedName;

                // Check for empty display column
                if (string.IsNullOrWhiteSpace(rawName)) {
                    issues.Add($"Table \"{structure.UnmodifiedName}\" \u2192 row #{r + 1} has an empty display column");
                    continue;
                }

                // Track name for duplicate detection (use the sanitized Name, which becomes the .asset filename)
                string assetName = rows[r].Name;
                if (!nameOccurrences.ContainsKey(assetName))
                    nameOccurrences[assetName] = new List<int>();
                nameOccurrences[assetName].Add(r + 1); // 1-based row numbers

                // Check for invalid file name characters (these prevent asset creation)
                char? invalidFileChar = GetInvalidFileNameChar(assetName);
                if (invalidFileChar != null) {
                    issues.Add($"Table \"{structure.UnmodifiedName}\" \u2192 row \"{rawName}\" contains invalid file name character '{invalidFileChar}'. This row will be skipped during import.");
                }

                // Check for problematic C# identifier characters
                string sanitized = SanitizeIdentifier(rawName);
                if (sanitized != assetName) {
                    issues.Add($"Table \"{structure.UnmodifiedName}\" \u2192 row \"{rawName}\" contains problematic characters. Suggested: \"{sanitized}\"");
                }
            }

            // Report duplicate names — these cause asset overwrite and broken lookup resolution
            foreach (var kvp in nameOccurrences) {
                if (kvp.Value.Count > 1) {
                    string rowNumbers = string.Join(", ", kvp.Value);
                    issues.Insert(0, $"DUPLICATE Table \"{structure.UnmodifiedName}\" \u2192 \"{kvp.Key}\" is used by rows #{rowNumbers}. This will cause asset overwrites and broken lookups!");
                }
            }

            return issues;
        }

        // Characters that Unity rejects in asset file names (cross-platform).
        // Path.GetInvalidFileNameChars() is OS-specific (macOS only blocks \0 and /),
        // so we enforce the full Windows set to match Unity's AssetDatabase behavior.
        private static readonly HashSet<char> InvalidAssetNameChars = new HashSet<char>(
            new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' }
        );

        /// <summary>
        /// Returns the first invalid file name character found in the string, or null if valid.
        /// </summary>
        private static char? GetInvalidFileNameChar(string name) {
            for (int i = 0; i < name.Length; i++) {
                if (InvalidAssetNameChars.Contains(name[i]) || char.IsControl(name[i]))
                    return name[i];
            }
            return null;
        }

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
                    _instance = Resources.Load<{structure.Name}_DB>({"\""+structure.Name+"/_"+structure.Name+"_Database\""});
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
                case ColumnType.scale:
                case ColumnType.percent:
                case ColumnType.currency:
                    type = "float";
                    break;
                case ColumnType.checkbox:
                    type = "bool";
                    break;
                case ColumnType.image:
                    type = "Sprite";
                    break;
                case ColumnType.date:
                case ColumnType.dateTime:
                    type = "System.DateTime";
                    break;
                case ColumnType.time:
                case ColumnType.duration:
                case ColumnType.email:
                case ColumnType.link:
                    type = "string";
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