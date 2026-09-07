# 创意工坊页面文案

创意工坊的标题和正文是**分语言**的：Steam 的 `SetItemUpdateLanguage` 可以给同一个条目上传
多份语言版本，玩家看到哪一份由他自己的 Steam 语言决定。所以这里中英各写一份**纯**该语言的
正文，不再中英混排。

- 简体中文（`schinese`）：下面「## 正文 · 简体中文」一节
- English（`english`，同时作为其他语言的回退）：下面「## 正文 · English」一节

改完跑 `python publish/build-workshop-json.py` 生成 `publish/workshop.json`，别手工改 JSON。

正文是 BBCode，不是 markdown —— 写 `**粗体**` 会原样显示出来，要写 `[b]粗体[/b]`。

## 写作约定

**简体中文正文以作者 2026-09-07 那一版为准。** 下面几条是从「我写的版本」和「作者改完的版本」
之间蒸馏出来的差异，写在这里是为了以后别再改回去。

1. **正文只说玩家能感觉到的结果，不解释机制。** 开头三句话说完「这是什么、解决什么、不改什么」
   就够了。`affects_gameplay`、「补上模拟镜像」这类实现说法不进正文。
2. **能靠常见问题承载的，正文不重复。** 「三个都要装」「版本不对会怎样」这些从需求列表下面
   删掉了 —— 需求列表本身已经说明白了，细节留给常见问题。
3. **常见问题只放玩家真会遇到、而且需要自己动手的问题。** 作者砍掉了三条：
   「为什么要 0.32.0」（属于设计理由）、「报 Bug 为什么要问题包」（属于流程解释）、
   「以手拒之的排序」（属于开发史）。**开发史和设计理由不进对外文案**，它们的位置在
   `docs/VERIFICATION.md`。
4. **已知缺口要点名具体是哪几张牌**，玩家才知道自己会不会碰到。只给数字没有用。
5. **玩家看得见的每一个名字都必须是游戏里的官方译名**，从 `Watcher.pck` 的
   `localization/zhs/*.json` 核过再写。这一条踩过很多次：我自己译的
   预视/神性形态/凝聚利刃/抽签护符/璀璨/涤罪者/紫莲花/心之堡垒/疾风连打/跳跃 **全是错的**，
   官方是 预见/天人形态/聚能成刃/画符/光辉/灭除之刃/紫色莲花/心灵堡垒/疾风连击/腾跃。
   读法见 `CombatSolver/tools/read-game-localization.ps1`，观者的 pck 路径是
   `mods/Watcher/Watcher.pck`，键前缀 `Watcher/localization/zhs/`。
6. 语气可以轻松（「欢迎捉虫！」、红蓝无限那行的 emoji），不必全程严肃。

英文正文照中文的结构和取舍走，不要自己多加段落。

## 标题

```
自动观者 | AutoWatcher
```

中英两版共用这一个标题。它本身就是双语的，够短，不值得再分。

## 正文 · 简体中文

---

让[b]杀戮尖塔2自动战斗求解器[/b]适用于[b]观者[/b]。

自动战斗求解器本身不兼容观者，本 Mod 装上之后求解器可以自动化观者的打牌。不改变任何游戏行为和数值。

[h2]需要装什么[/h2]

[list]
[*]自动战斗求解器，[b]至少 0.32.0[/b]
[*]观者（作者 Boninall），0.9.25 起
[*]RitsuLib
[/list]

[h2]求解器还在持续开发，本 Mod 也会相应持续更新[/h2]

适配层是贴着求解器的内部接口写的，求解器一改，适配层就可能得跟一次。所以本 Mod 分两条线发布：

[list]
[*][b]创意工坊版[/b]，也就是你现在看的这个 —— 对标求解器的[b]发布版[/b]（0.32.0 起）。
订阅即可。普通情况用这个就行。
[*][b]GitHub 版[/b] —— 对标某一个确定的求解器版本，可能是还没发布的开发版。每个 release 会写明它对标哪一个求解器版本并给出链接。
[/list]

本 Mod 的 GitHub：https://github.com/bingyang1132/AutoWatcher
求解器的创意工坊：https://steamcommunity.com/sharedfiles/filedetails/?id=3790899961
求解器的 GitHub：https://github.com/Torch1230/CombatSolver

自动求解器作者Torch的塔2mod交流群：1106541324 （QQ）

[h2]覆盖了什么[/h2]

[list]
[*][b]观者的全部 100 个卡牌类型[/b]逐个核对镜像，按反编译出的实现写，不是照着卡面文字猜
[*][b]四种姿态[/b]的进入、退出、伤害倍率和能量收支，以及心灵堡垒、疾风连击这些连带效果
[*][b]预见[/b]是真正的搜索分支：丢哪几张由求解器自己搜
[*][b]额外回合[/b]（腾跃）、手牌保留、回合开始与结束的各类钩子
[*][b]红蓝无限[/b]：🐯😡🥶😡🥶😡🥶😡🥶😡🥶😡🥶
[*][b]跨战斗收益[/b]：勤学精进的斩杀升级和许愿会被算进长期价值
[*]9 个遗物、3 个药水、14 个 Power 钩子，以及三处求解器没有注册表、只能用 Harmony 补的位置
[/list]

