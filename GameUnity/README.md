# 90 秒守点（Defense90）

一个竖屏守点小游戏，跑在抖音小游戏上（Unity WebGL）。工程用的是 DGame 框架，资源走 YooAsset，配置走 Luban，异步用 UniTask，界面用框架自带的 UIModule。

## 怎么跑起来

Unity 版本 2022.3.62f3c1。打开 `Assets/Scenes/GameStart/GameStart.unity` 直接 Play 就行，首页有个按钮切正常/压力模式，点开始进战斗。两个战斗场景在 `Assets/BundleAssets/Scenes/Defense90/` 下面，`Defense90.unity` 是正常，`Defense90Stress.unity` 是压力。

打抖音包的步骤：

1. Build Settings 切到 WebGL。
2. 按 F8（菜单 `DGame Tools/Build/一键打包AB`）打资源包，会输出到 `Assets/StreamingAssets/yoo`。这个目录没进仓库，刚克隆下来的话一定要先打一次。
3. 打开抖音 SDK 的构建面板（设置存在 `Assets/Editor/StarkBuilderSetting.asset`），输出到 `tt-minigame/`。
4. 用抖音开发者工具导入 `tt-minigame/`，真机调试或者扫码预览。

改数值的话，去改 `../GameConfig/Datas/` 下面守点开头的几张 xlsx，然后双击 `../GameConfig/一键转表工具_Bin.bat`，会重新生成 `Assets/Scripts/HotFix/GameProto/LubanConfig/` 里的代码。导表要装 .NET 8 运行时，生成出来的代码别手改。

仓库里没放 `Library`、`Temp`、`Logs`、`*.csproj`、资源包（`Bundles`、`StreamingAssets`）和构建输出（`Builds`、`tt-minigame`），这些都能重新生成。HybridCLR 在这个工程里没开，`HybridCLRData` 用不上。`Tools/unity.exe` 是我本地用的命令行工具，超过了 GitHub 单文件 100 MB 的上限，也没传。

## 工程结构

玩法代码都在 `Assets/Scripts/HotFix/GameLogic/Defense90/`：

```
DefenseRound.cs        规则：计时、刷怪、索敌、伤害、技能结算，不碰任何表现
DefenseGame.cs         玩法控制：状态切换、输入、HUD、切后台、最高分
DefenseBattlefield.cs  战场表现：对象池、深度排序、水花、水弹、击杀水泡、金币
DefenseSkill*.cs       技能的拖拽、时间轴和三段表现
DefenseVfxTierInfo.cs  两档特效的差异描述，编辑器对比面板会读
DefensePerfMonitor.cs  压力模式左下角的性能面板
DefenseSettings.cs     音量、特效档位、震动这些设置
DefenseAudio.cs 等     音频、震动、新手引导、飘字
```

界面在 `GameLogic/UI/Defense/`，配置表的读取在 `GameLogic/ConfigMgr/DefenseConfigMgr.cs`。Shader 在 `Assets/BundleAssets/Shaders/Defense/`，特效编辑器是 `Assets/Editor/DefenseSkillEditor.cs`，测试在 `Assets/Tests/Defense90/`。

数据上，`DefenseRules` 装一局要用的全部数值，由模式、敌人、技能三张表拼出来；`DefenseRound` 是规则本体，状态就 Ready、Playing、Paused、Won、Lost 几种。敌人、水花、水弹、飘字、死亡动画全是 struct，放在定长数组里循环用。敌人数组的长度跟着模式表的 `Capacity` 走，其他的是 48 到 64 个槽。

这么拆主要是两个考虑。一是规则和表现分开，`DefenseRound` 不依赖场景，测试里用固定种子就能完整跑一局。二是 WebGL 上 GC 很伤，所以战斗中不 new 对象、不 Instantiate，敌人和特效都是开局前建好的池子。

## 配置

四张表都在 `GameConfig/Datas/`：

- 守点模式配置表：单局时长、基地血量、刷怪间隔和后期加速、每批几只、同屏上限、随机种子、几只出一只乌龟、炮塔射速/伤害/射程
- 守点敌人配置表：血量、速度、攻击、攻击间隔、攻击距离（近战还是远程）、体型、分数
- 守点技能配置表：冷却、半径、伤害、三个阶段各自的时长、颜色、强度、震屏
- 守点特效档位表：完整和简化两档

