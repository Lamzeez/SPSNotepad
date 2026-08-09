# SPS Notepad

SPS Notepad is a highly optimized, lightweight, and single-executable tabbed text editor designed for maximum time-efficiency and zero bloat. Built natively in C# WinForms, it runs seamlessly on Windows without requiring any heavy frameworks.

## Features

- **Dual-Mode Tabs:** 
  - *Notepad Mode:* A clean, distraction-free plain text editor.
  - *Tracker Mode:* A structured form featuring specific fields (`Tag#`, `Address`, `ResN`, `VisN`, `Note`) for rapid data entry.
- **Tear-Off Windows:** Drag and drop tabs outside the main window to spawn independent tear-off windows that instantly synchronize their state.
- **Intelligent Save Engine & Dirty Tracking:** Automatically tracks modifications (indicated by a `*` in the tab title). When you close the app, it intelligently skips blank or unmodified tabs and only prompts you to save the work that matters.
- **Single-Instance File Routing:** An advanced single-instance controller guarantees that if you double-click a file that is already open, the app will instantly locate the window holding that file, forcefully bypass Windows background locks, and snap the correct tab to the front of your screen.
- **Cross-Window Search:** A fast, persistent `Ctrl+F` find bar that can search across all open tabs and windows simultaneously.
- **Instant Cache:** Your unsaved tabs and windows are instantly persisted in a highly optimized local `cache.xml` file, ensuring you never lose your workspace even if the app is abruptly closed.
- **Persistent Zoom:** Use `Ctrl + ScrollWheel` to adjust text size dynamically across all windows and tabs.

## Installation

No installation required! Simply download `SPSNotepad.exe` and double-click to run.

## Building from Source

This application can be compiled instantly without Visual Studio using the native C# compiler (`csc.exe`) included with the .NET Framework on Windows.

Open PowerShell in the project directory and run:

```powershell
& "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /target:winexe /out:SPSNotepad.exe /win32icon:logo.ico /reference:Microsoft.VisualBasic.dll /reference:System.Xml.dll SPSNotepad.cs
```
