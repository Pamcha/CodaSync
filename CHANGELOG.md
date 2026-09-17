# Changelog
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.7.0] - 2026-09-17
### Security
- **The Coda API token is no longer stored in the Requester asset**: it was a serialized field, hidden in the inspector but written in clear in the `.asset` file, so anyone with access to the project (repository, clones, git history) could read it and use it on every doc it could access. Keeping the asset out of version control was no real fix: an ignore rule does nothing for a file already tracked, and importers and exporters reference their Requester by GUID, so a clone without it loses every reference. Each team member now pastes their own token in the Requester, and it is stored in this computer's Unity preferences (EditorPrefs) under a key built from the Requester's GUID: one token per Requester and per computer, kept through pulls, branch switches, new clones and Unity upgrades. The Requester becomes an asset with no secret, to commit with the project
- The token is kept out of the project, not encrypted: Unity's preferences are stored in clear on the computer, and a key shipped in the package or stored next to the token wouldn't stop anyone able to read them. The token never appears in a log, a dialog or an exception, `Log Responses` included

### Upgrading from 1.6.0 or older
- **Tokens stored in a Requester are moved automatically**: on the first editor load with 1.7.0, and whenever a Requester is used, a token still stored in the asset is copied to this computer, removed from the asset, and the asset is saved right away. The copy comes first because Coda only shows a token once, and a token already set up on this computer for that Requester is never overwritten. A console message names each cleaned file. Then:
  1. Commit the modified Requester assets, so the token stops being shared
  2. Ask each team member to generate their own token and paste it in the Requester. Once the cleaned asset is pulled, anyone who relied on the shared token sees "No API token on this machine", and the buttons that call Coda are greyed out. A computer that opens the project with 1.7.0 before pulling runs the same cleanup (same diff, no conflict) and keeps a copy of the shared token until it is revoked
  3. Once everyone is set up, revoke the old token in your Coda account settings: it stayed readable by anyone with access to the project, git history included, and removing it from the asset doesn't change that
- A Requester that can't be written (locked by version control, or inside an immutable package) keeps its token in the asset: the token is still copied to this computer, a warning says so, and the cleanup runs again once the file is writable
- Going back to 1.6.0 or older: the asset no longer carries a token, paste it again in the Requester

### Added
- **The Requester inspector shows where the token stands**: no token on this computer, being checked, connected (with the Coda account name, email and token name), or rejected by Coda (invalid, expired or revoked). A pasted token is checked about a second later, and when the inspector opens, once per token per editor session. When the token can access all your Coda docs, a tip suggests restricting it to the game's doc: read only is enough to import, exporting asset references needs read and write
- **Test connection**, **Remove from this machine** and **How to get a token** buttons under the `API token` field. The inspector shows when the token was last checked
- **Connection status on the Table Importer and Asset Reference Exporter inspectors**: a warning when their Requester has no token on this computer or one Coda rejected, with a **Set up token** button that selects the Requester, and another when the token can't reach the document (restricted to another doc, or wrong document URL). Nothing is shown when all is well
- `CodaRequester.HasToken` and `ImporterExporter.Requester`

### Changed
- **Unity 6 (6000.0) or newer is now required**, declared in `package.json` so the Package Manager flags the package as incompatible with an older editor. Unity 2021 hadn't been able to compile the package since 1.0.5 anyway (`UnityWebRequest.PostWwwForm` only exists from Unity 2022.2), and Unity ended support for 2021.3 LTS and 2022.3 LTS in 2025. The README badges now show Unity 6.0 LTS and 6.3 LTS
- **Failed Coda responses say what to fix**, instead of the same raw `Empty/failed response` warning, which made a rejected token look like a network hiccup. The message follows what Coda answered: 401, token rejected; 403, the token doesn't grant access to the doc or table (restricted to another one, or read only when exporting); 404 and 410, doc or table not found; 429, rate limit; no answer or 5xx, Coda unreachable. What happens next (`Import aborted.`, `Operation aborted.`...) is still stated. A token or access problem also opens a dialog when the request came from a click, with **Set up token** for a rejected token
- **Without a token on this computer, nothing calls Coda**: Update Tables list, Import selected Tables, Validate Names, Export Assets References and Re-fetch from Coda (orphaned assets window) are greyed out with a tooltip saying why, and the table-list refresh that runs when an importer is inspected sends nothing. A teammate who never syncs sees no request, no progress bar and no warning. Clean orphaned assets stays available: it works on local data
- The automatic table-list refresh no longer sends a token Coda already rejected. The buttons stay enabled, so a click checks again
- Validate Names stops at a rejected token with a single message, instead of one warning per table. Other failures are still logged per table and the validation goes on
- `CodaRequester.APIToken` now returns the token stored on this computer for this Requester, or `""` when there is none. The serialized `_apiToken` field stays declared, hidden and empty, so an asset saved by any older version can still be migrated
- `ImporterExporter.GetTableList` takes an optional `userInitiated` parameter (`true` by default). With `false`, it opens no dialog and doesn't send a token Coda already rejected

