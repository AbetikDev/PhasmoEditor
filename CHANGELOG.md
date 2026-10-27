# Changelog

## 3.1.1

- Added Save old version: export the current encrypted game save to the Windows desktop.
- Every export has a unique dated filename; pending editor changes are not included.

## 3.1.0

- Automatically load the most recently modified save on startup and refresh every three seconds.
- Preserve pending edits and block stale writes when the game or cloud changes the file.
- Read Easy Save numeric dictionary keys and preserve the original plaintext outside edited values.
- Inline validation, unsaved-change protection, Ctrl+S and F5 shortcuts.
- Install the standalone app with all bundled components and a desktop shortcut in one click.
- `Install-and-build.cmd` installs the .NET 10 SDK locally, restores packages, tests and builds the EXE.
- Verified reading the local save and editing/restoring a temporary copy; game-side behavior was not tested.

## 3.0.0

- Redesigned interface with a dark Phasmophobia-inspired palette.
- English / Russian switching with saved language preferences.
- Recursive save discovery and manual file selection.
- Current values and a review of changes before saving.
- Backup history, restore and legacy backup support.
- Verified writes and detection of externally changed files.
- Standalone Windows x64 EXE with .NET included.
- Custom executable icon and an author GitHub link.

Tested on **Phasmophobia v0.19.1.0**.
