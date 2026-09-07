# AutoWatcher · 自动观者

[简体中文](README.md)

Teaches the **Combat Solver** to understand the **Watcher**.

The solver rejects third-party character mods by default. With the Watcher installed it stops
outright at "incompatible third-party mod detected". With AutoWatcher installed the solver not
only runs, it actually understands the Watcher's cards, stances and extra turns.

**This mod changes no game behaviour.** It only fills in the simulation mirrors the solver is
missing; the manifest sets `affects_gameplay` to `false`. Your cards, your damage, every number
is exactly what it would be without this mod. The only difference is that the solver gets its
arithmetic right.

## Requirements

| Dependency | Version | Where |
|---|---|---|
| Combat Solver | **0.32.0 or newer** | [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3790899961) / [GitHub](https://github.com/Torch1230/CombatSolver) |
| Watcher (by Boninall) | 0.9.25 or newer | [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3747526116) |
| RitsuLib | — | [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3747602295) |

All three are required. If any one is missing, AutoWatcher refuses to load rather than
registering half of itself.

**Why the solver has to be 0.32.0 or newer.** The adapter registers the Watcher's effects into
the solver's internal registries, and those registration points were added version by version.
0.32.0 is the first *released* build that ships all five of them (third-party strategic
evaluation, potion choices, card choices, pile discard choices, playability coverage).

**What happens on a version mismatch.** Nothing silent:

- Solver older than 0.32.0, or missing a registration point → AutoWatcher **refuses to load
  cleanly**, logs exactly what is missing and where to get a newer build, and registers nothing.
  The solver then stops at its own third-party check, as it would without this mod.
- The Watcher updates → as long as its internals are unchanged, AutoWatcher keeps working; if
  they changed, it refuses to load the same way. One caveat: the Watcher can change numbers or
  effects without changing any signature, and the structural check cannot catch that. The log
  will say this build of the Watcher is not the one the adapter was verified against. If you see
  that line and a route looks wrong, please file a bug.

## Two release channels

**The solver is under active development, and this mod follows it.** The adapter is written
against the solver's internal interfaces, so when the solver changes the adapter may have to be
brought back in line. Hence two channels:

| Channel | Targets | Where |
|---|---|---|
| **Steam Workshop** | *released* solver builds (0.32.0 and up) | just subscribe |
| **GitHub** | one specific solver version, possibly a development build | [Releases](https://github.com/bingyang1132/AutoWatcher/releases) |

Most people want the Workshop one. Every GitHub release states **which** solver version it was
built against and links to it; that channel exists to track solver changes that are not released
yet.

Do not mix the two. Mixing will not fail silently — the structural check refuses to load when it
does not line up — but there is no reason to.

When something breaks, check which pair you have installed first.

## What is covered

- **All 100 Watcher card types** mirrored one by one, written against the decompiled implementation
  rather than guessed from the card text
- **All four stances**: entry, exit, damage multipliers, energy, plus the knock-on effects from
  Mental Fortress, Violet Lotus and Rushdown
- **Scry as a real search branch**: which cards to discard is something the solver searches,
  instead of forcing a replan on every scry
- **Extra turns** (Vault), card retain, and the various turn-start / turn-end hooks
- **The Rushdown infinite**: the draw-on-entering-Wrath is modelled, so the solver recognises
  and expands the loop
- **Cross-combat value**: the deck upgrade from Lesson Learned's execute counts toward long-term value
- **9 relics, 3 potions, 14 power hooks**, plus three sites the solver has no registry for and
  which therefore need Harmony patches

## Known gaps

Eight effects are not modelled. They **never silently miscalculate** — the solver marks the route
in red with "unmirrored effect here", so you can see it. The list is in the
"显式记为未镜像的部分" section of [docs/VERIFICATION.md](docs/VERIFICATION.md).

The Talk to the Hand ordering problem is **fixed**. The solver used to refuse to reorder it ahead
of your attacks to gain the block; the mirror was always correct, what was broken was the solver's
action classification. The fix adds a third-party strategic-effect registration hook on the solver
side, so effects that sit on an enemy but pay out to the player reach the defensive classification,
and the adapter registers the block-return power through it. This is why AutoWatcher needs a solver
build that contains that hook — see the two channels above.

## Reporting bugs

The solver has a built-in bug-package export. Export one and open an
[issue](https://github.com/bingyang1132/AutoWatcher/issues) with it, describing what you saw and
saying whether you are on the Workshop or the GitHub build. With a bug package I can usually
locate the problem; a text description alone usually is not enough.

## Building from source

```bash
cp local.props.example local.props   # point it at your Steam install
dotnet build AutoWatcher.csproj -c Release
```

The build copies the DLL and manifest into `<game>/mods/AutoWatcher/`. It requires
`mods/CombatSolver/CombatSolver.dll` and `mods/Watcher/Watcher.dll` to already be present —
this repository contains no third-party binaries, and the Watcher is pinned by SHA256 only.

Everything development-facing (the per-card verification table, the fixture matrix, the
discrepancies reported from real games) is in [docs/VERIFICATION.md](docs/VERIFICATION.md);
the outstanding-work list is in [docs/PLAN.md](docs/PLAN.md).

## Credits and licence

- **Boninall** — author of the Watcher mod
- **Torch1230** and the Combat Solver contributors
- Thanks to **Claude (Anthropic)** for help with the development

MIT licensed, see [LICENSE](LICENSE). Third-party assembly references are documented in
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

This is an unofficial community mod, not affiliated with Mega Crit.
