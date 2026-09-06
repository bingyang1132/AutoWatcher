# 发布清单

状态记于 2026-09-06。勾掉的是已完成，`BLOCK` 是硬阻塞。

## 硬阻塞

- `BLOCK` **求解器要先发一个带扩展点的版本。** 适配层现在依赖三条尚未上游的求解器改动
  （`pr/unplanned-optional-choice-dismiss`、`pr/mod-pile-discard-choice`、
  `pr/card-playability-mirror-coverage`），外加六处 Harmony 补丁。这些不落地，别人装上跑不起来。
  清单里 `CombatSolver.min_version` 现在写的是 `0.29.0`，**是错的**，等有了目标版本再定。
- `BLOCK` **0.31.1 的行为验收还没做。** 只做了编译验证。矩阵 22 条要全量跑一遍。

## 打包

- [x] 图标 `publish/icon.png`（1254×1254，作者自制）
- [ ] 创意工坊要几种尺寸的图（模板里 `image.png` 是 467×467，另有 `icon.jpg` / `profile.jpg`）。
      本机没有图像处理工具（无 ImageMagick / PIL），缩放要么你来，要么装一个
- [ ] **名字要定**：图标文件叫 `AutoWatcher`，清单里现在是 `SolverWatcherAdapter` /
      「观者求解器适配」。两个名字得统一，创意工坊标题、GitHub 仓库名、`mod id` 都跟着它
- [ ] `workshop.json`：标题、描述、可见性、依赖项（要填 CombatSolver 和 Watcher 的
      创意工坊 item id，不是 mod id）
- [ ] `content/` 目录：`SolverWatcherAdapter.dll` + `SolverWatcherAdapter.json`

## 清单（`SolverWatcherAdapter.json`）

- [ ] `CombatSolver.min_version` 改成真正含扩展点的那一版
- [ ] `Watcher.min_version` 复核（现写 `0.9.25`，和钉死的哈希是同一版）
- [ ] `version` 从 `0.1.0` 抬到发布版本号
- [x] `affects_gameplay: false` —— 这条要保持，适配层不改任何游戏行为

## GitHub 仓库

- [ ] 建仓库（现在本地无 remote）
- [ ] 面向用户的 `README.md`。现在这份 326 行是内部核对记录，
      建议移到 `docs/VERIFICATION.md`，另写一份短的
- [ ] `LICENSE`。建议 MIT，和大多数 STS2 mod 一致
- [ ] `THIRD_PARTY_NOTICES.md`：说明 publicizer 的用法，以及对 sts2 / CombatSolver / Watcher
      三个程序集的引用关系
- [ ] `.gitignore` 复核，确认 `local.props`、`bin`、`obj` 都不进仓库
- [ ] **不要**把钉死的观者二进制放进仓库，只留哈希

## 发布前的加固

- [ ] 观者的**精确哈希钉死**换成「最低版本 + 结构自检」。哈希钉死会在观者每次更新时直接砖掉。
      自检要核对用到的那几个方法签名和两个私有字段还在不在，不在就干净地拒绝加载
- [ ] 求解器的**严格版本相等**换成最低版本
- [ ] 决定 8 处已知缺口哪些随版本发。我的看法是全部可以发 —— 没有一处会静默算错，
      那正是红字的用途 —— 但创意工坊页面必须写清楚
