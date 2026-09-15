## 对标的版本

| | 版本 | 从哪拿 |
|---|---|---|
| 自动战斗求解器 | `0.38.6` | [工坊](https://steamcommunity.com/sharedfiles/filedetails/?id=3790899961) / [GitHub](https://github.com/Torch1230/CombatSolver/releases/tag/v0.38.6) |
| 观者（Boninall） | `0.9.28` | 创意工坊 |
| 游戏 | `0.111.0` | — |

最低求解器版本**不变**，仍是 `0.38.2`。

## 这一版改了什么

### 修好：勤学精进与许愿的成长额度，从求解器 `0.38.3` 起就悄悄没了

求解器有一个第三方局外成长来源的登记入口（`GrowthSourceMirrors.Register`）。登记上去，
勤学精进的永久升级和许愿的金币才能各占一份独立的成长额度，玩家才能分别给它们设目标，
求解器也才会为凑这笔收益调整路线。

本 Mod 探测这个入口时**写死了参数个数**：

```csharp
return register?.GetParameters().Length == 4 ? register : null;
```

求解器 `0.38.3`（*feat: stop search at proven growth targets*）在这个方法末尾加了第 5 个
**可选**参数 `opportunityTarget`。签名从 4 参变 5 参，探测直接返回 `null`：

```
0.38.2 及以前   Register(id, card, hasTarget, title)                    → 绑上
0.38.3 起       Register(id, card, hasTarget, title, opportunityTarget) → 绑不上
```

**失败是静默的。** 没有报错，没有异常，路线上也不会有红字。两张牌的局外收益退回长期资源刻度
——数值不是零，只是不再有自己的额度，求解器不会为它们改路线。

现在只核对前 4 个参数的类型，后面多出来的**可选**参数一律容忍——上游在末尾加可选参数是正常的
加功能方式，不该被读成「入口没了」；多出来的参数如果不是可选的，那才算真的对不上。调用那一侧
按实际参数个数建实参数组，多的补 `null` 走默认（固定传 4 个会抛 `TargetParameterCountException`）。

扫过一遍，写死参数个数的探测只有这一处。

### 加载日志现在能区分「上游没有」和「签名对不上」

这次真正让人栽跟头的不是那行代码，是那句话。旧版的能力探测只有「绑上／没绑上」两态，
没绑上一律写成

> 局外成长来源登记：求解器没有这个入口

于是两件完全不同的事长得一模一样：**上游还没做**（跳过是预期行为），和**上游改了签名、
本 Mod 过期了**（是 bug）。查这个问题的时候，这句话把人往错的方向带了一轮。

现在分四态：已绑定 / 求解器没有这个入口 / **入口在但签名对不上** / 探测出错。后两种是需要
有人去改代码的情况，除了那行说明之外还会**单独告警一次**，不让它躺在一行看起来像常态的
说明里被读过去。

顺带说明：日志里那句「跨战斗收益目标：求解器没有这个入口」是诚实的——`LongTermGoals`
在求解器的全部提交历史里从未存在过，那是一处对上游尚未实现的功能做的预留探测。它现在会
明确显示成「上游没有」，而不是和签名不匹配共用一句话。

### 构建跟上 RitsuLib `0.6.0` 的新目录布局

只影响从源码构建，不影响装好的 Mod。RitsuLib `0.6.0` 把 `lib/<游戏API版本>/` 一个大 DLL
拆成了 `compat/<版本>/` 加 `shared/`，并自带 `RitsuLib.References.props`。有那个文件就走它，
没有就按旧布局引用，两种布局都能编。

## 为什么矩阵没抓到

`WATCHER-LESSON-LEARNED-FATAL-KILL` 这条用例断言的是**长期资源刻度**
（`-ExpectedInitialLongTermResourceValueAtLeast 30`），而那条正是入口绑不上时的回退路径。
所以入口断了之后这条用例照样通过——它锁的是回退值，不是成长额度本身。

这是一处真实的覆盖缺口，还没补。补它需要一条能断言「成长额度被兑现」的判据，
而不是断言回退刻度。

## 验收

- Release 编译 **0 警告 0 错误**，对求解器 `0.38.6` + RitsuLib `0.6.0`。零警告同时说明观者
  `0.9.28` 的哈希与钉死的那一版一致。
- 新的探测判据对着**已部署的** `CombatSolver.dll` 反编译出的实际签名逐项核过：

  ```csharp
  public static GrowthSourceHandle Register(
      string id, Func<CardModel> card, Func<CardModel, bool> hasTarget,
      Func<CardModel, string>? title = null,
      Func<GrowthOpportunityContext, GrowthOpportunityTarget>? opportunityTarget = null)
  ```

  前 4 项类型一致，第 5 项是可选参数——正是新判据放行的形状。核的是产物，不是源码。

- **这一版没有跑任何无头用例，包括那条本该覆盖它的。** RitsuLib `0.6.0` 换布局之后，无头
  harness 自己也找不到 RitsuLib 了（它按 `lib/<游戏API版本>/` 的老路径取，而且只搬一个 DLL，
  新版拆成了多个），`WATCHER-LESSON-LEARNED-FATAL-KILL` 在 harness 阶段就起不来。这是求解器
  仓库里那套工具的问题，已知，还没修。

  所以这一版的证据只有「编译通过」加「签名核对」两项，**没有运行时回归证据**。改动集中在能力
  探测的绑定条件与日志文案，不触碰任何镜像逻辑，但那是理由，不是证据。发现不对请报 Bug。

- 运行时确认方式：看加载日志那一行，应为「局外成长来源登记：已绑定」。

## 已知不准的地方

**1 处**，和上一版相同：

- 画符（`WATCHER_DRAW_TALISMAN`）的批量临时附魔。**不会静默算错**，求解器会在路线上打红字。

完整清单在 [docs/VERIFICATION.md](../docs/VERIFICATION.md)。

## 报 Bug

求解器自带问题包导出。导出后开 issue 附上它，说明你装的是工坊版还是 GitHub 版。欢迎捉虫！
