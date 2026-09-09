using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using CombatSolver;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Simulation;
using WatcherMod;

namespace AutoWatcher;

/// <summary>
/// 不依赖卡牌上下文的效果动词。卡牌、遗物、药水共用。
/// </summary>
/// <remarks>
/// <see cref="WatcherVerbs" /> 里那些取自牌面变量的动词（按伤害变量攻击、按格挡变量加格挡）
/// 只对卡牌成立，留在那边；这里放的是任何来源都能用的那部分。
/// </remarks>
internal static class WatcherSimVerbs
{
    public static int PowerAmount<TPower>(WatcherSim sim)
        where TPower : PowerModel
        => sim.Combat.GetAmount<TPower>(sim.Self);

    public static void Power(WatcherSim sim, Type powerType, int amount)
        => sim.Effects.ApplyPower(powerType, sim.Self, amount, sim.Self);

    /// <summary>施加一个 <c>TemporaryDexterityPower</c> 子类。不能改用 <see cref="Power" />。</summary>
    /// <remarks>
    /// <para>
    /// 原版基类在 <c>BeforeApplied</c> 里会自动配一份等量的 <c>DexterityPower</c>，回合结束时
    /// <c>RestoreTemporaryDexterity</c> 把所有 <c>TemporaryDexterityPower</c> 子类的层数求和，
    /// 一次性从 <c>DexterityPower</c> 里减掉。求解器完整复刻了这套。
    /// </para>
    /// <para>
    /// 关键在于施加侧和回收侧不对称：<c>ApplyPower</c> 不认这个基类，只加记账层；回收侧认，
    /// 照样按记账层扣真敏捷。用 <see cref="Power" /> 施加的话，加的时候少一份增益，回合结束
    /// 却照扣，于是下一回合开局凭空多出一个负敏捷——不是少算，是算反了。
    /// </para>
    /// </remarks>
    public static void TemporaryDexterity(WatcherSim sim, Type powerType, int amount)
        => sim.Effects.ApplyTemporaryDexterity(powerType, sim.Self, amount, sim.Self);

    /// <summary>把 Power 设成确切数量而不是叠加。对应 WatcherPowerCmdCompat.SetAmount。</summary>
    public static void SetPower(WatcherSim sim, Type powerType, int amount)
    {
        PowerModel? existing = sim.Combat.EffectivePowers()
            .FirstOrDefault(power => power.GetType() == powerType && ReferenceEquals(power.Owner, sim.Self));
        if (existing is null)
        {
            Power(sim, powerType, amount);
            return;
        }
        sim.Combat.SetPowerAmount(existing, amount);
    }

    public static void Draw(WatcherSim sim, int count)
    {
        if (count > 0)
            sim.Simulator.Draw(sim.Owner, count);
    }

    public static void GainEnergy(WatcherSim sim, decimal amount)
    {
        if (amount > 0)
            sim.Simulator.GainEnergy(sim.Owner, amount);
    }

    public static void BlockFor(WatcherSim sim, decimal amount, ValueProp props)
    {
        if (amount > 0)
            sim.Simulator.GainBlock(sim.Self, amount, props, null, null);
    }

    public static void AddCards<TCard>(
        WatcherSim sim,
        PileType pile,
        int count,
        CardPilePosition position = CardPilePosition.Bottom)
        where TCard : CardModel
    {
        if (count <= 0)
            return;
        sim.Simulator.CreateAndAddGeneratedCardsToCombat<TCard>(sim.Owner, pile, count, sim.Owner, position);
    }

    /// <summary>对应 WatcherCombatHelper.EnsureState：没有就以数量 1 施加一个。</summary>
    /// <remarks>
    /// 返回的一定是可变克隆。WatcherStatePower 的计数器 setter 会走 AssertMutable，对根实例
    /// 赋值会抛异常。
    /// </remarks>
    public static WatcherStatePower EnsureState(WatcherSim sim)
    {
        if (sim.Combat.GetAmount<WatcherStatePower>(sim.Self) <= 0)
            Power(sim, typeof(WatcherStatePower), 1);
        return sim.Combat.GetMutablePower<WatcherStatePower>(sim.Self)
            ?? throw new InvalidOperationException("施加后仍然取不到观者状态 Power。");
    }

    public static WatcherStatePower? PeekState(WatcherSim sim)
        => sim.Combat.GetPower<WatcherStatePower>(sim.Self);

    /// <summary>对应 WatcherCombatHelper.GainMantra，含真言满 10 转神圣。</summary>
    /// <remarks>
    /// 转换规则来自 Mantra.AfterPowerAmountChangedCompat：阈值是变更后 Amount 大于等于 10，
    /// 减的是正好 10 而不是清零，而且 _isResolving 闩锁让嵌套的那次变更不再触发钩子。所以
    /// 0 到 20 只转换一次、留下 10。这里不能写成循环。
    ///
    /// 原版这条路径挂在 hook 广播方法的 Harmony postfix 上，求解器用自己的 mirror 替掉了整个
    /// hook 分发、从不调那个被打补丁的方法，所以它既不生效也不记风险——是必须手写补上的无声
    /// 缺口之一。遗物达玛茹的回合初真言也走这条，所以它同样受益。
    /// </remarks>
    public static void GainMantra(WatcherSim sim, int amount)
    {
        if (amount <= 0)
            return;

        WatcherStatePower state = EnsureState(sim);
        state.TotalMantraGainedThisCombat += amount;
        state.MantraGainedThisTurn += amount;

        Power(sim, typeof(Mantra), amount);

        if (sim.Combat.GetMutablePower<Mantra>(sim.Self) is not { Amount: >= 10 } mantra)
            return;
        sim.Combat.SetPowerAmount(mantra, mantra.Amount - 10);
        WatcherStanceVerbs.EnterDivinity(sim);
    }