正常模式和压力模式只差两个数：每批刷 1 只还是 8 只，同屏上限 256 还是 512。

## 玩法

守满 90 秒赢，基地血量掉光输。

敌人有两种。兔子跑得快、血少，冲到脸上咬；乌龟慢、血厚，站远了吐水弹，水弹飞到基地才扣血，所以看到乌龟扎堆可以用技能先清掉。

技能是按住按钮往战场上拖，松手放出去。拖回按钮上或者拖出战场就算取消。按钮上有冷却遮罩，好了会有提示。

界面有首页、战斗 HUD、暂停、结算（破纪录会提示）和设置。结算后能直接重开，最高分和设置存在 PlayerPrefs，杀进程再进来还在。

切到后台会暂停，回来先弹暂停页，玩家点了继续才往下走。回前台那一帧不会把后台这段时间一次补上，不会一下子刷出一堆怪。

## TA 部分

特效风格我定成了"卡通糖果水"：青蓝的水、奶白的泡沫、深青色的描边，跟暖色的花园背景能拉开，又不至于太跳。Shader 全是手写的 URP HLSL，没有采样贴图。

技能分三段：先出范围提示圈，然后一根水柱砸下来，最后水花往外炸开。

主要的 Shader 是 `DefenseWater.shader`，用 `_Mode` 切 5 种形态：范围提示、水柱、水花扩散、水滴和阴影、彗星水弹。普攻打出来的水花很多，我把每个水花的播放进度塞进顶点 alpha，是不是击杀塞进顶点的绿通道，这样所有水花能共用一份材质，合成一个批次。`DefenseEffectSprite.shader` 负责击杀时的水泡（先鼓起来再破）和金币的星芒，也是同样的办法传进度。还有一个 `DefenseOrbitalPulse.shader`，是最早做的 UI 层技能脉冲，后来整体换成水系方案就没再用了，现在只有编辑器检查还引用它。

能调的参数分在三个地方：时长、半径、伤害、震屏在技能配置表里；水和泡沫的颜色、描边色、水柱尺寸、技能炸出来的水滴数在 `DefenseWaterTimeline.asset` 里；每档的水滴数量和拖尾开关在特效档位表里。

另外写了个编辑器窗口，菜单 `DGame/Defense/水系技能时间轴`。时间轴页可以拖时间看每一帧，两档并排放，下面列出差异；特效库页把 9 种特效的两档缩略图一起循环播放；最后一页检查 Shader、材质、预制体引用和档位表有没有配错。

### 完整档和简化档

设置页里可以切，对应特效档位表的两行。具体差别：

| 特效 | 完整 | 简化 |
|------|------|------|
| 范围提示 | 旋转虚线环、描边、焦散水面、气泡、中心准星 | 一个实线环，平涂 |
| 水柱 | 分层水纹、高光带、气泡、底部泡沫裙边、边缘抖动 | 直边双色柱加描边 |
| 技能命中 | 三层波纹、12 颗弹射水滴、焦散 | 一个环 |
| 普攻水花 | 双环，水滴普通 6 颗、击杀 10 颗 | 单环，水滴 2 颗、4 颗 |
| 普攻水弹 | 彗星拖尾 | 圆水滴 |
| 击杀 | 水泡带高光鼓起再炸开，金币有星芒 | 水泡直接破，金币不闪 |
| 震屏、伤害数字 | 开 | 关 |

档位表里对应的字段是 `ShaderDetail`（水纹、高光、气泡、焦散这些装饰）、`Fragments` / `KillFragments`（水滴数，6/10 对 2/4）、`Glow`（水弹拖尾）、`Shake`（震屏）和 `DamageText`（伤害飘字）。

代价也很直接：简化档画面层次少了，打击感弱一截；水滴少了，命中和击杀没那么爽；没拖尾以后不太看得出弹道方向；关了飘字就看不到伤害数值。震屏关掉倒没什么损失。

