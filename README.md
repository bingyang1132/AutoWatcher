# 自动观者 · AutoWatcher

[English](README.en.md)

让**自动战斗求解器**看懂**观者**。

求解器默认拒绝第三方角色 Mod，装上观者之后会直接停在「检测到不兼容的第三方 Mod」。
装上本 Mod 之后，求解器不但能跑，还能真正理解观者的牌、姿态和额外回合。

**本 Mod 不改变任何游戏行为。** 它只补上求解器缺失的模拟镜像，清单里 `affects_gameplay`
是 `false`。你的牌、伤害、一切数值都和不装它时完全一样，唯一的区别是求解器算得对。

## 需要装什么

| 依赖 | 说明 |
|---|---|
| 自动战斗求解器（CombatSolver） | [创意工坊](https://steamcommunity.com/sharedfiles/filedetails/?id=3790899961) ／ [GitHub](https://github.com/Torch1230/CombatSolver) |
| 观者（Watcher，作者 Boninall） | [创意工坊](https://steamcommunity.com/sharedfiles/filedetails/?id=3747526116) |
| RitsuLib | [创意工坊](https://steamcommunity.com/sharedfiles/filedetails/?id=3747602295) |

三个都要装。缺任何一个，本 Mod 都不会加载，也不会留下半装的状态。

## 两个版本

**求解器还在持续开发，本 Mod 跟着它走。** 适配层是贴着求解器的内部接口写的，
求解器一改，适配层就得跟一次。所以这里分两条线发布：

| 版本 | 对标 | 从哪拿 |
|---|---|---|
| **创意工坊版** | 创意工坊上的求解器 + 创意工坊上的观者 | 订阅即可，随求解器的工坊版本更新 |
| **GitHub 版** | 我们自己的开发版求解器 | [Releases](https://github.com/bingyang1132/AutoWatcher/releases) |

GitHub 的每个 release 里放两样东西：本 Mod 的构建产物，和它对应的**那一个**求解器版本的链接。
两者要配套用——拿工坊版求解器配 GitHub 版适配层，或者反过来，都可能对不上。

出了问题优先看你装的是哪一对。

## 覆盖了什么

- **全部 101 张观者卡牌**逐张核对镜像，按反编译出的实现写，不是照着卡面文字猜
- **四种姿态**的进入、退出、伤害倍率和能量收支，以及心之堡垒、紫莲花、疾风连打这些连带效果
- **预视**是真正的搜索分支：丢哪几张由求解器自己搜，不再每次预视都触发一次重算
- **额外回合**（跳跃）、手牌保留、回合开始与结束的各类钩子
- **红蓝无限**：凌波微步的进入愤怒抽牌已建模，求解器能认出并展开这条循环
- **跨战斗收益**：勤学精进的斩杀升级会被算进长期价值
- **9 个遗物、3 个药水、14 个 Power 钩子**，以及三处求解器没有注册表、只能用 Harmony 补的位置

## 已知缺口

有 8 处效果没有建模。它们**不会静默算错**——求解器会在路线上打出红字标明「这里有未镜像的
效果」，你看得见。清单在 [docs/VERIFICATION.md](docs/VERIFICATION.md) 的「显式记为未镜像的部分」一节。

还有一条已知的、**适配层修不了**的问题：求解器不会为了起甲把「以手拒之」排到攻击牌前面。
镜像本身是对的，坏的是求解器的动作分类，需要在求解器那边开一个第三方登记入口才能修。
细节同样在核对记录里。

## 报 Bug

求解器自带问题包导出。导出以后发到 [Issues](https://github.com/bingyang1132/AutoWatcher/issues)，
附上你看到的现象，并说明你装的是工坊版还是 GitHub 版。带上问题包我基本都能定位；
只有文字描述通常不够。

## 从源码构建

```bash
cp local.props.example local.props   # 按你的 Steam 路径改
dotnet build AutoWatcher.csproj -c Release
```

构建会把 DLL 和清单复制到 `<游戏目录>/mods/AutoWatcher/`。前提是
`mods/CombatSolver/CombatSolver.dll` 和 `mods/Watcher/Watcher.dll` 都已存在——
本仓库不包含任何第三方二进制，观者只按 SHA256 钉死校验。

开发相关的一切（逐牌核对、夹具矩阵、实机偏差记录）在 [docs/VERIFICATION.md](docs/VERIFICATION.md)，
未做项的清单在 [docs/PLAN.md](docs/PLAN.md)。

## 致谢与授权

- 观者 Mod 作者 **Boninall**
- 自动战斗求解器作者 **Torch1230**，以及求解器的各位贡献者
- 感谢 **Claude（Anthropic）** 在开发上的帮助

本 Mod 以 MIT 授权，见 [LICENSE](LICENSE)。第三方程序集的引用关系见
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。

本 Mod 是非官方社区作品，与 Mega Crit 无关。
