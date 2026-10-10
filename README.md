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

## 现状：已在实机跑通 ✅

**这个模组已经在《简单飞机2》v0.7.8.103 上实测通过**，链路是：

```
N.E.K.O. 插件  ←HTTP/JSON(21580)←  SP2-Bridge  ←反射←  SimplePlanes 2
```

实机确认的情况：

- ✅ BepInEx 5（Mono x64）能正常加载本模组，`/ping` 与 `/state` 均返回正常
- ✅ 四个关键类型名**全部确认无误**：`Assets.Scripts.Flight.FlightSceneScript`、
  `Assets.Scripts.Flight.FlightScenePlayer`、`Assets.Scripts.Craft.AircraftScript`、
  `Assets.Scripts.Craft.AircraftControls`
- ✅ 高度、空速、地速、马赫、姿态（俯仰/滚转/航向）、迎角、侧滑、过载、
  油量比例、经纬度、操纵面状态**全部读到真实值**
- ✅ Release 构建会把 `/probe` 诊断端点整块条件编译掉（已用字符串扫描确认）

### 实机踩到的坑（已修）

| 字段 | 之前 | 现在 | 原因 |
| --- | --- | --- | --- |
| `altitude_msl` | `-1005 m` | `4.21 m` | 原来拿 `Transform.position.y` 当海拔；SP2 的世界原点不在海平面 |
| `altitude_agl` | `-1257 m` | `1.58 m` | 同上 |
| `pitch` / `roll` / `heading` | 恒为 `0` | `1.24 / 0.17 / 44.8` | 原来只会由 `Transform` 推算，且候选名写成了不存在的 `Pitch`/`Roll` |
| 迎角 / 侧滑 | 推算值 | 读游戏字段 | 真名是 `AngleOfAttack` / `AngleOfSlip`（**不是** `Sideslip`） |
| 油量 | 读不到 | `100%` | 真名是 `FuelProportion`（0-1 比例，**不是** 0-100） |
| 地速 | 推算 | `GS` 字段 | 真名是 `GS` |
| 经纬度、过载、目标 | 未输出 | 已输出 | 真名 `Latitude`/`Longitude`/`VerticalG`/`TargetLocked` |

> **教训**：候选名写错**不会报错**，只会静默返回 `null`，然后被兜底逻辑填成 `0` ——
> 从外面看像是"读到了 0"。所以新增字段一定要先用 `/probe` 核对真名，
> 不要凭直觉写（`Pitch` vs `PitchAngle` 就是这么栽的）。

### 已知问题

- `aoa` / `sideslip` 在停机坪上偶尔给出异常大的角度（如 `147°`）。
  游戏字段本身在小速度下就不稳定，接入方对这两个值应保持怀疑。
- 老版本（如 1.5.x）的反射链可能不同 —— `docs/probe-*.json` 只覆盖了
  v0.7.8.103；其他版本请先跑 `/probe` 核对。

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
