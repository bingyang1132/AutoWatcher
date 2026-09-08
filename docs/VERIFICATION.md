# 自动观者 —— 核对记录

> 这份是开发用的核对记录：每一条镜像是怎么核的、哪些地方没做、实机报过哪些偏差、
> 夹具矩阵跑出什么结果。给使用者看的说明在仓库根目录的 [README.md](../README.md)。

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
dotnet build AutoWatcher.csproj -c Release
```

构建会把 DLL 和清单复制到 `<游戏目录>/mods/AutoWatcher/`。前提是
`mods/CombatSolver/CombatSolver.dll` 和 `mods/Watcher/Watcher.dll` 都已存在。

## 当前覆盖范围

**观者的全部 100 个卡牌类型都注册了精确镜像。**

数字的口径：反编译出 101 个 `WatcherCard` 子类，其中 `WatcherV2ChoiceTokenBase` 是抽象基类
（`IsPlayable => false`、不进卡牌图鉴），不是一张牌，所以具体卡牌类型是 100 个，全部登记。
这 100 个里包含选择令牌（如形态药剂的两张、许愿的三张）和多人局专属牌，它们不是玩家能
主动打出的 100 张不同的牌 —— 所以这里说"卡牌类型"而不是"张"。
（早先文档和文案写的"101 张"是把那个抽象基类算进去了，已订正。） 观者的牌实际只用到十来个效果动词，绝大多数牌是
原版命令加一个观者动词，所以镜像是声明式的组合，一张牌一个方法、一行一效果、按反编译源码的
调用顺序排列，可以逐行对照 `docs/` 里那份逐牌转写表来审。

**动词层**（`src/WatcherVerbs.cs`）：攻击（单体、全体、随机、多段、显式数值）、格挡、抽牌、
能量、任意 PowerModel 的施加与设值、生成牌进任意牌堆、牌堆间移动、直接击杀、真言（含满 10
转神圣）、天命消耗、预见的连带效果、额外回合、以及读取手牌数、敌人数、目标生命、上一张牌类型。

**姿态动词**（`src/WatcherStanceVerbs.cs`）：入定锁检查、同姿态短路、按神圣→预知→愤怒→平静
顺序清除、施加新姿态、平静退出 +2 能量、神圣进入 +3 能量，以及姿态变化的连带效果——心灵堡垒
的格挡、紫色莲花的能量、疾风连击从弃牌堆回手。

姿态本身就是原版可见的 `PowerModel`，按 Power 施加和清零之后求解器的状态指纹、剪枝和去重都
自动认得它，所以不需要额外的预测状态，`Wrath` 和 `Divinity` 的伤害倍率也自动正确——只读钩子
求解器本来就会回落到 mod 自己的实现。

**上一张牌类型**（粉碎关节、跟进、神圣、缠腰鞭四张牌的条件）先读模拟历史，路线第一张牌查不到时
回落到求解器的根历史快照。原版读的是整场战斗的打牌历史，所以上一张牌是**跨回合**的——一个回合
的第一张牌看到的是上一回合最后打出的那张。只有这一场还没打出过任何别的牌时才记风险。

**结算中强制结束回合**（结末、冥想、腾跃）走求解器自己的
`SimulatedCombatState.RequestPlayerTurnEnd`，和原版虚空形态用的是同一条路：搜索在这张牌结算完
之后立刻推进回合，并把这个动作标成结束回合。这一条必须真的建模，只记风险不够——风险只是显示上
的红字，不会把"结末后面还能接牌"这种不可能的续接从搜索里去掉。实机就出过事故：求解器给的路线
是结末之后再补一张打击收掉最后一个敌人，于是认为渎神的回合结束死亡永远不会到来，实际打击根本
没机会打出，下一回合开始玩家被渎神杀死。

腾跃给的额外回合也建了模。求解器判断"能不能再来一个回合"的地方是硬编码的，只看龙涎香和帕尔
之眼，所以由 `src/WatcherExtraTurnPatch.cs` 补上观者的来源和它的消耗。

### 红蓝无限

观者的招牌循环：有凌波微步在场时，每次进入愤怒抽 2 张；如果牌堆里只剩一张 1 费进平静的牌和
一张 1 费进愤怒的牌，退出平静补的 2 点能量正好抵掉两张牌的费用，于是可以一直打下去。

**求解器本身支持这类循环**，是 0.28.0 由 @ltlly 的 PR #35 加的：它按牌堆的"牌 ID 加升级等级"
多重集认出复现的循环形状，要求相邻两个周期的动作序列和状态增量都一致，然后给这条线额外的
搜索配额继续展开。整条路径不读任何具体牌名或 Power 名，仓库还有一条 CI 检查专门禁止把卡牌、
遗物、敌人名字写进循环规划文件，所以第三方 mod 的牌和原版牌一视同仁。

真正卡住这个循环的是适配层：凌波微步的抽牌以前没建模，求解器根本看不见这条线。现在建了模，
**验收里那条用例第一回合打出 11 张牌、`cycle_shapes=13`、战斗第一回合结束、无未镜像效果。**

抽牌的时机是这条线成立与否的关键，写在 `src/WatcherStanceVerbs.cs` 和
`src/WatcherRushdownPatch.cs` 的注释里：抽必须发生在触发它的那张牌离开出牌堆之后。要是在结算
当中就抽，那张牌还在出牌堆里，抽牌堆见底重洗时抽不到它，循环就断了。

有一个边界要知道：求解器判断"这个循环有没有收益"时，读的是一组通用维度——敌方生命、格挡、
玩家生命、能量、牌堆规模这些都是从模拟状态直接量出来的，任何来源都算得对。但"持久增益"那几个
维度是按原版 Power 类型分派的，观者自己的 Power 在那里记零。所以**收益体现在伤害或格挡上的
循环没问题**（红蓝无限就是这种），收益只体现在某个观者专属 Power 上的循环，会在有界配额用完
之后被当成没有进展停掉。

### 显式记为未镜像的部分

下表里的牌用 mod 的牌 ID 指称，不用中文名——观者的官方中文名要从游戏本地化里核对，这份文档还没核对过。

这些地方求解器会显示成红色，不会静默算错：

| 内容 | 原因 |
|---|---|
| `WATCHER_DRAW_TALISMAN` 的批量临时附魔 | 临时附魔栈是一张静态 `ConditionalWeakTable`，在模型状态之外，见下面「画符为什么是另一类」 |

以下四条原先在这张表里，现已补齐：

| 内容 | 怎么解决的 |
|---|---|
| `WATCHER_CONJURE_BLADE` 生成的 `WATCHER_EXPUNGER` 段数 | 生成接口会把加进去的那张牌返回出来，拿到之后写 `HitCount` 即可——原版也是先建后赋值再入堆。落点是 `DynamicVars.Repeat`，普通数值变量，会进指纹 |
| `WATCHER_PRESSURE_POINTS` 的无视格挡伤害 | 原版走的是 `CreatureCmd.Damage` 而不是 `DamageCmd.Attack`，求解器的 `Damage` 重载本身就收 `ValueProp`，照 `Unblockable \| Unpowered` 调即可。不能用攻击动词，那条路会套上全部攻击修正 |
| `WATCHER_BRILLIANCE` 的伤害 | 数值一直算得对，缺的是累计真言进指纹。求解器 0.33.0 起有 `PowerHiddenStateMirrors`，登记一个读取函数即可，见下面「隐藏状态这一类」 |
| `WATCHER_DEVA_FORM` 的第二个及之后的实例 | 整张实例表不必复现，只需要多记一个「实例个数」。见下面「隐藏状态这一类」 |

#### 隐藏状态这一类：光辉与天人形态

两张牌卡在同一个地方，值得写清楚，免得下次又从头查一遍。

状态指纹里 Power 的通用部分（`SimulatedCombatState.AddPower`）**只收 `DynamicVars`**。
`WatcherStatePower._totalMantraGainedThisCombat` 和 `DevaPower` 的实例表都不在那里。

- 对**续接**没有害处：续接戳的 Power 段实机侧和模拟侧共用同一个方法，同样只读 `DynamicVars`，
  两边一致，所以这类状态压根不进戳、也不会对不上。
- 对**搜索去重**有害处：只在这个状态上不同的两条分支指纹相同，会被当成同一个状态去掉一条。
  数值算得对，但算得对的那条可能被丢掉。**记风险标记解决不了这件事**，红字只是显示。

原版同形状的 Power（虚空形态、硬化外壳、自动机、束缚锁链）走的是另一条路：
`AddTurnStartStates` 按类型 `switch`，从 `StateStore` 里的预测状态取一个计数进指纹。那个 `switch`
原先没有第三方入口，所以上游开了一个：`PowerHiddenStateMirrors`，随求解器 **0.33.0** 发布（PR #58）。

适配层的用法在 `src/WatcherHiddenState.cs`：

| 状态 | 存在哪 | 需要什么 |
|---|---|---|
| 累计真言 | `WatcherStatePower` 的普通私有 `int` | 只要读取函数。`MemberwiseClone` 会把普通字段带进克隆 |
| 天人形态实例个数 | `DevaPower` 的 `_internalData` | 读取函数**加**根捕获。`PowerModel.DeepCloneFields` 会把 `_internalData` 重置成 `InitInternalData()`，所以克隆读到的是空表，必须在根捕获时读一次实机实例 |

**天人形态为什么只需要一个整数。** `AddInstance` 之后 `SetAmount(Instances.Sum())`，而
`AfterEnergyReset` 每回合先给 `Sum()` 点能量再把每个实例加一——下一回合的总和等于当前总和加实例
个数。总和就是 `Amount`，本来就在指纹里。个数存在 `simulator.StateStore` 里（不能存在 Power 上，
每次分叉都会被克隆重置），每回合的能量补在 `PersistentPowerSupport.TriggerAfterEnergyReset` 后面
——那一段也没有注册点。

**0.32.0 上会怎样**（创意工坊现在还是这一版）。登记是可选绑定：绑不上就跳过。光辉的伤害照样
算对，但会恢复记一条风险（只在累计真言不为零时）；天人形态的能量也照样算对（个数走
`StateStore`，与指纹无关），只是两条只在这些计数上不同的分支可能被去重。加载日志里那句
「Power 隐藏状态进指纹：……」会写明走的是哪一边。

**顺带记一处 0.33.0 的破坏性改动。** `CorePowerSupport.TriggerPlayerSideTurnEndEffects` 被拆开了：
改名成 `TriggerPlayerRegularSideTurnEndEffects`，结尾的 `EndTurnPowerSupport.TriggerLate` 和
`NormalizeCardAfflictions` 挪进了 `PlayerTurnEndLifecycle.RunPhaseTwo`。观者回合结束那条补丁
（神圣退出、终焉群体伤害等）原来补在前者后面，现在要补 `RunPhaseTwo` 才是同一个语义位置——终焉的
群体伤害跑在晚阶段 Power 之前还是之后是有区别的。两个名字都按字符串找，一份 DLL 同时对得上
0.32.0 和 0.33.x；Harmony 按参数名注入，而两版那个参数分别叫 `players` 和 `participants`，
所以有两个只差签名的 Postfix 薄壳。

#### 画符为什么是另一类

不是缺登记点，是那份状态根本不在模型里。`WatcherEnchantStack._extras` 是一张
`static ConditionalWeakTable<CardModel, List<EnchantmentModel>>`，按牌的**对象标识**索引。求解器
在克隆出来的预览牌上搜索，克隆件不在表里；反过来，在搜索中调 `ApplyTempEnchantment` 会把假设
写进一张搜索结束后还在的全局静态表，污染实机。所以这个方法在搜索里一次都不能调。

不过有两点比原先记的乐观：

- **随机池只含纯改数值的附魔。** `RandomPool` 过滤掉了带 `OnEnchant` 或 `OnPlay` 覆写的，剩下的
  只有 `ModifyCard()`。
- **每张牌只加一条，而且落在原版那个附魔槽里。** `MagicNumber` 是 1，升级只改费用和保留，所以
  `times` 恒为 1；`ApplyTempEnchantment` 在 `card.Enchantment == null` 时走的是
  `card.EnchantInternal`，也就是原版单槽，而求解器**已经建模了那个槽**
  （`PredictedCard.Enchant` / `EnchantmentStateSupport.Append`，槽本身进续接戳）。只有同一张牌上
  的第二条才进 `_extras`。

再加上它的随机数是 `new Rng(runSeed ^ (uint)(round * 2654435761u))`，完全可复现。所以这张牌是
**可做但工作量大**：两个选项走 `CardChoiceMirrors`，逐牌复现随机抽取，用求解器的 `Enchant` 而不是
观者的 `ApplyTempEnchantment`，并把 `CanApplyAsTempStack` 那几道门禁一起镜像过来。还要确认一件事：
这些附魔是**临时**的（战斗结束清掉），别让求解器按永久附魔去算跨战斗价值。

多人局专属牌（`WATCHER_COLD_OBSERVATION`、`WATCHER_MOCKERY`、`WATCHER_PERSUASION`、`WATCHER_RELINQUISH`、`WATCHER_SANCTIFICATION`）注册成记风险而不是空操作：求解器只支持
单人战斗，万一它们出现要能立刻看见。

### 遗物、药水与钩子

**9 个遗物**分三类，只有一类需要注册镜像：阳、香料、斗篷扣走求解器会分发的钩子，已注册；
清水和泪滴挂坠在战斗开始前触发，求解器的根快照取在战斗开始之后，已经反映了结果，**不该**
补镜像否则会重复；金瞳和紫色莲花自己什么都不重写，是按遗物 ID 轮询的，已分别在预见张数和
姿态连带效果里读到。达玛茹走回合开始，见下。

**3 个药水**：神赐甘露进神圣、瓶装奇迹加两张奇迹，都已镜像；姿态药水的二选一走
`PotionChoiceMirrors`，见下面「姿态药水这一条要值多少」。

### 四张选牌卡

四张打出后要玩家当场选的牌都接上了搜索分支，不再记成未建模选择。

| 牌 | 走哪条通道 | 为什么 |
|---|---|---|
| 许愿 | `CardChoiceMirrors`，`ModDefined` | 三个固定结果，效果由适配层施加 |
| 冥想 | `CardChoiceMirrors`，`ModDefined` | 候选来自弃牌堆，但取回的牌要拿到单回合保留，现成的 `MoveToHand` 搬完没有接手的位置 |
| 通晓万物 | `CardChoiceMirrors`，`ModDefined` | 现成的 `AutoPlayRepeated` 结算第一行就是 `if (source is not DecisionsDecisions) return true;`，候选也限定手牌里的技能牌 |
| 他山之石 | **不用登记**，走生成选项通道 | 候选是随机生成的三张，和原版的发现、飞溅、丰饶同一形状 |

几条实现上的要点：

- **冥想**的保留不是可选项。这张牌打完回合就结束，取回来的牌没有保留会当场被弃掉，
  下回合手里是 0 张而不是 N 张。张数照 `Dredge` 的口径按手牌上限截断；原版放不进手牌时走
  `DeferRetainCard`，截断之后走不到，没有建模。
- **冥想的进平静和结束回合留在 OnPlay 镜像里**，顺序是对的：求解器先跑 OnPlay 再解析选择，
  而 `RequestPlayerTurnEnd` 只打标记，回合要等整张牌（含那次选择）结算完才推进。
- **通晓万物的「打出两次」不用自己循环**。原版是先挂 `OmniscienceDoublePower` 再自动打出，
  那个 Power 的 `ModifyCardPlayCount` 加一次、之后自己移除，适配层早就镜像了它。顺序照原版：
  先挂 Power 再打。抽牌堆为空时下界给 0，否则搜索会永远等一个做不出的选择。
- **他山之石的三张候选是确定的**，不是猜的：用求解器给的同一个 `Rng.CombatCardGeneration` 和
  同一个 `GetDistinctForCombat`，抽出来的三张和实机一致，所以部署时按令牌定位不会错位。
  候选池照原版取已解锁的各角色牌池、多于一个时去掉自己那个、筛攻击牌、排除 `CardRarity.Token`
  （原版写的是 `(int)Rarity != 7`）。升级版原版只把选中那张设成本回合免费，这里对三张候选都设 ——
  最终只有一张进手牌，结果等价，而三张一起设不会让分支排序偏向任何一张。
- `ModDefined` 的结算里求解器不替登记方把令牌解析回牌（它连 `SourcePile` 都不查），
  因为多数登记方的选项是凭空造的令牌、不在任何牌堆里。候选确实来自牌堆时要自己解析，
  匹配用求解器自己的 `MatchesToken`，口径完全一致。

### 许愿的三选一

3 费，打出后在三个愿望里选一个。三张选项牌是真的牌，各自带 `MagicNumber`：

| 选项牌 | 效果 | 基础 / 升级 |
|---|---|---|
| `WatcherWishAlmighty` | `StrengthPower` | 3 / 4 |
| `WatcherWishLiveForever` | `WishPlatedArmorPower` | 6 / 8 |
| `WatcherWishFameAndFortune` | 金币 | 25 / 30 |

登记在 `src/WatcherCardChoices.cs`，走求解器的 `CardChoiceMirrors`（分支
`pr/third-party-card-choice`）。三个愿望是三个真分支，挑哪个由搜索自己比。

三点值得记下来：

- **金币不需要新刻度。** 求解器的长期资源刻度本来就是金币刻度：`贪婪之手` 是
  `RecordLongTermResource(面值)`，猎杀记 30，生成一瓶药水记 20。所以金币这一支就是
  `GainPlayerGold` 加 `RecordLongTermResource`，面值直记。
- **金币换不到血,所以「后面有没有商店」不需要判断。** Beam 里
  `LongTermResourceBeamValue = 25_000` 对 `Hp = 30_000`，够让攒钱路线活到最后不被剪掉；
  而最终选择是字典序，长期资源排在 `StrategicHpDeficit`、`CombatEndedTurn`、
  `PolicyHpDeficit`、`HealthResourceCost` 全部之后。也就是说只要另一个愿望能省下哪怕 1 点血，
  它就直接赢。求解器只会在金币白拿的时候选金币 —— 那时候选它本来就是对的。
- **想让金币换到血，走的是另一条路：成长额度。** 上一条说的是「求解器自己不会为金币付血」，
  那是对的默认。但玩家可能就是想付——早期一笔钱买到的东西比几点血值。这件事由玩家在成长策略
  侧栏里填，不由求解器猜，见 [`src/WatcherGrowthSources.cs`](../src/WatcherGrowthSources.cs)。
  两笔账都要记：`RecordLongTermResource` 记「这条线路带走了多少局外价值」，
  `RecordGrowthReward` 记「玩家愿意为这次收益额外付多少血」。
- **三张选项牌要跟着本牌一起升级。** 原版对三张都调了 `UpgradeInternal`，spec 里必须照做：
  部署时按 CardId 加升级等级在原生页面上定位，升级等级不对就找不到那个选项。
  数值也从选项牌自己的 `MagicNumber` 上读，不写死 —— 那三个数在原版 `OnPlay` 和选项牌的
  `CanonicalVars` 里各写了一遍。

`WatcherWish_P` 的 OnPlay 镜像必须是**空的**：求解器出牌时会自己调
`ResolveManualCardChoice` 查选择规格，不需要镜像主动请求，在镜像里再施加一次就是算两遍。
和姿态药水的 `StancePotionOnUse` 同一个道理。

这一版观者里 `TryConsumeKnowFateBoost` 是基类实现、恒为 `false` 且不消耗任何东西，
所以 `boosted` 就是本牌的升级状态，没有「即使没选也已经消耗了天命」这回事。
反编译里出现的 `WatcherWishV2` 只是 VFX 的类型名匹配，这一版没有这个类。
以后观者加了会消耗天命的变体，那一层顺序依赖才需要建模。

**14 个 Power 钩子镜像**：交叉核对了观者所有类型对求解器 51 个受镜像钩子的重写，逐条补上。
另有 7 个 `Modify*` / `Should*` 重写不用补——求解器会回落到 mod 自己的实现，本来就正确，
姿态的伤害倍率就在其中。

**三条 Harmony 补丁**补的是求解器根本没有注册表的位置：

| 位置 | 补了什么 | 不补的后果 |
|---|---|---|
| 玩家回合结束 | 神圣退出姿态、钻研造牌、终焉群伤、两个自我移除的 Power | **神圣永不消失**，之后每回合都按神圣的倍率算伤害，路线被系统性高估 |
| 玩家回合开始（Power） | 虔诚给真言、战歌造牌、收集造升级奇迹、沸腾之怒进愤怒抽牌、预知给天命、亵渎的死亡结算 | 观者的循环引擎整批失效，路线被系统性低估 |
| 玩家回合开始（遗物） | 达玛茹的真言 | 神圣循环的节奏算错 |

三条都在加载时解析目标方法，解析不到就抛异常让整个适配层不注册——宁可明确不可用，也不要
装着装着少了一半回合效果。

**仍未覆盖**：一批走 Harmony postfix 或求解器不分发的钩子——
下回合开始的深思沉眠与预测移动、抽牌前的保留结算与预知预见、保留时三张牌的数值增长、
预见弃牌触发的两张不可打出牌、能量重置时的天人形态与能量下降。详见 `docs/PLAN.md`。
预见挑哪几张丢，现在由求解器自己搜。它走求解器的 `ResolvePileDiscardChoice`：候选是抽牌堆顶
那几张（张数已过金瞳和守视），下界 0 上界全选，于是"丢掉"和"留着"都是正常的世界线分支，
排序和分支上限沿用求解器现成的那套。部署时也照计划应答原生选牌页面，不再每次预见都触发重算。
夹具 `WATCHER-SCRY-DISCARD-BRANCH`：天眼配四张打击在抽牌堆，`choice_branches=4`，红字 0 条。

标志性一击有"手上只有它一张攻击牌才打得出"的条件，登记在 `CardIsPlayableMirrors` 里，判据读
模拟的手牌。不登记的话求解器按恒真算，会把它排进路线，直到部署那一步才报 `card_unplayable`，
而这时前面几个动作已经打出去了，只能从更差的局面重算。钉死的这一版观者只有这一张需要登记；
姿态药水的两张选择令牌也重写了这个属性，但写死是 `false`，基类回落本来就对。
夹具 `WATCHER-SIGNATURE-MOVE-UNPLAYABLE`。

机械降神被抽到时自动打出自己，落进消耗堆之后给两张奇迹 —— 也就是两点能量。按原版 `AfterCardDrawn`
的三步镜像：只在它是被抽进手牌的那一张时触发、自动打出它、按落点判断发不发奇迹。落点判断不能
省，原版就是按落点判的而不是按牌上的关键字判的。奇迹进手牌底部。
夹具 `WATCHER-DEUS-EX-MACHINA-DRAW`：敌人 22 血，三张打击 18 点杀不掉，必须靠这两点能量打出
第四张打击才够 —— 也就是说这条夹具只有在奇迹真的到手时才过。

勤学精进斩杀时永久升级牌组里的一张随机牌。升到哪张超出单场战斗的范围也不影响本场，所以不模拟；
但"斩杀了没有"决定求解器该不该为它凑最后一击，那是本场之内的决策。按原版猎杀、饱食、贪婪之手
同一条路记：兑现时记一笔长期资源加一个 `FatalKillBonus` 目标。它消耗，所以打出去就先记一笔
"已经打出了" —— 不斩杀就白扔，求解器要能把"从没抽到"和"当普通攻击打掉了"分开。
折价取 `30`，和猎杀同档。夹具 `WATCHER-LESSON-LEARNED-FATAL-KILL`：敌人 8 血正好斩杀，
`long_term_resource=30`、`long_term_goals=FatalKillBonus`。

### 起手打击与防御的移除估值

登记在 [`src/Entry.cs`](../src/Entry.cs)，走求解器的 `CardRemovalValueMirrors`。

净化这类**移除**选择按移除估值从低到高排，估值低的先烧。求解器有一张按类型写死的表把原版十张
起手牌压回正确位置（伤害 × 2/3、格挡 × 1.2），但那张表**只列原版**——注释写明「其他来源的
打击、防御强弱取决于各自的机制，这里没有依据替它们排序」。观者的打击落进通用估值，按伤害记满，
于是被算成一张好攻击牌，净化永远不会先烧它。

实测一场女王：同一副牌、同为疾风连击 4。玩家手打消耗掉三张观者打击，把全知与内心宁静留在牌库
里；求解器反过来消耗了全知、内心宁静、痛击，把四张打击留着。两边差别就在这里——只有前者的牌库
能持续转起来。

登记的是**类别**不是数值，权重仍在求解器那一侧，我们只声明「这是我的起手打击/防御」。入口是
可选的：绑不上就跳过，行为和登记这个口子之前一样，加载日志里写明。

**这只解决「起手牌该先烧」这一层。** 移除排序仍然是单卡估值、不看牌库其余部分，所以南瓜、钢铁
闪光这类同样是死牌的仍然不会被优先烧；那一层是求解器的估值口径问题，已另行提 issue 讨论。

### 真言的战略估值

登记在 [`src/WatcherStrategicEffects.cs`](../src/WatcherStrategicEffects.cs)，走求解器的 `StrategicEffectMirrors`。

核对过的实现:`GainMantra` 里变更后 `Amount >= 10` 就减掉**正好 10** 并进神格（`Mantra.AfterPowerAmountChangedCompat`，`_isResolving` 闩锁让一次只转换一次，所以 0 到 20 只转一次、留下 10）；进神格给 **3 费**（`Divinity.AfterApplied`）；**已经在神格里再进不给费**（`ChangeStance` 的 `current == target` 直接返回）；神格**回合结束退出**（`Divinity.AfterSideTurnEnd`）。所以一个回合之内只兑得到一次 3 费。

不登记的后果落在**刻度选错**，不是数值大小:默认兜底把第三方 Power 记进 `ScalingPotential`，于是「先祈祷把真言推到 10、进神格、用多出来的费打伤害」这条线在 `ClassifyActionOptionFamilies` 眼里完全不产生**资源**信号，祈祷和攻击的先后关系没有依据。这和以手拒之那条是同一类问题（那条错在防御信号，这条错在资源信号）。

收益本身一直是精确模拟的——祈祷够了真的会进神格、真的会多 3 费。缺的只是让搜索在兑现**之前**就看得见这条线。

取值取「离下一次兑现还差多少」，按 3 费封顶。**故意不把神格的伤害倍率算进去**:倍率在进入神格之后由模拟精确结算，而把「接近阈值」估得比一次兑现还高，正是会让求解器一直祈祷下去的那种估值。这一条和反弹格挡「宁可往高了估」的取舍方向相反，理由写在代码注释里。

夹具 `WATCHER-MANTRA-TO-DIVINITY` 覆盖阈值转换本身；本次登记**改的是出牌顺序**，需要整条观者矩阵复核，见下。

### 局外成长额度：勤学精进与许愿的金币

登记在 [`src/WatcherGrowthSources.cs`](../src/WatcherGrowthSources.cs)，走求解器的
`GrowthSourceMirrors`。

长期资源刻度和成长额度是两回事。前者记「这条线路带走了多少局外价值」，它在最终选择里排在所有
掉血项之后，所以**永远换不到血**；后者是玩家在成长策略侧栏里填的「每次收益允许的额外战损」，
那才是求解器可以拿血去换的额度。观者这两处收益原来只记前者，于是求解器知道收益到手了，
却不知道玩家愿意为它挨打——表现就是它永远不为这两张牌多挨一点伤害。

| 来源 | id | 侧栏那一行 | 判据 | 对应原版 |
|---|---|---|---|---|
| 勤学精进 | `AutoWatcher.LessonLearned` | 牌自己的图标和名字 | `card is WatcherLessonLearned` | 猎杀、狂宴、贪婪之手（斩杀兑现） |
| 许愿的金币那一支 | `AutoWatcher.WishGold` | 金币愿望那张牌的图标，标题「许愿·金币愿望名」 | `card is WatcherWish_P` | 贪婪之手 |

三点值得记下来：

- **判据都不要求 `DeckVersion`。** 原版只有遗传算法、巨镰、黏稠强化那三个要求它，因为那三个
  升的是**自己**，战斗里临时生成的副本升了也带不出去。勤学精进升的是牌组里别的随机一张，
  这一张是不是局外牌组实例无所谓；许愿给的是金币，同理。
- **许愿那一行的图标和判据不是同一张牌。** 图标取金币愿望那张选项牌（这一行说的就是这一支），
  判据认的是牌组里的许愿——愿望牌只在选择里存在，进不了牌组。原版黏稠强化那一行也是这个形状：
  显示防御，判据认的是带黏稠强化的任意一张牌。
- **登记入口是可选的。** 求解器没有 `GrowthSourceMirrors` 时两个登记都返回空，收益照旧只走
  长期资源刻度，行为和登记这个口子之前完全一样，加载日志里写明「求解器没有这个入口」。
  已按元数据核对过反射用的那几个名字在有入口的求解器上都对得上、在没有入口的求解器上一个都
  找不到（两侧都验过）。

**还没有夹具。** 这两处的记账要在真实战斗里生效，前提是装着带 `GrowthSourceMirrors` 的求解器，
而那一版还没发。矩阵里也没有「给某个来源设一份额度再看路线变不变」的参数。所以现在只有编译期
和元数据两级验证，实机行为未验。

## 测试环境要求

**必须收窄 mod 集。** 你装的 LotmMod 会让求解器停在同一道门上
（`LotmModCode.BaseCode.SpireKeywordModel`），跟观者无关。要干净测量，`mods/` 里只留：

```
CombatSolver
Watcher
AutoWatcher
```

其余 gameplay mod 暂存到 `mods_staged_by_claude/`，测完可以移回去。

## 验收结果（2026-09-04，11/11 通过）

对 `游戏 v0.111.0 + 观者 0.9.25 + CombatSolver 0.29.0 + 适配 0.1.0` 实测：

| 用例 | 判别依据 | 结果 |
|---|---|---|
| `WATCHER-ERUPTION-WRATH` | 爆发是首个动作 | 通过 |
| `WATCHER-VIGILANCE-BLOCK` | 首回合格挡峰值 ≥ 8 | 通过 |
| `WATCHER-CALM-EXIT-ENERGY` | 固定 4 能量下首回合 3 个动作（没有退出平静的 2 点则任何顺序都只有 2 个） | 通过 |
| `WATCHER-MIRACLE-ENERGY` | 首回合 3 个动作（没有奇迹的 1 点则第二张爆发付不起） | 通过 |
| `WATCHER-WRATH-DOUBLE-DAMAGE` | 单敌人 21 血第一回合击杀（9 + 6×2；没有翻倍只有 15） | 通过 |
| `WATCHER-MANTRA-TO-DIVINITY` | 固定 4 能量下首回合 3 个动作（真言满 10 转神圣补 3 点才付得起第三张） | 通过 |
| `WATCHER-JUDGMENT-EXECUTE` | 21 血敌人第一回合被斩杀（没有击杀动词则这张牌是空操作） | 通过 |
| `WATCHER-SPIRIT-SHIELD-SCALING` | 四张打击在手时格挡峰值 ≥ 12（= 手牌数 × 3，且不计自己） | 通过 |
| `WATCHER-WREATH-VIGOR-DAMAGE` | 11 血敌人第一回合击杀（打击 6 + 激励 5；没施加 Power 只有 6） | 通过 |
| `WATCHER-SANDS-RETAIN-COST` | 手上只有时之沙时第二回合结束（4 费付不起、保留降 1 费后才付得起） | 通过 |
| `WATCHER-STANCE-REGRESSION-LOCK` | 100 血投影三回合结束，终局敌方总生命 0 | 通过 |
| `WATCHER-CRUSH-JOINTS-VAR-KEY` | 碎骨能打出且不让搜索失败（易感层数的变量键取的是 Power 类型名） | 通过 |
| `WATCHER-CONCLUDE-ENDS-TURN` | 两张结末一张打击、30 血敌人，第二回合才结束（结末打完回合就结束，第二张接不上） | 通过 |
| `WATCHER-BLASPHEMY-NO-SUICIDE` | 一副赢不了的牌里，渎神一次都不打 | 通过 |
| `WATCHER-SANDS-NO-DRAW-TURN-DISCOUNT` | 时之沙抽上来那回合仍是 4 费，3 点能量下先打三张打击+ | 通过 |
| `WATCHER-RUSHDOWN-INFINITE` | 猛虎下山加内心平静加暴怒+，第一回合就打完（红蓝无限） | 通过 |
| `WATCHER-TANTRUM-SHUFFLES-BACK` | 发泄打出后洗回抽牌堆，不再报未镜像 | 通过 |

大多数是算术判别：镜像算错，数值就对不上。后四条是实机报出来的问题的回归锁。
每条都带 `-ExpectedInitialUnmirroredCount 0`，所以**求解器在这些路线上不报告任何未镜像
效果**——验收标准的第二条成立。

其中真言那条一开始没过，但失败的是未镜像项而不是动作数：转换和补能量本来就对，只是
`WatcherStatePower` 重写了 `AfterCardPlayed`，而那是求解器会分发的钩子，未知类型每打一张牌
就记一条风险。补上那个钩子的镜像之后归零。这也是这套验收标准的价值所在——它把一个"结果
正确但会淹没红字"的问题顶了出来。

重跑：

```bash
pwsh -NoProfile -File tools/run-watcher-matrix.ps1
```

全量要跑好几分钟，一定放后台。脚本每跑完一条就往 `.matrix-progress.txt` 写一行带时间戳的结果，
用它跟进度——PowerShell 的标准输出要等进程退出才刷出来，后台看输出文件会一直是空的。
跑之前的注意事项和几种"看着卡死"的判别，见工作区的 `docs/headless-harness-playbook.md`。
### 实机报告过的偏差

**预计战损 2、实际 13。** 同一场里有两处不一致，只有一处是适配层的。

不是适配层的那处：第 4 回合计划用「暴怒+ 打完剩 6 血、锁镰补 6 点」杀掉一颗蛋，实际锁镰那 6 点
打到了别的敌人，蛋正好剩 6 血活下来，第 5 回合啃了一口。锁镰是每 3 张攻击牌对一个**随机**敌人
造成 6 点伤害（`Rng.CombatTargets.NextItem(HittableEnemies)`），求解器的镜像逐字照抄，效果没漏，
所以偏差在随机数流或敌人列表顺序上，属于原版求解器。愤怒（受伤 ×2）加易伤（×1.5）把 4 点的啃咬
放大成 12 点，这也是观者身上这类误差看着特别大的原因。

是适配层的那处：**上一张牌类型不跨回合**。粉碎关节+ 作为第 4 回合第一张牌打出，上一回合最后打的
是防御（技能），实机给了目标 2 层易伤，而镜像按"看不到前一张"处理、没给。原版读的是整场战斗的
打牌历史，不是本回合的。已改成回落到根历史快照。这一条之前被列为"已知未做"，这份问题包证明它
不是无害的——它会让模拟和实机的敌人状态从那一刻起分叉。

**旧日雕像战的计划外重算。** 日志里 `SEARCH_REUSE_MISS` 给出的四处差异全部归结为两个缺口，
都已修复：生成的洞察没继承升级（三处）、时之沙被保留后没降费（一处）。同一份日志里观者相关的
未镜像项也只有明察那一条，正是同一个升级问题。剩下的唯一一条 `SHRINK_POWER / AfterDeath`
是原版求解器的已知缺口且标了 `compensated=True`，与适配层无关。

**碎骨让整次搜索失败。** 易感层数的变量键是 `PowerVar<T>` 单参数构造按 `typeof(T).Name` 生成的，
也就是 `VulnerablePower`，不是牌面上显示的那个词。已修，并且把变量读取改成读不到时报出是哪张牌、
该牌实际有哪些键。

**求解器自动打出渎神把自己杀了。** 根因不在渎神，在结末：结末打出后本回合就结束，而当时那一条
只记了风险、没有真的结束回合。于是求解器给的路线是「火焰纹 → 渎神 → 结末 → 打击」，它以为最后
那张打击能在同一回合收掉最后一个敌人、渎神的回合结束死亡永远不会到来。实际结末打完回合就结束，
打击没机会打出，下一回合开始时玩家被渎神杀死。

这一条的教训是：**风险标记只是显示上的红字，不会把不可能的续接从搜索里去掉。**「这张牌之后还能
不能接牌」这类会改变可行动作集合的效果，必须真的建模。修法是走求解器自己的
`RequestPlayerTurnEnd`，和原版虚空形态一样。顺带把腾跃的额外回合也建了模。

死亡本身求解器计价没有问题：死亡是 -1e12 的分数、节点直接终止，所以只要回合结束死亡真的建模了，
求解器绝不会主动送死。`WATCHER-BLASPHEMY-NO-SUICIDE` 用一副赢不了的牌锁住这一点。

**以手拒之起不了甲 —— 是排序，不是镜像。已修。** 反弹格挡的镜像和钩子分发都是对的，逐条和反编译出来的
`WatcherMod.BlockReturnPower` 核对过（目标判定、施加者判定、`IsPoweredAttack`、`TotalDamage > 0`、
受益者三级回退、`Unpowered` 不吃敏捷、不自减），`WATCHER-BLOCK-RETURN-HOOK` 把这一半钉住了。

坏的是**求解器不会为了起甲把以手拒之排到攻击前面**。实测：手里以手拒之 + 两张打击，敌人这回合打
4 点，求解器给的顺序是「打击 打击 以手拒之」—— 第 1 回合 `max_block=0`，白挨 4 点；换成「以手拒之
打击 打击」本可以 4 甲全挡掉。第 2、3、4 回合都是 `max_block=4 actual_block=4`，也就是说层数一旦挂上
去，后面每回合都算得对，唯独挂上去的那一回合被浪费。

根因在原版求解器的动作分类。`ClassifyActionOptionFamilies` 判 `ImmediateDefense` 看四样东西：这次
动作的格挡增量、`ProjectedPlayerHp`、`PlayerHp`、`StrategicEffects.PreventionPotential`。以手拒之
打出的瞬间这四样一样都不动 —— 它自己不给甲，而反弹格挡这层 Power 挂在**敌人身上**，
`StateEvaluation` 的设置估值那圈只统计 `power.Owner == 玩家` 且是增益的 Power，敌人身上的直接跳过。
于是它被归成一张纯 `ImmediateOffense`，和打击同族但伤害更低，在族内代表里被打击压掉，只能等打击
打完才轮到它。

**修法在求解器那边。** `StrategicEffectModel.Requirements` / `Evaluate` 原来是按原版 Power 类型写死的
`switch`，没有第三方登记入口；设置估值那圈的"只看自己身上"也是硬编码。求解器侧新增
`StrategicEffectMirrors`：按 Power 类型登记 requirements 和估值委托，外加一个 `host` 说明这层
Power 挂在谁身上。`StrategicEffectHost.Enemy` 换一套准入判定——层数为正、不是临时 Power、宿主
不是玩家，不查增益减益，因为它在宿主眼里通常是减益。求解器本身不认识任何第三方类型，反弹格挡
的估值由适配层的 `src/WatcherStrategicEffects.cs` 提供。用法和估值该往哪个方向偏，见求解器仓库的
`docs/third-party-strategic-effects.md`。

夹具 `WATCHER-TALK-TO-THE-HAND-ORDERING`：以手拒之加两张打击、3 能量，判 `max_block ≥ 4`
（以手拒之 MagicNumber = 2，两张打击各 1 段，排最前面 = 4 甲，排中间 = 2，排最后 = 0）。
**做过反向对照**：把 `WatcherStrategicEffects.RegisterAll()` 注释掉重新构建，这条就不过；
加回来就过。

**估值不能按攻击牌张数算。** 反弹格挡是**按伤害段数**触发的：`CombatPredictionSimulator.Attack`
是 `for (i = 0; i < hitCount; i++) { Damage(...) }`，每段各产生一个 `DamageResult`，每个 result
各分发一次 `AfterDamageGiven`。发泄 1 张牌打 3 段就是 6 甲，不是 2 甲。观者一手多段牌，按张算会
系统性低估。

而求解器现成的 `StrategicEffectRequirements.AttackPlays` 数的正是**牌数**（`attackCount` 按
`liveCards` 里 `Type == Attack` 逐张累加），`CardChoiceSupport.CardValue` 也只读 `Damage` 基础值、
不乘段数。也就是说没有现成的按段计数可用，得新加。

分两件事看：
- **准入**（真正解决这个 bug 的那一半）：`ClassifyActionOptionFamilies` 判的是
  `after.PreventionPotential > before.PreventionPotential`，只要**非零**就够，多段不多段不影响。
  排序一旦被展开，格挡本身是精确模拟的，段数一点不差。
- **排名**（次要）：数值只影响这条线在真格挡兑现之前能在 Beam 里活多久。低估会剪掉好线，所以
  按既定规矩要往**高**了估，不能拿张数当代理。

`WATCHER-BLOCK-RETURN-HOOK`（单段，2 张打击 = 4 甲）和 `WATCHER-BLOCK-RETURN-MULTIHIT`
（发泄 3 段 = 6 甲）把按段触发这件事钉住了。

**直飞产卵虫战里满屏的红字。** 那一份日志里只有两条，各七千次上下：发泄打出后洗回抽牌堆、
凌波微步的进入愤怒抽牌。两条都已实现，见上面"红蓝无限"一节。同一份日志里没有任何重算、
状态不一致或搜索失败，也就是说那一局的问题全部就是这两个缺口。

### 姿态药水这一条要值多少

2026-09-06 鬼祟珊瑚群精英战，两份问题包。

求解器第 1 回合给的是「如水 | 停顿 | 粉碎关节+ | 打击 | 形态药剂」，`max_block=14
actual_block=3`，掉 11 血。手打是「爆发+ | 停顿 | 粉碎关节+ | 如水 | 形态药剂选平静」：
爆发+ 先进愤怒，于是停顿给 3+9=12 甲；药水再把姿态换成平静，既退出了愤怒的双倍受伤，又让
如水在回合结束因为平静补 5 甲。17 甲挡掉 14 点，**0 掉血**。问题包自己算出
`BetterWorldline 预计战损 11 → 0`。

2026-09-06 18:30 又复现了一次，路线一字不差、同样 11 血。那一份的 `settings.json` 里把
形态药剂设成了 `Force`：

```json
{ "slot": 0, "potionId": "STANCE_POTION", "directive": "Force" }
```

**强制用药在这里帮不上忙，只会保证这瓶药被白扔掉。** 效果没建模，用了收益就是零
（`potion_hp_saved=0`），求解器只是照指令把它插进路线里的某个位置。而且用了 `Force` 之后
`AuditSmartPotionUse` 直接返回（它只在 `PotionPolicy == Smart` 时才跑），所以那一局日志里
一条 `SMART_POTION_GRADIENT` 都没有——「省够 9 点血才值得用」那道闸没参与。这不是 bug，
是设定使然，但意味着强制用药的时候看不到那份值不值的审计。

**停顿的镜像是对的**，愤怒加成在里面（`src/Cards/WatcherCardMirrors.B.cs`）。
**求解器不肯进愤怒的判断，在它自己的世界观里也是对的**——进去了退不出来就是挨双倍伤害。
错的只有一件事：它不知道这瓶药能退出来。姿态药水在模拟里是空操作，所以它把药水当成一个没有
收益的动作插进路线，而找不到「进愤怒吃停顿的加成，再用药水退出来」这条线。

原来记的不修理由是错的：写的是"原版按引用相等比较两张选项牌，没法从状态推出来"。原版确实写
`val == calmChoice`，但两张选项牌是两个不同的类型（`WatcherStancePotionCalmChoice` 和
`WatcherStancePotionWrathChoice`），各只有一张，按类型判和按引用判在这里完全等价。

真正卡住的是**第三方登记不进去**，不是没有原语。原语是现成的：药水的选牌分支走
`PotionChoiceSupport`，用 `CardChoiceSpec` 加挂起选择，原版那四张三选一药水就是
`PlanChoiceEffect.GenerateToHand` 配 `PileType.None`，形状和这里要的一模一样。

挡住的是三个写死的开关：

```csharp
public static bool RequiresChoice(PotionModel potion)
    => GeneratesCardChoice(potion)          // AttackPotion / SkillPotion / PowerPotion / ColorlessPotion
        || potion is Ashwater or DropletOfPrecognition or GamblersBrew
           or LiquidMemories or TouchOfInsanity;
