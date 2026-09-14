<#
.SYNOPSIS
观者 × 女王：求解器会不会为了凑齐无限而铺垫。

.DESCRIPTION
这条不是镜像正确性用例，是**路线质量**用例，所以不在 run-watcher-matrix.ps1 里
（那一条跑的是短搜索 + 小遭遇，这一条要完整深搜索，一次 20~60 秒）。

根状态照抄问题包 `CombatSolver-0.38.1-QUEEN_BOSS-b6b09ddb84fd4cc6b41ed9e38bb96a6b`
里 `diagnostics/combat-state.json` 的 `exactContinuationState`：
A10、第三层女王、玩家 72/72、火炬头聚合体 211 + 女王 419、玩家身上 3 层荆棘。

这副牌里存在一个自持循环：**不惧妖邪 → 化智为空 → 亮剑**。
每周期净能量 0（−1 −1，退出平静 +2）、净过牌 +1（抽 3 + 抽 1，打 3 张）、
敌人掉 23 血。牌库磨到只剩这三张时，每周期必然触发洗牌把它们送回来。

实测（2026-09-14，求解器 0.38.1 + AutoWatcher 1.0.3 + 观者 0.9.28）：

| 配置 | 预计整场战损 |
|---|---|
| Medium（beam 60 / 12k 节点） | 37 |
| VeryHigh（beam 135 / 100k 节点） | 72 |
| 人工打出同一条无限 | 14 |

求解器**认得出**这个循环（`CYCLE_SUSTAIN` 记到 `energy_delta=0 enemy_delta=-23`），
也**保得住**（最深连走 13 个周期，每次都拿到代表名额），选中的路线第 11 回合
一口气打 49 张牌当场结束战斗。缺口在**什么时候凑齐**：可持续周期第 6 回合就
出现过，但第 3~10 回合它一共只打了 27 张牌，一路挨打，把 37 点战损全掉在
这一段。

判据是 `-ExpectedInitialProjectedBattleHpLostAtMost 14`，**现在两个预设都挂**，
这就是它作为负对照的价值：任何声称修好了的改动，必须让这条从挂变成过。

.NOTES
跑之前先确认：
1. `dotnet build -c Release -p:CopyModOnBuild=false` —— harness 取的是 Release 产物，
   不是平时部署用的 Debug。旧 DLL 配新清单会让整条用例挂住且不报错。
2. 三个环境变量都要设（COMBATSOLVER_HEADLESS_ROOT、
   COMBATSOLVER_HEADLESS_HOST_ROOT、NUGET_PACKAGES）。
3. 沙箱的 `combat_solver_settings.json` 决定用哪个预设。

.EXAMPLE
$env:COMBATSOLVER_HEADLESS_ROOT='D:\CombatSolverHeadless\wt-b2b3d2a1a499a73e'
$env:COMBATSOLVER_HEADLESS_HOST_ROOT='D:\CombatSolverHeadless\host-v1'
$env:NUGET_PACKAGES='D:\NuGetPackages'
pwsh -NoProfile -File tools\run-queen-infinite.ps1 -DetailedDiagnostics
#>
param(
    [int]$TimeoutSeconds = 420,
    [int]$ExpectedProjectedBattleHpLostAtMost = 14,
    [string]$ScenarioId = "WATCHER-QUEEN-INFINITE",
    [string]$SolverRepo = "E:\Modding\SlayTheSpire2\CombatSolver",
    [string]$GameRoot = "D:\Sponsored\Steam\steamapps\common\Slay the Spire 2",
    [string]$RitsuWorkshopRoot = "D:\Sponsored\Steam\steamapps\workshop\content\2868840\3747602295",
    # 这副牌的搜索比矩阵里那些小用例重得多，沙箱默认的 4 GB / 2 核不够。
    [int]$HeadlessMemoryReservationMiB = 12288,
    [int]$HeadlessCpuReservation = 8,
    [int]$SearchMaxDegreeOfParallelism = 8,
    [int]$NoGcRegionBudgetGigabytes = 8,
    # 打开之后日志里会有 CYCLE_SHAPE / CYCLE_REGION / CYCLE_SUSTAIN，
    # 用来分清「凑不齐」和「凑齐之后跑不远」。
    [switch]$DetailedDiagnostics
)

$ErrorActionPreference = 'Stop'
$runner = Join-Path $SolverRepo 'tools\run-unattended-test.ps1'
if (-not (Test-Path -LiteralPath $runner -PathType Leaf)) {
    throw "找不到 run-unattended-test.ps1：$runner"
}