### Fixed
- **A refused asset reference export passed for a sync**: a write Coda refused (a read-only token, typically) went unnoticed and still updated the last sync date. The failure is now reported with the table's name (`Asset references were not exported.`), only the first failure of an export opens a dialog, and the sync date is left untouched
- **Progress bar stuck on screen**: exporting asset references without a Requester or with an invalid document URL, or importing with no table selected, left the progress bar displayed behind the dialog

## [1.6.0] - 2026-08-28
### Fixed
- **Assets were not rewritten when the generated class changed**: adding a column in Coda regenerated the class, but only the assets whose row had also changed were written back. The others stayed on disk with the old schema, and a key absent from the YAML does not fall back to the Unity default for its type: it keeps the C# default, so a `string` field added to the class reads as `null` instead of `""` on every asset that was skipped. On one production project this turned an optional filter (`if (x.field != "" && ...)`) into a permanent refusal that no data change could clear, and a full audit found more than a hundred missing fields across nine tables plus two fields left over from deleted columns, accumulated silently since 1.3.0. The rewrite trigger is no longer "the row changed" but "the generated class changed": when a table's field set differs from the one its assets were written with, every asset of that table is rewritten, whether or not its row moved. Each one ends up carrying every key of the current schema (an empty column gives a key with an empty value) and losing the keys of fields the class no longer has. Row ids, GUIDs and `.meta` files are untouched, so no reference breaks
- The row-level diff introduced in 1.3.0 could never catch this on its own: it compares two in-memory objects under the same current class, so the YAML on disk never enters the comparison, and Unity serializes a null string as `""` anyway. The signal now comes from the class itself

### Added
- **Schema baseline stored on the Table Importer**: each table's field set, names and types, as of the last import that actually wrote its assets. It lives on the asset, so it is committed with the project and shared by the team. It is only advanced once the assets have been written, so an import cancelled or crashed between code generation and instance creation leaves it untouched and the next import redoes the rewrite instead of taking the drift for an up-to-date table
- The importer is now flushed to disk at the end of every import instead of waiting for a manual Save Project. The row-id cache and the sync dates, which had the same fragility since 1.4.0, become durable as well
- **"Schema changed" section in the import report**, naming the fields added and removed per table and how many assets were rewritten for that reason. A table with no baseline on record reports `no known schema for the assets on disk, all rewritten`
- Schema comparison covers field types and not just names, so a column retyped in Coda (Text to Number) is detected even though the field name does not change. Private fields carrying `[SerializeField]` are included, so a change to the generated `__codaRowId` itself would be caught too

### Changed
- The `created / updated / unchanged` counters stay driven by the data diff: an asset rewritten only because its class gained or lost a field carries the same data as before and is still counted `unchanged`. The accurate count introduced in 1.3.0 is preserved
- **Expect a large diff on the first sync after upgrading.** No table has a schema on record yet, so each imported table is rewritten once to repair whatever drift had accumulated. The diff stays proportional to that drift and not to the size of the tables: assets already carrying the current schema are rewritten with byte-identical content and do not appear in the diff at all. Assets carrying no `__codaRowId` belong to no row and are left alone; they surface through orphan detection instead

### Removed
- `SnapshotExistingFields()` and the `PreviousFields` EditorPrefs key. The class-change report used to compare a pre-codegen reflection snapshot against the Coda column names, two different vocabularies: it reported the generated `__codaRowId` as a removed field on every table, could not see a retype, and was overwritten at the start of every import, which meant an interrupted import lost the only record of the previous schema. Replaced by the persisted baseline, compared reflection against reflection

