# Changelog
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