[h2]已知缺口[/h2]

有 3 张牌的效果显式记为未镜像：光辉、天人形态、画符。它们[b]不会静默算错[/b] —— 求解器会在路线上打出红字标明「这里有未镜像的效果」。另有一批走 Harmony postfix 或求解器不分发的钩子也没有覆盖。近期会逐渐补齐

[h2]常见问题[/h2]

[b]装了本 Mod，求解器还是停在「检测到不兼容的第三方 Mod」？[/b]
说明适配层没有加载。日志里 AutoWatcher 开头那几行会写明原因，最常见的是求解器版本低于
0.32.0。适配层是全有或全无：前提不成立就一个镜像都不注册，绝不装一半。

[b]工坊版和 GitHub 版可以混着装吗？[/b]
不要。混了也不会静默出错——结构自检对不上就拒绝加载——但没必要。出了问题优先看你装的是哪一对。

[b]观者更新之后还能用吗？[/b]
只要观者的内部结构没变就照常工作，结构变了就同样干净地拒绝加载。但有一种情况自检抓不到：
观者可以在不改任何签名的前提下改数值或改效果。那时日志里会写「这一份观者不是逐条核对过的
那一版」——看到那句话又觉得路线不对，请报 Bug。

[h2]报 Bug[/h2]

求解器自带问题包导出。导出以后可以发到 GitHub Issues，附上看到的现象。或者在交流群讨论。欢迎捉虫！

[h2]致谢[/h2]

观者 Mod 作者 Boninall；自动战斗求解器作者 Torch1230 及各位贡献者。
感谢 Claude（Anthropic）在开发上的帮助。本 Mod 以 MIT 授权。

## 正文 · English

---

Makes the [b]Slay the Spire 2 combat route solver[/b] work with the [b]Watcher[/b].

The solver is not compatible with the Watcher on its own. With this mod installed, the solver can
automate playing the Watcher. It changes no game behaviour and no numbers.

[h2]Requirements[/h2]

[list]
[*]Combat Solver, [b]0.32.0 or newer[/b]
[*]Watcher (by Boninall), 0.9.25 or newer
[*]RitsuLib
[/list]

[h2]The solver is under active development, and this mod keeps up with it[/h2]

The adapter is written against the solver's internal interfaces, so when the solver changes the
adapter may have to be brought back in line. Hence two channels:

[list]
[*][b]Steam Workshop[/b] — this page. Targets [b]released[/b] solver builds (0.32.0 and up).
Just subscribe. This is the one you normally want.
[*][b]GitHub[/b] — targets one specific solver version, possibly a development build. Every
release states which solver version it was built against and links to it.
[/list]

This mod on GitHub: https://github.com/bingyang1132/AutoWatcher
The solver on the Workshop: https://steamcommunity.com/sharedfiles/filedetails/?id=3790899961
The solver on GitHub: https://github.com/Torch1230/CombatSolver

Torch's Slay the Spire 2 modding group (Chinese-language, QQ): 1106541324

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

Three cards have effects explicitly recorded as unmirrored: Brilliance, Deva Form and Draw
Talisman. They [b]never silently miscalculate[/b] — the solver marks the route in red
with "unmirrored effect here". A further set of hooks that run through Harmony postfixes, or that
the solver does not dispatch, is also not covered. These will be filled in over the coming
releases.

[h2]FAQ[/h2]

[b]I installed this mod and the solver still stops at "incompatible third-party mod detected".[/b]
The adapter did not load. The lines starting with AutoWatcher in the log say why; the most common
reason is a solver older than 0.32.0. The adapter is all-or-nothing: if its prerequisites do not
hold it registers nothing at all rather than half of itself.

[b]Can I mix the Workshop build with the GitHub build?[/b]
Do not. Mixing will not fail silently — the structural check refuses to load when it does not
line up — but there is no reason to. When something breaks, check which pair you have installed.

[b]Will it still work after the Watcher updates?[/b]
As long as the Watcher's internals are unchanged, yes; if they changed, it refuses to load the
same clean way. There is one case the check cannot catch: the Watcher can change numbers or
effects without changing any signature. The log will then say this build of the Watcher is not the
one the adapter was verified against — if you see that line and a route looks wrong, please file a
bug.

[h2]Reporting bugs[/h2]

The solver has a built-in bug-package export. Export one and open a GitHub issue with it,
describing what you saw. Or come discuss it in the group. Bug hunts welcome!

[h2]Credits[/h2]

Boninall, author of the Watcher mod; Torch1230 and the Combat Solver contributors.
Thanks to Claude (Anthropic) for help with the development. MIT licensed.
