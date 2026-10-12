# Changelog

<p>
  <a href="CHANGELOG.md">繁體中文</a> · <strong>English</strong>
</p>

## 1.0 — 2026-10-13

First public release.

### Buttons and actions

- Map the middle button, gesture button, both side buttons, thumb wheel left and right, the Actions Ring button and the wheel mode button.
- Built-in actions: back / forward, copy / paste / cut, undo / redo, Task View, show desktop, switch virtual desktop, Alt+Tab, play / pause, volume, mute, Enter, Esc.
- Custom shortcuts (any key combination, including the Win key) and launching a program or opening a file, folder or URL.

### Gestures and ring

- Actions Ring: hold to show a translucent eight-way ring with an icon and label per slice; all eight directions are customizable.
- Four-way gestures on the gesture button: left / right for back / forward, up for Task View, down for show desktop.

### Device

- Works over Bluetooth LE and the Logi Bolt receiver.
- No Logi Options+ needed: the app sets temporary HID++ diversion for the gesture and Actions Ring buttons itself, and releases it when paused or closed.
- Shows battery level and firmware version.
- Adjusts pointer speed (DPI), SmartShift and haptic feedback strength; applied values are saved and re-applied at the next start.

### Interface

- Four pages: Buttons (with a mouse diagram), Gestures & Ring (with a live ring preview), Pointer & Wheel, and Advanced.
- Interface language switches between Traditional Chinese and English, following the system by default.
- Starts minimized to the system tray by default; this can be turned off on the Advanced page.
- Every setting is saved automatically to `mxmaster-portable.ini` next to the EXE.
- Single instance: launching it again brings up the running window.
- Engineering mode window: view raw HID events and write them to a log file.

### Other

- A single EXE with no installer and no administrator rights; installs no driver, service or start-up entry and makes no network connections.