# 手牌 6 张，顺序照记录。
$hand = @(
    @{ cardId = 'WATCHER_MIRACLE';         pile = 'Hand'; count = 1; upgradeLevels = 0 }
    @{ cardId = 'ULTIMATE_DEFEND';         pile = 'Hand'; count = 1; upgradeLevels = 1 }
    @{ cardId = 'WATCHER_WHEEL_KICK';      pile = 'Hand'; count = 1; upgradeLevels = 1 }
    @{ cardId = 'WATCHER_FEAR_NO_EVIL';    pile = 'Hand'; count = 1; upgradeLevels = 1 }
    @{ cardId = 'WATCHER_SCRAWL_P';        pile = 'Hand'; count = 1; upgradeLevels = 1 }
    @{ cardId = 'FLASH_OF_STEEL';          pile = 'Hand'; count = 1; upgradeLevels = 1 }
)
# 抽牌堆 15 张。化智为空排在第 13 张，所以第 1 回合抄写抽不到它，起不了循环。
$draw = @(
    @{ cardId = 'ASCENDERS_BANE';          pile = 'Draw'; count = 1; upgradeLevels = 0 }
    @{ cardId = 'WATCHER_STRIKE_P';        pile = 'Draw'; count = 1; upgradeLevels = 0 }
    @{ cardId = 'WATCHER_INNER_PEACE';     pile = 'Draw'; count = 1; upgradeLevels = 1 }
    @{ cardId = 'WATCHER_WALLOP';          pile = 'Draw'; count = 1; upgradeLevels = 1 }
    @{ cardId = 'WATCHER_BOWLING_BASH';    pile = 'Draw'; count = 1; upgradeLevels = 1 }
    @{ cardId = 'WATCHER_INDIGNATION';     pile = 'Draw'; count = 1; upgradeLevels = 1 }
    @{ cardId = 'WATCHER_FLURRY_OF_BLOWS'; pile = 'Draw'; count = 1; upgradeLevels = 1 }
    @{ cardId = 'WATCHER_VIGILANCE';       pile = 'Draw'; count = 1; upgradeLevels = 1 }
    @{ cardId = 'WATCHER_WISH_P';          pile = 'Draw'; count = 1; upgradeLevels = 1 }
    @{ cardId = 'WATCHER_STRIKE_P';        pile = 'Draw'; count = 1; upgradeLevels = 0 }
    @{ cardId = 'WATCHER_CRESCENDO';       pile = 'Draw'; count = 1; upgradeLevels = 1 }
    @{ cardId = 'WATCHER_MENTAL_FORTRESS'; pile = 'Draw'; count = 1; upgradeLevels = 1 }
    @{ cardId = 'WATCHER_EMPTY_MIND';      pile = 'Draw'; count = 1; upgradeLevels = 1 }
    @{ cardId = 'WATCHER_ERUPTION_P';      pile = 'Draw'; count = 1; upgradeLevels = 1 }
    @{ cardId = 'WATCHER_STRIKE_P';        pile = 'Draw'; count = 1; upgradeLevels = 1 }
)

$argv = @(
    '-NoProfile', '-File', $runner,
    '-ScenarioId', $ScenarioId,
    '-CharacterId', 'WATCHER',
    '-EncounterId', 'QUEEN_BOSS',
    '-Ascension', '10',
    '-Sts2GameRoot', $GameRoot,
    '-RitsuWorkshopRoot', $RitsuWorkshopRoot,
    '-HeadlessMemoryReservationMiB', "$HeadlessMemoryReservationMiB",
    '-HeadlessCpuReservation', "$HeadlessCpuReservation",
    '-ClearPlayerPiles',
    '-ClearRunDeck',
    '-CardsJson', (ConvertTo-Json -Compress -InputObject @($hand + $draw)),
    '-RelicsJson', (ConvertTo-Json -Compress -InputObject @(
        @{ relicId = 'PAELS_EYE' }
        @{ relicId = 'POCKETWATCH' }
        @{ relicId = 'RAINBOW_RING' }
        @{ relicId = 'CENTENNIAL_PUZZLE' }
        @{ relicId = 'LIZARD_TAIL' }
        @{ relicId = 'TUNING_FORK' }
    )),
    '-PowersJson', (ConvertTo-Json -Compress -InputObject @(
        @{ powerId = 'THORNS_POWER'; target = 'Player'; amount = 3 }
    )),
    '-InitialPlayerHp', '72',
    '-InitialPlayerMaxHp', '72',
    '-InitialPlayerEnergy', '3',
    '-InitialEnemyMaxHpsJson', '[211,419]',
    '-InitialEnemyCurrentHpsJson', '[211,419]',
    '-SearchMaxDegreeOfParallelismForTest', "$SearchMaxDegreeOfParallelism",
    '-NoGcRegionBudgetGigabytesForTest', "$NoGcRegionBudgetGigabytes",
    '-ExpectedInitialProjectedBattleHpLostAtMost', "$ExpectedProjectedBattleHpLostAtMost",
    '-StopAfterInitialSolverResultAssertion',
    '-TimeoutSeconds', "$TimeoutSeconds",
    '-ExitOnComplete'
)
if ($DetailedDiagnostics) {
    $argv += @('-EnableDetailedDiagnosticLogsForTest', '1')
}

& pwsh @argv
exit $LASTEXITCODE
