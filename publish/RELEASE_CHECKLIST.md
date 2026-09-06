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
- [x] **0.31.1 的行为验收**：2026-09-06 全量跑过 23 条，22 通过。
      `WATCHER-DEUS-EX-MACHINA-DRAW` 那一条在批量里失败、单独重跑 3 次全过；失败时机和我在
      同一台机器上跑另一个 dotnet publish 重合，判为负载引起的抖动。**跑矩阵的时候别在同一台
      机器上跑别的构建。**
- `BLOCK` **以手拒之修好之后要再全量跑一次。** 改动落在 `StateEvaluation`，是跨切面的。

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
- [ ] 首次上传后把生成的 `mod_id.txt` 留在工作区
- [ ] 可见性从 `private` 改 `public`（先私有传一次，自己订阅装一遍确认能加载）

## 清单（`AutoWatcher.json`）

- [x] `id` = `AutoWatcher`，`name` = `自动观者`
- [x] `affects_gameplay: false` —— 这条要保持，适配层不改任何游戏行为
- [ ] `CombatSolver.min_version` 改成真正含扩展点的那一版
- [ ] `Watcher.min_version` 复核（现写 `0.9.25`，和钉死的哈希是同一版）
- [ ] `version` 从 `0.1.0` 抬到发布版本号

## GitHub 仓库

- [x] 面向用户的 `README.md`（中文）和 `README.en.md`（英文），互相链接
- [x] 原来那份 326 行的核对记录移到 `docs/VERIFICATION.md`
- [x] `LICENSE`（MIT）
- [x] `THIRD_PARTY_NOTICES.md`：publicizer 的用法，以及对 sts2 / CombatSolver / Watcher /
      RitsuLib 四个程序集的引用关系
- [x] `.gitignore` 复核：`local.props`、`bin`、`obj`、`.godot` 都不进仓库；仓库里没有任何
      第三方二进制
- [ ] 建远端仓库（现在本地无 remote），名字 `AutoWatcher`
- [ ] 本地目录还叫 `SolverWatcherAdapter`。要不要跟着改成 `AutoWatcher` 由你定 ——
      改了会动到无头 harness 的按路径哈希的实例目录
- [ ] Release 说明模板：写清这一版对标哪个求解器版本，并给出那一版的下载链接

## 发布前的加固

- [ ] 观者的**精确哈希钉死**换成「最低版本 + 结构自检」。哈希钉死会在观者每次更新时直接砖掉。
      自检要核对用到的那几个方法签名和两个私有字段还在不在，不在就干净地拒绝加载
- [ ] 求解器的**严格版本相等**换成最低版本
- [ ] 决定 8 处已知缺口哪些随版本发。我的看法是全部可以发 —— 没有一处会静默算错，
      那正是红字的用途 —— 创意工坊页面和两份 README 都已经写清楚了
