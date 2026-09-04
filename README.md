# 观者求解器适配（SolverWatcherAdapter）

把观者 mod 的牌和姿态教给战斗路线求解器。本 mod **不改变任何游戏行为**，只补上求解器缺失的
模拟镜像，所以清单里 `affects_gameplay` 是 `false`。

## 它解决什么问题

求解器默认拒绝任何第三方 gameplay mod 的 ModHelper 订阅者，装上观者之后会直接停在
"检测到不兼容的第三方 Mod"。观者只注册了一个订阅者 `WatcherMod.WatcherEnchantStackHookProxy`，
它的两个方法都是对牌上附魔列表的纯计算、没有自身状态，所以放行它是安全的。

放行之后求解器能跑，但观者的牌对它是未知类型，会退化成空操作或粗略推断。本 mod 为这些牌注册
精确镜像。

## 三个钉死的版本

| 依赖 | 钉死方式 | 值 |
|---|---|---|
| 观者 mod | 文件 SHA256 | `9b6d6b88…f377733`（v0.9.25，Workshop 3747526116） |
| 求解器 | 程序集版本 | `0.29.0.0` |
| 游戏 | 清单 `min_game_version` | `0.111.0`（public-beta） |

观者按**哈希**钉死而不是按版本号，因为作者不一定每次改动都升版本号，而适配层是按反编译出来的
具体实现逐个动词写的。一个没升版本号的签名改动会让某张牌变成"没有效果但看起来正常"——哈希是
唯一能挡住这种情况的检查。构建期由 `VerifyWatcherHash` 目标校验，运行期由 `AdapterSelfCheck` 再
校验一次。

自检不通过时**一个镜像都不注册**。装一半比不装更糟：求解器会拿着一部分正确的镜像给出看起来
可信的路线，缺掉的那部分静默变成空操作。全都不装的话，求解器会明确停在那道门上并显示原因。

## 构建

```bash
cp local.props.example local.props   # 按你的 Steam 路径改
dotnet build SolverWatcherAdapter.csproj -c Release
```

构建会把 DLL 和清单复制到 `<游戏目录>/mods/SolverWatcherAdapter/`。前提是
`mods/CombatSolver/CombatSolver.dll` 和 `mods/Watcher/Watcher.dll` 都已存在。

## 当前覆盖范围

**初始牌组，5 张牌全覆盖：**

| 牌 | 效果 | 镜像方式 |
|---|---|---|
| `WATCHER_STRIKE_P` | 6 伤害 | 复用求解器通用攻击镜像 |
| `WATCHER_DEFEND_P` | 5 格挡 | 复用求解器通用格挡镜像 |
| `WATCHER_ERUPTION_P` | 9 伤害，进入愤怒 | 通用攻击 + 姿态动词 |
| `WATCHER_VIGILANCE` | 8 格挡，进入平静 | 通用格挡 + 姿态动词 |
| `WATCHER_MIRACLE` | 1 能量（清水遗物每场开局塞一张） | 直接给能量 |

**姿态动词**，对应 `WatcherCombatHelper.ChangeStance<T>`：入定锁检查、同姿态短路、按
神圣→预知→愤怒→平静顺序清除、施加新姿态、平静退出 +2 能量、神圣进入 +3 能量。

姿态本身就是原版可见的 `PowerModel`，所以按 Power 施加和清零之后，求解器的状态指纹、剪枝和
去重都自动认得它，不需要额外的预测状态。这也让 `Wrath`/`Divinity` 的伤害倍率自动正确——只读
钩子求解器本来就会回落到 mod 自己的实现。

**姿态切换的六路连带效果**：心之堡垒的格挡和紫莲花的能量已实现；凌波微步的抽牌、疾风连打
回手、退出预知的悟命会记一条风险，在求解器里显示成红色的"未镜像"，而不是静默算错。

**未覆盖**：其余约 96 张牌、9 个遗物、3 个药水。预视和选牌类效果是搜索分支问题而不是镜像问题，
需要单独处理。

## 测试环境要求

**必须收窄 mod 集。** 你装的 LotmMod 会让求解器停在同一道门上
（`LotmModCode.BaseCode.SpireKeywordModel`），跟观者无关。要干净测量，`mods/` 里只留：

```
CombatSolver
Watcher
SolverWatcherAdapter
```

其余 gameplay mod 暂存到 `mods_staged_by_claude/`，测完可以移回去。

## 强度验收标准

**不要用胜率或手感做验收。** 姿态被冻结时求解器会低估自己在愤怒姿态下的伤害，于是打得保守，
于是活得久——这种路线能通过手感检验，通不过严格 diff。正式版角色的标准是：

1. **严格 diff 零差异**：模拟的终局状态和真实终局状态逐字段相等。
2. **`PredictionGaps` 里非补偿项为空**：求解器自己不报告任何未镜像效果。

胜率是在这两条都干净**之后**才有意义的指标，用来抓 diff 抓不到的东西，比如姿态在估值函数里
定价错了。反过来先看胜率，会让你在错误的地方停下来。

## 复现 harness 需要的一次性修复

无头 harness 需要 `%LOCALAPPDATA%\CombatSolver\headless-runtime\Roaming\SlayTheSpire2\default\1\settings.save`。
这台机器上那个目录存在但缺这个文件，于是 harness 跳过了初始化拷贝、又在检查时失败。从交互档案
播种一份即可：

```bash
cp "$APPDATA/SlayTheSpire2/steam/<steamid>/settings.save" \
   "$LOCALAPPDATA/CombatSolver/headless-runtime/Roaming/SlayTheSpire2/default/1/settings.save"
```

另外 harness 会复用上一次的游戏进程（`reused_process=True`）。改了 mod 之后必须先
`Stop-Process -Name SlayTheSpire2`，否则测的还是旧的加载状态。
