# 奶龙布局与播放回归测试

无第三方测试包、无海克斯依赖。C# 控制台链接实际生产脚本，用测试替身隔离游戏与 Godot 原生调用。需要 .NET 10 SDK：

```sh
dotnet run --project tests/sts_nndx.RegressionTests.csproj -- /private/tmp/nndx-layout-cases.json
```

覆盖五个原版角色的边界高度、0.2～3 倍体型、播放中缩放、祖先节点缩放、边界高度回退，以及远端结束、旧回调和部分启动失败后的清理。也覆盖快捷键修饰键冲突语义、原版忽略修饰键与 Godot 组合键的区别、本地输入配置保存 / 清除 / 重启恢复和损坏文件保留。输入控件还验证键盘录制、取消、清除、失焦退出以及拒绝绑定后的显示回退。验证旧配置中的手柄字段会被忽略，并且不会注册手柄动作或显示手柄设置。

多人头像检查包括四张头像替换、其他玩家不受影响、各结束路径恢复、界面重建、旧回调隔离、其他 Mod 改图保护、消息编解码与异常数据、房主转发、重复/乱序/跨房间消息拒绝，以及 120 次切换加 2400 次冷却内连按的资源复用和节点清理。

手动多人验收：

1. 两端安装同一新版，开始战斗后检查自己的头像及队友端对应头像一致；停止后都恢复。
2. 多次播放检查 face1/2/3/4，并等待一次自然结束；确认名字、血条与其他玩家头像不变。
3. 两端交替连按快捷键，停止操作后确认头像与角色都能恢复、卡牌仍可正常使用。
4. 播放期间结束战斗或死亡，进入下一场后确认没有头像残留。

上述命令同时导出生产代码生成的布局参数。用 Godot 4.5.1 的实际节点变换再验证一次：

```sh
godot --headless --path tests/godot --script verify_layout.gd \
  --log-file /private/tmp/nndx-layout-native.log -- /private/tmp/nndx-layout-cases.json
```

检查导出正式包中的四张头像是否能由 Godot 实际加载，以及切换纹理时保持 48×48 UI 尺寸：

```sh
godot --headless --path tests/godot --script verify_portraits.gd \
  --log-file /private/tmp/nndx-portrait-assets-native.log -- "$PWD/artifacts/release/sts_nndx.pck"
```

这些检查不启动游戏、不部署 Mod，也不能代替游戏内的多人目视测试。
