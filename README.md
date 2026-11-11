# AssetRipperPatcher

给 [AssetRipper](https://github.com/AssetRipper/AssetRipper) 打补丁的导出增强工具。
在 AssetRipper 的导出流程上挂钩（[Harmony](https://github.com/pardeike/Harmony)），
修复它开源版导出的 Unity 工程里那些「能打开但跑不起来 / 显示不正确」的问题。

## 它解决什么

AssetRipper 把 Unity 游戏的资源「搬」成可编辑工程，但它是**数据搬运**，不是**语义还原**。
很多资源依赖编辑器在导入时重新计算的东西，搬运过来就是错的或缺失的。本项目补齐这些：

| 补丁 | 修复的问题 |
|---|---|
| **Shader** | 用内置反编译器（[ShaderRipper](ShaderRipper)）替代占位 shader，输出可编译的 HLSL/GLSL |
| **Sprite 网格** | Tight 网格缺失时，用移植自 Unity 的 `SpriteMeshGenerator` 重新生成 vertices/indices |
| **Sprite 轮廓** | 修复 `outline` / `physicsShape` 坐标系错位、`m_Pivot` 越界、Single/Multiple 模式误判 |
| **纯 alpha 纹理** | `Alpha8` 纹理导出成白底透明图，而不是全黑 |
| **旧版动态字体** | 清掉悬空的字体图集引用，让 Unity 按 `m_FontNames` 重新渲染 |
| **MonoBehaviour 容错** | 结构不匹配时宽松读取并重试，并对重复日志限流 |
| **TypeTree 容错** | 没有 C# 类接管的资源（如 `PlayerSettings`）类型树不匹配时逐字段恢复，不再整条作废 |
| **资源去重** | 内容完全相同的资源只导出一份，其余改写为指向它的引用（GUID 与 fileID 一致，不产生 Missing） |
| **原生插件 / 包清单** | 补齐 `Plugins/` 与 `Packages/manifest.json` 里缺失的条目 |

## 用法

```bash
# 命令行导出（会应用全部补丁）
AssetRipperPatcher <游戏路径> <输出目录>

# 只导出指定类型（类名匹配，支持 * 通配）
AssetRipperPatcher <游戏路径> <输出目录> --only Sprite,Texture2D
AssetRipperPatcher <游戏路径> <输出目录> --only Sprite*

# 不带参数则打开新的桌面界面（Photino 原生窗口）
AssetRipperPatcher

# 调试界面：固定端口 + 不自动开窗口，用浏览器访问
AssetRipperPatcher --web --port 5199 --no-open

# 回退到 AssetRipper 原版 Web UI
AssetRipperPatcher --legacy-ui
```

### 桌面界面

默认启动的是基于 **Photino.NET + Fluent 2** 的桌面界面：

- 原生窗口里跑 WebView2，后端是进程内的 Kestrel（只绑定 `127.0.0.1`）
- 前端是原生 HTML/CSS/JS + [Fluent 2 web components](https://github.com/microsoft/fluentui)，无框架
- 数据走 `/api/*` 的 JSON 端点，图片/音频/模型/字体走二进制端点

两者共用同一套 HTTP 端点，所以**任何界面问题都可以用 `--web` 在浏览器里复现**并用 DevTools 调试。

界面特性：

- **中英双语**：右上角按钮切换，文案由后端 `/api/text` 统一下发（选项显示名与说明同源），语言状态存在后端
- **原生文件选择器**：选择游戏目录/输出目录走 [NativeFileDialog](https://github.com/StArraySharp/NativeFileDialog) 的系统对话框，不依赖 WebView2 的文件 API
- **设置项说明**：每个下拉选项选中后会显示该值的作用（复刻原版 WebUI 的 option-description 行为）
- **窄窗口适配**：960×640 起所有视图可用，资源详情面板在窄屏下变为可关闭的下方抽屉

| 参数 | 作用 |
|---|---|
| `--web` | 不创建原生窗口，改用默认浏览器打开 |
| `--port <N>` | 固定监听端口（默认 0 = 随机空闲端口） |
| `--no-open` | 不自动打开窗口或浏览器（配合 `--web` 可当后台服务跑） |
| `--legacy-ui` | 回退到 AssetRipper 原版 Web UI |

WebView2 缺失时会自动降级到浏览器模式，不会静默崩溃。

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

依赖两个同级目录的仓库（都以 git submodule 引入，克隆后需要 `git submodule update --init --recursive`）：

- `AssetRipper/` —— 上游本体，pin 在 2.0.0
- `ShaderRipper/` —— shader 反编译器，pin 在 tag `v1.2.2`

## 结构

```
AssetRipperPatcher/
  Patches/          每个补丁一个文件，用 [PatchMethod] 注解声明目标
  Patching/         注解、注册器、确定性重载排序
  Sprites/          移植自 Unity 的 SpriteUtility + SpriteMeshGenerator
  Shader/           shader 导出器
  Api/              新桌面界面的 JSON 端点（资源/设置/日志/导入导出/文案/对话框）
  WebUi/            Photino 宿主 + wwwroot 前端资源
    wwwroot/        HTML/CSS/JS，lib/fluent.js 是打包好的 Fluent 2 组件
    ui/             前端构建入口（仅在升级 Fluent 时需要 npm install）
AssetRipperPatcher.Tools/   Roslyn 分析器：编译期校验 [PatchMethod] 注解
AssetRipperPatcher.Tests/   运行期绑定自检 + 分析器回归 + 移植算法单测（NUnit）
tools/UnityPackageHarvester/ 开发期工具：抓 Unity 官方包，生成 UnityPackageCatalog.Generated.cs
```

### 重建前端 bundle

`WebUi/wwwroot/lib/fluent.js` 是已提交的构建产物，只有升级 Fluent 时才需要重新打包：

```bash
cd AssetRipperPatcher/WebUi/ui
npm install
npm run build      # -> ../wwwroot/lib/fluent.js
```

### Unity 包目录

`Assets/Plugins/` 里的裸 DLL 往往其实是某个 UPM 包的产物（TextMeshPro、Newtonsoft…）。
把 DLL 换成 `manifest.json` 里的包依赖能避免 CS0433，但**包内程序集的 GUID 是
Unity 导入时随机生成的**——删掉 DLL 会让所有指向它的 `m_Script` 变成
`Missing (Mono Script)`，而且不可恢复。

解决办法是用包内 `.cs.meta` 的 GUID（打包时固定）替换掉引用。数据由
`tools/UnityPackageHarvester` 从 `https://packages.unity.com` 的 tarball 里解出来，
写成 `List<UnityPackage>`：

```csharp
// UnityPackageCatalog.Generated.cs（自动生成）
new UnityPackage
{
    name = "com.unity.textmeshpro",
    version = "3.0.9",
    ...
    asmdefs =
    [
        new AsmDef
        {
            name = "Unity.TextMeshPro",
            types =
            [
                new ScriptType { @namespace = "TMPro", name = "TextMeshPro",
                                 guid = UnityGuid.Parse("9541d86e2fd84c1d9990edf0852d74ab") },
            ],
        },
    ],
}
```

查询与哈希都复用 AssetRipper 自己的实现（`ScriptHashing`、`UnityGuid`），
两边永远一致。重新生成：

```bash
dotnet run --project tools/UnityPackageHarvester        # 全量（约 8 分钟）
dotnet run --project tools/UnityPackageHarvester -- --only com.unity.ugui@3.0.0-exp.4
```

`tools/unity-packages.txt` 是包名清单，支持 `name@version` 显式 pin
（UGUI 只有 exp 版，否则会被「只用正式版」的规则跳过）。

#### UGUI 2.0+ 已经自带 TextMeshPro

Unity 2023.2 起 `com.unity.ugui` **2.0+** 内置了 TextMeshPro。同时再装
`com.unity.textmeshpro` 会让两个包各带一份 `Unity.TextMeshPro.dll`，报 CS0433
「类型在多个程序集中存在」。所以：

- 注入 `manifest.json` 时，UGUI 版本 ≥ 2.0 会剔除 `com.unity.textmeshpro`
  （见 `UnityPackageCatalog.RemoveSuperseded`）；
- 「裸 DLL 换包」时，同一程序集有多家提供者会优先挑聚合包（`ChooseProvider`）；
- 登记这条关系的表是 `UnityPackageCatalog.MergeRules`，未来别的包合并时加一行即可。

#### 注册表里只有实验版时怎么处理

Unity 官方注册表有时只公开预发布版本——`com.unity.ugui` 就只列了 `3.0.0-exp.4`，
而它在真实工程里是编辑器内置模块：`2.0.0`、`1.0.0` 在公共源上实测都是 **404**。
把 `3.0.0-exp.4` 写进 `manifest.json` 会让工程装不上这个包。

`UnityPackageCatalog.TryGetStableVersion` 统一处理：版本形如 `x.0.0-预发布后缀`
且 `x ≥ 2` 时降到 `(x-1).0.0`。

| 输入 | 输出 |
|---|---|
| `3.0.0-exp.4` | `2.0.0` |
| `3.0.0-pre.15` | `2.0.0` |
| `2.0.0-pre.1` | `1.0.0` |
| `1.0.0-exp.1` | `1.0.0-exp.1`（不处理） |
| `3.0.9` / `3.1.0-exp.1` / `1.0.0` | 原样 |

`1.0.0` 是 Unity 内置包的通用占位版本（`com.unity.modules.*` 全是它），
降到 `0.0.0` 没有意义，所以不处理。

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

## 文档

- [JSON API](docs/json-api.md) —— 后端接口，也是脚本化调用的入口
- [第三方声明](docs/THIRD-PARTY-NOTICES.md) —— 链接/内嵌的组件与各自许可

## License

本项目以 [GPL-3.0](License) 发布，与上游 AssetRipper 一致。

<a name="shaderripper"></a>**关于 ShaderRipper。** shader 反编译链接了
[ShaderRipper](https://github.com/StArraySharp/ShaderRipper)（Apache-2.0）。
Apache-2.0 与 GPL-3.0 本身不兼容（前者的专利终止与赔偿条款构成 GPL-3.0 §7
禁止的附加限制），所以 ShaderRipper 附带一份
[GPL-3.0 链接例外](ShaderRipper/LICENSE-EXCEPTION)：允许把它与 GPL-3.0 作品
链接，并以 GPL-3.0 分发合并结果，而 ShaderRipper 自身仍是 Apache-2.0。

发布二进制时，`licenses/` 目录下会随附 GPL-3.0 全文、Apache-2.0 全文、
该例外条款以及第三方声明 —— 分发时请保留整个目录。
