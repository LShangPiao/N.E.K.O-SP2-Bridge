# refs/ —— 引用程序集

**这个目录默认不放任何文件。**

里面本该是从游戏目录拷出来的托管程序集，用来编译插件。它们属于游戏本体，
**受版权保护，不能进仓库**，所以这里只留一份说明。

## 它们从哪来

编译脚本 `build.ps1` 会**直接从你的游戏安装目录**读取，不需要你手动拷贝：

```
<游戏目录>\SimplePlanes 2_Data\Managed\
├── Game.dll              ← SP2 主代码，反射目标
├── Jundroo.Common.dll
├── FishNet.Runtime.dll   ← 联机
├── UnityEngine*.dll
└── mscorlib.dll / netstandard.dll / System*.dll
```

BepInEx.dll 则从 `<游戏目录>\BepInEx\core\` 或 `tools\bepinex\BepInEx\core\` 取。

## 什么时候需要手动拷

只有在**编译机没装游戏**时才需要：把上面那些 DLL 拷到本目录，
然后改 `build.ps1` 里的 `$Managed` 指向这里。

## 版本对应关系

SP2 还在抢先体验，`Game.dll` 会随游戏更新变化。本插件全部走**反射**，
不硬引用具体类型，所以通常不会因为游戏更新而编译不过 ——
最多是某些字段名变了、少读几个值（这时用 `/probe` 重新确认名字即可）。
