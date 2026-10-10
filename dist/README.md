# dist/

这里是编译产物。

`Sp2Bridge.dll` 是**已编译好、可直接使用**的模组本体（约 43 KB），
随仓库一起分发 —— 因为它的编译依赖游戏本体的程序集
（`SimplePlanes 2_Data/Managed/*.dll`），那些是有版权的游戏文件，
不能放进 CI，所以 GitHub Actions 无法构建它。

## 安装

1. 先装 **BepInEx 5（Mono x64）** 到游戏根目录
   （会生成 `winhttp.dll`、`doorstop_config.ini`、`BepInEx/`）
2. 建目录 `<游戏>/BepInEx/plugins/N.E.K.O-SP2-Bridge/`
3. 把 `Sp2Bridge.dll` 放进去
4. 启动游戏，访问 <http://127.0.0.1:21580/ping>

## 自己重新编译

```powershell
.\build.ps1 -Install
```

需要 `tools/roslyn/` 里的编译器（见 [`../tools/README.md`](../tools/README.md)）
和本机的游戏程序集。加 `-Debug` 会额外编入 `/probe` 诊断端点。

> ⚠️ 重新编译前先**关闭游戏**，否则 DLL 被占用会写入失败。

## 两种构建的区别

| | Release（默认） | `-Debug` |
| --- | --- | --- |
| `/ping` `/state` `/aircraft` 等正常接口 | ✅ | ✅ |
| `/probe` 诊断端点 | ❌ 整块条件编译掉 | ✅ |
| 体积 | 43 KB | 47 KB |

发布给用户的一律用 Release 版；`/probe` 只是开发期用来核对游戏字段真名的。
