# 观者适配的分块路线图

## 为什么这件事比看起来便宜

101 张有 `OnPlay` 的观者牌，实际用到的效果词汇只有十来个：

| 调用 | 牌数 | 状态 |
|---|---|---|
| `DamageCmd.Attack` | 31 | 求解器通用攻击镜像已覆盖 |
| `CreatureCmd.GainBlock` | 18 | 求解器通用格挡镜像已覆盖 |
| `CardPileCmd.Draw` | 7 | 求解器通用抽牌镜像已覆盖 |
| `WatcherPowerCmdCompat.Apply<T>` | 36 | **已做**（泛型施加任意 PowerModel） |
| `EnterWrath/Calm/Divinity/Foreseen` + `ExitStance` | 18 | **已做** |
| `GainMantra` / `ConsumeKnowFate` | 5 | **已做**（含满 10 转神圣） |
| `Scry` / `ChooseOne` | 7 | 连带效果已做，挑牌记选择风险，见第三块 |
| `CreateWatcherCard` / `TakeExtraTurn` / `DeferRetainCard` | 4 | **已做** |
| 原版零头（`GainEnergy` 3、`Add` 2、`AutoPlay` 2、`Upgrade`/`Exhaust`/`Kill`/`Damage`/`GainGold` 各 1） | 11 | **已做** |

两个让这件事成立的结构性事实：

1. `WatcherPowerCmdCompat.Apply` 是对原版 `PowerCmd.Apply` 的纯反射包装，为了兼容不同游戏
   版本的签名，**没有任何自己的语义**。那 36 张牌要的就是原版施加 Power。
2. `SimulatedCombatState.ApplyPower(Type, ...)` 走 `MakeGenericMethod`，对任意 `PowerModel`
   子类都成立，不需要逐类型注册。观者那 42 个 Power 施加上去就能用。

所以要写的是十来个动词，不是 101 个牌镜像。观者的 42 个 Power 里有 7 个只重写了 `Modify*` /
`Should*`，这些走 `InvokeOriginal*` 回落到 mod 自己的实现，本来就正确——包括 `Wrath` 和
`Divinity` 的伤害倍率。

## 已完成

**第 0 块：探天花板。** 确认无头 harness 可用（`SMOKE-001` 通过），因此严格核对式的验收是可行的。
确认装上观者后求解器只在一个地方硬失败：
`IncompatibleGameplayModException: ... WatcherMod.WatcherEnchantStackHookProxy`。
另外确认了 LotmMod 会更早触发同一道门，所以测试必须收窄 mod 集。

**第 1 块：效果动词层 + 全部 101 张牌的 OnPlay 镜像。** 见 [README](../README.md#当前覆盖范围)。
真言满 10 转神圣也在动词层里补上了，那是五个无声缺口里最要紧的一个。

**指纹问题的结论：比预想的小得多。** 三份逐牌转写表核对下来，**没有任何一张牌的 `OnPlay` 读取
`WatcherStatePower` 的字段**，只有写入。唯一的读者是 `WATCHER_BRILLIANCE`（读
`TotalMantraGainedThisCombat`）。所以整个指纹风险被隔离在一张牌上，处理方式是：计数不为零时
才记一条风险，把这个已知的不确定性显式说出来。不需要为它动求解器，也不需要上 Harmony。

## 待做

**第 2 块（大部分已做）。** 三条 Harmony 补丁已覆盖玩家回合结束和回合开始。剩下的都是走
Harmony postfix 的 `XxxCompat` 方法或求解器根本不分发的钩子，各自需要一个新的接入点：

| 缺口 | 内容 | 影响 |
|---|---|---|
| `AfterSideTurnStartCompat` | 深思沉眠的下回合结算、预测移动改写敌人意图 | 两个 Power 完全不生效 |
| `BeforeHandDrawCompat` | 观者状态的延迟保留、预知的回合初预见 | 保留和预见时机错 |
| `AfterCardRetained` / `AfterFlush` | 洗炼的格挡、时之沙的费用、风车打击的伤害逐次增长 | 这三张牌在路线内被保留时数值不涨，系统性低估 |
| `OnScryDiscarded` | 圣歌与启示这两张不可打出牌的全部效果 | 完全不生效 |
| ~~`AfterCardChangedPiles`~~ | ~~凌波微步的延迟抽牌~~ | **已补**：`WatcherRushdownPatch` 挂在 `CombatPredictionSimulator.OnPlayWrapper` 之后，那正好是牌离开出牌堆之后。陶瓷鱼的金币不属于战斗状态，不做 |
| `AfterEnergyReset` | 天人形态的能量、能量下降 | 能量算错 |
| `AfterPlayerTurnStartEarly` | 观者状态的每回合计数器重置、神威天罚的每回合一次重置、悟命的真言发放 | 计数器不重置；神威天罚因此只能记风险不生效 |

`AfterEnergyReset` 有现成接入点（`TurnStartRelicSupport.TriggerAfterEnergyReset`），是下一个
最容易补的。`AfterPlayerTurnStartEarly` 没有独立接入点，但可以并到已有的回合开始补丁里，
只要确认它的时机在求解器流程里对得上。

**第 3 块：预见与选牌的搜索分支。** 7 张牌加姿态药水和天赋护符。这是搜索分支问题而不是镜像
问题：预见要看牌堆顶 N 张并选一个子集丢掉，得在 beam 上再开一层组合分支。目前按"一张都不丢"
建模并记选择风险——那是玩家一定做得出的选择，所以路线仍然可执行，只是没有探索丢牌的可能性。
仓库里有类似机制可参考（`KnowledgeDemonChoiceSupport`、`TurnStartChoiceSupport`、
`UnresolvedPlayerChoice`）。成本估不准。

**第 4 块：遗物与药水。已完成。** 见 [README](../README.md#遗物药水与钩子)。
## 已知的坑

**`SemanticStateFieldPolicy.ClassifyString` 会抛异常**，对任何未分类的 `StringVar` 状态字段。
观者里只有 `WatcherIntentProxy` 用 `StringVar`，而它是选牌界面用的临时卡、用完就丢，大概不会
进快照。真踩到的话，`PresentationOnlyFields` 是可变的 `HashSet`，加一行就行。

**`CalculatedVarSpecRegistry` 是唯一真正封闭的注册表**，`SupportedTypes` 是不可变数组，未命中
时直接抛 `NotSupportedException`。好消息是观者里**没有** `CalculatedVar` 也没有
`IComputedDynamicVar`（`BrillianceDamageVar` 继承的是 `DamageVar`），所以这条暂时不挡路。
但 `BrillianceDamageVar.UpdateCardPreview` 读 `WatcherStatePower.TotalMantraGainedThisCombat`，
接光辉的时候要一起处理。

**注册时机是硬约束。** `MethodMirrorRegistry.Lookup` 先查 `_resolvedSnapshot` 再查
`_registrations`，某个类型一旦被查过，之后再注册会被**静默丢弃**；而且并行搜索的各条车道会同时
读那个字典。所以注册必须在 mod 初始化阶段一次性同步做完。重复注册同一类型会抛异常。

**不要往上游提。** 这些代码引用第三方 mod 的类型和 helper 签名，`Torch1230/CombatSolver` 不会要。
适配层保持独立 mod、钉死三个版本，是这个方案能长期维持的前提。
