using CombatSolver.Engine.InCombat.Mirrors.Cards.OnPlay;
using WatcherMod;

namespace AutoWatcher;

/// <summary>
/// 观者初始牌组的 OnPlay 镜像。
/// </summary>
/// <remarks>
/// 打击和防御与原版同名牌逐字一致，直接复用求解器的通用镜像。爆发和警戒是"原版命令 + 一个
/// 姿态动词"，这也是观者绝大多数牌的形状——所以这五张牌一旦通过严格 diff，剩下的牌基本是
/// 同一套动词的重新组合，而不是新的建模问题。
/// </remarks>
internal static class StarterDeckMirrors
{
    /// <summary>造成 6 点伤害。</summary>
    public static void Strike(WatcherStrike_P card, CardOnPlayMirrorContext context)
        => GeneralCardMirrors.GeneralAttackOnPlay(card, context);

    /// <summary>获得 5 点格挡。</summary>
    public static void Defend(WatcherDefend_P card, CardOnPlayMirrorContext context)
        => GeneralCardMirrors.GeneralBlockOnPlay(card, context);

    /// <summary>造成 9 点伤害，然后进入愤怒。</summary>
    public static void Eruption(WatcherEruption_P card, CardOnPlayMirrorContext context)
    {
        GeneralCardMirrors.GeneralAttackOnPlay(card, context);
        WatcherStanceVerbs.EnterWrath(context);
    }

    /// <summary>获得 8 点格挡，然后进入平静。</summary>
    public static void Vigilance(WatcherVigilance card, CardOnPlayMirrorContext context)
    {
        GeneralCardMirrors.GeneralBlockOnPlay(card, context);
        WatcherStanceVerbs.EnterCalm(context);
    }

    /// <summary>获得 1 点能量。清水遗物每场战斗开局往手里塞一张。</summary>
    public static void Miracle(WatcherMiracle card, CardOnPlayMirrorContext context)
        => context.Simulator.GainEnergy(card.Owner, card.DynamicVars.Energy.BaseValue);
}
