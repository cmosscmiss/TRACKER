TRACKER 1.0 RC 1
================

Price tracking for products (Amazon and other stores).


HOW TO RUN IT
-------------
Unzip the folder wherever you like and run Tracker.exe. There is no installation:
you can keep it on the desktop, in a programs folder or on a USB stick.

The first time, Windows may show the "Windows protected your PC" warning
(SmartScreen). That is normal for programs without a digital signature: click
"More info" and then "Run anyway".


REQUIREMENTS
------------
- Windows 10 version 1809 (October 2018) or later, 64-bit.
- WebView2 runtime. It ships with Windows 11 and with up-to-date Windows 10
  installs. If the browser pane shows up blank, or the app reports that it cannot
  start it, install it from:
  https://developer.microsoft.com/microsoft-edge/webview2/

You do NOT need to install .NET: this build carries everything it needs.


YOUR DATA
---------
The product database, the settings and the error log are stored in:

    %LocalAppData%\Tracker

That is, OUTSIDE this folder. To update to a newer version just replace the
program folder: you will not lose your products or their price history. To move
everything to another machine, copy that data folder as well.


STARTUP AND SYSTEM TRAY
-----------------------
By default the app registers itself to start with Windows and stays hidden in the
system tray, reading prices in the background; the window's X button hides it
there instead of closing it. To quit for good, use the tray icon's menu (right
click -> Exit). All of this can be changed in Settings -> General, WINDOW section.

If you move the program folder, the automatic startup fixes itself the next time
you open the app.
