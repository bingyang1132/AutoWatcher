# 创意工坊页面文案

创意工坊的标题和正文是**分语言**的：Steam 的 `SetItemUpdateLanguage` 可以给同一个条目上传
多份语言版本，玩家看到哪一份由他自己的 Steam 语言决定。所以这里中英各写一份**纯**该语言的
正文，不再中英混排。

- 简体中文（`schinese`）：下面「## 正文 · 简体中文」一节
- English（`english`，同时作为其他语言的回退）：下面「## 正文 · English」一节

改完跑 `python publish/build-workshop-json.py` 生成 `publish/workshop.json`，别手工改 JSON。

正文是 BBCode，不是 markdown —— 写 `**粗体**` 会原样显示出来，要写 `[b]粗体[/b]`。

## 标题

```
自动观者 | AutoWatcher
```

中英两版共用这一个标题。它本身就是双语的，够短，不值得再分。

## 正文 · 简体中文

---

让[b]杀戮尖塔2自动战斗求解器[/b]适用于[b]观者[/b]。

[b]本 Mod 不改变任何游戏行为。[/b] 它只补上求解器缺失的模拟镜像，清单里 affects_gameplay
是 false。牌、伤害、一切数值都和不装它时完全一样，唯一的区别是装上之后求解器可以自动化观者
的打牌。

[h2]需要装什么[/h2]

[list]
[*]自动战斗求解器，[b]至少 0.32.0[/b]
[*]观者（作者 Boninall），0.9.25 起
[*]RitsuLib
[/list]

三个都要装。版本不对不会静默出错——求解器太旧或缺了登记入口，本 Mod 会干净地拒绝加载并写明
原因。细节见下面的常见问题。

[h2]求解器还在持续开发，本 Mod 也会相应持续更新[/h2]

适配层是贴着求解器的内部接口写的，求解器一改，适配层就可能得跟一次。所以本 Mod 分两条线发布：

[list]
[*][b]创意工坊版[/b]，也就是你现在看的这个 —— 对标求解器的[b]发布版[/b]（0.32.0 起）。
订阅即可。普通情况用这个就行。
[*][b]GitHub 版[/b] —— 对标某一个确定的求解器版本，可能是还没发布的开发版。每个 release 会
写明它对标哪一个求解器版本并给出链接。
[/list]

本 Mod 的 GitHub：https://github.com/bingyang1132/AutoWatcher
求解器的创意工坊：https://steamcommunity.com/sharedfiles/filedetails/?id=3790899961
求解器的 GitHub：https://github.com/Torch1230/CombatSolver

[h2]覆盖了什么[/h2]

[list]
[*][b]观者的全部 100 个卡牌类型[/b]逐个核对镜像，按反编译出的实现写，不是照着卡面文字猜
[*][b]四种姿态[/b]的进入、退出、伤害倍率和能量收支，以及心灵堡垒、疾风连击这些连带效果
[*][b]预视[/b]是真正的搜索分支：丢哪几张由求解器自己搜
[*][b]额外回合[/b]（腾跃）、手牌保留、回合开始与结束的各类钩子
[*][b]红蓝无限[/b]：🐯😡🥶😡🥶😡🥶😡🥶😡🥶😡🥶
[*][b]跨战斗收益[/b]：勤学精进的斩杀升级和许愿会被算进长期价值
[*]9 个遗物、3 个药水、14 个 Power 钩子，以及三处求解器没有注册表、只能用 Harmony 补的位置
[/list]

[h2]已知缺口[/h2]

有 5 处效果显式记为未镜像。它们[b]不会静默算错[/b] —— 求解器会在路线上打出红字标明「这里有
未镜像的效果」，你看得见。另有一批走 Harmony postfix 或求解器不分发的钩子也没有覆盖。
两份清单都在 GitHub 的核对记录里。

[h2]常见问题[/h2]

[b]装了本 Mod，求解器还是停在「检测到不兼容的第三方 Mod」？[/b]
说明适配层没有加载。日志里 AutoWatcher 开头那几行会写明原因，最常见的是求解器版本低于
0.32.0。适配层是全有或全无：前提不成立就一个镜像都不注册，绝不装一半。

[b]为什么求解器一定要 0.32.0 以上？[/b]
适配层要把观者的效果登记进求解器的内部注册表，而这些登记入口是逐版加进去的。0.32.0 是第一个
发布产物里就带全五条的版本：第三方战略估值、药水选择、卡牌选择、牌堆弃牌、可打出性覆盖。

[b]工坊版和 GitHub 版可以混着装吗？[/b]
不要。混了也不会静默出错——结构自检对不上就拒绝加载——但没必要。出了问题优先看你装的是哪一对。

[b]观者更新之后还能用吗？[/b]
只要观者的内部结构没变就照常工作，结构变了就同样干净地拒绝加载。但有一种情况自检抓不到：
观者可以在不改任何签名的前提下改数值或改效果。那时日志里会写「这一份观者不是逐条核对过的
那一版」——看到那句话又觉得路线不对，请报 Bug。

[b]报 Bug 为什么一定要问题包？[/b]
路线错在哪几乎都得看完整的战斗状态才能定位，只有文字描述通常不够。求解器自带导出。

[b]「以手拒之」不会被排到攻击牌前面起甲？[/b]
已经修好了。镜像本身一直是对的，坏的是求解器的动作分类：它只统计自己身上的增益，挂在敌人
身上的那层完全看不见。修法是在求解器那边开一个第三方战略估值的登记入口。需要求解器 0.32.0 以上。

