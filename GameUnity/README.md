# 90 秒守点（Defense90）

竖屏守点小游戏，目标平台是抖音小游戏（Unity WebGL）。工程基于 DGame 框架，用到了 YooAsset 管资源、Luban 管配置、UniTask 做异步、UIModule 管窗口。

## 1. 运行

- Unity 2022.3.62f3c1
- 启动场景：`Assets/Scenes/GameStart/GameStart.unity`
- 战斗场景：`Assets/BundleAssets/Scenes/Defense90/Defense90.unity`（正常）、`Defense90Stress.unity`（压力）

Editor 里打开 `GameStart.unity` 直接 Play。首页的模式按钮切换正常 / 压力，点开始进战斗。

打抖音包：

1. Build Settings 切到 WebGL。
2. 菜单 `DGame Tools/Build/一键打包AB`（快捷键 F8），打资源包到 `Assets/StreamingAssets/yoo`。这个目录不进仓库，克隆下来以后第一次打包前必须先做这一步。
3. 打开抖音 SDK 的构建面板（配置在 `Assets/Editor/StarkBuilderSetting.asset`），输出到 `tt-minigame/`。
4. 抖音开发者工具导入 `tt-minigame/`，真机调试或扫码预览。

改配置：编辑 `../GameConfig/Datas/守点*.xlsx`，然后跑 `../GameConfig/一键转表工具_Bin.bat`，代码会生成到 `Assets/Scripts/HotFix/GameProto/LubanConfig/`。生成的文件不要手改。导表需要装 .NET 8 运行时。

仓库里没有放这些东西，都能重新生成：`Library`、`Temp`、`Logs`、资源包（`Bundles`、`StreamingAssets`）、构建输出（`Builds`、`tt-minigame`）、`*.csproj`。HybridCLR 在这个工程里是关着的，所以 `HybridCLRData` 也不需要。`Tools/unity.exe` 是本地用的命令行工具，超过 GitHub 的单文件大小限制，也没放进来。

## 2. 工程结构

```
GameUnity/
├─ Assets/
│  ├─ DGame/                         框架
│  ├─ Scripts/HotFix/GameLogic/
│  │  ├─ Defense90/
│  │  │  ├─ DefenseRound.cs          规则层：计时、刷怪、索敌、伤害、技能结算，不碰表现
│  │  │  ├─ DefenseGame.cs           玩法控制：状态、输入、HUD、切后台、最高分
│  │  │  ├─ DefenseBattlefield.cs    战场表现：对象池、深度排序、水花/水弹/击杀水泡/金币、相机叠加
│  │  │  ├─ DefenseVfxTierInfo.cs    两档特效的差异说明，编辑器对比面板读这里
│  │  │  ├─ DefenseSkill*.cs         技能拖拽、时间轴、三阶段表现
│  │  │  ├─ DefensePerfMonitor.cs    压力模式的性能面板
│  │  │  ├─ DefenseSettings.cs       音量、特效档位、震动等设置
│  │  │  └─ DefenseAudio.cs 等       音频、震动、引导、飘字
│  │  ├─ UI/Defense/                 首页、战斗、暂停、结算、设置
│  │  └─ ConfigMgr/DefenseConfigMgr  配置表访问
│  ├─ Scripts/HotFix/GameProto/      Luban 生成代码
│  ├─ BundleAssets/                  打包资源：场景、预制体、Shader、音频、UI
│  ├─ BundleAssets/Shaders/Defense/  DefenseWater / DefenseEffectSprite / DefenseOrbitalPulse
│  ├─ Editor/DefenseSkillEditor.cs   特效编辑器
│  └─ Tests/Defense90/               EditMode / PlayMode 测试
└─ tt-minigame/                      抖音包输出目录
```

### 数据结构

- `DefenseRules`：一局要用的全部数值，由模式、敌人、技能三张表拼出来。测试里可以直接改字段造极端情况。
- `DefenseRound`：规则状态机，状态有 Ready / Playing / Paused / Won / Lost。
- `DefenseEnemy`、`DefenseImpact`、`DefenseProjectile`、`DefenseFloatText`、`DefenseDeath`：都是 struct，放在定长数组里。敌人数组的长度取模式表的 `Capacity`，飘字、水弹这些是 48 到 64 个槽，循环复用。

为什么这么拆：规则和表现分开以后，`DefenseRound` 可以在 EditMode 测试里用固定种子完整跑一局，不需要场景。表现层每帧只读规则状态。敌人和特效全部用定长数组加预先建好的对象池，战斗中不 new、不 Instantiate，WebGL 上 GC 很贵，这一点比较重要。

