# 奶龙布局与播放回归测试

无第三方测试包、无海克斯依赖。C# 控制台链接实际生产脚本，用测试替身隔离游戏与 Godot 原生调用。需要 .NET 10 SDK：

```sh
dotnet run --project tests/sts_nndx.RegressionTests.csproj -- /private/tmp/nndx-layout-cases.json
```

覆盖五个原版角色的边界高度、0.2～3 倍体型、播放中缩放、祖先节点缩放、边界高度回退，以及远端结束、旧回调和部分启动失败后的清理。

上述命令同时导出生产代码生成的布局参数。用 Godot 4.5.1 的实际节点变换再验证一次：

```sh
godot --headless --path tests/godot --script verify_layout.gd \
  --log-file /private/tmp/nndx-layout-native.log -- /private/tmp/nndx-layout-cases.json
```

这些检查不启动游戏、不部署 Mod，也不能代替游戏内的多人目视测试。
