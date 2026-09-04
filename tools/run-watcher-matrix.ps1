#requires -Version 7.0
<#
  观者适配层的验收矩阵。
  每个用例都设计成"只有镜像正确才能通过"，而不是"跑起来不报错就算过"。

  用法：
    pwsh -NoProfile -File tools\run-watcher-matrix.ps1
    pwsh -NoProfile -File tools\run-watcher-matrix.ps1 -Only WATCHER-CALM-EXIT-ENERGY

  前提：
    1. mods/ 里只有 CombatSolver、Watcher、SolverWatcherAdapter 三个。
       其他 gameplay mod（尤其 LotmMod）会让求解器停在第三方检查上，与观者无关。
    2. 改过 mod 之后先 Stop-Process -Name SlayTheSpire2，否则 harness 会复用旧进程。
#>
param(
    [string]$SolverRepo = "E:\Modding\SlayTheSpire2\CombatSolver",
    [string]$GameRoot = "D:\Sponsored\Steam\steamapps\common\Slay the Spire 2",
    [string]$RitsuWorkshopRoot = "D:\Sponsored\Steam\steamapps\workshop\content\2868840\3747602295",
    [string]$Only = ""
)

$ErrorActionPreference = "Stop"
$runner = Join-Path $SolverRepo "tools\run-unattended-test.ps1"
if (-not (Test-Path -LiteralPath $runner)) { throw "找不到 harness：$runner" }

function Hand([hashtable[]]$entries) {
    ($entries | ForEach-Object {
        [pscustomobject]@{ cardId = $_.Id; pile = "Hand"; count = ($_.Count ?? 1) }
    }) | ConvertTo-Json -Compress -AsArray
}

$cases = @(
    @{
        Id = "WATCHER-ERUPTION-WRATH"
        Why = "爆发造成伤害后进入愤怒。姿态必须落在模拟里，且求解器不得报告任何未镜像效果。"
        Args = @(
            "-EncounterId", "FUZZY_WURM_CRAWLER_WEAK", "-EnemyCurrentHp", "60",
            "-ClearPlayerPiles",
            "-CardsJson", (Hand @(@{ Id = "WATCHER_ERUPTION_P" })),
            "-ExpectedPlayedCardId", "WATCHER_ERUPTION_P",
            "-ExpectedPlayerPowersJson", '{"WRATH":1}',
            "-ExpectedInitialUnmirroredCount", "0"
        )
    },
    @{
        Id = "WATCHER-VIGILANCE-CALM"
        Why = "警戒获得格挡后进入平静。"
        Args = @(
            "-EncounterId", "FUZZY_WURM_CRAWLER_WEAK", "-EnemyCurrentHp", "60",
            "-ClearPlayerPiles",
            "-CardsJson", (Hand @(@{ Id = "WATCHER_VIGILANCE" })),
            "-ExpectedPlayedCardId", "WATCHER_VIGILANCE",
            "-ExpectedPlayerPowersJson", '{"CALM":1}',
            "-ExpectedInitialUnmirroredCount", "0"
        )
    },
    @{
        # 这是整个矩阵里最有区分度的一条。
        # 起手 3 能量，警戒 2 点、爆发 2 点，一共 4 点。
        # 只有"退出平静补 2 点能量"被建模，求解器才能找到两张都打的路线：
        #   警戒(-2) -> 1 点，进入平静
        #   爆发前先退出平静(+2) -> 3 点，付 2 点 -> 1 点
        # 没建模的话求解器只会认为能打一张。
        Id = "WATCHER-CALM-EXIT-ENERGY"
        Why = "退出平静的 2 点能量。没有它求解器只能打一张牌，有它才能两张都打并以愤怒收尾。"
        Args = @(
            "-EncounterId", "FUZZY_WURM_CRAWLER_WEAK", "-EnemyCurrentHp", "60",
            "-ClearPlayerPiles",
            "-CardsJson", (Hand @(@{ Id = "WATCHER_VIGILANCE" }, @{ Id = "WATCHER_ERUPTION_P" })),
            "-ExpectedInitialExecutableActionCountAtLeast", "2",
            "-ExpectedPlayerPowersJson", '{"WRATH":1}',
            "-ExpectedInitialUnmirroredCount", "0"
        )
    },
    @{
        # 爆发先造成 9 点伤害再进入愤怒，所以爆发自己不吃翻倍；随后的打击 6 点吃翻倍变 12 点。
        # 合计 21 点。愤怒没建模的话只有 15 点，敌人活下来，回合数断言就会失败。
        Id = "WATCHER-WRATH-DOUBLE-DAMAGE"
        Why = "愤怒姿态把后续攻击翻倍。爆发 9 + 打击 6x2 = 21，正好击杀；没建模只有 15 点。"
        Args = @(
            "-EncounterId", "FUZZY_WURM_CRAWLER_WEAK", "-EnemyCurrentHp", "21",
            "-ClearPlayerPiles",
            "-CardsJson", (Hand @(@{ Id = "WATCHER_ERUPTION_P" }, @{ Id = "WATCHER_STRIKE_P" })),
            "-ExpectedFinishedTurn", "1",
            "-ExpectedInitialUnmirroredCount", "0"
        )
    },
    @{
        Id = "WATCHER-MIRACLE-ENERGY"
        Why = "奇迹给 1 点能量。起手 3 点，奇迹 0 点费用给 1 点，之后要能打出两张 2 费牌。"
        Args = @(
            "-EncounterId", "FUZZY_WURM_CRAWLER_WEAK", "-EnemyCurrentHp", "60",
            "-ClearPlayerPiles",
            "-CardsJson", (Hand @(
                @{ Id = "WATCHER_MIRACLE" },
                @{ Id = "WATCHER_ERUPTION_P" },
                @{ Id = "WATCHER_VIGILANCE" })),
            "-ExpectedInitialExecutableActionCountAtLeast", "3",
            "-ExpectedInitialUnmirroredCount", "0"
        )
    }
)

if ($Only) { $cases = $cases | Where-Object { $_.Id -eq $Only } }
if (-not $cases) { throw "没有匹配的用例：$Only" }

$results = @()
foreach ($case in $cases) {
    Write-Host ""
    Write-Host "=== $($case.Id)" -ForegroundColor Cyan
    Write-Host "    $($case.Why)"
    $argv = @(
        "-NoProfile", "-File", $runner,
        "-ScenarioId", $case.Id,
        "-CharacterId", "WATCHER",
        "-Sts2GameRoot", $GameRoot,
        "-RitsuWorkshopRoot", $RitsuWorkshopRoot,
        "-TimeoutSeconds", "150",
        "-KeepGameOpen"
    ) + $case.Args
    & pwsh @argv 2>&1 | Select-Object -Last 4 | ForEach-Object { Write-Host "    $_" }
    $ok = $LASTEXITCODE -eq 0
    $results += [pscustomobject]@{ Case = $case.Id; Passed = $ok }
    Write-Host ("    -> " + ($ok ? "通过" : "未通过")) -ForegroundColor ($ok ? "Green" : "Red")
}

Write-Host ""
Write-Host "=== 汇总" -ForegroundColor Cyan
$results | Format-Table -AutoSize
$failed = ($results | Where-Object { -not $_.Passed }).Count
Write-Host ("通过 {0}/{1}" -f ($results.Count - $failed), $results.Count)
exit ($failed -gt 0 ? 1 : 0)
