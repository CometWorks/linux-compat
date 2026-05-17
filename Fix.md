# In-Game Script Editor Enter Key Fix

## Root Cause

The Linux SDL input path did not add Enter to the game's buffered text input.

- `SdlGameWindow.HandleEvent` mapped SDL Enter keydown to `MyKeys.Enter`, but only injected `\b` into the text buffer for Backspace.
- SDL3 `SDL_EVENT_TEXT_INPUT` only delivers printable text, not control characters like Enter.
- `MyGuiControlMultilineEditableText` inserts a newline only when buffered text contains `\r`.
- On Windows, `WM_CHAR` is buffered directly, including Enter as `\r`.

As a result, the script editor saw the Enter key state on Linux, but it never received the `\r` character required to insert a newline.

## Fix

Inject `\r` into the buffered text input on SDL keydown when the mapped key is `MyKeys.Enter`, matching the Windows `WM_CHAR` behavior used by the game UI controls.

This keeps the existing Backspace compatibility shim and extends it to Enter.
