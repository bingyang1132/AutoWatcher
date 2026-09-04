# 观者适配的分块路线图

## 为什么这件事比看起来便宜

101 张有 `OnPlay` 的观者牌，实际用到的效果词汇只有十来个：

| 调用 | 牌数 | 状态 |
|---|---|---|
| `DamageCmd.Attack` | 31 | 求解器通用攻击镜像已覆盖 |
| `CreatureCmd.GainBlock` | 18 | 求解器通用格挡镜像已覆盖 |
| `CardPileCmd.Draw` | 7 | 求解器通用抽牌镜像已覆盖 |
| `WatcherPowerCmdCompat.Apply<T>` | 36 | **待做**，见下 |
| `EnterWrath/Calm/Divinity/Foreseen` + `ExitStance` | 18 | **已做** |
| `GainMantra` / `ConsumeKnowFate` | 5 | 待做 |
| `Scry` / `ChooseOne` | 6 | 搜索分支问题，见第三块 |
| `CreateWatcherCard` / `TakeExtraTurn` / `DeferRetainCard` | 4 | 待做 |
| 原版零头（`GainEnergy` 3、`Add` 2、`AutoPlay` 2、`Upgrade`/`Exhaust`/`Kill`/`Damage`/`GainGold` 各 1） | 11 | 部分已有镜像 |

两个让这件事成立的结构性事实：

1. `WatcherPowerCmdCompat.Apply` 是对原版 `PowerCmd.Apply` 的纯反射包装，为了兼容不同游戏
   版本的签名，**没有任何自己的语义**。那 36 张牌要的就是原版施加 Power。
2. `SimulatedCombatState.ApplyPower(Type, ...)` 走 `MakeGenericMethod`，对任意 `PowerModel`
   子类都成立，不需要逐类型注册。观者那 42 个 Power 施加上去就能用。

所以要写的是十来个动词，不是 101 个牌镜像。观者的 42 个 Power 里有 7 个只重写了 `Modify*` /
`Should*`，这些走 `InvokeOriginal*` 回落到 mod 自己的实现，本来就正确——包括 `Wrath` 和
`Divinity` 的伤害倍率。

## 已完成

**第 0 块：探天花板。** 确认无头 harness 可用（`SMOKE-001` 通过），因此严格 diff 式的验收是
可行的。确认装上观者后求解器只在一个地方硬失败：
`IncompatibleGameplayModException: ... WatcherMod.WatcherEnchantStackHookProxy`。

**第 1 块（部分）：初始牌组 + 姿态动词。** 见 [README](../README.md#当前覆盖范围)。

## 待做

**第 1 块剩余：`WatcherPowerCmdCompat.Apply` 动词 + 真言与天命计数。**
36 张牌等这一个动词。真言和天命的计数存在 `WatcherStatePower` 的私有 CLR 字段里；
`PredictionUtils.CloneModelForSimulation` 用的是 `MemberwiseClone`，所以这些 int 在克隆时自动
带过去，根状态播种是对的。**但状态指纹不包含它们**，意味着只在计数上不同的两条分支会被去重
掉。这是接这一块时唯一需要认真处理的问题，处理方式有两条路：

- 把计数折射进一个求解器指纹能看见的地方（代价是要动求解器，或者上 Harmony）；
- 或者先只接不影响指纹的部分（比如 `TotalMantraGainedThisCombat` 只被光辉的伤害读，如果
  在一条路线内它单调不减，去重的风险可能可控）。

这个判断必须在真实数据上做，不要凭推理决定。

**第 2 块：五个 Harmony 路由的 hook。**
观者把 `BeforeHandDraw`、`AfterPowerAmountChanged`、`AfterSideTurnStart`、`AfterFlush`、
`AfterCardRetained` 挂在原版 hook 广播方法的 Harmony postfix 上，实现方法叫 `XxxCompat`。
求解器用自己的 mirror 替掉了整个 hook 分发，从不调那个被打补丁的方法，所以这五个**既不生效
也不记风险**——是唯一一类无声的缺口。其中 `Mantra.AfterPowerAmountChangedCompat` 是真言攒到
10 转神圣，观者的核心循环。

已知的实现细节（来自反编译）：阈值是变更后 `Amount >= 10`，减的是正好 10 而不是清零，而且
`_isResolving` 闩锁意味着 0 到 20 只转换一次、留下 10。**不要写成循环。**

**第 3 块：预视与选牌。** 6 张牌。这是搜索分支问题而不是镜像问题：预视要看牌堆顶 N 张并选一个
子集丢掉，得在 beam 上再开一层组合分支。仓库里有类似机制可参考（`KnowledgeDemonChoiceSupport`、
`TurnStartChoiceSupport`、`UnresolvedPlayerChoice`）。成本估不准。

**第 4 块：9 个遗物 + 3 个药水。** 量小。

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
