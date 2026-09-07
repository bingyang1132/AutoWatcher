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
    1. mods/ 里只留 CombatSolver、Watcher、AutoWatcher 三个。
       其他 gameplay mod（尤其 LotmMod）会让求解器停在第三方检查上，与观者无关。
    2. 改过 mod 之后先 Stop-Process -Name SlayTheSpire2，否则 harness 会复用旧进程，
       测到的是旧的加载状态。本脚本开头会自动杀，并且每条用例都用独立进程（-ExitOnComplete）。

  为什么不复用进程省开机时间——这条路试过两次都失败，不要再试：
    一条用例 28 秒里约 25 秒是开机，真正搜索只要 70 毫秒，看着很值得复用。harness 那边确实支持：
    不带 -ExitOnComplete 时会跑 WaitUntilReusableAsync，回到主菜单后写出可复用标记，日志里能看到
    PROCESS_QUIESCENT reuse_process=true。
    卡住的是调用方。复用模式下游戏进程故意留活着，它继承了子进程的标准输出句柄，于是
    `$out = & pwsh ...` 会一直等到句柄关闭（也就是等到游戏退出）才返回。换成 Start-Process
    -RedirectStandardOutput 会因为参数里的空格路径被拆断；换成 `*>` 文件重定向仍然挂。
    两轮下来花掉近二十分钟没有拿到结果，收益（8 分钟降到 1 分钟）不值这个代价。就用一条一进程。

  想快就少跑几条：用 -Tag 只跑和本次改动相关的。改单张牌的镜像不可能弄坏爆发或警戒；
  只有动词层、姿态、Harmony 补丁、注册这类跨切面的改动才需要全量。
#>
param(
    [string]$SolverRepo = "E:\Modding\SlayTheSpire2\CombatSolver",
    [string]$GameRoot = "D:\Sponsored\Steam\steamapps\common\Slay the Spire 2",
    [string]$RitsuWorkshopRoot = "D:\Sponsored\Steam\steamapps\workshop\content\2868840\3747602295",
    [string]$Only = "",
    # 只跑带某个标签的用例。标签按"哪一层改动可能弄坏它"划分：
    #   smoke    最基本的几条，任何改动都值得跑
    #   stance   姿态动词        energy 能量收支      damage 伤害倍率与数值
    #   patches  Harmony 补丁    hooks  钩子镜像      cards  单张牌的镜像
    #   retain   手牌保留        draw   抽牌与循环    turnflow 回合流程
    [string]$Tag = "",
    [string]$ProgressPath = "",
    # 整个矩阵的硬时限。一条用例正常 28 秒，但 harness 卡住时会一直耗到自己的 150 秒上限，
    # 23 条全卡就是近一小时。到点就停下并把没跑的列成"未运行"，不要让它无声地拖下去。
    [int]$MaxTotalMinutes = 20,
    [switch]$NoRestart
)

$ErrorActionPreference = "Stop"
$runner = Join-Path $SolverRepo "tools\run-unattended-test.ps1"
if (-not (Test-Path -LiteralPath $runner)) { throw "找不到 harness：$runner" }