### 配置表（`GameConfig/Datas/`）

| 表 | 能调什么 |
|----|----------|
| 守点模式配置表 | 单局时长、基地血量、刷怪间隔和后期缩放、每批数量、同屏上限、随机种子、重甲频率、炮塔射速/伤害/射程 |
| 守点敌人配置表 | 生命、速度、攻击、攻击间隔、攻击距离（近战或远程水弹）、体型、分数 |
| 守点技能配置表 | 冷却、半径、伤害、三个阶段的时长、颜色、强度、震屏 |
| 守点特效档位表 | 完整 / 简化两档 |

正常和压力两个模式只差两项：每批刷怪数（1 只 vs 8 只）和同屏上限（256 vs 512），其余数值一样。

## 3. 玩法

- 守住 90 秒算赢，基地血量归零算输。
- 两种敌人：兔子跑得快、血少、近战；乌龟慢、血厚，在远处吐水弹，水弹飞到基地才扣血，所以可以用技能先把乌龟清掉。
- 技能：按住按钮拖到战场上瞄准，松手放出去；拖回按钮或拖出战场就取消。有冷却遮罩和就绪提示。
- 界面：首页、战斗 HUD、暂停、结算（破纪录会提示）、设置。
- 结算后可以重开。最高分和设置存在 PlayerPrefs 里，杀掉进程重进还在。
- 切后台会暂停，回来以后弹暂停页，玩家点继续才恢复。回到前台的第一帧不会把后台的时间一次性补上，所以不会突然刷出一堆怪。

## 4. TA 实现

特效统一做成"卡通糖果水"的样子：青蓝色的水、奶白色的泡沫、深青色描边，和暖色的花园背景能区分开，又不会太跳。Shader 都是 URP HLSL，不采样贴图。

技能分三个阶段：范围提示 → 水柱落下 → 命中扩散。

- `DefenseWater.shader`：用 `_Mode` 区分 5 种形态，0 范围提示、1 水柱、2 水花扩散、3 水滴和阴影、4 彗星水弹。普攻水花的播放进度写在顶点 alpha 里，是否击杀写在顶点绿通道里，所有水花共用一份材质，这样能合批。
- `DefenseEffectSprite.shader`：击杀时的水泡（鼓起来再破掉）和金币的星芒闪光，进度也走顶点 alpha。
- `DefenseOrbitalPulse.shader`：早期做的 UI 层技能脉冲，后来改用水系方案，现在战斗里没用到，只保留在编辑器检查里。

可调参数分三处：时长、半径、伤害、震屏在技能配置表；水和泡沫、描边的颜色、水柱尺寸、技能弹射水滴数在 `DefenseWaterTimeline.asset`；各档的水滴数量、拖尾开关在特效档位表。

编辑器入口是菜单 `DGame/Defense/水系技能时间轴`：
- 时间轴页可以拖动时间，两档并排显示，下面列出差异；
- 特效库页把 9 种特效的两档缩略图循环播放；
- 材质与校验页检查 Shader、材质、预制体引用和档位表有没有配错。

### 完整 / 简化两档

设置页可以切换，对应特效档位表的两行。

| 特效 | 完整 | 简化 |
|------|------|------|
| 范围提示 | 旋转虚线环、描边、焦散水面、气泡、中心准星 | 一个实线环，平涂 |
| 水柱 | 分层水纹、高光带、气泡、底部泡沫裙边、边缘抖动 | 直边双色柱加描边 |
| 技能命中 | 三层波纹、12 颗弹射水滴、焦散 | 一个环 |
| 普攻水花 | 双环，水滴普通 6 颗 / 击杀 10 颗 | 单环，水滴 2 颗 / 4 颗 |
| 普攻水弹 | 彗星拖尾 | 圆水滴 |
| 击杀 | 水泡带高光鼓起再破裂飞溅，金币有星芒 | 水泡直接破，金币不闪 |
| 震屏、伤害数字 | 开 | 关 |

档位表字段：

