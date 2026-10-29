# Predator Control 1.4.0 revision 1

Continues the 1.4.0 release; this is not a 1.4.1 release.

- Product/base version: **1.4.0**.
- Informational version: **1.4.0+rev.1** (SemVer build metadata).
- File/manifest version: **1.4.0.1**; assembly version: **1.4.0.0**.
- Jelli displays **1.4.0 rev 1**.

## UI scope

Only the **expanded Jelli dashboard** is compact: 480 × 590 logical pixels, compared with the original 1.4.0 dashboard's 760 × 760. Its cards, spacing and headings are condensed; compatibility details remain expandable. The attached flyout uses a 480-pixel panel and the original companion area.

The floating jellyfish retains its original dimensions, drawing, interactions and animations. The previous dashboard-only 72% mascot scale has been removed, and the original 94-pixel space above the panel is restored.

The Classic panel, game-profile form, Classic keyboard UI and designer layout match the 1.4.0 source baseline. Comparison against the pre-1.4.1 backup confirmed those files had not been resized during the compact Jelli iteration. The shared Nitro keyboard fixes from 1.4.0 remain intact.

## Validation and use

Full self-contained Release publish, TypeScript build, ESLint and all six frontend tests pass. Simulated-browser checks confirm a full-size 140 × 180 companion container with no scaling and a compact expanded panel. No application hardware commands or OEM service changes were executed. Physical Nitro model/BIOS validation remains pending as described in the 1.4.0 notes.

Extract the revision ZIP into a new folder and close any older running Predator Control instance before launching its executable. Previous local 1.4.0/1.4.1 builds are preserved for rollback; use the ZIP in `releases/1.4.0/revision-1/` for this revision. Jelli requires the installed WebView2 Runtime; .NET is bundled.
