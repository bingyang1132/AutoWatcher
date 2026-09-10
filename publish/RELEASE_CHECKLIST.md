# 发布清单

状态记于 2026-09-06。勾掉的是已完成，`BLOCK` 是硬阻塞。

## 已定下来的

| 项 | 值 |
|---|---|
| 中文名 | 自动观者 |
| 英文名 | AutoWatcher |
| 副标题 | 自动战斗求解器 - 观者 mod 适配 |
| `mod id` / 程序集 / 命名空间 / mods 目录 | `AutoWatcher` |
| 创意工坊标题 | `自动观者 \| AutoWatcher` |
| GitHub 仓库 | `bingyang1132/AutoWatcher` |
| 授权 | MIT |
| 依赖 | 自动战斗求解器、观者（Boninall）、RitsuLib |

**两条发布线。** 创意工坊版对标创意工坊上的求解器 + 观者；GitHub 版对标我们自己的开发版
求解器。GitHub 的每个 release 放两样：本 Mod 的构建产物，和它对应的**那一个**求解器版本的
链接。文案里已经写明求解器在持续开发、本 Mod 跟进，并给了两个 GitHub 链接。

## 硬阻塞

- `BLOCK` **求解器要先发一个带扩展点的版本。** 适配层现在依赖三条尚未上游的求解器改动
  （`pr/unplanned-optional-choice-dismiss`、`pr/mod-pile-discard-choice`、
  `pr/card-playability-mirror-coverage`、`pr/third-party-strategic-effects`），外加六处 Harmony
  补丁。这些不落地，别人装上跑不起来。其中 `pr/third-party-strategic-effects` 是以手拒之排序
  问题的修法，没有它那张牌会被排到攻击后面、白挨一回合。
  清单里 `CombatSolver.min_version` 现在写的是 `0.29.0`，**是错的**，等有了目标版本再定。
- [x] **行为验收**：2026-09-06 19:51 全量 **25/25 通过**（含以手拒之排序和姿态药水两条新夹具）。
      对的是 游戏 v0.111.0 + 观者 0.9.25 + 求解器 0.31.1 + `pr/third-party-strategic-effects`
      + `pr/third-party-potion-choice`。

      同一天 19:28 那一轮 24/25，挂的是姿态药水那条新夹具，**原因是夹具参数写错不是代码错**：
      编了一个不存在的敌人行动 ID、用 `-EnemyCurrentHp` 把遭遇战里每只敌人都设成了 18、
      没显式指定 `-PotionPolicyForTest RequireAtLeastOne`（默认 Smart 验的是药水估值的启发式，
      不是二选一有没有展开成分支）。三条都记进夹具注释了。
      反向对照做过：注掉 `PotionChoiceMirrors.Register<StancePotion>` 这条立刻挂，恢复即过。

      同一天早一轮 23 条只过了 22，`WATCHER-DEUS-EX-MACHINA-DRAW` 挂了；单独重跑 3 次全过，
      这一轮在批量里也过了。失败那轮我正在同一台机器上跑另一个 `dotnet publish`，而且那轮
      每条平均 52 秒、这轮 37 秒 —— 是负载引起的抖动。**跑矩阵的时候别在同一台机器上跑别的
      构建。**

### 2026-09-06 22:46 全量 25 条通过（求解器 0.31.3）

分两批跑的，同一批二进制：批量跑了 21 条全过，撞 25 分钟时限剩 4 条未运行，那 4 条单独补跑
也全过。**0 条失败。**

覆盖这次的改动：四张选牌卡（许愿、冥想、通晓万物、他山之石）、`SolverCompat` 的可选绑定、
结构自检重写、卡牌数订正。

单条耗时从 0.31.1 的 ~37 秒变成 0.31.3 的 ~67 秒，每条均匀变慢、和夹具内容无关，
所以是开机变慢不是搜索变慢。矩阵默认时限已从 25 分钟改成 40 分钟。

### 2026-09-07 01:13 全量 25/25 通过（对着**发布版**求解器 0.32.0）

一批跑完，0 失败、0 未运行。这一轮的意义和之前几轮不同：装的是 v0.32.0 的发布产物，
也就是用户实际拿到的那份求解器，不是我们的集成开发版。适配层也是对着它重新编译的。

`LongTermGoals` 在 0.32.0 里没有，`SolverCompat` 的可选绑定按设计跳过——这一轮第一次
真实走到那条路径，勤学精进的计价不受影响。

## 打包

- [x] 图标 `publish/icon.png`（1254×1254，作者自制）
- [x] 创意工坊预览图 `publish/image.png`（512×512，479 KB）。
      上传器只认工作区里的 `image.png` 一个文件，其余尺寸不是必需的。
      原图缩到 1024 是 1783 KB，超 Steam 的 1 MB 上限；640 是 727 KB；选了 512 留余量
