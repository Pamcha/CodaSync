# Changelog
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]


## [1.0.0] - 2022-08-26
### Added
- Requester Scriptable Object to setup your credentials with Coda.io (see doc to see how to find your API Key on Coda)
- TableImporter Scriptable Object to setup the connexion with a Coda doc you have the rights for (i.e. owner, editor, or viewer)
- AssetReferenceExporter Scriptable Object to setup the connexion with a Coda doc you have the rights for, in order to store the reference of the assets you want to link in your Coda tables. you can currently reference Sprites, Audioclips.
- Generation of Scriptable Object classes, database classes, and the scriptable object instances, based on the tables you have chosen to sync in your Unity project
- References to other generated instances (i.e. another generated scriptable object) for lookup columns in Coda or columns with a reference to a referenced asset (e.g. a sprite). Tables that are referenced in lookup columns must also be synced with the Unity project or they will generate a missing reference error
