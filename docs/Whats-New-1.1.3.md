# AO Nexus v1.1.3 — Resize rendering fix

Custom panels, buttons, the header and news cards now redraw their full surface when resized. Native dropdowns invalidate their custom border and arrow area after Windows finishes resizing them. This removes old border fragments left inside maximized or resized windows.

The regression check reproduces the missing repaint on v1.1.2 and passes 22 checks with the fix: 16 grow/shrink cases plus three maximize/restore cycles for the team and account panels. Surrounding layout and themed-control checks cover the affected controls. Guide screenshots are refreshed from v1.1.3.

Game GUI remains experimental. All [v1.1.2 features](Whats-New-1.1.2.md) are included. Login behaviour, saved settings and the backend are unchanged; experimental parallel login remains excluded.
