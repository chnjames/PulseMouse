# Changelog

All notable user-facing changes are documented here. This project follows the spirit of [Keep a Changelog](https://keepachangelog.com/) and uses semantic versioning.

## [Unreleased]

### Added

- Context Pulse: context-aware command mappings based on the window under the pointer.
- Pinned global mappings, per-application mappings, a rhythm map, browser templates, and a repeat-last-action command.
- Captured target-window activation for shortcut actions, with safe failure when the target is gone or cannot be activated.
- Context snapshots, schema 9 migration, id-based command history, meaning reminders, and command heat data.
- Middle-button delayed decision: native click replay, directional gesture recognition, and native stationary-hold pass-through.
- Expanded Chinese/English UI localization and responsive settings layout.

### Changed

- The public documentation now describes the v0.9 pre-release state and distinguishes automated checks from pending manual validation.
- Product version shown in the window is `v0.9.0`.

## [0.8.0] - 2026-10-13

### Added

- Rebuilt WPF shell with Dashboard, Commands, Gestures, Workflows, and Settings views.
- Right-click chord input, automatic side-button detection, shortcut actions, usage history, hesitation hints, and state-aware tray icons.
- A portable build script and Core smoke tests.

## Earlier history

Earlier experimental releases existed before public packaging. Their source history remains available through Git; obsolete design notes and historical test reports are intentionally not carried as active documentation.
