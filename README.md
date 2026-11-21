# Universal-IVA-Editor

<img width="1280" height="720" alt="Thumb" src="https://github.com/user-attachments/assets/3b861b73-bd2b-43d7-a291-3e82f69bca6e" />

Add / replace IVA and props in-game — and more.

**Public release: 0.5.3** · KSP 1.12.x · [MIT License](LICENSE)

Universal IVA Editor lets you assign existing Internal Spaces to crewed parts and edit their placement, props, seats, cameras, entrances, and ladders in the VAB/SPH. Save your work as shareable per-part presets without editing another mod's part.cfg.

## Installation

1. Download the binary ZIP from [Releases](https://github.com/jongshh/Universal-IVA-Editor/releases).
2. Merge its GameData folder into your KSP installation.
3. Install HarmonyKSP and the IVA/prop asset mods you intend to use.
4. Start KSP and open IVA Editor from the VAB/SPH toolbar.

When updating, preserve `GameData/UniversalIVAEditor/UserPresets`.

### Required dependencies

- KSP 1.12.x
- HarmonyKSP
- The IVA and prop assets referenced by your presets

### Optional dependencies and integrations

- **ModuleManager:** required by many external mod configurations; the framework does not bundle it.
- **RasterPropMonitor (RPM):** RPM displays, instruments, and switches.
- **ASET Props / ASET Avionics:** install the packages and dependencies required by the particular props you use.
- **MOARdV Avionics Systems (MAS):** MAS displays and props.
- **FreeIVA:** internal movement and FreeIVA entry/exit features, subject to the IVA's support.
- **FAR / Chatterer / HullCamVDS:** supported through the relevant prop integrations. Some features also need a suitable provider or camera on the vessel.
- **Reviva + B9PartSwitch:** saved presets for the selected part can appear in Reviva's IVA Switch. Further testing across different mod combinations is welcome.

Third-party models, textures, and DLLs are not included. **Currently Probe Control Room Recontrolled is unsupported.**

## How to use this mod

For a basic overview, watch the [demo video](https://www.youtube.com/watch?v=OH0dZ3DR1M0). It shows an older UI, so button names and layout may differ from the current version.

1. Open IVA Editor and select a crewed part.
2. Choose an Internal Space in IVA placement, or open User Presets and use Import selected.
3. Use the gizmos or numeric fields to edit the IVA, props, and seats. Camera and EVA provide camera connections, entry points, and ladders.
4. Use **Export** to save a shareable cfg, or **Apply for Part** to save the default preset for that part type.
5. If Reviva is installed, select the saved preset in its IVA Switch; saving alone does not force a switch.
6. **Reset for Part** removes the active default mapping. Separately exported files remain available.

**There is no autosave.** Export or Apply before closing the editor or changing targets. Apply for Part affects that part type; per-craft presets are not currently provided.

## Preset sharing

Share the exported `.cfg` from:

```text
GameData/UniversalIVAEditor/UserPresets
```

Both players need the IVA and prop assets referenced by the preset, along with any mods required for those props' functions. The cfg contains configuration and references, not the assets themselves. Missing assets or incompatible source definitions are reported; the preset cannot recreate an uninstalled IVA or prop.

## Testing and issue reports

The public release follows closed-beta feedback. It has not been fully tested in a stock KSP environment with only the required dependencies, and compatibility with every mod combination is not guaranteed.

If you encounter a problem, open a [GitHub Issue](https://github.com/jongshh/Universal-IVA-Editor/issues) and attach `KSP.log`, your mod versions, steps to reproduce, and the relevant preset when possible.

Or Join [JSIndustries Discord server](https://discord.gg/72hcxvaSzz) for quick support.

## What this mod does not do

This mod does not create or deform IVA meshes, or provide general editing of colors, shaders, materials, or textures. The focus is placing existing assets and keeping the framework lightweight.

## Build from source

Use a .NET SDK with .NET Framework 4.8 targeting support and a local KSP 1.12.x installation containing HarmonyKSP:

```powershell
./Build.ps1 -KspRoot 'C:/Games/Kerbal Space Program'
```

The DLL is written to `GameData/UniversalIVAEditor/Plugins`. KSP and third-party references are resolved from your installation and are not redistributed.

## License

MIT, copyright 2026 Jongsh. See [LICENSE](LICENSE). The included `Compatibility/FreeIvaRules.cfg` is a fixed compatibility descriptor, not a per-user generated patch. Its references do not require every listed mod to be installed.
