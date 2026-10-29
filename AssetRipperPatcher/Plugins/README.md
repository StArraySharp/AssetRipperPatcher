# Plugins

解包 Unity 游戏后直接复制到目标工程 `Assets/Editor` 文件夹的编辑器脚本。

- 本文件夹**不参与** patcher 的编译与语法分析（csproj 已排除）。
- 构建和发布时整个文件夹原样复制到输出目录的 `Plugins/` 下。
- 里面的 `.cs` 面向 Unity Editor（`UnityEditor` 命名空间），由 Unity 自己编译；
  不要让任何 .NET 工程引用它们。
