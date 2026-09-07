using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using CombatSolver;
using CombatSolver.Engine.Common.Mirrors;
using CombatSolver.Engine.InCombat.Mirrors.Cards.OnPlay;
using CombatSolver.Engine.InCombat.Simulation;
using WatcherMod;
using V = AutoWatcher.WatcherVerbs;
using S = AutoWatcher.WatcherStanceVerbs;

namespace AutoWatcher;

/// <summary>
/// 观者卡牌的 OnPlay 镜像。一张牌一个方法，一行一效果，按反编译源码的调用顺序排列。
/// </summary>
/// <remarks>
/// 每个方法都可以逐行对照 <c>scratchpad/watcher-cards-*.md</c> 里那份逐牌转写表来审。
/// 没能力完整镜像的地方一律显式调 <see cref="WatcherVerbs.Unmirrored" /> 或
/// <see cref="WatcherVerbs.PlayerChoice" />，让求解器把它显示成红色，而不是静默算错。
/// </remarks>
internal static partial class WatcherCardMirrors
{
    /// <summary>读牌上的动态变量。键不存在时报出这张牌和它实际有哪些键。</summary>
    /// <remarks>
    /// 直接索引字典的话，键写错只会在结算到这张牌时抛一个不带上下文的 KeyNotFound，堆栈要翻到
    /// 底才知道是哪张牌哪个键。实机就出过一次：易感的键是 PowerVar 单参数构造给的类型名
    /// VulnerablePower，我按牌面显示名写成了 Vulnerable，结果整次搜索失败。
    /// </remarks>
    private static DynamicVar RequireVar(CardModel card, string key)
    {
        if (card.DynamicVars.TryGetValue(key, out DynamicVar? value))
            return value;
        throw new KeyNotFoundException(
            $"观者镜像在 {card.Id.Entry} 上读不到动态变量 {key}。该牌实际有：" +
            string.Join("、", card.DynamicVars.Select(pair => pair.Key)));
    }

    private static decimal Var(CardModel card, string key) => RequireVar(card, key).BaseValue;

    private static int VarInt(CardModel card, string key) => RequireVar(card, key).IntValue;

    public static int RegisterA(MethodMirrorRegistry<CardModel, CardOnPlayMirrorContext> r)
    {
        r.Register<WatcherAlpha>(Alpha);
        r.Register<WatcherBattleHymn>(BattleHymn);
        r.Register<WatcherBeta>(Beta);
        r.Register<WatcherBlasphemy>(Blasphemy);
        r.Register<WatcherBowlingBash>(BowlingBash);
        r.Register<WatcherBrilliance>(Brilliance);
        r.Register<WatcherCarveReality>(CarveReality);
        r.Register<WatcherCataclysm>(Cataclysm);
        r.Register<WatcherColdObservation>(MultiplayerOnly);
        r.Register<WatcherCollect>(Collect);
        r.Register<WatcherConclude>(Conclude);
        r.Register<WatcherConjureBlade>(ConjureBlade);
        r.Register<WatcherConsecrate>(Consecrate);
        r.Register<WatcherCrescendo>(Crescendo);
        r.Register<WatcherCrushJoints>(CrushJoints);
        r.Register<WatcherCutThroughFate>(CutThroughFate);
        r.Register<WatcherDeceiveReality>(DeceiveReality);
        r.Register<WatcherDeusExMachina>(NoOpOnPlay);
        r.Register<WatcherDevaForm>(DevaForm);
        r.Register<WatcherDevotion>(Devotion);
        r.Register<WatcherDrawTalisman>(DrawTalisman);
        r.Register<WatcherDrawTalismanDirectedHand>(NoOpOnPlay);
        r.Register<WatcherDrawTalismanRandomDeck>(NoOpOnPlay);
        r.Register<WatcherEmptyBody>(EmptyBody);
        r.Register<WatcherEmptyFist>(EmptyFist);
        r.Register<WatcherEmptyMind>(EmptyMind);
        r.Register<WatcherEstablishment>(Establishment);
        r.Register<WatcherEvaluate>(Evaluate);
        r.Register<WatcherExpunger>(Expunger);
        r.Register<WatcherFasting2>(Fasting);
        r.Register<WatcherFearNoEvil>(FearNoEvil);
        r.Register<WatcherFlurryOfBlows>(FlurryOfBlows);
        return 32;
    }

    /// <summary>OnPlay 本身没有任何游戏效果的牌：不可打出的标记牌、选项展示牌。</summary>
    private static void NoOpOnPlay(CardModel card, CardOnPlayMirrorContext context)
    {
    }