- [x] `publish/workshop.json`（标题、中英两份正文、可见性 `private`、依赖）。
      正文由 `publish/build-workshop-json.py` 从 `steam-description.md` 生成，别手工改 JSON。
      依赖三个 item id 都已填：RitsuLib `3747602295`、观者 `3747526116`、求解器 `3790899961`
- [x] 上传工作区 `ModUploader-win-x64/AutoWatcherWorkshop/` 已建好：
      `workshop.json` + `image.png` + `content/`（`AutoWatcher.dll`、`AutoWatcher.json`、
      `THIRD_PARTY_NOTICES.md`）。发版前要把 `content/` 里的 DLL 换成正式版构建
- [x] 上传器支持分语言描述。原来不支持，已给 `sts2-mod-uploader` 加 `localizations` 字段
      （commit `9043bb8`），新 exe 已装进 `ModUploader-win-x64/`，旧的留成
      `ModUploader.exe.bak-before-localizations`
- [x] 首次上传完成：**item id `3797303841`**，`mod_id.txt` 已在工作区，别删——以后更新靠它认条目。
      条目页 https://steamcommunity.com/sharedfiles/filedetails/?id=3797303841
      上传时中英两份标题描述都上了，三个依赖也都加上了
- [ ] 可见性从 `private` 改 `public`。**下一步在作者手里**：自己订阅装一遍确认能加载，
      确认了再把 `publish/steam-description.md` 同目录的 `workshop.json` 里 `visibility`
      改成 `public` 重传一次

## 清单（`AutoWatcher.json`）

- [x] `id` = `AutoWatcher`，`name` = `自动观者`
- [x] `affects_gameplay: false` —— 这条要保持，适配层不改任何游戏行为
- [x] `CombatSolver.min_version` = `0.32.0`（第一个发布产物里就带全五条登记入口的版本）
- [ ] `Watcher.min_version` 复核（现写 `0.9.25`，和钉死的哈希是同一版）
- [x] `version` = `1.0.0`

## GitHub 仓库

- [x] 面向用户的 `README.md`（中文）和 `README.en.md`（英文），互相链接
- [x] 原来那份 326 行的核对记录移到 `docs/VERIFICATION.md`
- [x] `LICENSE`（MIT）
- [x] `THIRD_PARTY_NOTICES.md`：publicizer 的用法，以及对 sts2 / CombatSolver / Watcher /
      RitsuLib 四个程序集的引用关系
- [x] `.gitignore` 复核：`local.props`、`bin`、`obj`、`.godot` 都不进仓库；仓库里没有任何
      第三方二进制
- [x] 远端仓库 https://github.com/bingyang1132/AutoWatcher （public，默认分支 `main`）
- [ ] 本地目录还叫 `SolverWatcherAdapter`。要不要跟着改成 `AutoWatcher` 由你定 ——
      改了会动到无头 harness 的按路径哈希的实例目录
- [x] `publish/RELEASE_NOTES_TEMPLATE.md`；v1.0.0 已按它发布

## 发布前的加固

- [x] 观者的精确哈希钉死换成「结构自检 + 核对标记」（`src/AdapterSelfCheck.cs`）。
      观者不能按版本号判——`Watcher.dll` 的 `AssemblyVersion` 是 `0.0.0.0`，真正的版本只在
      `Watcher.json` 里。哈希降级成「这一版核对过没有」，不一致仍加载但日志明确警告
- [x] 求解器的严格版本相等换成最低版本 `0.32.0`，外加对五个登记入口的结构自检
- [ ] 决定 8 处已知缺口哪些随版本发。我的看法是全部可以发 —— 没有一处会静默算错，
      那正是红字的用途 —— 创意工坊页面和两份 README 都已经写清楚了


---

## 1.0.1（2026-09-07 发布）

- [x] 四张牌补齐：聚能成刃、点穴、光辉、天人形态。已知缺口 5 处 → 1 处（画符）。
- [x] 上游两条随求解器 `0.33.0` 发布：PR #57（污染层数）、PR #58（Power 隐藏状态入口）。
- [x] 兼容 `0.33.0` 的回合结束流程改动。`CorePowerSupport.TriggerPlayerSideTurnEndEffects`
      被拆开，新版要补 `PlayerTurnEndLifecycle.RunPhaseTwo`；两个名字按字符串找，一份 DLL
      同时对得上 `0.32.0` 和 `0.33.x`，两边各编译一次都是 0 警告 0 错误。
- [x] 清单里求解器最低版本**仍是 `0.32.0`**。四张牌的数值在 `0.32.0` 上全对，只有光辉多记一条
      风险。创意工坊的求解器现在还是 `0.32.0`，抬门槛会把工坊用户全挡在外面。