    /// <summary>施加回合结束死亡标记，并按原版记下施加时的回合数。</summary>
    /// <remarks>
    /// 那个 Power 靠一个私有字段记住自己是哪一回合被挂上的，下一个回合开始时才生效。字段在
    /// 原版是 AfterApplied 里写的，而求解器不分发 AfterApplied，所以要在施加处补写。回合数
    /// 两侧都取模拟的回合号，口径自洽。
    /// </remarks>
    public static void ApplyEndTurnDeath(WatcherSim sim)
    {
        Power(sim, typeof(EndTurnDeathPower), 1);
        if (sim.Combat.GetMutablePower<EndTurnDeathPower>(sim.Self) is { } marker)
            marker._appliedOnTurn = sim.Combat.GetPlayerTurnNumber(sim.Owner);
    }

    /// <summary>对应 WatcherCombatHelper.ConsumeKnowFate。返回实际消耗掉的天命层数。</summary>
    /// <remarks>
    /// 两个细节按原版实现：尝试标记 KnowFateConsumptionAttemptedThisCard 在检查层数之前就置位，
    /// 也就是即使一层都没消耗到也算尝试过；消耗满的时候是移除整个 Power 而不是减到 0。
    /// </remarks>
    public static int ConsumeKnowFate(WatcherSim sim, int amount)
    {
        if (amount <= 0)
            return 0;

        WatcherStatePower state = EnsureState(sim);
        state.KnowFateConsumptionAttemptedThisCard = true;

        if (sim.Combat.GetMutablePower<KnowFatePower>(sim.Self) is not { Amount: > 0 } power)
            return 0;

        int consumed = Math.Min(power.Amount, amount);
        state.KnowFateConsumedThisTurn = true;
        sim.Combat.SetPowerAmount(power, power.Amount - consumed);
        state.KnowFateLastObserved = sim.Combat.GetAmount<KnowFatePower>(sim.Self);
        return consumed;
    }

    /// <summary>对应 WatcherCombatHelper.GetEffectiveScryAmount。</summary>
    /// <remarks>
    /// 金瞳和守视都是按遗物 ID 或 Power 存在性轮询的，不是钩子，所以必须在这里读，光靠钩子
    /// 镜像会漏掉。加成先于扣减，且基础值为零时整条短路。
    /// </remarks>
    public static int EffectiveScryAmount(WatcherSim sim, int amount)
    {
        if (amount <= 0)
            return 0;
        if (sim.Owner.Relics.Any(relic => relic.Id.Entry == "GOLDEN_EYE"))
            amount += 2;
        if (PowerAmount<GuardNextScryPower>(sim) > 0)
            amount = Math.Max(0, amount - 2);
        return amount;
    }

    /// <summary>预见：看牌堆顶的几张，挑任意张丢掉。</summary>
    /// <remarks>
    /// 挑哪几张交给求解器自己搜。求解器把它登记成一次 0..N 的弃牌选择，候选就是抽牌堆顶那
    /// 几张，于是"丢掉"和"留着"都变成正常的世界线分支，由后面的模拟自己分出高下——预见的
    /// 价值本来就在这里：把垃圾牌推进弃牌堆，让下一圈抽到的都是好牌。
    ///
    /// 候选必须按抽牌堆的实际顺序给，下标 0 是牌堆顶。部署时求解器要拿这个顺序去原生选牌页面
    /// 上定位卡牌，顺序错了就会选错张。
    ///
    /// 分支不是白开的。卡牌预见一回合最多几次，展开完整子集划得来；香料每次洗牌都触发，
    /// 而能循环整个牌库的牌组一回合能洗十几次，那样每次洗牌都把搜索宽度乘一遍，会把出牌
    /// 决策整个压过去——实测一局无限里预见分支 `75224` 条、节点展开只有 `15549` 条，单次搜索
    /// 70 秒。所以按来源分：卡牌照常展开，遗物收成一条。
    ///
    /// 连带效果照原版 OnScry 实现：涅槃按层数给不受力量影响的格挡，弃牌堆里的经纬回手。
    /// 这两样都在挑牌之前结算。
    /// </remarks>
    /// <param name="maxBranches">
    /// 给 1 表示按固定策略作答、不在搜索里展开分支。每次洗牌都触发的来源（香料）要这样用。
    /// </param>
    public static void Scry(WatcherSim sim, int amount, string source, int? maxBranches = null)
    {
        int effective = EffectiveScryAmount(sim, amount);
        if (effective <= 0)
            return;

        if (PowerAmount<NirvanaPower>(sim) is > 0 and var nirvana)
            BlockFor(sim, nirvana, ValueProp.Unpowered);

        PredictedCard[] weaves = sim.OwnerState.DiscardPile.Cards
            .Where(card => card.Preview.Id.Entry == "WATCHER_WEAVE")
            .ToArray();
        if (weaves.Length > 0)
            sim.Simulator.AddToPile(weaves, PileType.Hand);

        PredictedCard[] looked = sim.OwnerState.DrawPile.Cards.Take(effective).ToArray();
        if (looked.Length == 0)
            return;

        if (sim.Combat is not ICombatPredictionChoiceSink choices)
        {
            sim.PlayerChoice($"{source} 预见 {looked.Length} 张后丢弃哪几张");
            return;
        }

        _ = choices.ResolvePileDiscardChoice(
            sim.Simulator,
            source,
            sim.Owner,
            PileType.Draw,
            looked,
            maxBranches);
    }
}
