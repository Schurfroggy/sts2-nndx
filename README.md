
这是一个《杀戮尖塔 2》的Mod：战斗中按 **C** 即可变成奶龙开始大笑，支持多人模式。

目前已上架创意工坊。

## 依赖

- 游戏版本 ≥ 0.107.1
- [STS2-RitsuLib](https://github.com/BAKAOLC/STS2-RitsuLib) ≥ 0.4.45

## 快捷键

默认键盘快捷键为 C。在 RitsuLib 提供的 Mod 设置入口中，选择“奶龙大笑 → 按键设置”即可修改，立即生效并保存在本机，不影响联机队友。

- 键盘支持普通按键和组合键；按 Backspace / Delete 清除，Esc 取消，也可恢复 C。
- 选择已被原版当前按键配置、Godot 输入动作或其他 RitsuLib 快捷键占用的绑定时，会提示冲突并保留原绑定。原版会忽略修饰键，因此原版占用 C 时，Ctrl+C 也会被拒绝。
- 后续游戏改键或其他 Mod 新注册快捷键导致冲突时，奶龙绑定会暂停并提示；冲突消失后恢复。其他 Mod 若自行监听输入，可能无法检测。
- 设置、地图、牌堆等覆盖界面和控制台中不会触发播放。配置保存在 Godot 用户数据目录下的 `sts_nndx/input.json`，不写入游戏跑局存档。

## 多人头像

多人战斗中，每次开始大笑会随机使用 `res/face/face1.png`～`face4.png` 中的一张头像；所有客户端显示同一张。手动停止、自然播放结束、死亡或战斗结束时恢复原头像，姓名和血条不变。

头像预加载后复用，不随按键重复读取文件。头像编号与现有播放消息一起发送；操作序号、跑局种子和房间标识用于拒绝过期、重复和跨战斗消息。沿用 150ms 切换冷却，不使用游戏战斗随机数。

本次使用新的 `overlay_v2` 同步消息，避免旧版解析新消息；多人测试需要所有客户端安装此新版，旧版不会接收新版的播放同步。

## 构建与打包

正式版默认不编译 `Scripts/Testing/`，也不包含 `nndx_scale` 测试命令。构建和打包正式版：

```sh
python3 tools/build_mod.py release
```

显式启用体型测试工具并打包：

```sh
python3 tools/build_mod.py test-tools
```

两种包均为 MSIL，分别输出到 `artifacts/release/` 与 `artifacts/test-tools/`，不会自动部署。脚本会检查 DLL 架构、测试工具是否符合所选版本，以及 PCK 是否排除了测试源码。Godot .NET 编辑器可通过 `--godot` 或 `GODOT` 指定；依赖已经还原时可加 `--no-restore`。

只编译测试 DLL 时，可使用：

```sh
dotnet build -c ExportRelease -p:EnableNndxTestTools=true -p:DeployMod=false
```

测试 DLL 和中间文件位于 `.godot/mono/temp/bin/TestTools/`、`.godot/mono/temp/obj/TestTools/`，与正式构建分开。`EnableNndxTestTools` 默认是 `false`，测试源码保留在仓库，测试开关不改变 MSIL 架构要求。

## 单人体型测试（仅测试构建）

安装 `test-tools` 包后，单人战斗中按键盘左上角的反引号键打开控制台，输入 `nndx_scale 0.5`、`nndx_scale 1`、`nndx_scale 2` 等命令，可将默认视觉体型设为指定倍率（范围 0.1～3）。关闭控制台后按 C 测试；也可以在播放中打开控制台改变体型。

`nndx_scale reset` 恢复首次测试前的默认体型，战斗结束时也会自动恢复。原版的临时缩放效果仍与默认体型相乘。此命令不改变符文、能力层数或存档，仅限单人战斗使用，无需其他玩法 Mod。
