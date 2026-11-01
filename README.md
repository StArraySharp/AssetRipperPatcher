# AssetRipperPatcher

给 [AssetRipper](https://github.com/AssetRipper/AssetRipper) 打补丁的导出增强工具。
在 AssetRipper 的导出流程上挂钩（[Harmony](https://github.com/pardeike/Harmony)），
修复它开源版导出的 Unity 工程里那些「能打开但跑不起来 / 显示不正确」的问题。

## 它解决什么

AssetRipper 把 Unity 游戏的资源「搬」成可编辑工程，但它是**数据搬运**，不是**语义还原**。
很多资源依赖编辑器在导入时重新计算的东西，搬运过来就是错的或缺失的。本项目补齐这些：

| 补丁 | 修复的问题 |
|---|---|
| **Shader** | 用内置反编译器（[ShaderRipper](../ShaderRipper)）替代占位 shader，输出可编译的 HLSL/GLSL |
| **Sprite 网格** | Tight 网格缺失时，用移植自 Unity 的 `SpriteMeshGenerator` 重新生成 vertices/indices |
| **Sprite 轮廓** | 修复 `outline` / `physicsShape` 坐标系错位、`m_Pivot` 越界、Single/Multiple 模式误判 |
| **纯 alpha 纹理** | `Alpha8` 纹理导出成白底透明图，而不是全黑 |
| **旧版动态字体** | 清掉悬空的字体图集引用，让 Unity 按 `m_FontNames` 重新渲染 |
| **MonoBehaviour 容错** | 结构不匹配时宽松读取并重试，并对重复日志限流 |
| **原生插件 / 包清单** | 补齐 `Plugins/` 与 `Packages/manifest.json` 里缺失的条目 |

## 用法

```bash
# 命令行导出（会应用全部补丁）
AssetRipperPatcher <游戏路径> <输出目录>

# 只导出指定类型（类名匹配，支持 * 通配）
AssetRipperPatcher <游戏路径> <输出目录> --only Sprite,Texture2D
AssetRipperPatcher <游戏路径> <输出目录> --only Sprite*

# 不带参数则启动 AssetRipper 的 Web UI
AssetRipperPatcher
```

导出配置读程序目录下的 `AssetRipper.Settings.json`，命令行与 Web UI 共用同一份。
**文件不存在时会自动创建**，并写入一组面向「导出可编辑 Unity 工程」的默认值：

| 设置项 | 默认值 | 原因 |
|---|---|---|
| `SpriteExportMode` | `Native` | 把网格/轮廓写进 `.meta`；`Yaml` 模式只产出 `.asset`，importer 里 outline 为空 |
| `TextExportMode` | `Txt` | 文本资源导出成可直接阅读的纯文本，而不是 `.bytes` |
| `SaveSettingsToDisk` | `true` | 之后每次启动沿用同一份配置 |

文件已存在时不会覆盖，尊重手动修改。

## 构建

```bash
dotnet build AssetRipperPatcher.slnx
dotnet test  AssetRipperPatcher.slnx
```

依赖两个同级仓库：

- `AssetRipper/` —— git submodule，上游本体
- `../ShaderRipper/` —— shader 反编译器

## 结构

```
AssetRipperPatcher/
  Patches/          每个补丁一个文件，用 [PatchMethod] 注解声明目标
  Patching/         注解、注册器、确定性重载排序
  Sprites/          移植自 Unity 的 SpriteUtility + SpriteMeshGenerator
  Shader/           shader 导出器
AssetRipperPatcher.Tools/   Roslyn 分析器：编译期校验 [PatchMethod] 注解
AssetRipperPatcher.Tests/   运行期绑定自检 + 分析器回归 + 移植算法单测
```

### 补丁声明方式

补丁目标用注解声明，编译期由分析器校验、运行期由注册器绑定，**两侧共用同一份声明**：

```csharp
[PatchMethod(typeof(ExportHandler), nameof(ExportHandler.Export), PatchType = PatchType.Postfix)]
public static void DoSomething(GameData gameData, string outputPath, FileSystem fileSystem) { ... }
```

分析器会检查目标是否存在、重载索引是否越界、参数注入是否符合 Harmony 规则
（HRP001/002/003/…）。绑定失败在启动时直接抛异常，不静默跳过。

## 说明

本项目为个人使用而维护，不保证对任意游戏通用。补丁的坐标/数值换算都基于对
具体游戏数据的逆向分析，遇到新游戏可能需要重新校准。

## License

见 [License](License)。上游 AssetRipper 的许可同样适用。
