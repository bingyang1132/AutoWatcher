#requires -Version 7.0
<#
  观者适配层的验收矩阵。

  设计原则：每条用例都要"只有镜像正确才能通过"。所以断言尽量落在能用算术自己验的量上
  （能量够不够打第二张牌、格挡数值、正好击杀的回合），而不是"跑起来不报错"。

  所有用例都只断言预测侧，用 -StopAfterInitialSolverResultAssertion 在求解器给出首轮结果后
  就停。这样不依赖战斗真的结束，也避开了遭遇战敌人数量带来的不确定性。

  每条都带 -ExpectedInitialUnmirroredCount 0，也就是求解器自己不得报告任何未镜像效果。
  这是"达到正式版强度"两条标准里的第二条。

  用法：
    pwsh -NoProfile -File tools\run-watcher-matrix.ps1
    pwsh -NoProfile -File tools\run-watcher-matrix.ps1 -Only WATCHER-CALM-EXIT-ENERGY

  前提：
    1. mods/ 里只留 CombatSolver、Watcher、SolverWatcherAdapter 三个。
       其他 gameplay mod（尤其 LotmMod）会让求解器停在第三方检查上，与观者无关。
    2. 改过 mod 之后先 Stop-Process -Name SlayTheSpire2，否则 harness 会复用旧进程，
       测到的是旧的加载状态。本脚本开头会自动杀，并且每条用例都用独立进程（-ExitOnComplete），
       因为复用进程在切换敌人注入方式时会卡住。
#>
param(
    [string]$SolverRepo = "E:\Modding\SlayTheSpire2\CombatSolver",
    [string]$GameRoot = "D:\Sponsored\Steam\steamapps\common\Slay the Spire 2",
    [string]$RitsuWorkshopRoot = "D:\Sponsored\Steam\steamapps\workshop\content\2868840\3747602295",
    [string]$Only = "",
    [switch]$NoRestart
)

$ErrorActionPreference = "Stop"
$runner = Join-Path $SolverRepo "tools\run-unattended-test.ps1"
if (-not (Test-Path -LiteralPath $runner)) { throw "找不到 harness：$runner" }

function Hand([string[]]$cardIds) {
    ($cardIds | Group-Object | ForEach-Object {
        [pscustomobject]@{ cardId = $_.Name; pile = "Hand"; count = $_.Count }
    }) | ConvertTo-Json -Compress -AsArray
}

