
这是一个《杀戮尖塔 2》的Mod：战斗中按 **C** 即可变成奶龙开始大笑，支持多人模式。

目前已上架创意工坊。

## 依赖

- 游戏版本 ≥ 0.107.1
- [STS2-RitsuLib](https://github.com/BAKAOLC/STS2-RitsuLib) ≥ 0.2.27

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
