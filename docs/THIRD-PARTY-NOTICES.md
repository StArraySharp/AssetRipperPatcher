# Third-party notices

AssetRipperPatcher is distributed under the GNU General Public License,
version 3 (see `LICENSE-GPL-3.0.txt`). It links or embeds the components below,
each under its own license. Copies of the licenses that require one to travel
with a distribution are collected in this folder.

## Components linked into the program

| Component | Used for | License |
|---|---|---|
| [AssetRipper](https://github.com/AssetRipper/AssetRipper) | Unity asset import and project export, as a git submodule | GPL-3.0 |
| [ShaderRipper](https://github.com/StArraySharp/ShaderRipper) | ShaderLab decompilation from compiled shader blobs | Apache-2.0, **with a GPL-3.0 linking exception** |
| [Harmony](https://github.com/pardeike/Harmony) | Runtime patching of AssetRipper | MIT |
| [AsmResolver](https://github.com/Washi1337/AsmResolver) | IL inspection for the script-reference patches | MIT |
| [NativeFileDialog](https://github.com/StArraySharp/NativeFileDialog) | System file and folder pickers | MIT |
| Photino.NET / Photino.Native | Native window hosting the WebView2 shell | Apache-2.0 |
| [Microsoft.Web.WebView2](https://learn.microsoft.com/microsoft-edge/webview2/) | Embedded browser for the desktop UI | Microsoft terms |
| [Fluent UI Web Components](https://github.com/microsoft/fluentui) | UI component library (bundled into `wwwroot/lib/fluent.js`) | MIT |
| [K4os.Compression.LZ4](https://github.com/MiloszKrajewski/K4os.Compression.LZ4) | Unity LZ4 blob decompression | MIT |
| [Antlr4.Runtime.Standard](https://github.com/antlr/antlr4) | Runtime for ShaderRipper's grammars | BSD-3-Clause |
| D3DCompiler / DXIL (Microsoft, embedded by ShaderRipper) | Shader disassembly | Windows SDK / Apache-2.0 with LLVM exception |

## Notes on the ShaderRipper exception

ShaderRipper is Apache-2.0, which GPL-3.0 §7 does not permit a GPL-3.0 work to
link, because Apache-2.0's patent-termination and indemnity clauses count as
additional restrictions. Its copyright holder also holds the copyright on this
project, so `LICENSE-EXCEPTION-ShaderRipper-GPL.txt` grants permission to link
the two and distribute the result under GPL-3.0 without ShaderRipper itself
becoming GPL-3.0.

The exception covers ShaderRipper's own code only. The third-party components
ShaderRipper embeds keep their own licenses, listed above.

If you redistribute a build of AssetRipperPatcher, include this folder intact:
`LICENSE-GPL-3.0.txt` (GPL-3.0 §6), `LICENSE-ShaderRipper-Apache-2.0.txt` and
`LICENSE-EXCEPTION-ShaderRipper-GPL.txt` (Apache-2.0 §4(a)), and this file.