    /// <summary>
    /// 只在多人局出现的牌。求解器只支持单人战斗，所以这些牌不该被打出。
    /// </summary>
    /// <remarks>
    /// 注册成记风险而不是空操作：万一它真的被打出来了，我们希望立刻在路线里看到，而不是
    /// 悄悄按无效果处理。
    /// </remarks>
    private static void MultiplayerOnly(CardModel card, CardOnPlayMirrorContext context)
        => V.Unmirrored(context, $"{card.Id.Entry} 是多人局专属牌，单人局不该出现");

    private static void Alpha(WatcherAlpha card, CardOnPlayMirrorContext context)
        => V.AddCards<WatcherBeta>(context, PileType.Draw, 1, CardPilePosition.Random);

    private static void BattleHymn(WatcherBattleHymn card, CardOnPlayMirrorContext context)
        => V.Power(context, typeof(BattleHymnPower), VarInt(card, "MagicNumber"));

    private static void Beta(WatcherBeta card, CardOnPlayMirrorContext context)
        => V.AddCards<WatcherOmega>(context, PileType.Draw, 1, CardPilePosition.Random);

    private static void Blasphemy(WatcherBlasphemy card, CardOnPlayMirrorContext context)
    {
        // 顺序要紧：先进神圣，再挂回合结束死亡标记。
        S.EnterDivinity(context);
        WatcherSimVerbs.ApplyEndTurnDeath(WatcherSim.From(context));
    }

    /// <summary>打击次数等于可命中敌人数，全部打在同一个目标上。</summary>
    /// <remarks>次数在任何伤害落地之前就取好了，所以中途死掉的敌人不会减少次数。</remarks>
    private static void BowlingBash(WatcherBowlingBash card, CardOnPlayMirrorContext context)
    {
        int hits = V.HittableEnemyCount(context);
        if (hits > 0)
            V.Attack(context, hits);
    }

    /// <summary>伤害加上本场战斗累计获得的真言。</summary>
    /// <remarks>
    /// 累计真言在 <c>WatcherStatePower</c> 的一个普通私有 <c>int</c> 里，读得到，所以伤害一直
    /// 算得对。以前记风险不是因为数值不对，而是因为那个计数进不了状态指纹：只在计数上不同的
    /// 两条分支指纹相同，会被当成同一个状态去掉一条——「先攒真言再打光辉」可能连搜索都到不了。
    ///
    /// 有 <c>PowerHiddenStateMirrors</c> 那个登记入口时（见 <see cref="WatcherHiddenState" />），
    /// 计数进指纹，两条分支分得开，这条风险不存在了；没有那个入口的求解器上仍然记风险，
    /// 而且只在计数不为零时记——为零时读到的值和指纹一致，没有风险可言。
    /// </remarks>
    private static void Brilliance(WatcherBrilliance card, CardOnPlayMirrorContext context)
    {
        int mantraGained = V.PeekState(context)?.TotalMantraGainedThisCombat ?? 0;
        if (mantraGained > 0 && !WatcherHiddenState.MantraGainedInFingerprint)
        {
            V.Unmirrored(
                context,
                $"{card.Id.Entry} 的伤害取自累计真言 {mantraGained}，而这个求解器版本没有把它算进状态指纹");
        }
        V.AttackFor(context, card.DynamicVars.Damage.BaseValue + mantraGained);
    }

    private static void CarveReality(WatcherCarveReality card, CardOnPlayMirrorContext context)
    {
        V.Attack(context);
        V.AddCards<WatcherSmite>(context, PileType.Hand, 1);
    }

    private static void Cataclysm(WatcherCataclysm card, CardOnPlayMirrorContext context)
    {
        V.AttackAllEnemies(context, VarInt(card, "Repeat"));
        S.EnterWrath(context);
    }

    /// <summary>X 费：收集层数等于实付能量，升级后再加一。</summary>
    private static void Collect(WatcherCollect card, CardOnPlayMirrorContext context)
    {
        int amount = context.Card.ResolveEnergyXValue(context.State) + (card.IsUpgraded ? 1 : 0);
        if (amount > 0)
            V.Power(context, typeof(CollectPower), amount);
    }

    /// <summary>群体攻击，然后强制结束回合。连打的中间几次不结束，只有最后一次结束。</summary>
    private static void Conclude(WatcherConclude card, CardOnPlayMirrorContext context)
    {
        V.AttackAllEnemies(context);
        if (context.CardPlay.IsLastInSeries)
            V.ForceEndTurn(context);
    }

