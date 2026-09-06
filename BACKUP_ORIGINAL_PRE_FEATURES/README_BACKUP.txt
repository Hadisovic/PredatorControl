PREDATOR CONTROL - BACKUP OF PREVIOUS VERSION (PRE-HARDWARE-FEATURES)
Created: 2026-09-05 01:24:08

This directory contains pristine backups of all files before the reverse-engineered
PredatorSense 5.0 hardware features were implemented:

1. PredatorControl-standalone.exe: Original compiled standalone executable (September 4, 2026).
2. PredatorControlApp.dll: Original compiled DLL (627 KB, 12:15 AM).
3. Acer-P-Helper-main_original.zip: Untouched original repository archive.
4. src\Form1.cs: Original main dashboard form prior to GPU MUX, LCD Overdrive, WinKey, etc.
5. src\WmiController.cs: Original WMI driver prior to new hardware control dispatches.
6. src\PredatorKeyHook.cs: Original physical key hook prior to WinKey suppression.
7. src\GameProfile.cs: Original game profile class.
8. src\GameSyncForm.cs: Original Game Sync configuration dialog.

TO RESTORE PREVIOUS STATE:
Copy the files from this directory back to their parent folders.