- [x] GitHub release `v1.0.1`，附 `AutoWatcher-1.0.1.zip`。
- [x] 创意工坊 `3797303841` 已更新，`visibility` 写成 `public`，改动说明写了这四张牌。
- [ ] **四条新夹具没有运行。** 作者会在本地单独跑。这一版也没有原版角色的回归证据。
      release 说明里如实写了这一条。

---

## 1.0.2（2026-09-09 准备中）

跟进工坊的观者 `0.9.28` 和求解器 `0.34.8`。

- [x] 观者 `0.9.27 → 0.9.28` 反编译整体 diff：+927 / −25 行，**只有两处动了玩法**，都已跟进。
      逐条见 [docs/VERIFICATION.md](../docs/VERIFICATION.md)「观者 0.9.27 → 0.9.28」。
- [x] 钉死的观者副本换成 `0.9.28`，`PinnedTargets.VerifiedWatcherDllSha256` 和 csproj 的构建期
      校验值同步更新（`3262f520…c4311e5`）。构建 **0 警告**，也就是哈希对上了。
- [x] 求解器换到 `0.34.8`（Release 构建并部署），适配层对着它重编，`SolverCompat` 的三个反射
      入口仍然在（`GrowthSourceMirrors`、`CardRemovalValueMirrors`、`PowerHiddenStateMirrors`）；
      `LongTermGoals` 照旧不在发布版里，按设计跳过。
- [x] `AutoWatcher.json`：`version` = `1.0.2`，`Watcher.min_version` `0.9.25 → 0.9.28`。
      求解器最低版本**仍是** `0.32.0` —— 新用到的 `CardIsPlayableMirrors.Invoke` 从 `0.32.0`
      起签名没变过，抬门槛没有理由。
- [x] 夹具 30 → **32**，通晓万物那两条各验一半，正向都通过；`BLOCKED-EXHAUST` 的反向对照
      也做过（注掉判定 → 挂在「首个选牌是 `GRAND_FINALE`」）。
- [x] 面向用户的文案里观者要求从 `0.9.25 起` 改成 `0.9.28 起`：`README.md`、`README.en.md`、
      `publish/steam-description.md` 中英两份。
- [x] `publish/workshop.json` 重新生成，`changeNote` 写的是 1.0.2 的实际改动。
- [x] 上传工作区 `ModUploader-win-x64/AutoWatcherWorkshop/` 已换成 Release 构建的
      `AutoWatcher.dll`（1.0.2.0）和新的 `AutoWatcher.json`、`workshop.json`、`image.png`。
- [x] **全量 32/32 通过**（2026-09-10 00:20–00:41，求解器 `0.34.8` + 观者 `0.9.28`）。
      0 失败、0 未运行，21 分钟跑完，单条约 39 秒（0.34.7 那会儿是 ~52 秒）。
- [x] 上传创意工坊 `3797303841`（2026-09-10）。中英两份标题描述都传了，三个依赖沿用原有的
      没有改动。上传日志里 `k_EItemUpdateStatusInvalid` 是正常的完成态，不是错误。
- [x] GitHub release [`v1.0.2`](https://github.com/bingyang1132/AutoWatcher/releases/tag/v1.0.2)，
      附 `AutoWatcher-1.0.2.zip`，说明用的是
      [publish/RELEASE_NOTES_1.0.2.md](RELEASE_NOTES_1.0.2.md)。
- [x] 这一版仍然没有原版角色的回归证据，release 说明里如实写了。
- [x] release 说明里单列了一条提醒：成长额度填了非零值之后，求解器会关掉「打到可接受战损
      就提早收手」，每场都会跑满深化预算。根因在求解器侧，但额度是本 Mod 加进去的。
- [x] 原文末尾那句「已经在跟求解器作者沟通这一条」**已删**。issue 还没提，那句话当时不成立。
      GitHub release 正文已同步更新；工坊的 changeNote 和正文本来就没有这句。

### 1.0.2 之后：求解器又连发到 0.35.3

发版当晚上游从 `0.34.8` 一口气发到 `0.35.3`。**1.0.2 不需要改也不需要重发**：

- 适配层对着 `0.35.3` Release 编译 **0 警告 0 错误**，六个登记入口和全部补丁目标都在
  （`nameof` 的那些是编译期校验，唯一的字符串查找 `OnPlayWrapper` 也还在）。
- 清单里求解器最低版本 `0.32.0` 不用动。
- 本机部署已换成 `0.35.3` + 观者 `0.9.28` + AutoWatcher `1.0.2`。
- **32 条矩阵还没有对着 `0.35.3` 跑过**，现有的 32/32 是对 `0.34.8` 的。上游这几版动了回合末
  失血、单体/群体目标判断、死亡后的清理顺序，值得补跑一轮。