$cases = @(
    @{
        Id = "WATCHER-ERUPTION-WRATH"
        Why = "爆发可打出且完全镜像。求解器不得报告任何未镜像效果。"
        Args = @(
            "-EnemyCurrentHp", "60", "-ClearPlayerPiles",
            "-CardsJson", (Hand @("WATCHER_ERUPTION_P")),
            "-ExpectedInitialFirstActionCardId", "WATCHER_ERUPTION_P",
            "-ExpectedInitialUnmirroredCount", "0"
        )
    },
    @{
        Id = "WATCHER-VIGILANCE-BLOCK"
        Why = "警戒给 8 点格挡。数值错了这条就过不去。"
        Args = @(
            "-EnemyCurrentHp", "60", "-ClearPlayerPiles",
            "-CardsJson", (Hand @("WATCHER_VIGILANCE")),
            "-ExpectedInitialFirstActionCardId", "WATCHER_VIGILANCE",
            "-ExpectedInitialMaxBlockAtLeast", "8",
            "-ExpectedInitialUnmirroredCount", "0"
        )
    },
    @{
        # 矩阵里最有区分度的一条，纯算术。
        # 退出平静的 2 点能量是在结算爆发"之内"发生的，付不了爆发自己的费用，只能支撑
        # 再往后的动作。所以把能量固定成 4 点，让"三个动作"成为唯一需要这 2 点的线路：
        #   4 点。警戒 2 费 -> 2 点，8 格挡，进入平静。
        #   爆发 2 费 -> 0 点，9 伤害，结算中退出平静补 2 点 -> 2 点，进入愤怒。
        #   打击 1 费 -> 1 点，6x2 = 12 伤害。共 3 个动作、8 格挡、21 伤害。
        # 那 2 点没建模的话，任何出牌顺序都只有 2 个动作：
        #   警戒 爆发 -> 打击付不起（8 格挡 9 伤害）
        #   爆发 打击 -> 警戒付不起（0 格挡 21 伤害）
        # 三个动作的线路在格挡和伤害上都严格更优，所以求解器一定会选它，不会像上一版那样
        # 因为奇迹带保留关键字而留牌。这里刻意不放奇迹，就是为了去掉那个自主选择。
        Id = "WATCHER-CALM-EXIT-ENERGY"
        Why = "退出平静补 2 点能量。固定 4 点能量下，没有它任何顺序都只有 2 个动作。"
        Args = @(
            "-EnemyCurrentHp", "80", "-ClearPlayerPiles", "-InitialPlayerEnergy", "4",
            "-CardsJson", (Hand @(
                "WATCHER_VIGILANCE", "WATCHER_ERUPTION_P", "WATCHER_STRIKE_P")),
            "-ExpectedInitialExecutableActionCountAtLeast", "3",
            "-ExpectedInitialMaxBlockAtLeast", "8",
            "-ExpectedInitialUnmirroredCount", "0"
        )
    },
    @{
        # 第二条纯算术判别，针对奇迹的能量：
        #   起手 3 能量。奇迹 0 费给 1 点 -> 4 点，两张爆发各 2 费刚好打完，共 3 个动作。
        #   奇迹的能量没建模的话只有 3 点，第二张爆发付不起，只有 2 个动作。
        #   这里用两张爆发而不是爆发加警戒，是为了不让"退出平静补能量"顶替奇迹的能量，
        #   否则这条就测不出奇迹。
        Id = "WATCHER-MIRACLE-ENERGY"
        Why = "奇迹给 1 点能量。没有它第二张爆发付不起，动作数只有 2。"
        Args = @(
            "-EnemyCurrentHp", "80", "-ClearPlayerPiles",
            "-CardsJson", (Hand @("WATCHER_MIRACLE", "WATCHER_ERUPTION_P", "WATCHER_ERUPTION_P")),
            "-ExpectedInitialExecutableActionCountAtLeast", "3",
            "-ExpectedInitialUnmirroredCount", "0"
        )
    },
    @{
        # 第三条纯算术判别，针对愤怒的伤害倍率：
        #   爆发先造成 9 点伤害，之后才进入愤怒，所以爆发自己不吃翻倍。
        #   随后的打击 6 点吃翻倍变 12 点。合计 21 点，正好击杀 21 血的敌人。
        #   愤怒没建模的话只有 9 + 6 = 15 点，第一回合杀不掉，投影结束回合就不是 1。
        Id = "WATCHER-WRATH-DOUBLE-DAMAGE"
        Why = "愤怒把后续攻击翻倍。爆发 9 加打击 6x2 正好 21 点击杀；没建模只有 15 点。"
        Args = @(
            "-InitialEnemyCurrentHpsJson", "[21]", "-ClearPlayerPiles",
            "-CardsJson", (Hand @("WATCHER_ERUPTION_P", "WATCHER_STRIKE_P")),
            "-ExpectedInitialCombatEndedTurn", "1",
            "-ExpectedInitialUnmirroredCount", "0"
        )
    },
    @{
        # 观者的核心循环：真言攒满 10 转神圣，进神圣再给 3 点能量。纯算术：
        #   固定 4 点能量。膜拜 2 费给 5 真言 -> 剩 2 点。
        #   第二张膜拜 2 费 -> 剩 0 点，真言到 10，转成神圣，进神圣补 3 点 -> 剩 3 点。
        #   打击 1 费 -> 剩 2 点。共 3 个动作。
        # 转换没建模的话真言停在 10、不进神圣、不补能量，打击付不起，只有 2 个动作。
        # 这条走的是 Harmony postfix 那条无声路径，求解器从不调那个方法，必须手写补上，
        # 所以它同时也是"五个无声缺口"里最要紧那一个的验证。
        Id = "WATCHER-MANTRA-TO-DIVINITY"
        Why = "真言满 10 转神圣并补 3 点能量。没建模的话第三个动作付不起。"
        Args = @(
            "-EnemyCurrentHp", "120", "-ClearPlayerPiles", "-InitialPlayerEnergy", "4",
            "-CardsJson", (Hand @("WATCHER_WORSHIP", "WATCHER_WORSHIP", "WATCHER_STRIKE_P")),
            "-ExpectedInitialExecutableActionCountAtLeast", "3",
            "-ExpectedInitialUnmirroredCount", "0"
        )
    },
    @{
        # 直接击杀动词。审判比的是当前生命，21 <= 30 所以斩杀成立。
        # 没有击杀动词的话这张牌是纯空操作，敌人活着，投影结束回合不会是 1。
        Id = "WATCHER-JUDGMENT-EXECUTE"
        Why = "审判在目标生命不高于阈值时直接击杀。没建模的话这张牌什么都不做。"
        Args = @(
            "-InitialEnemyCurrentHpsJson", "[21]", "-ClearPlayerPiles",
            "-CardsJson", (Hand @("WATCHER_JUDGMENT")),
            "-ExpectedInitialCombatEndedTurn", "1",
            "-ExpectedInitialUnmirroredCount", "0"
        )
    },
    @{
        # 格挡数值来自手牌数而不是固定值。护体结算时自己已经离开手牌，
        # 所以手里剩 4 张打击，格挡 = 4 x 3 = 12。
        # 按固定值或者把自己也数进去，得到的都不是 12。
        Id = "WATCHER-SPIRIT-SHIELD-SCALING"
        Why = "护体的格挡等于手牌数乘 3，且不计自己。四张打击在手时应为 12。"
        Args = @(
            "-EnemyCurrentHp", "120", "-ClearPlayerPiles",
            "-CardsJson", (Hand @(
                "WATCHER_SPIRIT_SHIELD",
                "WATCHER_STRIKE_P", "WATCHER_STRIKE_P",
                "WATCHER_STRIKE_P", "WATCHER_STRIKE_P")),
            "-ExpectedInitialMaxBlockAtLeast", "12",
            "-ExpectedInitialUnmirroredCount", "0"
        )
    },
    @{
        # 泛型施加任意 PowerModel 的端到端验证，也就是那 36 张"施加一个 Power"的牌走的路径。
        # 烈焰之环给 5 点激励，激励加到下一次攻击上：打击 6 + 5 = 11，正好击杀 11 血。
        # Power 没被施加的话只有 6 点，杀不掉。
        # 激励的伤害加成来自原版 VigorPower 的只读钩子，求解器本来就会回落到它的实现，
        # 所以这条同时验证了"姿态与 Power 一旦在模拟里正确，倍率和加成就自动正确"这个前提。
        Id = "WATCHER-WREATH-VIGOR-DAMAGE"
        Why = "烈焰之环施加 5 点激励，让打击 6 点变 11 点，正好击杀 11 血。"
        Args = @(
            "-InitialEnemyCurrentHpsJson", "[11]", "-ClearPlayerPiles",
            "-CardsJson", (Hand @("WATCHER_WREATH_OF_FLAME", "WATCHER_STRIKE_P")),
            "-ExpectedInitialCombatEndedTurn", "1",
            "-ExpectedInitialUnmirroredCount", "0"
        )
    },    @{
        # 实机报出来的那个 bug 的回归锁，纯算术：
        #   起手 3 能量，时之沙 4 费，第一回合付不起。它带保留，所以留在手上，
        #   而每次被保留费用降 1 -> 第二回合 3 费，正好付得起，20 点伤害击杀 20 血。
        #   保留降费没建模的话费用永远是 4，这张牌一辈子打不出来，战斗不会在第二回合结束。
        # 这条对应打旧日雕像时反复出现的计划外重算：日志里第一处差异就是这张牌的费用
        # 预测 4、实际 3。
        Id = "WATCHER-SANDS-RETAIN-COST"
        Why = "时之沙每次被保留降 1 费。没建模的话 4 费永远付不起，第二回合结束不了。"
        Args = @(
            "-InitialEnemyCurrentHpsJson", "[20]", "-ClearPlayerPiles",
            "-CardsJson", (Hand @("WATCHER_SANDS_OF_TIME")),
            "-ExpectedInitialCombatEndedTurn", "2",
            "-ExpectedInitialUnmirroredCount", "0"
        )
    },    @{
        # 实机报出来的第二个 bug 的回归锁。碎骨读一个 PowerVar 变量，而它的键是 Power 的
        # 类型名 VulnerablePower，不是牌面显示的那个词。写错的时候这张牌一被打出来就抛
        # KeyNotFound，整次搜索直接失败，界面显示"搜索动作回放失败"。
        # 所以这条不需要断言数值：只要求解器能给出一条包含这张牌的路线，就说明修好了。
        # 这里不断言未镜像项为 0——碎骨在路线首张时读不到上一张牌的类型，会按约定记一条风险，
        # 而出牌顺序由求解器自己定，断言 0 会变成不确定的。
        Id = "WATCHER-CRUSH-JOINTS-VAR-KEY"
        Why = "碎骨的易感层数取自 PowerVar，键是类型名。写错时这张牌一打出来整次搜索就失败。"
        Args = @(
            "-EnemyCurrentHp", "80", "-ClearPlayerPiles",
            "-CardsJson", (Hand @("WATCHER_DEFEND_P", "WATCHER_CRUSH_JOINTS")),
            "-ExpectedInitialExecutableActionCountAtLeast", "2"
        )
    },    @{
        # 回归锁，不是算术判别。3 这个数是对已验证的构建实测出来的，
        # 不是推导出来的。姿态或伤害倍率的建模一旦变化，这个回合数就会变。
        Id = "WATCHER-STANCE-REGRESSION-LOCK"
        Why = "爆发加打击循环打 100 血，投影三回合结束。实测值，用来锁住姿态与伤害倍率的建模。"
        Args = @(
            "-EnemyCurrentHp", "100", "-ClearPlayerPiles",
            "-CardsJson", (Hand @("WATCHER_ERUPTION_P", "WATCHER_STRIKE_P")),
            "-ExpectedInitialCombatEndedTurn", "3",
            "-ExpectedInitialFinalEnemyHpAtMost", "0",
            "-ExpectedInitialUnmirroredCount", "0"
        )
    }
)