    /// <summary>X 费：生成一张灭除之刃，其打击次数等于实付能量。</summary>
    /// <remarks>
    /// 生成接口按牌类型创建规范实例，不接受实例级的负载，但它把加进去的那张牌返回出来，拿到
    /// 之后照原版那样写 <c>HitCount</c> 即可——原版就是先 <c>CreateCard</c> 再赋值再入堆。
    /// <c>HitCount</c> 的落点是 <c>DynamicVars.Repeat.BaseValue</c>，而 Repeat 是个普通数值变量，
    /// <c>SemanticStateFieldPolicy</c> 把它算成 Behavior，所以次数会进状态指纹和续接戳，
    /// 两条只在次数上不同的分支不会被去重掉，实机续算也不会因为这一格对不上而作废。
    /// 升级版的写法和洞察那条一致：改的是生成结果，不是规范实例。
    /// </remarks>
    private static void ConjureBlade(WatcherConjureBlade card, CardOnPlayMirrorContext context)
    {
        int amount = context.Card.ResolveEnergyXValue(context.State) + (card.IsUpgraded ? 1 : 0);
        if (amount <= 0)
            return;
        foreach (SimCardPileAddResult added in context.Simulator
                     .CreateAndAddGeneratedCardsToCombat<WatcherExpunger>(
                         card.Owner, PileType.Draw, 1, card.Owner, CardPilePosition.Random))
        {
            ((WatcherExpunger)added.CardAdded.MutablePreview).HitCount = amount;
        }
    }

    private static void Consecrate(WatcherConsecrate card, CardOnPlayMirrorContext context)
        => V.AttackAllEnemies(context);

    private static void Crescendo(WatcherCrescendo card, CardOnPlayMirrorContext context)
        => S.EnterWrath(context);

    private static void CrushJoints(WatcherCrushJoints card, CardOnPlayMirrorContext context)
    {
        V.Attack(context);
        // 条件在伤害结算之后才判断，读的是同一主人打出的上一张牌。
        // 易感的变量键是 PowerVar<VulnerablePower> 单参数构造给的，也就是 Power 的类型名，
        // 不是牌面上显示的那个词。写成显示名会在结算时抛 KeyNotFound。
        if (V.PreviousPlayedCardType(context) == CardType.Skill)
            V.PowerOnTarget(context, typeof(VulnerablePower), VarInt(card, "VulnerablePower"));
    }

    private static void CutThroughFate(WatcherCutThroughFate card, CardOnPlayMirrorContext context)
    {
        V.Attack(context);
        V.Scry(context, VarInt(card, "MagicNumber"));
        V.Draw(context, 1);
    }

    private static void DeceiveReality(WatcherDeceiveReality card, CardOnPlayMirrorContext context)
    {
        V.Block(context);
        V.AddCards<WatcherSafety>(context, PileType.Hand, 1);
    }

    /// <summary>第一张按数量 1 施加；第二张之后原版是往内部实例表里追加一个 1。</summary>
    /// <remarks>
    /// 两条分支对 <c>Amount</c> 的效果一样都是加一（<c>AfterApplied</c> 首次补一个 1，
    /// <c>AddInstance</c> 追加一个 1，两者随后都 <c>SetAmount(Instances.Sum())</c>），差别只在
    /// 走不走施加流程，所以照原版的分支写。
    ///
    /// 整张实例表不必复现：每回合先给总和点能量再把每个实例加一，等价于「总和每回合涨实例
    /// 个数」。总和就是 <c>Amount</c>，缺的只有个数，存放和每回合的能量都在
    /// <see cref="WatcherDevaInstances" />。
    /// </remarks>
    private static void DevaForm(WatcherDevaForm card, CardOnPlayMirrorContext context)
    {
        Creature self = V.Self(context);
        SimulatedCombatState combat = V.Combat(context);
        if (V.PowerAmount<DevaPower>(context) <= 0)
        {
            V.Power(context, typeof(DevaPower), 1);
        }
        else if (combat.GetMutablePower<DevaPower>(self) is { } existing)
        {
            combat.SetPowerAmount(existing, existing.Amount + 1);
        }
        else
        {
            V.Unmirrored(context, $"{card.Id.Entry} 取不到已有的天人形态实例");
            return;
        }

        if (combat.GetPower<DevaPower>(self) is { } deva)
            WatcherDevaInstances.AddInstance(context.Simulator, deva);
    }

