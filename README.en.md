# AutoWatcher · 自动观者

[简体中文](README.md)

Makes the **Slay the Spire 2 combat route solver** work with the **Watcher**.

The solver is not compatible with the Watcher on its own. With this mod installed, the solver can
automate playing the Watcher. It changes no game behaviour and no numbers (the manifest sets
`affects_gameplay` to `false`).

## Requirements

| Dependency | Version | Where |
|---|---|---|
| Combat Solver | **0.32.0 or newer** | [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3790899961) / [GitHub](https://github.com/Torch1230/CombatSolver) |
| Watcher (by Boninall) | 0.9.28 or newer | [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3747526116) |
| RitsuLib | — | [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3747602295) |

**What happens on a version mismatch:**

- Solver older than 0.32.0, or missing a registration point → AutoWatcher **refuses to load
  cleanly** and logs exactly what is missing and where to get the right build. The solver then
  stops at its own third-party check, as it would without this mod.
- The Watcher updates → as long as its internals are unchanged, AutoWatcher keeps working; if
  they changed, it refuses to load the same way.

## Two release channels

**The solver is under active development, and this mod will keep being updated alongside it.** The adapter is written
against the solver's internal interfaces, so when the solver changes the adapter may have to be
brought back in line. Hence two channels:

| Channel | Targets | Where |
|---|---|---|
| **Steam Workshop** | *released* solver builds (0.32.0 and up) | [subscribe](https://steamcommunity.com/sharedfiles/filedetails/?id=3797303841) |
| **GitHub** | one specific solver version, possibly a development build | [Releases](https://github.com/bingyang1132/AutoWatcher/releases) |

Torch's Slay the Spire 2 modding group (Chinese-language, QQ): 1106541324

When something breaks, check which pair you have installed first.

## What is covered

- **All 100 Watcher card types** mirrored one by one, written against the decompiled implementation
  rather than guessed from the card text
- **All four stances**: entry, exit, damage multipliers, energy, plus the knock-on effects from
  Mental Fortress and Flurry of Blows
- **Scry as a real search branch**: which cards to discard is something the solver searches
- **Extra turns** (Vault), card retain, and the various turn-start / turn-end hooks
- **The Wrath/Calm infinite**: 🐯😡🥶😡🥶😡🥶😡🥶😡🥶😡🥶
- **Cross-combat value**: the deck upgrade from Lesson Learned's execute, and Wish's gold, count
  toward long-term value. When the solver supports third-party growth sources, both also get their
  own row in the growth-strategy sidebar, so you can set how much extra damage you are willing to
  take for each
- **9 relics, 3 potions, 14 power hooks**, plus three sites the solver has no registry for and
  which therefore need Harmony patches

## Known gaps

Just one card's effect is still explicitly recorded as unmirrored: **Draw Talisman** (a weak
Ancient card the mod adds itself). It **never silently miscalculates** — the solver marks the
route in red with "unmirrored effect here". A further set of hooks that run through Harmony
postfixes, or that the solver does not dispatch, is also not covered. These will be filled in over
the coming releases.

Both full lists are in [docs/VERIFICATION.md](docs/VERIFICATION.md).

## FAQ

**I installed this mod and the solver still stops at "incompatible third-party mod detected".**
The adapter did not load. The lines starting with `[AutoWatcher]` in the log say why; the most
common reason is a solver older than 0.32.0. The adapter is all-or-nothing: if its prerequisites
do not hold it registers nothing at all rather than half of itself.

**Why does the solver have to be 0.32.0 or newer?**
The adapter registers the Watcher's effects into the solver's internal registries, and those
registration points were added version by version. 0.32.0 is the first build whose **released
binary** ships all five: third-party strategic evaluation, potion choices, card choices, pile
discard choices, and playability coverage. The source of `0.31.3` has the first four, but its
released binary does not include card choices.

**Can I mix the Workshop build with the GitHub build?**
Don't. Mixing will not fail silently — the structural check refuses to load when it does not line
up — but there is no reason to. When something breaks, check which pair you have installed first.

**Will it still work after the Watcher updates?**
As long as the Watcher's internals are unchanged, yes; if they changed, it refuses to load the
same clean way. There is one case the check cannot catch: the Watcher can change numbers or
effects **without changing any signature**. The log will then say this build of the Watcher is not
the one the adapter was verified against — if you see that line and a route looks wrong, please
file a bug.

**Why do you need a bug package?**
Locating a wrong route almost always needs the full combat state; a text description alone usually
is not enough. The solver has a built-in export.

**Talk to the Hand does not get ordered ahead of my attacks to gain the block.**
That is fixed. The mirror was always right; what was broken was the solver's action
classification — it only counted buffs on the player, so a Power sitting on the enemy that pays
out to the player was invisible, and the card got treated as a weaker plain attack and sorted
last. The fix adds a third-party strategic-evaluation registration point on the solver side.
Needs solver 0.32.0 or newer.

## Reporting bugs

The solver has a built-in bug-package export. Export one and open an
[issue](https://github.com/bingyang1132/AutoWatcher/issues) with it, describing what you saw.
Bug hunts welcome!

## Building from source

```bash
cp local.props.example local.props   # point it at your Steam install
dotnet build AutoWatcher.csproj -c Release
```

The build copies the DLL and manifest into `<game>/mods/AutoWatcher/`. It requires
`mods/CombatSolver/CombatSolver.dll` and `mods/Watcher/Watcher.dll` to already be present.
This repository contains no third-party binaries.

Everything development-facing (the per-card verification table, the fixture matrix, the
discrepancies reported from real games) is in [docs/VERIFICATION.md](docs/VERIFICATION.md);
the outstanding-work list is in [docs/PLAN.md](docs/PLAN.md).

## Credits and licence

- **Boninall** — author of the Watcher mod
- **Torch1230** and the Combat Solver contributors
- Thanks to **Claude (Anthropic)** for help with the development

MIT licensed, see [LICENSE](LICENSE). Third-party assembly references are documented in
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