```

第三方药水在这里永远是 `false`，于是求解器根本不为它开选择分支；`GetSpec` 和 `Apply` 同样是
封闭 switch，默认分支直接抛。也就是说 `PotionOnUseMirrors` 这个钩子触发的时候，"要不要开分支"
早就已经被否决了，适配层再怎么写也够不着。

要修的是给这三个开关加第三方登记表，和 `pr/third-party-strategic-effects` 是同一个形状。

**已修。** 求解器那边加了 `PotionChoiceMirrors`（分支 `pr/third-party-potion-choice`）：三个开关
各加一句查表前置，登记表为空时一句都不触发，原版行为一个字节不变。适配层在
`src/WatcherItemMirrors.cs` 里登记形态药剂的 spec 和 apply，选项用两张真正的选择令牌，
效果由 apply 自己施加；`StancePotionOnUse` 相应清空，否则姿态会被施加两次。
新的 `PlanChoiceEffect.ModDefined` 只是表明"结算归登记方"，部署侧本来就是按卡牌令牌在原生页面
上定位，与效果无关。

- 实机验证：2026-09-06，作者在游戏里确认求解器现在会走「进愤怒吃停顿的加成，再用药水退出来」。
- 回归锁：夹具 `WATCHER-STANCE-POTION-WRATH-KILL`。敌人 18 血、手里只有爆发+，先用药水进愤怒
  才够 9×2=18 一回合击杀，同时钉住选的是愤怒那张令牌。
- 反向对照：把 `PotionChoiceMirrors.Register<StancePotion>` 注掉重新构建，这条挂在
  "结束回合为 3、预期为 1"；恢复登记后重新通过。

顺带两点：

- 这 7 处未建模选择里，姿态药水是**最便宜的一条**——它的选项根本不涉及牌，就是两个固定结果。
- 观者一共只有 3 个药水（`Ambrosia`、`BottledMiracle`、`StancePotion`，按 Watcher.dll 的元数据
  数过），三个都已经注册了镜像。**没有漏检的药水**：没有玩家选择的两个走
  `PotionOnUseMirrors` 就够了、也确实是对的，卡住的只有带选择的这一个。规则是通用的——
  任何 mod 的药水，不带选择的能用镜像补上，带选择的都会撞上这道封闭开关。

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

另外 harness 会复用上一次的游戏进程（`reused_process=True`），而它判断能不能复用只比对
**CombatSolver 自己**的 DLL 和清单哈希，认不出适配层改没改。所以改了 mod 之后必须先
`Stop-Process -Name SlayTheSpire2`，否则测的还是旧的加载状态。矩阵脚本开头会自动杀一次，
每条用例也都用独立进程。
