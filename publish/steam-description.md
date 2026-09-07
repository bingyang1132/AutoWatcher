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

让[b]自动战斗求解器[/b]看懂[b]观者[/b]。

求解器默认拒绝第三方角色 Mod，装上观者之后会直接停在「检测到不兼容的第三方 Mod」。
装上本 Mod 之后，求解器不但能跑，还能真正理解观者的牌、姿态和额外回合。

[b]本 Mod 不改变任何游戏行为。[/b] 它只补上求解器缺失的模拟镜像，清单里 affects_gameplay
是 false。你的牌、伤害、一切数值都和不装它时完全一样，唯一的区别是求解器算得对。

[h2]需要装什么[/h2]

[list]
[*]自动战斗求解器，[b]至少 0.32.0[/b]
[*]观者（作者 Boninall），0.9.25 起
[*]RitsuLib
[/list]

三个都要装。缺任何一个，本 Mod 都不会加载，也不会留下半装的状态。

求解器要 0.32.0 以上，是因为适配层要把观者的效果登记进它的内部注册表，而这些登记入口是逐版
加进去的——0.32.0 是第一个发布产物里就带全的版本。

版本不对不会静默出错：求解器太旧或缺了登记入口，本 Mod 会干净地拒绝加载，日志里写明缺什么，
一个镜像都不注册。

[h2]求解器还在持续开发，本 Mod 跟着它走[/h2]

适配层是贴着求解器的内部接口写的，求解器一改，适配层就得跟一次。所以本 Mod 分两条线发布：

[list]
[*][b]创意工坊版[/b]，也就是你现在看的这个 —— 对标求解器的[b]发布版[/b]（0.32.0 起）。
订阅即可。绝大多数人用这个就行。
[*][b]GitHub 版[/b] —— 对标某一个确定的求解器版本，可能是还没发布的开发版。每个 release 会
写明它对标哪一个求解器版本并给出链接。
[/list]

两条线不要混着装。混了也不会静默出错——结构自检对不上就拒绝加载——但没必要。

本 Mod 的 GitHub：https://github.com/bingyang1132/AutoWatcher
求解器的创意工坊：https://steamcommunity.com/sharedfiles/filedetails/?id=3790899961
求解器的 GitHub：https://github.com/Torch1230/CombatSolver

[h2]覆盖了什么[/h2]

[list]
[*][b]观者的全部 100 个卡牌类型[/b]逐个核对镜像，按反编译出的实现写，不是照着卡面文字猜
[*][b]四种姿态[/b]的进入、退出、伤害倍率和能量收支，以及心之堡垒、紫莲花、疾风连打这些连带效果
[*][b]预视[/b]是真正的搜索分支：丢哪几张由求解器自己搜，不再每次预视都触发一次重算
[*][b]额外回合[/b]（跳跃）、手牌保留、回合开始与结束的各类钩子
[*][b]红蓝无限[/b]：凌波微步的进入愤怒抽牌已建模，求解器能认出并展开这条循环
[*][b]跨战斗收益[/b]：勤学精进的斩杀升级会被算进长期价值
[*]9 个遗物、3 个药水、14 个 Power 钩子，以及三处求解器没有注册表、只能用 Harmony 补的位置
[/list]

[h2]已知缺口[/h2]

有 8 处效果没有建模。它们[b]不会静默算错[/b] —— 求解器会在路线上打出红字标明「这里有未镜像
的效果」，你看得见。具体清单在 GitHub 的核对记录里。

「以手拒之」的排序问题已经修好。之前求解器不会为了起甲把它排到攻击牌前面，修法是在求解器
那边开一个第三方登记入口，让「挂在敌人身上、收益归玩家」的效果也能进防御判定。所以本 Mod
需要含这个入口的求解器版本。

[h2]报 Bug[/h2]

求解器自带问题包导出。导出以后发到 GitHub Issues，附上你看到的现象，并说明你装的是工坊版
还是 GitHub 版。带上问题包我基本都能定位；只有文字描述通常不够。

[h2]致谢[/h2]

观者 Mod 作者 Boninall；自动战斗求解器作者 Torch1230 及各位贡献者。
感谢 Claude（Anthropic）在开发上的帮助。本 Mod 以 MIT 授权。

