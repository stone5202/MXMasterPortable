# MX Master Portable

<p align="center">
  <a href="README.md">繁體中文</a> · <strong>English</strong>
</p>

<p align="center">
  <img src="src/app.png" width="128" alt="MX Master Portable app icon">
</p>

MX Master Portable is a portable Windows utility for the Logitech MX Master 4. It is a single EXE that needs no administrator rights and installs no driver or service, yet lets you remap buttons, use gestures and an Actions Ring, and adjust pointer speed, SmartShift and haptic feedback. It is meant for PCs where Logi Options+ cannot be installed.

> Current version: 1.1 · Interface languages: Traditional Chinese, English (follows the system by default)

See the [changelog](CHANGELOG.en.md) or [GitHub Releases](https://github.com/stone5202/MXMasterPortable/releases) for what changed in each version. The changelog is also available in [Traditional Chinese](CHANGELOG.md).

> This is an independent open-source project. It is not affiliated with, endorsed by or sponsored by Logitech. Logitech, MX Master, Logi Options+ and Logi Bolt are trademarks of Logitech.

## Screenshots

<p align="center">
  <img src="docs/screenshots/buttons.en.png" width="80%" alt="Buttons page: mouse diagram and the button mapping list">
</p>

<p align="center">
  <img src="docs/screenshots/ring.en.png" width="45%" alt="Gestures and ring page">
  &nbsp;
  <img src="docs/screenshots/pointer.en.png" width="45%" alt="Pointer and wheel page">
</p>

<p align="center">
  Button mapping · Gestures & ring · Pointer & wheel
</p>

## Download and use

1. Download `MXMasterPortable.exe` from [Releases](https://github.com/stone5202/MXMasterPortable/releases/latest).
2. Put it in any folder you can write to (settings are stored next to the EXE) and run it.
3. By default the app starts minimized to the system tray. Double-click the tray icon to open settings; use the tray icon menu to exit.

The EXE is not code-signed, so Windows SmartScreen may warn on first launch. On managed PCs with WDAC, AppLocker or Smart App Control, IT approval may be required. This app does not, and should not be used to, bypass those policies.

## Features

- **Button mapping**: choose an action for the middle button, gesture button, both side buttons, thumb wheel left and right, the Actions Ring button and the wheel mode button.
- **Built-in actions**: about 50, including back / forward, copy / paste / cut, undo / redo, select all / save / find, Task View, show desktop, switch virtual desktop, Alt+Tab, minimize / maximize / close / snap window, play / pause, previous / next track, volume and mute, File Explorer, Windows Settings, Search, Start menu, Calculator, Task Manager, screenshot, emoji panel, clipboard history, quick settings, lock PC, browser tabs (new / close / reopen / refresh / zoom), type today's date or the current time, Enter, Esc, Delete.
- **Custom actions**: any key combination (for example `Ctrl+Shift+T` or `Win+R`), or launch a program or open a file, folder or URL.
- **Actions Ring**: a translucent eight-way ring with all eight positions customizable; the editor shows a numbered picture of the ring. Use it in hold mode (hold to show, move to choose, release to run) or click mode (press once to show, then left-click a slice, with no holding).
- **Four-way thumb gestures**: hold the gesture button and swipe: left / right for back / forward, up for Task View, down for show desktop.
- **Device settings**: shows battery level and firmware version; adjusts pointer speed (DPI), SmartShift, wheel scroll direction (standard / inverted) and haptic feedback strength. Values you apply are saved and re-applied at the next start.
- **Connections**: Bluetooth LE and the Logi Bolt receiver.
- **Also**: every setting is saved automatically, only one instance runs, mapping can be paused from the tray, and the interface switches between Chinese and English.
- **Engineering mode**: view the raw HID events the mouse sends, to identify buttons or report problems.

## Requirements

- Windows 10 or Windows 11
- .NET Framework 4.x (included with Windows; nothing to install)
- Logitech MX Master 4 (Bluetooth LE or Logi Bolt)

## Known limitations

- Developed and tested only on the MX Master 4 (firmware `RBM 27.03`). Other models may only get standard button mapping.
- No Flow, Easy-Switch management, per-application profiles or firmware updates.
- The four thumb-gesture directions have fixed actions for now.
- Device settings are re-applied once at start-up only, not when the mouse reconnects while the app is running.
- The low-level mouse hook cannot tell devices apart: with several mice connected, side-button and horizontal-scroll mappings apply to all of them.
- When Logi Options+ runs at the same time, the two can overwrite each other's button diversion and device settings. Use one or the other.
- Button positions on the diagram are schematic and have not been checked one by one against the hardware.

## How it works

- Standard buttons and horizontal scroll: a Windows low-level mouse hook (`WH_MOUSE_LL`) plus `SendInput` for keyboard actions.
- Special buttons (gesture button `0x00C3`, Actions Ring button `0x01A0`): temporary diversion is set through Windows' built-in HID interface with HID++ 2.0 feature `0x1B04`, and button notifications arrive through Raw Input. Diversion is released when the app is paused or exits.
- Device settings: HID++ features `0x1004` (battery), `0x0003` (firmware), `0x2201` (DPI), `0x2111` (SmartShift), `0x2121` (wheel scroll direction) and `0x19B0` (haptics). Every write is read back to confirm it.

HID++ feature numbers and packet layouts follow the public implementations in the open-source projects [Solaar](https://github.com/pwr-Solaar/Solaar) and [logiops](https://github.com/PixlOne/logiops).

## Privacy

- The app makes no network connections and contains no analytics, advertising or tracking.
- Settings are stored in `mxmaster-portable.ini` next to the EXE.
- Mouse events are written to `mxmaster-events.log` next to the EXE only while capture is on in engineering mode. The log holds button and HID packets, never keyboard input.
- No driver, service, scheduled task or start-up entry is installed, and the registry is not modified.

## Build from source

No Visual Studio or .NET SDK is needed; the C# compiler that ships with the Windows .NET Framework is enough:

```powershell
git clone https://github.com/stone5202/MXMasterPortable.git
cd MXMasterPortable
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

The output is `build\MXMasterPortable.exe`. The whole app is one source file, `src/Program.cs` (C# 5, WinForms), with no external dependencies.

## Project structure

```text
src/
├── Program.cs        # All of the code
├── app.ico           # EXE and window icon
└── app.png           # Logo shown in the interface
tools/
└── make-icon.ps1     # Generates app.ico / app.png
docs/
└── screenshots/      # Screenshots used in the README
build.ps1             # Build script
```

## Contributing

Issues and pull requests are welcome. When reporting a button or device problem, attach the engineering-mode log and mention the connection type (Bluetooth or Bolt) and the mouse firmware version.

Before submitting a change, make sure `build.ps1` builds with no warnings, and keep the app portable, free of administrator rights and free of external dependencies.

## License

This project is licensed under the [MIT License](LICENSE).
