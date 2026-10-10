# 排错

## 第一步永远是这个

游戏开着、人在飞行场景里，浏览器打开：

```
http://127.0.0.1:21580/ping
```

看到 `{"ok":true,...,"service":"SP2-Bridge"}` 就说明模组活着。
看到「无法访问」再往下查。

## 连不上

按顺序确认：

1. **游戏正在运行，并且你已经进入飞行场景**（主菜单里没有遥测可读）。
2. `BepInEx\plugins\N.E.K.O-SP2-Bridge\Sp2Bridge.dll` 存在。
3. 游戏根目录有 `winhttp.dll` + `doorstop_config.ini` + `.doorstop_version`。
4. 端口没被别的程序占了。看 `sp2-bridge.log`：
   - `HTTP 服务未能启动，端口 ... 可能被占用` → 改 `neko-sp2-bridge.ini` 里的 `port`，
     **同时**改 N.E.K.O. 插件里的 `bridge_url`。
5. 用了 BepInEx 5 **Mono** 版 —— IL2CPP 版在这游戏上不工作。

## 有数据，但值是 null / 少字段

这是**预期内**的：SP2 还在抢先体验，很多字段名没被公开确认过。
本模组用候选名逐个试，试不到就写 `null`（绝不编数字）。

要补齐它们：

1. 编译 DEBUG 版：`.\build.ps1 -Debug -Install`
2. 进飞行场景，打开 `http://127.0.0.1:21580/probe`
3. 它会把 `FlightSceneScript` / `FlightScenePlayer` / `AircraftScript` /
   `Controls` / 发动机组件的全部成员名 + 当前值列出来
4. 找到对应的名字，填进 `src/GameAccess.cs` 里的候选名数组
5. 重新编译（不带 `-Debug`）

顺手把结果贴给开发者，能省掉别人重复一次。

## 数值看着不对

- **空速等于地速**：说明没读到原生空速字段，用的是速度模长（`derived.airspeed = true`）。
- **姿态/迎角/G 有值但和座舱仪表差一点**：这几个是算出来的，见 `docs/protocol.md`。
- **离地高度跳变**：地形采样在地形块边缘会失效，下一次采样就恢复。

## 日志文件

`BepInEx\plugins\N.E.K.O-SP2-Bridge\sp2-bridge.log`
超过 2 MB 自动截断。里面有插件启动、配置、采集异常的完整记录。

## 卸载

删掉这几个即可，游戏原文件没被碰过：

```
<游戏目录>\winhttp.dll
<游戏目录>\doorstop_config.ini
<游戏目录>\.doorstop_version
<游戏目录>\BepInEx\
```

（如果还装了别的 BepInEx 插件，只删
`BepInEx\plugins\N.E.K.O-SP2-Bridge\` 就行。）
