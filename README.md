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

**全部 101 张观者卡牌都注册了精确镜像。** 观者的牌实际只用到十来个效果动词，绝大多数牌是
原版命令加一个观者动词，所以镜像是声明式的组合，一张牌一个方法、一行一效果、按反编译源码的
调用顺序排列，可以逐行对照 `docs/` 里那份逐牌转写表来审。

**动词层**（`src/WatcherVerbs.cs`）：攻击（单体、全体、随机、多段、显式数值）、格挡、抽牌、
能量、任意 PowerModel 的施加与设值、生成牌进任意牌堆、牌堆间移动、直接击杀、真言（含满 10
转神圣）、天命消耗、预视的连带效果、额外回合、以及读取手牌数、敌人数、目标生命、上一张牌类型。

**姿态动词**（`src/WatcherStanceVerbs.cs`）：入定锁检查、同姿态短路、按神圣→预知→愤怒→平静
顺序清除、施加新姿态、平静退出 +2 能量、神圣进入 +3 能量，以及姿态变化的连带效果——心之堡垒
的格挡、紫莲花的能量、疾风连打从弃牌堆回手。

姿态本身就是原版可见的 `PowerModel`，按 Power 施加和清零之后求解器的状态指纹、剪枝和去重都
自动认得它，所以不需要额外的预测状态，`Wrath` 和 `Divinity` 的伤害倍率也自动正确——只读钩子
求解器本来就会回落到 mod 自己的实现。

### 显式记为未镜像的部分

下表里的牌用 mod 的牌 ID 指称，不用中文名——观者的官方中文名要从游戏本地化里核对，这份文档还没核对过。

这些地方求解器会显示成红色，不会静默算错：

| 内容 | 原因 |
|---|---|
| `WATCHER_CUT_THROUGH_FATE` / `WATCHER_JUST_LUCKY` / `WATCHER_THIRD_EYE` 的预视丢牌选择 | 挑哪几张丢是搜索分支问题，要在 beam 上再开一层组合分支。连带效果（涅槃格挡、经纬回手）已实现，按"一张都不丢"建模，那是玩家一定做得出的选择，所以路线仍可执行 |
| `WATCHER_OMNISCIENCE` / `WATCHER_FOREIGN_INFLUENCE` / `WATCHER_MEDITATE` / `WATCHER_WISH_P` 的选牌 | 同上 |
| `WATCHER_DRAW_TALISMAN` 的批量临时附魔 | 需要附魔系统的建模 |
| `WATCHER_CONJURE_BLADE` 生成的 `WATCHER_EXPUNGER` 段数 | 求解器的生成接口按牌类型创建规范实例，不接受实例级负载 |
| `WATCHER_DEVA_FORM` 的第二个及之后的实例 | 那个 Power 自己维护一个实例表，N 张牌是 N 个独立成长的实例，不等于一个数量为 N 的实例 |
| `WATCHER_PERSEVERANCE` / `WATCHER_SANDS_OF_TIME` / `WATCHER_WINDMILL_STRIKE` 被保留时的数值增长 | 发生在保留钩子里。根状态克隆时已带上之前累积的结果，缺的只是路线内部发生的保留 |
| `WATCHER_CONCLUDE` / `WATCHER_MEDITATE` / `WATCHER_VAULT` 打出后强制结束回合 | 结束回合在求解器里是它自己的动作、由搜索决定，卡牌镜像不该越过它改回合流程 |
| `WATCHER_PRESSURE_POINTS` 的无视格挡伤害 | 带 Unblockable 和 Unpowered，动词层里没有对应形式。标记本身叠对了 |
| `WATCHER_LESSON_LEARNED` 的永久牌组升级 | 超出单场战斗模拟的范围 |
| `WATCHER_BRILLIANCE` 的伤害 | 取自 `WatcherStatePower` 的私有计数器，而该计数不在状态指纹里。只在计数不为零时才记风险 |
| 三张牌的"上一张牌类型"条件 | 路线的第一张牌在模拟历史里看不到前一张。不从镜像读实时状态，因为镜像跑在后台线程上 |

多人局专属牌（`WATCHER_COLD_OBSERVATION`、`WATCHER_MOCKERY`、`WATCHER_PERSUASION`、`WATCHER_RELINQUISH`、`WATCHER_SANCTIFICATION`）注册成记风险而不是空操作：求解器只支持
单人战斗，万一它们出现要能立刻看见。

**仍未覆盖**：9 个遗物、3 个药水，以及五个走 Harmony postfix 的回合流程钩子
（详见 `docs/PLAN.md`）。
## 测试环境要求

**必须收窄 mod 集。** 你装的 LotmMod 会让求解器停在同一道门上
（`LotmModCode.BaseCode.SpireKeywordModel`），跟观者无关。要干净测量，`mods/` 里只留：

```
CombatSolver
Watcher
SolverWatcherAdapter
```

其余 gameplay mod 暂存到 `mods_staged_by_claude/`，测完可以移回去。

## 验收结果（2026-09-04，6/6 通过）

对 `游戏 v0.111.0 + 观者 0.9.25 + CombatSolver 0.29.0 + 适配 0.1.0` 实测：

| 用例 | 判别依据 | 结果 |
|---|---|---|
| `WATCHER-ERUPTION-WRATH` | 爆发是首个动作，未镜像项 0 | 通过 |
| `WATCHER-VIGILANCE-BLOCK` | 首回合格挡峰值 ≥ 8 | 通过 |
| `WATCHER-CALM-EXIT-ENERGY` | 固定 4 能量下首回合 3 个动作（没有退出平静的 2 点则任何顺序都只有 2 个） | 通过 |
| `WATCHER-MIRACLE-ENERGY` | 首回合 3 个动作（没有奇迹的 1 点则第二张爆发付不起） | 通过 |
| `WATCHER-WRATH-DOUBLE-DAMAGE` | 单敌人 21 血第一回合击杀（9 + 6×2；没有翻倍只有 15） | 通过 |
| `WATCHER-STANCE-REGRESSION-LOCK` | 100 血投影三回合结束，终局敌方总生命 0 | 通过 |

前五条都是算术判别：镜像算错，数值就对不上。第六条是实测出来的回归锁。
六条都带 `-ExpectedInitialUnmirroredCount 0`，所以**求解器在观者初始牌组上不报告任何未镜像
效果**——验收标准的第二条已经成立。

重跑：

```bash
pwsh -NoProfile -File tools/run-watcher-matrix.ps1
```
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
