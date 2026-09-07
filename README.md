# 自动观者 · AutoWatcher

[English](README.en.md)

让**杀戮尖塔2战斗路线求解器**适用于**观者**。

**本 Mod 不改变任何游戏行为。** 它只补上求解器缺失的模拟镜像，清单里 `affects_gameplay`
是 `false`。牌、伤害、一切数值都和不装它时完全一样，唯一的区别是装上之后求解器可以自动化观者的打牌。

## 需要装什么

| 依赖 | 版本要求 | 说明 |
|---|---|---|
| 自动战斗求解器（CombatSolver） | **至少 0.32.0** | [创意工坊](https://steamcommunity.com/sharedfiles/filedetails/?id=3790899961) ／ [GitHub](https://github.com/Torch1230/CombatSolver) |
| 观者（Watcher，作者 Boninall） | 0.9.25 起 | [创意工坊](https://steamcommunity.com/sharedfiles/filedetails/?id=3747526116) |
| RitsuLib | — | [创意工坊](https://steamcommunity.com/sharedfiles/filedetails/?id=3747602295) |

**版本不对会怎样：**

- 求解器低于 0.32.0，或者缺了某个登记入口 → 本 Mod **干净地拒绝加载**，日志里写明缺什么、
  可以去哪拿对的版本。求解器会照常停在它自己那道第三方检查上。
- 观者更新了 → 只要它的内部结构没变，本 Mod 照常工作；结构变了就同样拒绝加载。

## 两个版本

**求解器还在持续开发，本 Mod 也会相应持续更新。** 适配层是贴着求解器的内部接口写的，
求解器一改，适配层就可能得跟一次。所以这里分两条线发布：

| 版本 | 对标 | 从哪拿 |
|---|---|---|
| **创意工坊版** | 求解器的**发布版**（0.32.0 起） | [创意工坊](https://steamcommunity.com/sharedfiles/filedetails/?id=3797303841) 订阅即可 |
| **GitHub 版** | 某一个确定的求解器版本，可能是开发版 | [Releases](https://github.com/bingyang1132/AutoWatcher/releases) |

## 覆盖了什么

- **观者的全部 100 个卡牌类型**逐个核对镜像，按反编译出的实现写，不是照着卡面文字猜
- **四种姿态**的进入、退出、伤害倍率和能量收支，以及心灵堡垒、疾风连击这些连带效果
- **预视**是真正的搜索分支：丢哪几张由求解器自己搜
- **额外回合**（腾跃）、手牌保留、回合开始与结束的各类钩子
- **红蓝无限**：🐯😡🥶😡🥶😡🥶😡🥶😡🥶😡🥶
- **跨战斗收益**：勤学精进的斩杀升级和许愿会被算进长期价值
- **9 个遗物、3 个药水、14 个 Power 钩子**，以及三处求解器没有注册表、只能用 Harmony 补的位置

## 已知缺口

有 5 处效果显式记为未镜像。它们**不会静默算错**——求解器会在路线上打出红字标明「这里有未镜像
的效果」，你看得见。另有一批走 Harmony postfix 或求解器不分发的钩子也没有覆盖。
两份清单都在 [docs/VERIFICATION.md](docs/VERIFICATION.md)。

## 报 Bug

求解器自带问题包导出。导出以后发到 [Issues](https://github.com/bingyang1132/AutoWatcher/issues)，
附上看到的现象。欢迎捉虫！

## 从源码构建

```bash
cp local.props.example local.props   # 按你的 Steam 路径改
dotnet build AutoWatcher.csproj -c Release
```

构建会把 DLL 和清单复制到 `<游戏目录>/mods/AutoWatcher/`。前提是
`mods/CombatSolver/CombatSolver.dll` 和 `mods/Watcher/Watcher.dll` 都已存在——

开发相关的一切（逐牌核对、夹具矩阵、实机偏差记录）在 [docs/VERIFICATION.md](docs/VERIFICATION.md)，
未做项的清单在 [docs/PLAN.md](docs/PLAN.md)。

## 致谢与授权

- 观者 Mod 作者 **Boninall**
- 自动战斗求解器作者 **Torch1230**，以及求解器的各位贡献者
- 感谢 **Claude（Anthropic）** 在开发上的帮助

本 Mod 以 MIT 授权，见 [LICENSE](LICENSE)。第三方程序集的引用关系见
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