# harness 跑的是求解器仓库的 Release 构建产物，而适配层是照着游戏 mods 目录里那份求解器编译的
# （csproj 的 HintPath 就指在那儿）。两份版本一旦不一样，适配层在 harness 里加载不上，于是观者的
# ModHelper 订阅者不再被放行，每条用例都撞 IncompatibleGameplayModException 再等满 150 秒超时。
# 23 条全跑完要一小时，最后只告诉你"全都没过"。这个坑踩过两次，所以开跑前先对一次版本。
$solverBuildDll = Join-Path $SolverRepo ".godot\mono\temp\bin\Release\CombatSolver.dll"
$solverDeployedDll = Join-Path $GameRoot "mods\CombatSolver\CombatSolver.dll"
foreach ($required in @($solverBuildDll, $solverDeployedDll)) {
    if (-not (Test-Path -LiteralPath $required)) { throw "找不到求解器：$required" }
}
# 比的是内容哈希，不是版本号。本地开发时版本号常常几十个提交都不动，只比版本号会漏掉
# "两份都是 0.31.1.0 但代码不一样"这种情况——而那正是最容易出现、也最难看出来的一种。
$buildVersion = [Reflection.AssemblyName]::GetAssemblyName($solverBuildDll).Version
$buildHash = (Get-FileHash -LiteralPath $solverBuildDll -Algorithm SHA256).Hash
$deployedHash = (Get-FileHash -LiteralPath $solverDeployedDll -Algorithm SHA256).Hash
if ($buildHash -ne $deployedHash) {
    $deployedVersion = [Reflection.AssemblyName]::GetAssemblyName($solverDeployedDll).Version
    throw @"
求解器的两份产物内容不一样，先不要跑：
  harness 用的 Release 构建产物 $solverBuildDll
    版本 $buildVersion 哈希 $buildHash
  适配层编译时引用的     $solverDeployedDll
    版本 $deployedVersion 哈希 $deployedHash
先在求解器仓库跑一次 dotnet build CombatSolver.csproj -c Release（它会顺带部署到 mods/），
再重新构建适配层。
"@
}
Write-Host "求解器 $buildVersion 构建产物与部署一致（$($buildHash.Substring(0, 12))）" -ForegroundColor DarkGray


# 整个矩阵要跑好几分钟，一定是放后台跑的。而 PowerShell 的标准输出要等进程退出才刷出来，
# 后台看到的输出文件在跑完之前一直是 0 字节，看着像卡死。所以每跑完一条就往进度文件写一行，
# 让外面随时能知道跑到哪了、还动不动。
if (-not $ProgressPath) {
    $ProgressPath = Join-Path $PSScriptRoot "..\.matrix-progress.txt"
}
$ProgressPath = [IO.Path]::GetFullPath($ProgressPath)
function Write-MatrixProgress([string]$line) {
    $stamped = "{0} {1}" -f (Get-Date -Format "HH:mm:ss"), $line
    Add-Content -LiteralPath $ProgressPath -Value $stamped -Encoding UTF8
    Write-Host $stamped
}
Set-Content -LiteralPath $ProgressPath -Value "" -Encoding UTF8

function Hand([string[]]$cardIds) {
    ($cardIds | Group-Object | ForEach-Object {
        [pscustomobject]@{ cardId = $_.Name; pile = "Hand"; count = $_.Count }
    }) | ConvertTo-Json -Compress -AsArray
}

