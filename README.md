# Onlyfun for Android

Onlyfun is an Android-only Mophun emulator built with Unity. This fork focuses on getting legacy Mophun games running on modern Android phones with as little setup as possible.

## Download

Download the newest Android APK from the [Releases](https://github.com/willlrock/onlyfun/releases) page.

The current development build targets Android 8.0+ (API 26) and ARM64 devices. It is a test build: install it manually and allow Android to install apps from your browser or file manager when prompted.

## Import a game

1. Open Onlyfun and tap **+**.
2. Select the original `.mpn` file. If the game is split into numbered parts such as `1_4_Game.mpn` … `4_4_Game.mpn`, select all parts in the same picker operation. You can select its related `.mpc` resource packs at the same time.
3. Onlyfun assembles multipart MPNs in numeric order, imports the MPC resources into the game's private folder, validates the result, and detects encrypted Mophun code before decrypting supported files locally when needed.

The original file is never modified. Decrypted working copies are stored in the app's private storage and cached by the source file's SHA-256, so the same game is not decrypted again on every import.

No key files, desktop tools, manual conversion, or extra user steps are required.

If only one part of a multipart game is selected and the other parts are not available to the Android file provider, Onlyfun reports which parts are missing instead of trying to launch a broken file.

If a game uses an unsupported Mophun format, Onlyfun shows a readable error and records the technical details in `onlyfun.log`.

## Honey Cave 2

The Android build includes the encryption profile required by the encrypted Honey Cave 2 MPNs commonly found in preservation archives. After import, the game should launch in the same way as an already decrypted copy.

## Game configuration

Open a game's settings before launch, or use the gear button while it is running. Some Sony Ericsson games require a matching phone model and system version 1.30; Honey Cave 2 uses its legacy compatibility profile automatically.

## Reporting a problem

Please include:

- Android model and OS version;
- the Onlyfun APK version;
- the exact error shown in the app;
- `onlyfun.log` from the export dialog.

Do not upload commercial game files or ROMs to the repository.

## Development

This project is intended to be opened with Unity **6000.5.6f1 (Unity 6.5)** and the Android Build Support module. The Android player is built with IL2CPP and API 26 minimum SDK.

The encrypted-game importer is organized around profiles, so additional Mophun encryption families can be added without changing the Android import flow.

## Credits

Onlyfun is based on the original Nofun project by Radrat Softworks. Thanks to JaGoTu for the decompression work, 1upus for help with Mophun encryption, and the Kahvibreak preservation community for recovered resources.

## License

Copyright 2023 Radrat Softworks.

The source code is licensed under the [Apache License 2.0](LICENSE).