本 Mod 是非官方社区作品，与 Mega Crit 无关。

## 正文 · English

---

Teaches the [b]Combat Solver[/b] to understand the [b]Watcher[/b].

The solver rejects third-party character mods by default — with the Watcher installed it stops
outright at "incompatible third-party mod detected". With AutoWatcher installed the solver not
only runs, it actually understands the Watcher's cards, stances and extra turns.

[b]This mod changes no game behaviour.[/b] It only fills in the simulation mirrors the solver is
missing; the manifest sets affects_gameplay to false. Your cards, your damage, every number is
exactly what it would be without this mod. The only difference is that the solver gets its
arithmetic right.

[h2]Requirements[/h2]

[list]
[*]Combat Solver, [b]0.32.0 or newer[/b]
[*]Watcher (by Boninall), 0.9.25 or newer
[*]RitsuLib
[/list]

All three are required. If any one is missing, AutoWatcher refuses to load rather than
registering half of itself.

The solver has to be 0.32.0 or newer because the adapter registers the Watcher's effects into the
solver's internal registries, and those registration points were added version by version.
0.32.0 is the first released build that ships all of them.

A version mismatch never fails silently: if the solver is too old or missing a registration
point, AutoWatcher refuses to load cleanly, logs exactly what is missing, and registers nothing.

[h2]The solver is under active development, and this mod follows it[/h2]

The adapter is written against the solver's internal interfaces, so every time the solver
changes, the adapter has to be brought back in line. Hence two channels:

[list]
[*][b]Steam Workshop[/b] — this page. Targets [b]released[/b] solver builds (0.32.0 and up).
Just subscribe. This is the one most people want.
[*][b]GitHub[/b] — targets one specific solver version, possibly a development build. Every
release states which solver version it was built against and links to it.
[/list]

Do not mix the two. Mixing will not fail silently — the structural check refuses to load when it
does not line up — but there is no reason to.

This mod on GitHub: https://github.com/bingyang1132/AutoWatcher
The solver on the Workshop: https://steamcommunity.com/sharedfiles/filedetails/?id=3790899961
The solver on GitHub: https://github.com/Torch1230/CombatSolver

[h2]What is covered[/h2]

[list]
[*][b]All 100 Watcher card types[/b] mirrored one by one, written against the decompiled
implementation rather than guessed from the card text
[*][b]All four stances[/b]: entry, exit, damage multipliers, energy, plus knock-on effects from
Mental Fortress, Violet Lotus and Rushdown
[*][b]Scry as a real search branch[/b]: which cards to discard is something the solver searches,
instead of forcing a replan on every scry
[*][b]Extra turns[/b] (Vault), card retain, and the various turn-start / turn-end hooks
[*][b]The Rushdown infinite[/b]: the draw-on-entering-Wrath is modelled, so the solver recognises
and expands the loop
[*][b]Cross-combat value[/b]: the deck upgrade from Lesson Learned's execute counts toward
long-term value
[*]9 relics, 3 potions, 14 power hooks, plus three sites the solver has no registry for
[/list]

[h2]Known gaps[/h2]

Eight effects are not modelled. They [b]never silently miscalculate[/b] — the solver marks the
route in red with "unmirrored effect here", so you can see it. The full list is in the
verification notes on GitHub.

The Talk to the Hand ordering problem is fixed. The solver used to refuse to reorder it ahead of
your attacks to gain the block; the fix adds a third-party registration hook on the solver side so
that effects sitting on an enemy but paying out to the player reach the defensive classification.
AutoWatcher therefore needs a solver build containing that hook.

[h2]Reporting bugs[/h2]

The solver has a built-in bug-package export. Export one and open a GitHub issue with it,
describing what you saw and saying whether you are on the Workshop or the GitHub build. With a
bug package I can usually locate the problem; a text description alone usually is not enough.

[h2]Credits[/h2]

Boninall, author of the Watcher mod; Torch1230 and the Combat Solver contributors.
Thanks to Claude (Anthropic) for help with the development. MIT licensed.

This is an unofficial community mod, not affiliated with Mega Crit.