$cases = @(
    @{
        Id = "WATCHER-ERUPTION-WRATH"
        Tags = @("smoke", "stance")
        Why = "爆发可打出且完全镜像。求解器不得报告任何未镜像效果。"
        Args = @(
            "-EnemyCurrentHp", "60", "-ClearPlayerPiles",
            "-CardsJson", (Hand @("WATCHER_ERUPTION_P")),
            "-ExpectedInitialFirstActionCardId", "WATCHER_ERUPTION_P",
            "-ExpectedInitialUnmirroredCount", "0"
        )
    },
    @{
        Id = "WATCHER-LESSON-LEARNED-FATAL-KILL"
        Tags = @("cards", "damage")
        Why = "勤学精进斩杀时永久升级牌组里一张牌。敌人 8 血，这一刀正好斩杀，求解器要把这笔局外收益记进长期资源。"
        Args = @(
            "-EnemyCurrentHp", "8", "-ClearPlayerPiles",
            "-CardsJson", (Hand @("WATCHER_LESSON_LEARNED")),
            "-ExpectedInitialFirstActionCardId", "WATCHER_LESSON_LEARNED",
            "-ExpectedInitialLongTermResourceValueAtLeast", "30",
            "-ExpectedInitialUnmirroredCount", "0"
        )
    },
    @{
        Id = "WATCHER-DEUS-EX-MACHINA-DRAW"
        Tags = @("cards", "draw", "hooks")
        Why = "机械降神被抽到时自动打出自己、消耗、给两张奇迹。敌人 22 血：三张打击 18 点杀不掉，必须先用化智为空把它抽出来、拿两张奇迹换出第四点能量，四张打击 24 点才够。不建模就打不出第一回合击杀。"
        Args = @(
            "-EnemyCurrentHp", "22", "-ClearPlayerPiles",
            "-CardsJson", '[{"cardId":"WATCHER_EMPTY_MIND","pile":"Hand","count":1},{"cardId":"WATCHER_STRIKE_P","pile":"Hand","count":4},{"cardId":"WATCHER_DEUS_EX_MACHINA","pile":"Draw","count":1}]',
            "-ExpectedInitialCombatEndedTurn", "1",
            "-ExpectedInitialUnmirroredCount", "0"
        )
    },
    @{
        Id = "WATCHER-SIGNATURE-MOVE-UNPLAYABLE"
        Tags = @("cards", "hooks")
        Why = "标志性一击要手上只有它一张攻击牌才打得出。手里四张攻击、三费，最优是打三张打击共 3 个动作；把它当成随时可打的话最优会变成它加一张打击、只有 2 个动作。"
        Args = @(
            "-EnemyCurrentHp", "60", "-ClearPlayerPiles",
            "-CardsJson", '[{"cardId":"WATCHER_SIGNATURE_MOVE","pile":"Hand","count":1},{"cardId":"WATCHER_STRIKE_P","pile":"Hand","count":3}]',
            "-ExpectedInitialFirstActionCardId", "WATCHER_STRIKE_P",
            "-ExpectedInitialExecutableActionCountAtLeast", "3",
            "-ExpectedInitialUnmirroredCount", "0"
        )
    },
    @{
        Id = "WATCHER-SCRY-DISCARD-BRANCH"
        Tags = @("cards", "draw")
        Why = "天眼的预视要开出真正的搜索分支：牌堆顶三张里挑哪几张丢，由求解器自己搜，不再记成未建模选择。"
        Args = @(
            "-EnemyCurrentHp", "60", "-ClearPlayerPiles",
            "-CardsJson", '[{"cardId":"WATCHER_THIRD_EYE","pile":"Hand","count":1},{"cardId":"WATCHER_STRIKE_P","pile":"Draw","count":4}]',
            "-ExpectedInitialFirstActionCardId", "WATCHER_THIRD_EYE",
            "-ExpectedInitialChoiceBranchesEvaluatedAtLeast", "1",
            "-ExpectedInitialUnmirroredCount", "0"
        )
    },
    @{
        # 反弹格挡挂在敌人身上，不在自己身上。这条只验钩子分发：直接把 BLOCK_RETURN_POWER
        # 注到敌人头上，手里两张打击，各触发一次 -> 4 点格挡。手上没有任何别的格挡来源。
        #
        # 注意这条**不**验"求解器会不会为了起甲把以手拒之排到前面"——那条现在是坏的，
        # 见 README 的已知取舍：Beam 的设置估值只统计自己身上的增益，敌人身上的这层
        # 在动作分类里完全不可见，以手拒之被当成一张伤害更低的普通攻击排到最后。
        Id = "WATCHER-BLOCK-RETURN-HOOK"
        Tags = @("hooks", "damage")
        Why = "反弹格挡挂在敌人身上，攻击它自己起 2 甲。钩子没分发到就是 0 甲。"
        Args = @(
            "-EnemyCurrentHp", "60", "-ClearPlayerPiles",
            "-PowerId", "BLOCK_RETURN_POWER", "-PowerAmount", "2", "-PowerTarget", "Enemy",
            "-CardsJson", (Hand @("WATCHER_STRIKE_P", "WATCHER_STRIKE_P")),
            "-ExpectedInitialMaxBlockAtLeast", "4",
            "-ExpectedInitialUnmirroredCount", "0"
        )
    },
    @{
        # 同一层反弹格挡，换成多段攻击。发泄 1 费、3 点伤害打 3 段，所以是 3 次触发 = 6 甲，
        # 不是 1 张牌 = 2 甲。原版 for (i < hitCount) 每段各走一次 Damage()，每个 DamageResult
        # 各分发一次 AfterDamageGiven —— 这条把"按段算不按张算"钉死。
        #
        # 这也是为什么估值不能拿攻击牌张数当代理：观者一手多段牌，按张算会低估三倍。
        Id = "WATCHER-BLOCK-RETURN-MULTIHIT"
        Tags = @("hooks", "damage")
        Why = "反弹格挡按伤害段数触发。发泄 3 段 = 6 甲；按张算只有 2 甲。"
        Args = @(
            "-EnemyCurrentHp", "60", "-ClearPlayerPiles",
            "-PowerId", "BLOCK_RETURN_POWER", "-PowerAmount", "2", "-PowerTarget", "Enemy",
            "-CardsJson", (Hand @("WATCHER_TANTRUM")),
            "-ExpectedInitialMaxBlockAtLeast", "6",
            "-ExpectedInitialUnmirroredCount", "0"
        )
    },
    @{
        # 以手拒之自己不给甲，甲来自"之后打中这个敌人"。所以它必须排在攻击前面才有收益，
        # 这条锁的就是排序，不是镜像——镜像由上面两条 BLOCK-RETURN 锁着。
        #
        # 没有第三方战略估值登记时求解器给的顺序是「打击 打击 以手拒之」，第 1 回合 max_block=0：
        # 打出它的瞬间格挡增量、ProjectedPlayerHp、PlayerHp、PreventionPotential 四样一样都不动，
        # 于是被归成一张纯 ImmediateOffense，和打击同族但伤害更低，在族内代表里被压掉。
        # 登记之后它同时进 ImmediateDefense 族，不再被压。
        #
        # 判据是算术的：以手拒之 MagicNumber = 2，两张打击各 1 段，
        # 排在最前面 = 2 + 2 = 4 甲，排中间 = 2，排最后 = 0。
        Id = "WATCHER-TALK-TO-THE-HAND-ORDERING"
        Tags = @("hooks", "damage")
        Why = "以手拒之要排到攻击前面才起得了甲。排在后面这一回合就是 0 甲。"
        Args = @(
            "-EnemyCurrentHp", "60", "-ClearPlayerPiles", "-InitialPlayerEnergy", "3",
            "-CardsJson", (Hand @("WATCHER_TALK_TO_THE_HAND", "WATCHER_STRIKE_P", "WATCHER_STRIKE_P")),
            "-ExpectedInitialMaxBlockAtLeast", "4",
            "-ExpectedInitialUnmirroredCount", "0"
        )
    },
    @{
        # 形态药剂让玩家在平静和愤怒之间二选一。挑哪个是一次真正的搜索分支，走求解器的
        # PotionChoiceMirrors 登记；不登记的话 PotionChoiceSupport.RequiresChoice 对第三方药水
        # 恒为 false，求解器根本不为它开分支，这瓶药在模拟里就是个没有收益的空动作。
        #
        # 判据是算术的：敌人 18 血，手里只有爆发+（9 点伤害，打出后进入愤怒）。
        # 先用药水进愤怒、再打爆发+ 才是 9×2=18 正好击杀；直接打爆发+ 只有 9 点，
        # 第一回合杀不掉。所以"第一回合结束战斗"这一条只有药水的选择被建模了才成立。
        # 顺带钉住选的是愤怒那张令牌，不是平静那张。
        #
        # 反向对照做过：把适配层里 PotionChoiceMirrors.Register<StancePotion> 注掉重新构建，
        # 这条立刻挂在"结束回合为 3、预期为 1"。
        #
        # 写这条时踩的三个坑，别再踩：
        #   1. 敌人血量要用 -InitialEnemyCurrentHpsJson "[18]" 指定成一个敌人。
        #      -EnemyCurrentHp 是把遭遇战里每个敌人都设成那个值，多打几只就要多花回合。
        #   2. 别自己编 -InitialEnemyMoveIdsJson 的行动 ID。编错了 harness 直接抛
        #      "怪物 X 没有行动 Y"，而这条本来也不需要固定敌人行动。
        #   3. 要显式 -PotionPolicyForTest RequireAtLeastOne。默认的 Smart 会先搜一条不用药水的
        #      路线，再靠梯度审计判断这瓶药值不值 9 点血——那验的是药水估值的启发式，
        #      不是"二选一有没有展开成分支"。强制用药才把判据收在要测的那一件事上。
        Id = "WATCHER-STANCE-POTION-WRATH-KILL"
        Tags = @("hooks", "damage", "cards")
        Why = "形态药剂的二选一是真分支。先用药水进愤怒，爆发+ 才够 18 点正好击杀。"
        Args = @(
            "-InitialEnemyCurrentHpsJson", "[18]", "-ClearPlayerPiles", "-InitialPlayerEnergy", "3",
            "-InitialPlayerMaxHp", "80",
            "-CardsJson", (Hand @("WATCHER_ERUPTION_P")),
            "-PotionsJson", '[{"potionId":"STANCE_POTION","slot":0}]',
            "-PotionPolicyForTest", "RequireAtLeastOne",
            "-ExpectedInitialFirstActionPotionId", "STANCE_POTION",
            "-ExpectedInitialFirstActionChoiceCardId", "WATCHER_STANCE_POTION_WRATH_CHOICE",
            "-ExpectedInitialCombatEndedTurn", "1",
            "-ExpectedInitialUnmirroredCount", "0"
        )
    },
    @{
        Id = "WATCHER-VIGILANCE-BLOCK"
        Tags = @("smoke", "stance")
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
        Tags = @("stance", "energy")
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
        Tags = @("energy")
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
        Tags = @("stance", "damage")
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
        Tags = @("stance", "hooks")
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
        Tags = @("damage")
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
        Tags = @("damage")
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
        Tags = @("damage")
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
        Tags = @("patches", "retain")
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
        Tags = @("cards", "damage")
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
        Tags = @("smoke", "stance", "damage")
        Why = "爆发加打击循环打 100 血，投影三回合结束。实测值，用来锁住姿态与伤害倍率的建模。"
        Args = @(
            "-EnemyCurrentHp", "100", "-ClearPlayerPiles",
            "-CardsJson", (Hand @("WATCHER_ERUPTION_P", "WATCHER_STRIKE_P")),
            "-ExpectedInitialCombatEndedTurn", "3",
            "-ExpectedInitialFinalEnemyHpAtMost", "0",
            "-ExpectedInitialUnmirroredCount", "0"
        )
    },
    @{
        # 实机事故的回归锁。原来的路线是 火焰纹 -> 渎神 -> 结末 -> 打击：求解器以为最后那张
        # 打击能在同一回合杀死最后一个敌人，于是渎神的回合结束死亡永远不会到来。实际结末打完
        # 回合就结束了，打击没机会打出，下一回合开始时玩家被渎神杀死。
        #
        # 敌人正好 30 血，手里两张结末一张打击、3 点能量，牌堆里没别的。
        #   正确：第一回合 打击 6 + 结末 12 = 18，结末打完回合就结束，第二张结末打不出来；
        #         敌人剩 12，第二回合重新抽回这三张再打 18，第二回合死。
        #   有 bug：打击 6 + 结末 12 + 结末 12 = 30，第一回合就死。
        # 所以用结束回合数判别，1 和 2 不会混。
        Id = "WATCHER-CONCLUDE-ENDS-TURN"
        Tags = @("turnflow")
        Why = "结末打出后本回合就结束，后面接不了牌。渎神自杀事故的根因。"
        Args = @(
            "-EnemyCurrentHp", "30", "-ClearPlayerPiles", "-InitialPlayerEnergy", "3",
            "-CardsJson", (Hand @("WATCHER_CONCLUDE", "WATCHER_CONCLUDE", "WATCHER_STRIKE_P")),
            "-ExpectedInitialCombatEndedTurn", "2",
            "-ExpectedInitialUnmirroredCount", "0"
        )
    },
    @{
        # 渎神只有在"这回合就能赢"的时候才该打，因为它下回合开始就要人命。
        # 所以这里给一副赢不了的牌：整副只有渎神和两张防御，一点伤害都没有。
        # 这时打渎神就是纯自杀，求解器必须一次都不打它。
        #
        # 注意不要用"敌人血很多"来构造赢不了：harness 的血量会被上限截断，敌人照样能被打死，
        # 而在能打死的那一回合打渎神其实是对的（神圣的伤害倍率白拿，死亡永远不会到来）。
        # 第一版用了 200 血，结果求解器在致命回合打了渎神并且赢了——那是正确下法，不是 bug。
        Id = "WATCHER-BLASPHEMY-NO-SUICIDE"
        Tags = @("turnflow", "patches")
        Why = "赢不了的时候不能打渎神。"
        Args = @(
            "-EnemyCurrentHp", "60", "-ClearPlayerPiles", "-InitialPlayerEnergy", "3",
            "-CardsJson", (Hand @("WATCHER_BLASPHEMY", "WATCHER_DEFEND_P", "WATCHER_DEFEND_P")),
            "-ExpectedInitialAbsentActionCardId", "WATCHER_BLASPHEMY",
            "-ExpectedInitialUnmirroredCount", "0"
        )
    },
    @{
        # 时之沙是抽上来那回合还要 4 费，只有在手上过了一个回合末（被保留）才降到 3。
        #   3 点能量、手里一张时之沙加三张打击+。
        #   正确：时之沙 4 费打不出，三张打击+ 各 1 费共 27 伤害。
        #   减错费：时之沙 3 费一张打完，20 伤害，能量清空。
        # 27 比 20 高，所以只有减费没被提前应用，第一个动作才会是打击+。
        Id = "WATCHER-SANDS-NO-DRAW-TURN-DISCOUNT"
        Tags = @("patches", "retain")
        Why = "时之沙抽上来那回合不降费，要在手上过一个回合末才降。"
        Args = @(
            "-EnemyCurrentHp", "200", "-ClearPlayerPiles", "-InitialPlayerEnergy", "3",
            "-CardsJson", '[{"cardId":"WATCHER_SANDS_OF_TIME","pile":"Hand","count":1},{"cardId":"WATCHER_STRIKE_P","pile":"Hand","count":3,"upgradeLevels":1}]',
            "-ExpectedInitialFirstActionCardId", "WATCHER_STRIKE_P",
            "-ExpectedInitialUnmirroredCount", "0"
        )
    },
    @{
        # 观者的红蓝无限。整副只有内心平静和暴怒+，两张都是 1 费，都不消耗。
        #   猛虎下山 1 费，之后每次进入愤怒抽 2 张。
        #   内心平静 1 费进平静（不在平静里就是进平静）。
        #   暴怒+ 1 费打伤害并进愤怒，退出平静补 2 点能量，然后凌波微步抽 2 张。
        # 每轮净耗能量 0，抽回来的正好是这两张，所以能一直打下去。
        #
        # 关键在抽牌的时机：抽必须发生在暴怒进弃牌堆之后。要是在结算当中就抽，
        # 暴怒还在出牌堆里，抽牌堆见底重洗时抽不到它，循环就断了。
        # 断了的话第一回合最多打三张牌，敌人打不死。
        Id = "WATCHER-RUSHDOWN-INFINITE"
        Tags = @("patches", "draw", "stance")
        Why = "凌波微步的进入愤怒抽牌撑起红蓝无限，第一回合就该打完。"
        Args = @(
            "-EnemyCurrentHp", "200", "-ClearPlayerPiles", "-InitialPlayerEnergy", "3",
            "-CardsJson", '[{"cardId":"WATCHER_RUSHDOWN","pile":"Hand","count":1},{"cardId":"WATCHER_INNER_PEACE","pile":"Hand","count":1},{"cardId":"WATCHER_ERUPTION_P","pile":"Hand","count":1,"upgradeLevels":1}]',
            "-ExpectedInitialCombatEndedTurn", "1",
            "-ExpectedInitialUnmirroredCount", "0"
        )
    },
    @{
        # 发泄打出后会把自己随机洗回抽牌堆。这一步之前没建模，实机日志里一直在报未镜像。
        Id = "WATCHER-TANTRUM-SHUFFLES-BACK"
        Tags = @("hooks", "cards")
        Why = "发泄打出后洗回抽牌堆，不能再报未镜像。"
        Args = @(
            "-EnemyCurrentHp", "200", "-ClearPlayerPiles", "-InitialPlayerEnergy", "3",
            "-CardsJson", (Hand @("WATCHER_TANTRUM")),
            "-ExpectedInitialFirstActionCardId", "WATCHER_TANTRUM",
            "-ExpectedInitialUnmirroredCount", "0"
        )
    }
)