[h2]报 Bug[/h2]

求解器自带问题包导出。导出以后发到 GitHub Issues，附上看到的现象。欢迎捉虫！

[h2]致谢[/h2]

观者 Mod 作者 Boninall；自动战斗求解器作者 Torch1230 及各位贡献者。
感谢 Claude（Anthropic）在开发上的帮助。本 Mod 以 MIT 授权。

本 Mod 是非官方社区作品，与 Mega Crit 无关。

## 正文 · English

---

Teaches the [b]Combat Solver[/b] to understand the [b]Watcher[/b].

[b]This mod changes no game behaviour.[/b] It only fills in the simulation mirrors the solver is
missing; the manifest sets affects_gameplay to false. Cards, damage, every number is exactly what
it would be without this mod. The only difference is that with it installed, the solver can
automate playing the Watcher.

[h2]Requirements[/h2]

[list]
[*]Combat Solver, [b]0.32.0 or newer[/b]
[*]Watcher (by Boninall), 0.9.25 or newer
[*]RitsuLib
[/list]

All three are required. A version mismatch never fails silently — if the solver is too old or
missing a registration point, AutoWatcher refuses to load cleanly and says why. Details in the
FAQ below.

[h2]The solver is under active development, and this mod keeps up with it[/h2]

The adapter is written against the solver's internal interfaces, so every time the solver
changes, the adapter has to be brought back in line. Hence two channels:

[list]
[*][b]Steam Workshop[/b] — this page. Targets [b]released[/b] solver builds (0.32.0 and up).
Just subscribe. This is the one you normally want.
[*][b]GitHub[/b] — targets one specific solver version, possibly a development build. Every
release states which solver version it was built against and links to it.
[/list]

This mod on GitHub: https://github.com/bingyang1132/AutoWatcher
The solver on the Workshop: https://steamcommunity.com/sharedfiles/filedetails/?id=3790899961
The solver on GitHub: https://github.com/Torch1230/CombatSolver

[h2]What is covered[/h2]

[list]
[*][b]All 100 Watcher card types[/b] mirrored one by one, written against the decompiled
implementation rather than guessed from the card text
[*][b]All four stances[/b]: entry, exit, damage multipliers, energy, plus knock-on effects from
Mental Fortress and Flurry of Blows
[*][b]Scry as a real search branch[/b]: which cards to discard is something the solver searches
[*][b]Extra turns[/b] (Vault), card retain, and the various turn-start / turn-end hooks
[*][b]The Wrath/Calm infinite[/b]: 🐯😡🥶😡🥶😡🥶😡🥶😡🥶😡🥶
[*][b]Cross-combat value[/b]: the deck upgrade from Lesson Learned's execute, and Wish's gold,
count toward long-term value
[*]9 relics, 3 potions, 14 power hooks, plus three sites the solver has no registry for
[/list]

[h2]Known gaps[/h2]

Five effects are explicitly recorded as unmirrored. They [b]never silently miscalculate[/b] —
the solver marks the route in red with "unmirrored effect here", so you can see it. A further set
of hooks is also not covered. Both lists are in the verification notes on GitHub.

[h2]FAQ[/h2]

[b]I installed this mod and the solver still stops at "incompatible third-party mod detected".[/b]
The adapter did not load. The lines starting with AutoWatcher in the log say why; the most common
reason is a solver older than 0.32.0. The adapter is all-or-nothing: if its prerequisites do not
hold it registers nothing at all rather than half of itself.

[b]Why does the solver have to be 0.32.0 or newer?[/b]
The adapter registers the Watcher's effects into the solver's internal registries, and those
registration points were added version by version. 0.32.0 is the first build whose released
binary ships all five: third-party strategic evaluation, potion choices, card choices, pile
discard choices, and playability coverage.

[b]Can I mix the Workshop build with the GitHub build?[/b]
Do not. Mixing will not fail silently — the structural check refuses to load when it does not
line up — but there is no reason to. When something breaks, check which pair you have installed.

[b]Will it still work after the Watcher updates?[/b]
As long as the Watcher's internals are unchanged, yes; if they changed, it refuses to load the
same clean way. There is one case the check cannot catch: the Watcher can change numbers or
effects without changing any signature. The log will then say this build of the Watcher is not the
one the adapter was verified against — if you see that line and a route looks wrong, please file a
bug.

[b]Why do you need a bug package?[/b]
Locating a wrong route almost always needs the full combat state; a text description alone usually
is not enough. The solver has a built-in export.

[b]Talk to the Hand does not get ordered ahead of my attacks to gain the block.[/b]
That is fixed. The mirror was always right; what was broken was the solver's action
classification — it only counted buffs on the player, so a Power sitting on the enemy that pays
out to the player was invisible. The fix adds a third-party strategic-evaluation registration
point on the solver side. Needs solver 0.32.0 or newer.

[h2]Reporting bugs[/h2]

The solver has a built-in bug-package export. Export one and open a GitHub issue with it,
describing what you saw. Bug hunts welcome!

[h2]Credits[/h2]

Boninall, author of the Watcher mod; Torch1230 and the Combat Solver contributors.
Thanks to Claude (Anthropic) for help with the development. MIT licensed.

This is an unofficial community mod, not affiliated with Mega Crit.
