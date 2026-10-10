# N.E.K.O-SP2-Bridge

> 给《简单飞机2 SimplePlanes 2》做的**只读飞行遥测桥接**。
> 让 AI 副驾驶（猫娘）能读懂你正在飞的这架飞机，并陪着聊天给建议。

发布名：**Neko Copilot** · 版本 **0.1.0** · 许可证 **GPL-3.0**

配套的 N.E.K.O. 插件：
[`n.e.k.o_plugin_sp2_copilot`](https://github.com/LShangPiao/n.e.k.o_plugin_sp2_copilot)

---

## 这是什么

飞行时的数据都在游戏里：空速、高度、升降率、姿态、迎角、油量、发动机。
但它们散在 HUD 和各个面板上，玩家得自己拼起来看 ——
**而他还得同时把飞机开好**。

这个模组把这些数据整理成外部程序能读的形式，让 AI 能**基于真实数字**开口，
而不是瞎猜。

## 定位：只读

**不发控制指令、不动油门、不碰操纵面、不写存档。** 操纵杆始终在玩家手里。

这不是妥协，是设计承诺 —— 让玩家能安心挂着，不用担心后台程序替他做了什么。

> 模组只提供 `GET` 接口，代码里不存在任何修改游戏状态的路径。

## 现状：请先读这一段 ⚠️

**这个模组还没在实机上跑过。** 开发机上没有安装《简单飞机2》，
所以它能做到的是：

- ✅ 类型名来自社区已验证的开源 SP2 模组（`Assets.Scripts.Flight.FlightSceneScript`、
  `Assets.Scripts.Craft.AircraftScript` 等），不是凭空猜的
- ✅ 架构、线程模型、只读约束与姊妹项目
  `N.E.K.O-Juno-bridge` / `N.E.K.O-KSP-bridge` 完全一致（那两个已在实机验证）
- ✅ 所有取值都走反射 + 候选名，试不到就返回 `null`，**不会因为字段名不对而崩**
- ❌ 未经编译验证（缺游戏程序集）
- ❌ 空速、迎角、油量、起落架等字段的**确切名字**尚未确认

**要让它完整，只差一次实机运行**：装 DEBUG 版 → 打开 `/probe` →
把真实的字段名填进 `src/GameAccess.cs` 的候选名数组 → 重新编译。
详细步骤见 [`docs/TROUBLESHOOTING.md`](docs/TROUBLESHOOTING.md)。

在那之前，姿态、位置、速度、迎角、过载、离地高度这些**由几何关系推算**的字段
就已经能用了（推算出来的会在返回里用 `derived` 标出来）。

## 为什么用 BepInEx

SP2 还在抢先体验，**官方 modding 支持尚未上线**，没有任何官方扩展点可用。

社区（[SP2FreeCamera](https://github.com/EEEureka/SP2FreeCamera)、
[SP2 小地图插件](https://github.com/hahaha8459812/simpleplanes2-minimap-plugin) 等）
已经验证 BepInEx 5 **Mono** 在这款游戏上可用：

- SP2 是 Unity **Mono** 后端（不是 IL2CPP）→ 普通 C# 插件直接就能用
- BepInEx **不修改任何游戏原文件**，只是在目录里多放几个文件
- 卸载就是删掉那几个文件

⚠️ 必须下载 **BepInEx 5 Mono Windows x64** 版。IL2CPP 版在这游戏上不工作。

---

## 安装

需要 **BepInEx 5（Mono, x64）** + 本插件。

```powershell
# 1. 准备 BepInEx（见 tools/README.md）
#    下载 BepInEx_win_x64_5.4.23.5.zip 放到 tools\dl\

# 2. 装 BepInEx 运行时 + 插件
.\install.ps1 -GameDir "E:\Steam\steamapps\common\SimplePlanes 2"

# 或者只编译（不装）
.\build.ps1 -GameDir "E:\Steam\steamapps\common\SimplePlanes 2"
```

改代码之后的常规流程：

```powershell
.\build.ps1 -Install
```

### 验证

进游戏、开一架飞机，浏览器打开 <http://127.0.0.1:21580/ping>：

```json
{"ok":true,"protocol":1,"service":"SP2-Bridge","game":"SimplePlanes 2"}
```

---

## 接口

协议 v1 的完整定义见
[`docs/protocol.md`](docs/protocol.md)
（与插件侧的 `docs/bridge-protocol.md` 是同一份）。

| 路径 | 内容 |
| --- | --- |
| `GET /ping` | 连通性 |
| `GET /state` | 高度、速度、升降率、航向、姿态、迎角、G、油门、操纵输入 |
| `GET /aircraft` | 机体名、油量、发动机、起落架、襟翼、损伤 |
| `GET /navigation` | 位置、航向、风 |
| `GET /environment` | 时间、天气、气温、气压 |
| `GET /all` | 一次性拿全上面四份 |
| `GET /probe` | 字段探针（仅 DEBUG 构建） |

试试看：

```powershell
curl http://127.0.0.1:21580/state
```

---

## 配置

`BepInEx\plugins\N.E.K.O-SP2-Bridge\neko-sp2-bridge.ini`，
第一次启动会自动生成带注释的默认版本，**改完要重启游戏**。

| 键 | 默认 | 说明 |
| --- | --- | --- |
| `port` | `21580` | 只监听 127.0.0.1。**改这里要同时改 N.E.K.O. 插件的 `bridge_url`** |
| `poll_interval_ms` | `200` | 采集间隔，50-60000。调小更实时、更耗 CPU |
| `log_to_file` | `true` | 是否写 `sp2-bridge.log` |

---

## 已确认 / 待确认的字段

**已确认**（来自社区开源模组的实测，直接可用）

```
Assets.Scripts.Flight.FlightSceneScript  .Instance / .LocalPlayer / .FlightUI
Assets.Scripts.Craft.AircraftScript      .Position / .Velocity / .Controls
FlightScenePlayer                        .Aircraft / .Velocity / .Position
Controls                                 .Flaps / .Roll / .Pitch / .Yaw / .Brake
```

**由几何关系推算**（现在就能用，`derived` 里会标出来）

姿态（俯仰/滚转/航向）、迎角、侧滑、过载、离地高度、马赫数。

**待实机确认**（用 `/probe` 补齐）

空速、失速标志、接地标志、油量、起落架状态、发动机运转状态、天气与风。

---

## 已知限制

- **不提供 `limits`**：机体没给 Vne / 失速速度 / 最大 G，就不给 ——
  消费方因此不会误报超速。这是刻意的。
- **`navigation.target` 与 `nearest_airport` 目前是 null**：
  SP2 的航路点/机场接口尚未确认，宁缺勿假。
- **抢先体验期间 `Game.dll` 会变**：全部走反射，不会因为更新编译不过，
  最多少读几个字段。
- **多线程**：采集在主线程，HTTP 在后台线程，后台线程绝不调 Unity API。
  将来若要加控制接口，必须改成「入队 → 主线程执行」。

---

## 致谢

类型名与构建方式参考了这些先行者的公开代码：

- [EEEureka/SP2FreeCamera](https://github.com/EEEureka/SP2FreeCamera)
- [hahaha8459812/simpleplanes2-minimap-plugin](https://github.com/hahaha8459812/simpleplanes2-minimap-plugin)
  （尤其是它的 `docs/TECHNICAL_REFERENCE.md`）
- [sj414/SP2-Aero-Toolbox](https://github.com/sj414/SP2-Aero-Toolbox)

它们让这个模组不用从零开始逆向。

---

## 许可证

GNU General Public License v3.0，见 [LICENSE](LICENSE)。

星河拓航Studio (Galaxy Exploration Studio) · 猫娘计划
<https://xhth.top/>