| 字段 | 完整 | 简化 | 管什么 | 简化的代价 |
|------|------|------|--------|------------|
| `ShaderDetail` | 开 | 关 | 水纹、高光、气泡、焦散、多层波纹、技能水滴、金币闪光 | 画面层次少了，打击感弱一些 |
| `Fragments` / `KillFragments` | 6 / 10 | 2 / 4 | 普攻、击杀水花的水滴数 | 命中和击杀没那么爽 |
| `Glow` | 开 | 关 | 水弹拖尾 | 看不太出弹道方向 |
| `Shake` | 开 | 关 | 技能和基地受击时震屏 | 没有明显代价 |
| `DamageText` | 开 | 关 | 伤害飘字 | 看不到伤害数值 |

简化档没有加新的 Shader 变体，同一份 Shader 里用 `_Detail` 跳过装饰部分，所以包体和 Shader 编译时间都不变。

实测：iQOO 9 压力模式各跑 60 秒，两档帧耗时差不多（P95 23 vs 24 ms，P99 都是 40 ms），飘字耗时从 0.09 降到 0.01 ms。所以这台机器上特效不是瓶颈，简化档主要是给更差的机器留的。注意这组数据是在糖果水特效改版之前测的，新版两档还没重新测。详细见 [PERFORMANCE.md](PERFORMANCE.md)。

## 5. 优化记录

性能题的一页说明在 [PERFORMANCE.md](PERFORMANCE.md)。下面是整个过程中踩过的坑：

| 问题 | 怎么处理的 | 代价 |
|------|------------|------|
| 一开始按 APK 的路子在做，后来才搞清楚抖音小游戏应该走 WebGL | 改成 WebGL 构建 | 返工 |
| 第一个 WebGL 包启动 30 多秒（开发者工具：FP 40100 ms，渲染 35028 ms，callMain 3168 ms） | 首包只放启动场景和必要代码，战斗资源走 YooAsset 按需加载；字体裁剪、合图集减下载量；开 WebGL2、SDK 压缩和后台内存优化；WebGL 内存固定 128 MB。之后真机 callMain 1736 ms，总加载 2846 ms（iQOO 9） | 进战斗前多一次资源加载 |
| 首包太大 | 首包压到 0.86 MB，其余资源放远程 | 第一次进需要下载 |
| 为了省一点包体把代码剥离等级拉到 High，结果大量代码被裁掉，运行报错 | 用 `Assets/HybridCLRGenerate/link.xml` 和 `Assets/Scripts/DisStripCode/` 保住反射和生成代码，最后剥离等级退回 Minimal | 包体大一点 |
| 中文字体太大 | 只导出用到的字 | 加新文案要重新导字体 |
| 打图集、压缩后 UI 裁切不对 | `Atlas_Defense` 和 `Atlas_Defense_UI` 关掉旋转和紧密打包（紧密打包按网格裁，导致 UI 错位），padding 2；WebGL 用 ASTC 6x6，最大 2048 | 图集空白多，体积稍大 |
| 怪多的时候一打就卡 | 对象池、定长数组、共享材质加顶点参数合批、增量深度排序、HUD 小变化合并提交，普攻不再触发全场顿帧 | 见 PERFORMANCE.md |

## 6. 已知问题

- 只在 iQOO 9 上测过，中低端机的压力模式还会掉帧。
- 开发者工具的性能测试里 TTI 没数据（没做主动上报），wasm 编译和 callMain 两项还是未通过。
- 模拟器会提示不支持 ASTC，和真机纹理支持的差异没逐个核对。不支持 ASTC 的设备会软解，内存和加载时间都会上去。
- 压力模式下约 2% 的帧超过 33 ms，HUD 偶尔有几十毫秒的尖峰，原因还没定位。
- `DefenseOrbitalPulse` 的材质还挂在 `DefenseAssets.asset` 上，运行时没用到，后面可以清掉。

## 7. 用时

总共 6 小时左右：

- 玩法原型：0.5 小时
- 生图、音乐音效、换资源和细节完善：3 小时
- 研究抖音小游戏流程和 WebGL（启动、内存、包体、剥离、字体、图集）：2.5 小时

时间大部分花在平台适配和性能上，玩法只做了题目要求的两种敌人和一个技能。

## 8. 第三方素材、插件和 AI

- 框架：DGame，自带 YooAsset、Luban、UniTask、DOTween 等。
- 抖音小游戏 Unity SDK（StarkSDK / TTSDK）。
- 美术：AI 生图，工具是 image2.6。
- 音乐音效：网上的开源音效。
- AI 编码：codex gpt6.1-sol、claude opus5.5，主要拿来查框架 API、写样板代码、测试和文档。玩法设计、架构和性能定位是我自己做的，代码都看过，可以解释和改。

笔试题答案在 [笔试答案.md](笔试答案.md)。
