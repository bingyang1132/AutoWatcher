using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using CombatSolver;
using CombatSolver.Engine.Common.Mirrors;
using CombatSolver.Engine.InCombat.Mirrors.Cards.OnPlay;
using STS2RitsuLib;
using WatcherMod;

namespace AutoWatcher;

/// <summary>
/// 把观者的牌与姿态教给战斗路线求解器。本 mod 不改变任何游戏行为。
/// </summary>
/// <remarks>
/// 注册必须在任何一场战斗开始之前一次性同步做完。求解器的 <c>MethodMirrorRegistry</c> 会把
/// 查找结果缓存进 <c>_resolvedSnapshot</c>，某个类型一旦被查过，之后再注册会被静默丢弃；
/// 而且并行搜索的各条车道会同时读那个字典。mod 初始化阶段是唯一同时满足这两个条件的时机。
/// </remarks>
[ModInitializer(nameof(Initialize))]
public static class Entry
{
    public const string ModId = "AutoWatcher";

    private static Logger? _logger;

    /// <summary>注册成功后记录实际注册了多少张牌，方便在日志里核对覆盖范围。</summary>
    private static int RegisteredCardCount { get; set; }

    public static void Initialize()
    {
        _logger = RitsuLibFramework.CreateLogger(ModId);

        AdapterSelfCheck.Result check = AdapterSelfCheck.Run();
        if (!check.Ok)
        {
            _logger.Error($"自检未通过，未注册任何镜像。{check.Detail}");
            return;
        }

        try
        {
            RegisterAll();
        }
        catch (Exception ex)
        {
            // 注册是全有或全无。半套镜像会让求解器给出看起来可信、实际缺一半效果的路线，
            // 那比求解器直接停在门口糟得多。
            _logger.Error($"注册镜像时失败，求解器将停在第三方 mod 检查上：{ex}");
            return;
        }

        _logger.Info($"已注册 {RegisteredCardCount} 张观者卡牌的镜像与全套效果动词。{check.Detail}");
    }

    private static void RegisterAll()
    {
        // 求解器默认拒绝任何第三方 gameplay mod 的 ModHelper 订阅者。观者只注册了这一个，
        // 而它两个方法都是对牌上附魔列表的纯计算、没有自身状态，只读钩子求解器本来就会回落到
        // 原实现，所以放行它是安全的。这里用 typeof 取全名而不是写死字符串，避免以后改名却静默失配。
        PredictionModHookSubscriberCapture.KnownPreRootSubscriberTypeNames.Add(
            typeof(WatcherEnchantStackHookProxy).FullName!);

        // 带"什么时候才打得出"条件的牌。不登记的话求解器按恒真算，路线会排进打不出的牌。
        WatcherPlayabilityMirrors.Register();

        MethodMirrorRegistry<CardModel, CardOnPlayMirrorContext> onPlay = CardOnPlayMirrors.Registry;
        onPlay.Register<WatcherStrike_P>(StarterDeckMirrors.Strike);
        onPlay.Register<WatcherDefend_P>(StarterDeckMirrors.Defend);
        onPlay.Register<WatcherEruption_P>(StarterDeckMirrors.Eruption);
        onPlay.Register<WatcherVigilance>(StarterDeckMirrors.Vigilance);
        onPlay.Register<WatcherMiracle>(StarterDeckMirrors.Miracle);

        // 这几条 Harmony 补丁补的是求解器根本没有注册表、或者判断写死了的位置：回合结束的
        // AfterSideTurnEnd、回合开始的遗物与 Power 流程、手牌保留的成长、额外回合的来源。
        // 目标方法解析不到就在这里抛出去，让整个注册失败。
        var harmony = new Harmony(ModId);
        harmony.Patch(
            WatcherTurnEndPatch.ResolveTarget(),
            postfix: new HarmonyMethod(typeof(WatcherTurnEndPatch), nameof(WatcherTurnEndPatch.Postfix)));
        harmony.Patch(
            WatcherRetainPatch.ResolveTarget(),
            prefix: new HarmonyMethod(typeof(WatcherRetainPatch), nameof(WatcherRetainPatch.Prefix)),
            postfix: new HarmonyMethod(typeof(WatcherRetainPatch), nameof(WatcherRetainPatch.Postfix)));
        harmony.Patch(
            WatcherPowerTurnStartPatch.ResolveTarget(),
            postfix: new HarmonyMethod(typeof(WatcherPowerTurnStartPatch), nameof(WatcherPowerTurnStartPatch.Postfix)));
        harmony.Patch(
            WatcherTurnStartPatch.ResolveTarget(),
            postfix: new HarmonyMethod(typeof(WatcherTurnStartPatch), nameof(WatcherTurnStartPatch.Postfix)));
        harmony.Patch(
            WatcherExtraTurnPatch.ResolvePrepareTarget(),
            postfix: new HarmonyMethod(typeof(WatcherExtraTurnPatch), nameof(WatcherExtraTurnPatch.PreparePostfix)));
        harmony.Patch(
            WatcherExtraTurnPatch.ResolveLivePrepareTarget(),
            postfix: new HarmonyMethod(typeof(WatcherExtraTurnPatch), nameof(WatcherExtraTurnPatch.PreparePostfix)));
        harmony.Patch(
            WatcherExtraTurnPatch.ResolveConsumeTarget(),
            postfix: new HarmonyMethod(typeof(WatcherExtraTurnPatch), nameof(WatcherExtraTurnPatch.ConsumePostfix)));
        harmony.Patch(
            WatcherRushdownPatch.ResolveTarget(),
            postfix: new HarmonyMethod(typeof(WatcherRushdownPatch), nameof(WatcherRushdownPatch.Postfix)));

        WatcherHookMirrors.RegisterAll();

        // 会改变"该按什么顺序出牌"的观者 Power。目前只有反弹格挡一个。
        WatcherStrategicEffects.RegisterAll();

        // 打出后当场让玩家选一个的牌。目前只有许愿一张。
        WatcherCardChoices.RegisterAll();

        RegisteredCardCount = 5
            + WatcherCardMirrors.RegisterA(onPlay)
            + WatcherCardMirrors.RegisterB(onPlay)
            + WatcherCardMirrors.RegisterC(onPlay);
    }
}