if ($Only) { $cases = $cases | Where-Object { $_.Id -eq $Only } }
if (-not $cases) { throw "没有匹配的用例：$Only" }

if (-not $NoRestart) {
    Get-Process -Name "SlayTheSpire2" -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 2
}

$results = @()
foreach ($case in $cases) {
    Write-Host ""
    Write-Host "=== $($case.Id)" -ForegroundColor Cyan
    Write-Host "    $($case.Why)"
    $argv = @(
        "-NoProfile", "-File", $runner,
        "-ScenarioId", $case.Id,
        "-CharacterId", "WATCHER",
        "-EncounterId", "FUZZY_WURM_CRAWLER_WEAK",
        "-Sts2GameRoot", $GameRoot,
        "-RitsuWorkshopRoot", $RitsuWorkshopRoot,
        "-StopAfterInitialSolverResultAssertion",
        "-TimeoutSeconds", "150",
        "-ExitOnComplete"
    ) + $case.Args
    $output = & pwsh @argv 2>&1
    $ok = $LASTEXITCODE -eq 0
    if (-not $ok) {
        $output | Select-String -Pattern '"error"' | Select-Object -First 1 |
            ForEach-Object { Write-Host "    $($_.Line.Trim())" -ForegroundColor DarkYellow }
    }
    $results += [pscustomobject]@{ Case = $case.Id; Passed = $ok }
    Write-Host ("    -> " + ($ok ? "通过" : "未通过")) -ForegroundColor ($ok ? "Green" : "Red")
}

Write-Host ""
Write-Host "=== 汇总" -ForegroundColor Cyan
$results | Format-Table -AutoSize
$failed = ($results | Where-Object { -not $_.Passed }).Count
Write-Host ("通过 {0}/{1}" -f ($results.Count - $failed), $results.Count)
exit ($failed -gt 0 ? 1 : 0)
