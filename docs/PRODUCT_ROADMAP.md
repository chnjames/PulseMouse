# Product roadmap

## Product position

**PulseMouse is a mouse-first trigger layer for a few actions users already know by heart.** It is not trying to replace a launcher, macro platform, or command palette. Its advantage is keeping the pointer and visual attention in the current task.

## Current pre-release: v0.9.0

The current codebase includes:

- rhythm shortcuts with right-click chord, side-button, and classic middle-button input;
- per-application meanings through Context Pulse, with global fallback and pinned mappings;
- app, folder, URL, shortcut, workflow, repeat-last-action, and directional-gesture actions;
- local configuration, import/export, diagnostics, history, Chinese/English UI, and safety fallback for unsafe targets.

Automated Core smoke tests pass. Live validation of global input interception, target activation, scaling, and compatibility remains the release gate; see [RELEASE_CHECKLIST.md](RELEASE_CHECKLIST.md).

## Next release priorities

1. **Release confidence** — complete manual validation, package an installer, code-sign binaries, and publish checksums.
2. **Onboarding** — guide a new user through choosing an input method, binding three actions, and practising the first rhythm.
3. **Trust and compatibility** — improve multi-display/DPI, mouse hot-plug, sleep/wake recovery, accessibility, and diagnostics.
4. **Evidence before expansion** — run a small usability study focused on retention, daily successful actions, and unmatched-input rate before adding broad automation features.

## Non-goals for now

- Becoming a general-purpose automation marketplace.
- Cloud accounts, telemetry, or configuration sync by default.
- Use in games or environments protected by anti-cheat software.
