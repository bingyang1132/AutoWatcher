using CombatSolver;
using WatcherMod;

namespace AutoWatcher;

/// <summary>
/// 观者身上那些会改变"该按什么顺序出牌"的 Power，登记进求解器的战略估值。
///
/// 求解器的战略估值默认只看玩家自己身上的增益，第三方类型落进默认分支按层数记一点 Scaling。
/// 对大多数 Power 够用，对反弹格挡不够——它挂在**敌人**身上，那一圈直接跳过，于是以手拒之在
/// 动作分类里完全不给防御信号，被当成一张伤害更低的普通攻击排到所有攻击后面，而它的收益恰恰
/// 依赖排在攻击前面。实测：手里以手拒之加两张打击、敌人这回合打 4 点，求解器给的顺序是
/// 「打击 打击 以手拒之」，第 1 回合 max_block=0 白挨 4 点。
/// </summary>
internal static class WatcherStrategicEffects
{
    public static void RegisterAll()
    {
        // 反弹格挡：玩家每打中这个敌人**一段**，就起 Amount 点甲（Unpowered，不吃敏捷），
        // 而且不自减，这一整场都在。
        //
        // 估值取「层数 × 可打出的攻击牌张数」。按张不按段是低估——发泄一张打三段就是三倍，
        // 观者一手多段牌——但求解器现成的量里没有按段计数：StrategicEffectRequirements.AttackPlays
        // 数的是 liveCards 里 Type == Attack 的张数，CardChoiceSupport.CardValue 也只读 Damage
        // 基础值、不乘段数。分两件事看：
        //
        //   准入（真正解决那个 bug 的一半）：ClassifyActionOptionFamilies 判的是
        //     after.PreventionPotential > before.PreventionPotential，只要非零就够，段数不影响。
        //     顺序一旦被展开，格挡本身是精确模拟的，一点不差。
        //   排名（次要）：数值只影响这条线在真格挡兑现之前能在 Beam 里活多久。低估会剪掉好线，
        //     所以按既定规矩宁可往高了估。这里没往高估，靠的是 Prevention 自带的上限：
        //     它把值夹在「这回合进来的伤害 × min(2, 剩余回合)」以内，对任何有威胁的局面，
        //     层数 × 张数早就顶到上限了，段数的差别在那之上看不见。真出现顶不到上限的局面，
        //     说明这层 Power 相对威胁本来就小，估低一点也不会剪掉真正重要的线。
        StrategicEffectMirrors.Register<BlockReturnPower>(
            StrategicEffectRequirements.AttackPlays,
            static (power, context) => StrategicEffectModel.Prevention(
                Math.Max(1, power.Amount) * context.AttackPlays,
                context),
            StrategicEffectHost.Enemy);

        // 真言是**阈值资源**，不是线性叠加的增益。
        //
        // 核对过的实现：GainMantra 里变更后 Amount >= 10 就减掉正好 10 并进神格
        // （Mantra.AfterPowerAmountChangedCompat，有 _isResolving 闩锁，一次只转换一次）；
        // 进神格给 3 费（Divinity.AfterApplied）；已经在神格里再进不给费
        // （ChangeStance 的 current == target 直接返回）；神格在回合结束退出
        // （Divinity.AfterSideTurnEnd）。所以一回合之内只兑得到一次 3 费。
        //
        // 不登记的后果落在**刻度选错**上，而不是数值大小上：默认兜底把它记进
        // ScalingPotential，于是「先祈祷把真言推到 10、进神格、用多出来的费打伤害」这条线在
        // ClassifyActionOptionFamilies 眼里完全不产生**资源**信号，祈祷和攻击的先后关系就没有
        // 依据。收益本身是精确模拟的——祈祷够了真的会进神格、真的会多 3 费——缺的只是让搜索在
        // 兑现之前就看得见这条线。
        //
        // 取值：离下一次兑现还差多少，按 3 费封顶。故意**不**把神格的伤害倍率算进去：倍率在
        // 进入神格之后由模拟精确结算，而把「接近阈值」这件事估得比一次兑现还高，正是让求解器
        // 一直祈祷下去的那种估值。
        StrategicEffectMirrors.Register<Mantra>(
            StrategicEffectRequirements.None,
            static (power, _) => StrategicEffectModel.Resource(
                DivinityEnergyGain * Math.Min(DivinityMantraThreshold, Math.Max(0, power.Amount))
                    / DivinityMantraThreshold));
    }

    /// <summary>真言满这个数转神格，并从层数里减掉正好这个数。</summary>
    private const int DivinityMantraThreshold = 10;

    /// <summary>进入神格给的能量。已经在神格里再进不给。</summary>
    private const int DivinityEnergyGain = 3;
}
