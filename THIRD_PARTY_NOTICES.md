# Third-Party Notices · 第三方声明

Last updated: 2026-09-06

自动观者（AutoWatcher）本身以 MIT 授权（见 `LICENSE`）。本文件说明它和三个第三方程序集的关系。

AutoWatcher itself is MIT licensed (see `LICENSE`). This file documents its relationship to three
third-party assemblies.

## 本仓库不包含任何第三方二进制

No third-party binaries are included in this repository.

`sts2.dll`、`CombatSolver.dll`、`Watcher.dll`、`STS2-RitsuLib.dll` 全部在构建时从本机的游戏安装
目录读取（路径由 `local.props` 指定），并且都以 `Private="false"` 引用——它们不会被复制进构建
产物，也不会随发布包分发。发布包只含 `AutoWatcher.dll`、`AutoWatcher.json` 和本文件。

All four are read at build time from the local game installation (paths configured in
`local.props`) and referenced with `Private="false"` — they are never copied into build output and
never redistributed. A release package contains only `AutoWatcher.dll`, `AutoWatcher.json` and
this file.

## 依赖

### Slay the Spire 2 · `sts2.dll`

- 版权归 Mega Crit。
- 仅作编译期引用。本 Mod 是非官方社区作品，与 Mega Crit 无关。

### Combat Solver（自动战斗求解器）· `CombatSolver.dll`

- 作者：Torch1230 及各位贡献者
- GitHub: https://github.com/Torch1230/CombatSolver
- 自动观者是它的适配层：向它的镜像注册表登记观者的牌与效果，并在它没有注册表的少数位置
  用 Harmony 打补丁。运行期依赖，编译期引用。
- 求解器自身的第三方声明（含 Random Foreseer 的许可）随求解器分发，不在本仓库重复。

### Watcher（观者）· `Watcher.dll`

- 作者：Boninall
- Steam Workshop: https://steamcommunity.com/sharedfiles/filedetails/?id=3747526116
- 本 Mod **不修改、不重分发**观者的任何内容。它只读取观者的类型与实现，在求解器一侧建立
  等价的模拟镜像。
- 观者按文件 SHA256 钉死校验（构建期 `VerifyWatcherHash`，运行期 `AdapterSelfCheck`）。
  钉死是为了正确性，不是授权限制：镜像是照着具体实现逐个动词写的，一个没升版本号的签名改动
  会让某张牌变成「没有效果但看起来正常」，哈希是唯一能挡住这种情况的检查。

### RitsuLib · `STS2-RitsuLib.dll`

- Steam Workshop: https://steamcommunity.com/sharedfiles/filedetails/?id=3747602295
- 用于 mod 加载与日志。仅作编译期引用。

## 关于 publicizer

构建使用 `Krafs.Publicizer` 对 `sts2`、`CombatSolver`、`Watcher` 三个程序集放开内部成员访问。
这个过程只发生在**本机构建期**，产生的中间程序集写在 `obj/PublicizedAssemblies/` 下，被
`.gitignore` 排除，不进仓库也不进发布包。

- `CombatSolver`：适配层要登记进求解器的内部镜像注册表。
- `Watcher`：观者有几个 Power 把状态放在私有字段上，而维护那些字段的钩子求解器不分发，
  只能在适配层补写。
- `sts2`：镜像要按原版的具体实现逐字对照。

The publicizer runs at build time on the developer's machine only. Its output lives under
`obj/PublicizedAssemblies/`, is git-ignored, and is neither committed nor redistributed.

## 美术资源

`publish/icon.png` 及由它派生的创意工坊图片为 bingyang1132 原创，随本仓库以 MIT 授权。

`publish/icon.png` and the Workshop images derived from it are original work by bingyang1132,
MIT licensed along with the rest of this repository.