    private static void Devotion(WatcherDevotion card, CardOnPlayMirrorContext context)
        => V.Power(context, typeof(DevotionPower), VarInt(card, "MagicNumber"));

    private static void DrawTalisman(WatcherDrawTalisman card, CardOnPlayMirrorContext context)
    {
        V.PlayerChoice(context, $"{card.Id.Entry} 在随机全牌堆与定向手牌两个选项之间选一个");
        V.Unmirrored(context, $"{card.Id.Entry} 会给牌批量附加临时附魔");
    }

    private static void EmptyBody(WatcherEmptyBody card, CardOnPlayMirrorContext context)
    {
        V.Block(context);
        S.ExitStance(context);
    }

    private static void EmptyFist(WatcherEmptyFist card, CardOnPlayMirrorContext context)
    {
        V.Attack(context);
        S.ExitStance(context);
    }

    private static void EmptyMind(WatcherEmptyMind card, CardOnPlayMirrorContext context)
    {
        // 抽牌在退出姿态之前，对任何按当前姿态响应抽牌的效果有影响。
        V.Draw(context, VarInt(card, "MagicNumber"));
        S.ExitStance(context);
    }

    private static void Establishment(WatcherEstablishment card, CardOnPlayMirrorContext context)
        => V.Power(context, typeof(EstablishmentPower), VarInt(card, "MagicNumber"));

    /// <summary>升级版会把生成的那张洞察也升级掉。</summary>
    /// <remarks>
    /// 生成接口会返回加进去的那张牌，所以拿到之后直接升级即可。实机报出来的第一处差异就是
    /// 这里：预测 WATCHER_INSIGHT+0、实际 +1，导致续算作废。
    /// </remarks>
    private static void Evaluate(WatcherEvaluate card, CardOnPlayMirrorContext context)
    {
        V.Block(context);
        foreach (SimCardPileAddResult added in context.Simulator
                     .CreateAndAddGeneratedCardsToCombat<WatcherInsight>(
                         card.Owner, PileType.Draw, 1, card.Owner, CardPilePosition.Random))
        {
            if (card.IsUpgraded)
                context.Simulator.Upgrade(added.CardAdded);
        }
    }

    private static void Expunger(WatcherExpunger card, CardOnPlayMirrorContext context)
        => V.Attack(context, VarInt(card, "Repeat"));

    private static void Fasting(WatcherFasting2 card, CardOnPlayMirrorContext context)
    {
        int amount = VarInt(card, "MagicNumber");
        V.Power(context, typeof(StrengthPower), amount);
        V.Power(context, typeof(DexterityPower), amount);
        V.Power(context, typeof(EnergyDownPower), 1);
    }

    /// <summary>目标本来打算攻击的话，攻击后进入平静。</summary>
    /// <remarks>
    /// 意图在伤害结算之前就取好了，所以把目标打死、或者目标因为受击而改变意图，都仍然给平静。
    ///
    /// 必须走 <c>SimulatedCombatState.IsEnemyIntendingToAttack</c>，不能读
    /// <c>Target.Monster.IntendsToAttack</c>。后者是**实时**意图：镜像跑在后台线程上，读到的是
    /// 玩家此刻在屏幕上看到的那个意图，而不是这条路线推进到该回合时模拟出的意图。求解器为此
    /// 专门维护了一份预测意图集合，它按分支 fork、并且进状态指纹；原版的「直击要害」用的就是
    /// 这个接口。
    ///
    /// 这一条实机报出过偏差：第 4 回合 不惧妖邪+ 打在一颗打算啃咬的蛋上，实机因此进入平静，
    /// 于是紧接着的 暴怒+ 只打 9 点；镜像读到的实时意图是不攻击、没进平静、留在愤怒里，把
    /// 暴怒+ 算成 18 点，于是预测那颗 21 血的蛋会被打死。实际它剩 6 点活了下来，下一回合多啃
    /// 一口，预计战损 2 变成实际 13。
    /// </remarks>
    private static void FearNoEvil(WatcherFearNoEvil card, CardOnPlayMirrorContext context)
    {
        bool wasAttacking = context.CardPlay.Target is { } target
            && V.Combat(context).IsEnemyIntendingToAttack(target);
        V.Attack(context);
        if (wasAttacking)
            S.EnterCalm(context);
    }

    /// <summary>只有伤害。从弃牌堆回手不在这张牌上，而在姿态切换里。</summary>
    private static void FlurryOfBlows(WatcherFlurryOfBlows card, CardOnPlayMirrorContext context)
        => V.Attack(context);
}
