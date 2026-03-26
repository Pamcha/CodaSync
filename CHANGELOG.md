# Changelog
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.2.0] - 2026-03-26
### Added
- **Import report** — structured console output at the end of each import showing asset stats (created/updated/skipped per table), class changes (new classes, added/removed fields), lookup failures with actionable suggestions, and warnings
- When a lookup fails, the report now suggests whether the referenced table was not selected for import or whether the row is missing in Coda

### Fixed
- Lookup columns referencing rows with dashes in their name (e.g. "Demir - Base") failed to resolve — name sanitization was inconsistent between asset creation and lookup resolution
- GameObject lookup columns failed to resolve — missing type mapping in `GetAssetType()`
- Extracted shared `CodaSyncUtils.SanitizeName()` method to prevent future sanitization divergence between `TableStruct`, `CodeGenerator`, and `InstanceGenerator`

## [1.1.2] - 2026-03-23
### Fixed
- Asset reference resolution now works when importing a subset of tables — Type Tables (Sprite, AudioClip, etc.) are automatically fetched during import even if only specific tables are selected
- "Deselect All" in Table Importer no longer deselects hidden Type Tables, preventing accidental loss of asset reference data
- Asset Reference Exporter no longer double-counts assets — previously, a single sprite file was counted as 2 assets (Texture2D + Sprite sub-asset)
- Single sprites are now exported with their file name (e.g. "Emile Placeholder") instead of Unity's internal sub-asset name (e.g. "Emile Placeholder_0"). Sprite sheets still use per-slice names (_0, _1, etc.)

## [1.1.1] - 2026-03-18
### Added
- Support for new Coda column types: `scale`, `percent`, `currency` (mapped to `float`), `dateTime` (mapped to `DateTime`), `time`, `duration`, `email`, `link` (mapped to `string`)

## [1.1.0] - 2026-02-27
### Added
- **Validate Names button** in Table Importer inspector — checks table, column, and row names for invalid C# identifiers and reports issues in the console
- **Duplicate row name detection** — duplicates are flagged as critical (red) in the validation report and skipped during import to prevent asset overwrites and broken lookups
- **Auto-validation during import** — name issues are logged automatically before code generation
- **Cancelable progress bar** on import and validation operations — click Cancel to abort cleanly at any step
- **Meaningful progress bar** — displays current table name, step description, and real progress percentage
- Rich text console output with color-coded messages and emojis (⚠️ warnings in orange, ❌ errors in red, ✅ success in green)

### Improved
- Table Importer editor UX: alphabetical table list, selection counter, zebra striping, prominent Import button, icons on buttons
- Empty display column rows are now skipped during import with a clear warning
- `AssetDatabase.SaveAssets()` between instance creation and field assignment passes for reliable SO-to-SO lookup resolution

### Fixed
- Rows with invalid file name characters (`* ? < > : " | / \`) are now detected and skipped during import instead of crashing Unity
- Cleaned up debug logging in `InstanceGenerator` — consistent `[CodaSync]` prefix, no more noisy image request logs, errors only logged on failure

## [1.0.7] - 2026-01-25
### Fixed
- Fixed a bug where ScriptableObject lookup references would fail to resolve when the referenced SO class has a field named `name` that shadows `UnityEngine.Object.name`. The asset search now correctly extracts the actual asset filename for comparison, handling cases where the field value contains path-like strings (e.g., « Category/assetName" instead of "assetName").

## [1.0.6] - 2025-08-07
### Fixed
- The asset exporter references tables are now based on the asset path and not the assetID because these might change in rare occasions, making them less trustable. The id is still kept in the table and will be updated if it has been modified in Unity for some reason. The goal is to prevent duplication in these asset ref tables
- The instance generator now uses asset path and not asset Id to retrieve the asset. The asset id method is kept as a fallback for backward compatibility

## [1.0.5] - 2025-07-24
### Fixed
- Fixed a bug where ScriptableObject references between tables (e.g., relation columns) could point to incorrect assets when row names were similar (e.g., "item hero 2" vs "item 2 hero").
- The internal asset name normalization (e.g., replacing spaces with underscores) is now accounted for when resolving references, ensuring accurate linking between generated ScriptableObjects.

## [1.0.4] - 2025-02-25
### Fixed
- The database class static instance was not found through ressource.load due to a wrong path configuration in the generated DB class. You can now use the database class (i.e. the class that has a list to all instances of a class, for example if you have a character table, you will have a Character class with a scriptable object per row and a Character_DB class wit a scriptable objects that has a list with a reference to all the Character scriptable objects)

## [1.03] - 2025-02-21
### Added
- The table importer now has a Get visible colums only field: when checked, the columns that are hidden in coda are removed from the generated Scriptable objects. This allow you to have coda docs with working tables that contain columns (e.g. for testing purpose inside coda) that are not used in Unity
- Game objects can be saved in a Game Object table


## [1.0.2] - 2024-04-09
### Fixed
- Last update date is saved and displayed
- Game objects can be saved in a Game Object table
- the list of added folders si now saved properly


## [1.0.1] - 2024-03-21
### Added
- slider colum type (treated as numbers in Unity)

## [1.0.0] - 2022-08-26
### Added
- Requester Scriptable Object to setup your credentials with Coda.io
- TableImporter Scriptable Object to setup the connexion with a Coda doc you have the rights for (i.e. owner, editor, or viewer)
- AssetReferenceExporter Scriptable Object to setup the connexion with a Coda doc you have the rights for to export the parameters of the assets you want to link in your Coda doc
- Generation of Scriptable Object classes, database classes, and the scriptable object instances, based on your synced tables on your Coda doc
- References to other generated instances for lookup columns or columns with a reference to a referenced asset (e.g. a sprite)