## [1.5.0] - 2026-08-08
### Added
- **"Clean orphaned assets" window**: 1.4.0 started reporting the assets whose Coda row was deleted, without offering any way to act on them. A new button on the Table Importer opens a dedicated window listing them in three groups: **🗑 Safe to delete** (row gone, nothing references the asset, ticked by default), **⚠ Deleted but referenced** (something in the project still points at it, unticked, expandable to see and ping every referencing asset) and **❓ Unmanaged** (no row id, made by hand or predating 1.4.0, listed for information and never deletable). Deletion goes through a confirmation naming the exact files, then `AssetDatabase.DeleteAsset`. The sync itself still never deletes anything
- Incoming references are found through Unity's own dependency database (reverse `AssetDatabase.GetDependencies`), so they are picked up whatever the serialization mode and even in scenes that aren't open. The scan walks every asset in the project, so it only runs on an explicit click (opening the window, or "Rescan references") behind a cancelable progress bar, never during an import. References coming from other assets in the same deletion batch are ignored, otherwise two orphans pointing at each other would protect one another forever
- Row ids of the last sync are now cached on the Table Importer, so the window opens on local data with no network call: fix a reference, hit "Rescan references", delete, all without re-importing. The window shows when those ids were last fetched and warns past a week; **Re-fetch from Coda** refreshes them on demand (one failed response aborts the refresh and leaves the cache untouched, so a table that didn't answer can never have all of its assets flagged as orphaned)
- **Hide tables from the import list**: on some docs, many Coda tables are useful in Coda but never imported into Unity (views, tables that only feed a select). Each row of "Available Tables" now has a **Hide** button that removes the table from the list and deselects it; a **Hidden tables (N)** foldout lists what was hidden, with an **Unhide** button per entry. **Hide all views** hides every table Coda reports as a view in one click. Hidden tables are never ticked by "Select All" and never imported. The list is stored per table id, so hiding survives a rename in Coda, a table-list refresh, and stays put if the table is later deleted in the doc (its entry remains in the foldout so it can be cleared)

## [1.4.1] - 2026-08-04
### Fixed
- **Table wiped when its class name collides with another class in the project**: existing assets were looked up with `AssetDatabase.FindAssets("t:{Table}")`, whose type filter only takes a short type name. In a project that also holds another class with the same name (e.g. a `Prop` MonoBehaviour alongside the generated `Prop` ScriptableObject), the filter resolved to the wrong script and returned nothing: every existing asset was missed, every row was treated as new, its re-creation was refused by the file-already-exists guard (`can't create "x": a file already exists at ...`), and the table's `_X_Database` list came out empty. The table folder is now enumerated directly, so asset identity no longer depends on the editor's search index

### Removed
- `AssetReferenceExporter.FindAssetsByType<T>()`, unused inside the package and carrying the same short-type-name search flaw

## [1.4.0] - 2026-07-06
### Added
- **Stable row-id identity for generated assets**: every generated class now carries a hidden serialized `__codaRowId` field stamped with the stable Coda row id (`i-xxxx`). Existing assets are matched by id instead of file name. On the first import after upgrading, assets are adopted by their current name and stamped; they all show as `updated` once, then stabilize
- **Renaming a row in Coda now renames the asset** instead of creating a duplicate and leaving the old file behind. Renames are listed in the import report: new "Renamed" section with old → new names, plus a per-table `renamed` counter
- **Orphan detection in the import report** (detection only, the sync never deletes anything): assets whose row id no longer exists in Coda are listed as `Orphaned (row deleted in Coda)`; assets with no row id and no matching row are listed as `Unmanaged (not linked to any Coda row)` and will never be offered for cleanup. Note: assets generated before 1.4.0 whose row was already deleted carry no id and appear as Unmanaged; clean those manually once
- New rows are created only after all renames are applied, and never overwrite an existing file: a name collision (e.g. with an orphaned asset) is skipped with a warning

## [1.3.1] - 2026-07-06
### Fixed
- **Import crash on `Curl error 61` (brotli)**: Coda's CDN sometimes answers with brotli compression, which Unity's libcurl cannot decode, leaving an empty response body that crashed the import with `JsonSerializationException`. All API requests now pin `Accept-Encoding: gzip, deflate` so the CDN only replies with encodings Unity can decode
- **Remaining unguarded deserializations**: `OnTableStructureResponse` (table structures) and `OnTablesDataResponse` (rows) now guard against empty/failed responses, following the 1.3.0 guard on the table list. A failed response logs a clear `[CodaSync]` warning naming the table and aborts the import cleanly instead of throwing (no partial generation that would break lookups)
- Progress bar no longer stays stuck on screen when the table-list request fails
- **Ghost "Requesting tables list" requests**: the table list used to re-fetch on every `OnValidate` (script recompile, `AssetDatabase.Refresh`, editor focus regain, Play mode, the sync itself). It now refreshes only when the importer is actually inspected, when "Update Tables list" is clicked, or when the document URL really changes
- Import progress label counted hidden Type Tables as selected ("8 selected tables" when 3 were ticked); it now reports only the tables the user selected

## [1.3.0] - 2026-05-29
### Added
- **"Do-not-edit" header on generated classes** — every generated `{Table}.cs` and `{Table}_DB.cs` now starts with an `// <auto-generated>` banner naming the source Coda table and explaining that edits are overwritten on the next sync. Marks the file as generated for Roslyn/StyleCop analyzers and for AI agents, while clarifying that a temporary edit to fix a compile error is OK but won't persist
- **`unchanged` category in the import report** — assets summary and per-table detail now distinguish `created` / `updated` / `unchanged` / `skipped`

### Improved
- **Accurate `updated` count** — the import report previously marked every pre-existing asset as `updated` (it only meant "the asset already existed"). It now diffs each pre-existing asset's serialized state before/after the field-assignment pass (lookups included) and only counts a real change as `updated`, otherwise `unchanged`
- Unchanged assets are no longer marked dirty or re-saved, keeping the consumer's git diff clean and avoiding needless asset rewrites
- `OnValidate` no longer fires a Coda API request synchronously — the table-list refresh is debounced via `EditorApplication.delayCall`, so script reloads / entering Play mode / asset re-imports no longer trigger one request per event

### Fixed
- Importer no longer throws `JsonSerializationException: No JSON content found` when the Coda API returns an empty or failed response (rate-limit, timeout, token cooldown, network loss) during `OnValidate` — the empty/failed response is now guarded and logged as a `[CodaSync]` warning instead of crashing

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
