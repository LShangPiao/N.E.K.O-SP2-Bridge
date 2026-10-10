# 接口协议 v1（摘要）

完整定义在同系列插件仓库里：
**`n.e.k.o_plugin_sp2_copilot` → `docs/bridge-protocol.md`**

两份文档必须保持一致；改字段时两边一起改。

## 端点

| 路径 | 用途 | 本模组实现 |
| --- | --- | --- |
| `GET /ping` | 连通性探测 | ✅ |
| `GET /state` | 飞行状态、姿态、操纵输入 | ✅ |
| `GET /aircraft` | 机体、发动机、油量、构型 | ✅ |
| `GET /navigation` | 位置、航向、风 | ✅（`target` / `nearest_airport` 暂为 null） |
| `GET /environment` | 时间、天气、气温气压、风 | ✅（尽力而为） |
| `GET /all` | 一次性拿全四份 | ✅（本模组额外提供） |
| `GET /probe` | 字段探针 | ✅（仅 DEBUG 构建） |

只有 `GET`。**没有写接口。**

## 单位

| 量 | 单位 |
| --- | --- |
| 速度 | m/s |
| 高度 | m（`altitude_agl` 离地 / `altitude_msl` 海拔） |
| 升降率 | m/s，正 = 爬升 |
| 角度 | 度（`pitch` 正 = 抬头，`roll` 正 = 右倾，`heading` 0-360） |
| 油门、操纵输入 | 0-1 |
| 油量 | kg 与 0-100 |
| 过载 | G |

## 三条铁律

1. **取不到就写 `null`** —— 不写 `0`、不写 `-1`、不写空字符串。
2. **`NaN` / `Infinity` 写 `null`** —— JSON 里它们不是合法字面量。
3. **不编数字** —— 机体没给限速就**不给** `limits`，消费方因此不会误报超速。

## 关于 `derived`

有一部分值不是游戏直接给的，而是用已知量**推算**出来的。
这些会在返回里额外放一个 `derived` 块标出来，例如：

```json
"derived": {
  "airspeed": true,
  "attitude": true,
  "aoa": true,
  "g_force": true,
  "altitude_agl": true
}
```

当前可能被推算的量：

| 字段 | 推算方式 |
| --- | --- |
| `airspeed` | 读不到空速字段时，用速度矢量模长 |
| `mach` | 空速 ÷ 音速（音速按气温算，读不到气温按 288.15 K） |
| `pitch` / `roll` / `heading` | 由机体 Transform 的朝向算出 |
| `aoa` / `sideslip` | 由速度与机体朝向的夹角算出 |
| `g_force` | 由速度变化率推算法向过载（做了轻度平滑） |
| `altitude_agl` | 海拔减去地形高度（Unity Terrain 采样，兜底用射线） |
| `on_ground` | 读不到接地字段时，按「离地 ≤ 3 m 且几乎不上升」判定 |

消费方可以照常用这些字段，只是心里要有数：**它们是算出来的，不是量出来的**。
一旦在实机上确认了游戏原生的字段名字，应当改成直接读取，并把 `derived` 标记去掉。
