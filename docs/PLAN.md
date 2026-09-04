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

**第 2 块：剩下四个 Harmony 路由的回合流程钩子。**
观者把 `BeforeHandDraw`、`AfterSideTurnStart`、`AfterFlush`、`AfterCardRetained` 挂在原版 hook
广播方法的 Harmony postfix 上，实现方法叫 `XxxCompat`。求解器用自己的 mirror 替掉了整个 hook
分发、从不调那个被打补丁的方法，所以这些**既不生效也不记风险**。涉及的能力：预知的回合初预视、
观者状态的回合初重置、预测移动、深思沉眠，以及三张牌被保留时的数值增长
（`WATCHER_PERSEVERANCE` 的格挡、`WATCHER_SANDS_OF_TIME` 的费用、`WATCHER_WINDMILL_STRIKE`
的伤害）。第五个 `AfterPowerAmountChanged` 已经在动词层里覆盖了。

**第 3 块：预视与选牌的搜索分支。** 7 张牌。这是搜索分支问题而不是镜像问题：预视要看牌堆顶 N
张并选一个子集丢掉，得在 beam 上再开一层组合分支。目前按"一张都不丢"建模并记选择风险——那是
玩家一定做得出的选择，所以路线仍然可执行，只是没有探索丢牌的可能性。仓库里有类似机制可参考
（`KnowledgeDemonChoiceSupport`、`TurnStartChoiceSupport`、`UnresolvedPlayerChoice`）。成本估不准。

**第 4 块：9 个遗物 + 3 个药水。** 量小。其中 `PureWater` 不需要镜像——它在战斗开局往手里塞
奇迹，而求解器的根快照是在战斗开始之后取的，那张奇迹本来就在手上了。
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