# 要用 @() 包住。只筛出一条时 PowerShell 会把数组拆成单个哈希表，
# 那时 .Count 数的是哈希表的键个数，进度里就会报出"共 3 条"这种假数。
if ($Only) { $cases = @($cases | Where-Object { $_.Id -eq $Only }) }
if ($Tag)  { $cases = @($cases | Where-Object { $_.Tags -contains $Tag }) }
if (-not $cases) { throw "没有匹配的用例：$Only" }

if (-not $NoRestart) {
    Get-Process -Name "SlayTheSpire2" -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 2
}

$results = @()
$deadline = (Get-Date).AddMinutes($MaxTotalMinutes)
Write-MatrixProgress ("开始 共 {0} 条 时限 {1} 分钟 进度文件 {2}" -f $cases.Count, $MaxTotalMinutes, $ProgressPath)
$index = 0
$aborted = $false
foreach ($case in $cases) {
    $index++
    if ($aborted) {
        $results += [pscustomobject]@{ Case = $case.Id; Passed = $false; Note = "未运行" }
        continue
    }
    # 留出这一条自己的超时。剩余预算不够一条完整用例就别开头了，开了也只会撞死线。
    $remaining = [int]($deadline - (Get-Date)).TotalSeconds
    if ($remaining -lt 40) {
        Write-MatrixProgress ("到时限 {0} 分钟，剩下 {1} 条未运行" -f $MaxTotalMinutes, ($cases.Count - $index + 1))
        $aborted = $true
        $results += [pscustomobject]@{ Case = $case.Id; Passed = $false; Note = "未运行" }
        continue
    }
    $caseTimeout = [Math]::Min(150, $remaining)
    Write-Host ""
    Write-Host "=== $($case.Id)" -ForegroundColor Cyan
    Write-Host "    $($case.Why)"
    # 并行度必须钉死：不钉的话线程调度会让展开顺序变化，像"结束回合数"这种最优性断言
    # 会随机不过——实测出现过一次。不要再加 -ShortSearchBudgetOverrideMilliseconds：这些夹具
    # 的搜索本身只要 70 毫秒，给一个几秒的预算是纯粹白等，每条会多花那么多秒。
    $argv = @(
        "-NoProfile", "-File", $runner,
        "-ScenarioId", $case.Id,
        "-CharacterId", "WATCHER",
        "-EncounterId", "FUZZY_WURM_CRAWLER_WEAK",
        "-Sts2GameRoot", $GameRoot,
        "-RitsuWorkshopRoot", $RitsuWorkshopRoot,
        "-StopAfterInitialSolverResultAssertion",
        "-ForceShortSearchOnly",
        "-SearchMaxDegreeOfParallelismForTest", "1",
        "-TimeoutSeconds", "$caseTimeout"
        "-ExitOnComplete"
    ) + $case.Args
    $output = & pwsh @argv 2>&1
    $ok = $LASTEXITCODE -eq 0
    if (-not $ok) {
        $output | Select-String -Pattern '"error"' | Select-Object -First 1 |
            ForEach-Object { Write-Host "    $($_.Line.Trim())" -ForegroundColor DarkYellow }
    }
    $results += [pscustomobject]@{ Case = $case.Id; Passed = $ok; Note = ($ok ? "" : "未通过") }
    Write-MatrixProgress ("[{0}/{1}] {2} {3}" -f $index, $cases.Count, $case.Id, ($ok ? "通过" : "未通过"))
}

Write-Host ""
Write-Host "=== 汇总" -ForegroundColor Cyan
$results | Format-Table -AutoSize
$failed = ($results | Where-Object { -not $_.Passed }).Count
$notRun = ($results | Where-Object { $_.Note -eq "未运行" }).Count
Write-MatrixProgress ("结束 通过 {0}/{1} 未通过 {2} 未运行 {3}" -f ($results.Count - $failed), $results.Count, ($failed - $notRun), $notRun)
exit ($failed -gt 0 ? 1 : 0)
