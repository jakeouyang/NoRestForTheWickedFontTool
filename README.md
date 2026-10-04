# No Rest for the Wicked Font Tool

[English](#english) | [简体中文](#简体中文)

![WickedFontTool](docs/images/wicked-font-tool.png)

## 简体中文

《恶意不息》Windows 版简体中文字体替换工具。选择游戏根目录和 `.ttf` / `.otf` 字体文件，即可一键安装或还原；发布包自带 .NET 运行时，无需 Python、Unity 编辑器或额外依赖。

### 使用方法

1. 从 [Releases](https://github.com/jakeouyang/NoRestForTheWickedFontTool/releases) 下载并解压最新版。
2. 退出游戏，运行 `WickedFontTool.exe`。
3. 选择包含 `NoRestForTheWicked.exe` 的游戏目录，以及要使用的字体文件。
4. 点击“生成并安装”。需要恢复时，点击“还原字体”。

工具会校验游戏目录与字体文件。字体也可以拖进字体输入框。若游戏目录无写入权限，请按日志提示以管理员身份重新运行。

### 游戏更新与安全性

- 扫描 `Data` 目录以及 `StreamingAssets` 内实际为 UnityFS 的资源包，不依赖资源包的文件名或扩展名。因此游戏更新后资源包改名时通常仍可重新定位。
- 识别序列化 Font 对象中的 NotoSerif SC / TC / JP / KR 的 Regular / Bold，共 8 个目标字体；同时兼容它们分散在不同资源文件中的情况。
- 找不到完整目标、遇到无法解析的资源，或资源结构不兼容时会停止安装并保存 `scan.json` 诊断，而不是写入不完整的字体替换。
- 修改前保存以 SHA-256 标识的原始备份；写入采用事务处理，完成后校验目标字体与所有非目标资源。中断后下次运行会尝试恢复未完成事务。
- “还原字体”只接受仍与本工具补丁摘要完全一致的文件，不会用旧备份覆盖游戏更新或其他 Mod 的改动。

备份、运行记录与诊断文件保存在游戏根目录 `.WickedFontTool` 下。请保留该目录，直到不再需要还原功能。

> 新字体需要覆盖游戏实际使用的汉字。字体的字距、布局和最终游戏显示效果仍应在游戏内确认。

### 已知限制

工具针对当前可识别的 Unity 资源结构实现。未来游戏版本若改用新的字体族、静态图集、加密资源或不兼容的序列化格式，可能需要更新工具才能适配。

## English

A Windows font replacement tool for *No Rest for the Wicked*. Select the game directory and a `.ttf` / `.otf` font, then install or restore with one click. The release is self-contained and needs no Python, Unity editor, or separate runtime installation.

### Usage

1. Download and extract the latest package from [Releases](https://github.com/jakeouyang/NoRestForTheWickedFontTool/releases).
2. Exit the game and run `WickedFontTool.exe`.
3. Select the folder that contains `NoRestForTheWicked.exe`, then select your font file.
4. Choose **Generate and Install**. Choose **Restore Fonts** to restore files managed by this tool.

The tool validates both the game directory and font. You can also drag a font into the font field. If the game folder is not writable, rerun the tool as administrator as indicated by the log.

### Updates and safeguards

- Scans `Data` and UnityFS containers under `StreamingAssets` by content rather than package names or extensions, so renamed bundles after a game update can usually still be found.
- Locates serialized NotoSerif SC / TC / JP / KR Regular and Bold font objects, including games where those eight targets are split across different resources.
- Stops before installation and writes a `scan.json` diagnostic if targets are incomplete or a resource cannot be read safely.
- Uses SHA-256-addressed originals and a transactional write path. It verifies target fonts and all non-target data after writing, and recovers unfinished transactions on the next launch.
- Restore only writes files whose current patch hash exactly matches this tool's record, protecting game updates and changes from other mods.

Backups, logs, and diagnostics live in `.WickedFontTool` inside the game directory. Keep that directory while restore capability is needed.

> Your selected font must contain the characters used by the game. In-game rendering, spacing, and layout still need in-game verification.

### Limitations

This tool supports the Unity resource layout it can currently identify. A future version using a different font family, static atlas, encrypted assets, or an incompatible serialized format may require a tool update.

## Build from source

Clone with submodules, then build with the .NET 8 SDK:

```powershell
git clone --recurse-submodules https://github.com/jakeouyang/NoRestForTheWickedFontTool.git
cd NoRestForTheWickedFontTool
dotnet build WickedFontTool.csproj -c Release
```

The GitHub Actions release workflow publishes a self-contained `win-x64` package when a tag matching `v*` is pushed. See [THIRD-PARTY.md](THIRD-PARTY.md) for dependency and asset notices.
