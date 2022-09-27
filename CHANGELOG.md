# Changelog
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]


## [1.0.0] - 2022-08-26
### Added
- Requester Scriptable Object to setup your credentials with Coda.io
- TableImporter Scriptable Object to setup the connexion with a Coda doc you have the rights for (i.e. owner, editor, or viewer)
- AssetReferenceExporter Scriptable Object to setup the connexion with a Coda doc you have the rights for to export the parameters of the assets you want to link in your Coda doc
- Generation of Scriptable Object classes, database classes, and the scriptable object instances, based on your synced tables on your Coda doc
- References to other generated instances for lookup columns or columns with a reference to a referenced asset (e.g. a sprite)
