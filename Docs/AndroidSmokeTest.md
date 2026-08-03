# Onlyfun Android smoke test

This checklist covers the first Android reliability increment. It does not require a
commercial game to be checked into the repository.

1. Build and install a development APK, then launch it from the Android launcher.
2. Confirm that the game library appears and that the application does not ask to be
   opened through a file manager.
3. Tap **Install**, select `HoneyCave2.mpn` in the stock Android Files picker, and
   confirm that the game appears in the library.
4. Disconnect or remove the original source file and confirm that the imported game
   remains available. The private copy is stored under `persistentDataPath/__Games`.
5. Open the game's settings and confirm `101x80`, portrait, Sony Ericsson T310,
   Mophun 1.30, interpreter, and 15 FPS.
6. Start the game. If loading fails, confirm that an error is shown, the app stays
   open, and the library can be used again.
7. Collect `onlyfun.log` from the application's persistent data directory and confirm
   that it contains the exception type, message, stack trace, and loading context.

Known scope limitation: external `ACTION_VIEW`, `ACTION_SEND`, and `onNewIntent`
imports are planned for the next Android lifecycle increment. This checklist tests
the in-app system picker path.