简化档没有加新的 Shader 变体，是在同一份 Shader 里用 `_Detail` 把装饰部分跳过去，所以包体和 Shader 编译时间不受影响。

两档我在 iQOO 9 上用压力模式各跑了 60 秒，帧耗时几乎一样（P95 23 和 24 ms，P99 都是 40 ms），只有飘字的耗时从 0.09 降到了 0.01 ms。也就是说在这台手机上特效不是瓶颈，简化档更多是给差一点的机器准备的。要说明的是，这组数据是糖果水特效改版之前测的，改版之后的两档还没重新测。细节在 [PERFORMANCE.md](PERFORMANCE.md)。

## 优化过程

性能题的那一页单独写在 [PERFORMANCE.md](PERFORMANCE.md)，这里记一下整个过程里踩的坑。

最开始我是按 APK 的思路在做，后来才弄明白抖音小游戏应该走 WebGL，中间返工了一次。

第一个 WebGL 包启动要 30 多秒，开发者工具里 FP 40100 ms、渲染 35028 ms、callMain 3168 ms。后来照着抖音的文档一项项改：首包只留启动场景和必要的代码，战斗资源改成用到再下载；字体裁剪、合并图集，减少下载量；打开 WebGL2、SDK 压缩和后台内存优化；WebGL 内存固定成 128 MB。改完之后在 iQOO 9 上 callMain 1736 ms，总加载 2846 ms，首包 0.86 MB。代价是进战斗前多了一次资源加载，第一次进游戏要下载。

为了再省一点包体，我把代码剥离等级拉到了 High，结果打包时一大片代码被裁掉，运行直接报错。后来用 `Assets/HybridCLRGenerate/link.xml` 和 `Assets/Scripts/DisStripCode/` 把反射和生成代码保住，剥离等级最后退回了 Minimal，包体大了一点。

中文字体整个打进去太大，只好把用到的字单独导出来。以后加新文案就得重新导一次字体。

图集压缩以后 UI 有几处裁切不对。查下来是紧密打包按网格裁的问题，`Atlas_Defense` 和 `Atlas_Defense_UI` 关掉了旋转和紧密打包，padding 设成 2，WebGL 平台用 ASTC 6x6、最大 2048。图集里空白多了，体积稍微大一些。

怪多的时候一打就卡，这个是性能题的主体，主要做了对象池、定长数组、共享材质加顶点参数合批、增量深度排序、HUD 小变化攒一下再提交，以及普攻不再触发全场顿帧。具体看 PERFORMANCE.md。

## 已知问题

- 只在 iQOO 9 一台手机上测过，中低端机跑压力模式还是会掉帧。
- 开发者工具的性能测试里 TTI 没有数据（没做主动上报），wasm 编译和 callMain 两项还是没通过。
- 模拟器会提示不支持 ASTC，各个真机的纹理支持情况我没有逐台核对。不支持 ASTC 的机器会软解，内存和加载时间都会上去。
- 压力模式下大概 2% 的帧超过 33 ms，HUD 偶尔会冒出几十毫秒的尖峰，原因还没查到。
- `DefenseOrbitalPulse` 的材质还挂在 `DefenseAssets.asset` 上，运行时用不到，之后可以清掉。

## 用时

前后加起来 6 个小时左右。原型半小时；生图、做音乐音效、替换资源、补细节花了 3 个小时；剩下两个半小时基本都在研究抖音小游戏的流程和 WebGL，包括启动、内存、包体、代码剥离、字体和图集。

时间大头花在了平台适配和性能上，玩法内容就只做了题目要求的两种敌人和一个技能。

## 第三方和 AI

- 框架是 DGame，YooAsset、Luban、UniTask、DOTween 这些都是框架带的。
- 抖音小游戏的 Unity SDK（StarkSDK / TTSDK）。
- 美术是 AI 生图，用的 image2.6。
- 音乐音效是网上找的开源素材。
- 写代码用了 codex gpt6.1-sol 和 claude opus5.5，主要是查框架 API、写样板代码、补测试和文档。玩法设计、架构和性能定位是我自己做的，代码我都看过，能讲清楚，也能改。

笔试题的作答在 [笔试题作答.md](笔试题作答.md)。
