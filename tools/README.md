# tools/ —— 构建依赖

编译脚本需要的两样东西都放进 `tools/`（都不进仓库，见 `.gitignore`）。

## 1. C# 编译器（csc.exe）

优先放这里：

```
tools\roslyn\csc.exe
```

拿不到的话，`build.ps1` 会依次找：

1. `-Csc` 参数指定的路径
2. `tools\roslyn\csc.exe`
3. PATH 里的 `csc.exe`
4. Visual Studio / Build Tools 里的 `MSBuild\Current\Bin\Roslyn\csc.exe`

最省事的办法是装 **Visual Studio Build Tools**（勾选 .NET 桌面开发），
或者从 NuGet 抓 `Microsoft.Net.Compilers` 把里面的 csc 拷出来。

## 2. BepInEx 5（Mono, Windows x64）

**必须是 Mono 版，不是 IL2CPP 版** —— SP2 是 Unity Mono 游戏。

- 下载：<https://github.com/BepInEx/BepInEx/releases>
- 找形如 `BepInEx_win_x64_5.4.23.5.zip` 的那个

两个用途：

- 编译时提供 `BepInEx.dll`（放 `tools\bepinex\` 或直接用游戏目录里已装好的）
- 安装时提供运行时（放 `tools\dl\`，`install.ps1` 会自动解压）

## 目录结构

```
tools/
├── roslyn/            csc.exe（可选，装了 VS 就不需要）
├── bepinex/           BepInEx\core\BepInEx.dll（可选）
└── dl/                BepInEx 发行包 zip（可选）
```
