# Cells

Cells is a small Windows tray app that shows the battery level of every wireless device it can find: headsets, mice, keyboards, gamepads, earbuds and the laptop battery itself. It talks to the devices directly over HID and Bluetooth, so no vendor software (Synapse, G Hub, iCUE, SteelSeries GG) needs to be running. The look is borrowed from Teenage Engineering hardware: a flat light grey body, black hairlines, numbered channels with one bright colour each, and level meters drawn as rows of ticks.

## Using it

The tray icon itself stays a plain icon. Hover it and a card slides up above the taskbar with every device, its level, a tick meter and its state (charging, low, asleep, last seen). Click the icon to pin the card open; in that mode a square next to each device shows whether it is listed on hover, and clicking a row toggles it. Right-click the icon for everything else.

The desktop widget shows the same list. It sits under all windows like a desktop gadget, never takes focus, stays out of Alt+Tab, and when the desktop is brought forward (Show desktop, Win+D) it lifts itself above it. Drag it anywhere; the position is remembered. The menu can lock it in place, switch between three sizes and between the light and dark theme, or hide it.

Right-clicking a device on the widget or the card gives per-device actions: show it on hover or not, rename it, hide it everywhere, or forget a device that is no longer around. Hidden devices come back from the Hidden devices submenu.

A notification appears once when a device drops to the low battery level (20% by default, adjustable or off), and optionally when a charging device reaches 100%. Devices are checked every minute by default (anything from 10 seconds to 30 minutes can be picked), again right after something is plugged in or the PC wakes up, and on demand with Refresh now.

Windows 11 puts new tray icons behind the arrow. Drag the icon from there onto the taskbar to keep it visible.

## Supported devices

| Source | Devices | How the level is read | Checked on real hardware |
| --- | --- | --- | --- |
| System | Laptop battery, UPS | Windows power status | no |
| PlayStation | DualSense, DualSense Edge, DualShock 4 over USB or Bluetooth | battery byte in the input report | DualSense over USB |
| Razer headsets | Barracuda X (both receivers), BlackShark V2 / V2 Pro / V2 HyperSpeed / V3 / V3 Pro / V3 X | Airoha "PA" frames or the receiver's own report | Barracuda X, receiver 1532:0552 |
| Razer | Wireless mice and keyboards | 90-byte feature report, command class 07 | no |
| Logitech | Unifying, Bolt and Lightspeed receivers, direct USB and Bluetooth, G headsets | HID++ 2.0 battery features (1004, 1000, 1001, 1F20), HID++ 1.0 registers | no |
| SteelSeries | Arctis 1 / 7 / 9 / Pro / Pro Wireless, Arctis Nova family, Aerox, Prime and Rival wireless mice | vendor output reports | no |
| Corsair | Void, HS70, HS80 and everything on Slipstream receivers | legacy headset report and the Bragi protocol | no |
| HyperX | Cloud II / III / Alpha / Alpha 2 / Flight / Stinger Core wireless, Pulsefire Haste 2 | per model vendor reports | no |
| Glorious and friends | Glorious, Hitscan, G-Wolves and WLMouse wireless mice | 65-byte feature report, command 83 | no |
| MCHOSE | A7 Ultra, A7 V2 family and L7 Pro over the receiver or cable | inverted feature report 11, commands 03 and 06 | no |
| Compx family | Cheap 2.4 GHz mice on Compx, Areson and SinoWealth chips (Redragon, Fantech, Attack Shark, VXE, Corsair Sabre and other rebrands) | 17-byte report 08, command 04 | receiver 25A7:FA7B |
| Bluetooth | Any paired device Windows already knows the level of | device property shown in Bluetooth settings | no |
| AirPods and Beats | While a pair is connected to the PC | Apple proximity adverts, nearest signal | no |
| Xbox | Xbox controllers | Windows.Gaming.Input, XInput as a fallback | no |
| Generic HID | Devices that report the standard Battery Strength usage | HID usage 06:20 | no |

The Compx family is matched by the shape of its vendor collection rather than by brand, so rebrands that are not listed usually work too. The app only sends queries; the Razer headset readers switch the headset into the vendor app's remote mode for the moment of the query and back, the same way Synapse does.

## When a device is missing

Save device report in the tray menu writes every HID collection on the PC plus what each reader saw into a text file and opens it. That file is what is needed to add a new device. The same report comes from `Cells.exe --scan`.

A Barracuda X that keeps showing OFF while it plays sound has usually lost its data link after the receiver restarted (unplugged, or the USB port went to sleep). Sound keeps working but the headset stops answering battery queries until it is switched off and on once.

Vendor apps can hold the same receiver. If a device appears in Synapse or G Hub but not here, quit the vendor app and press Refresh now.

## Install

Releases are a single `Cells.exe`. Started from anywhere outside its install folder it offers to install itself for the current Windows user, without admin rights: it copies itself to `%LOCALAPPDATA%\Programs\Cells`, adds a Start menu shortcut, starts with Windows in the tray and appears in Settings > Apps, where it can be uninstalled like any other program. Just run it keeps it portable. Settings and remembered devices live in `%LOCALAPPDATA%\Cells`.

## Building

Needs the .NET 10 SDK on Windows 10 2004 or later.

```powershell
dotnet build src/Cells
```

`publish.ps1` builds a Release copy straight into the install folder and starts it, replacing a running copy. With `-SelfContained` the exe runs on machines without .NET installed; `-Target` puts it somewhere else.

The `CELLS_DATA_DIR` environment variable points the app at another data folder. A copy started that way runs next to the installed one and never touches autostart, which is how changes are tested without disturbing the real install.

## Code layout

`Hid` holds a thin P/Invoke layer over hid.dll: enumeration with collection usages and report lengths, feature reports, and overlapped reads with timeouts. Every reader in `Providers` implements one protocol family and adds readings to a shared poll; `DeviceMonitor` runs them on a background thread, merges the results into the remembered device list and keeps devices that went quiet with their last level. `UI` contains the hand-drawn list used by both the widget and the tray card, the tray icon on Shell_NotifyIcon version 4 (which is what makes a custom hover card possible), the menus and the dialogs.

## Credits

The vendor protocols were worked out by other people. The byte layouts here follow the notes and sources of OpenRazer, HeadsetControl, Solaar, BatteryHub (Wireless-Device-Battery-Monitor), barracuda-x-battery, razer-battery-barracuda-x, redragon-mouse-linux, AirStatus, Voltide and OpenMouse. No code was copied from them.
