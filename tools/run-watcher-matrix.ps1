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
