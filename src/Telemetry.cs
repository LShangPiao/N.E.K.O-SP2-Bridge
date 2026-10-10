// N.E.K.O-SP2-Bridge — 遥测采集
//
// 线程模型（和 N.E.K.O-Juno-bridge 一致，这是踩过坑的结论）
//   主线程：Capture() 把所有 Unity 数据读出来，渲染成 JSON 字符串
//   后台线程：HTTP 服务只读这些**已经渲染好的字符串**，绝不碰 Unity 对象
//   在后台线程调 Unity API 会直接把游戏搞崩。
//
// 单位（协议 v1，见 ../docs/protocol.md）
//   速度 m/s · 高度 m · 升降率 m/s（正=爬升）· 角度 度 · 油门/操纵 0-1
//
// 诚实原则
//   读不到就写 null，绝不写 0 / -1 / ""。
//   凡是用已知量**推算**出来的（空速、G、离地高度、是否接地），
//   在返回里额外放一个 derived 块标出来，让消费方知道这不是游戏直接给的。

using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace NekoSp2Bridge
{
    public static class Telemetry
    {
        // ── 渲染好的快照（HTTP 线程只读这些字符串）────────────────────────
        private static string stateJson = NotReady("尚未采集");
        private static string aircraftJson = NotReady("尚未采集");
        private static string navigationJson = NotReady("尚未采集");
        private static string environmentJson = NotReady("尚未采集");
        private static string pingJson = "{\"ok\":true,\"protocol\":1,\"service\":\"SP2-Bridge\",\"game\":\"SimplePlanes 2\"}";

        private static readonly object Gate = new object();
        private static long captureCount;
        private static string lastCaptureError;

        // ── G 值推算用的上一帧状态 ────────────────────────────────────────
        private static Vector3 prevVelocity;
        private static float prevTime;
        private static bool hasPrev;
        private static float smoothedG = 1.0f;

        // ── 地形缓存 ─────────────────────────────────────────────────────
        private static Terrain[] terrains = new Terrain[0];
        private static float terrainsRefreshedAt = -999f;

        // ── 发动机列表缓存 ───────────────────────────────────────────────
        private static MonoBehaviour[] engineComponents = new MonoBehaviour[0];
        private static object engineOwner;
        private static float enginesRefreshedAt = -999f;

        private const float Gravity = 9.80665f;
        private const float SeaLevelSpeedOfSound = 340.29f;
        private const float RefineInterval = 1.0f;

        public static string ProtocolVersionJson
        {
            get { return pingJson; }
        }

        public static string StateJson
        {
            get { lock (Gate) { return stateJson; } }
        }

        public static string AircraftJson
        {
            get { lock (Gate) { return aircraftJson; } }
        }

        public static string NavigationJson
        {
            get { lock (Gate) { return navigationJson; } }
        }

        public static string EnvironmentJson
        {
            get { lock (Gate) { return environmentJson; } }
        }

        public static long CaptureCount
        {
            get { return captureCount; }
        }

        public static string LastError
        {
            get { return lastCaptureError; }
        }

        private static string NotReady(string message)
        {
            JsonObj obj = new JsonObj();
            obj.Set("ok", false);
            obj.Set("protocol", 1);
            obj.Set("error", message);
            return BridgeJson.Serialize(obj);
        }

        /// <summary>主线程调用：采一次快照。</summary>
        public static void Capture()
        {
            try
            {
                JsonObj state = BuildState();
                JsonObj aircraft = BuildAircraft();
                JsonObj navigation = BuildNavigation();
                JsonObj environment = BuildEnvironment();

                lock (Gate)
                {
                    stateJson = BridgeJson.Serialize(state);
                    aircraftJson = BridgeJson.Serialize(aircraft);
                    navigationJson = BridgeJson.Serialize(navigation);
                    environmentJson = BridgeJson.Serialize(environment);
                    captureCount++;
                    lastCaptureError = null;
                }
            }
            catch (Exception ex)
            {
                lastCaptureError = BridgeLog.Safe(ex);
                BridgeLog.Error("采集失败: " + lastCaptureError);
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // /state
        // ══════════════════════════════════════════════════════════════════

        private static JsonObj BuildState()
        {
            JsonObj root = Begin();
            JsonObj derived = new JsonObj();

            if (GameAccess.Scene == null)
            {
                root.Set("scene", "menu");
                root.Set("in_world", false);
                root.Set("aircraft", new JsonObj());
                root.Set("flight", new JsonObj());
                root.Set("controls", new JsonObj());
                root.Set("derived", derived);
                return root;
            }

            root.Set("scene", "flight");
            root.Set("in_world", true);

            JsonObj flight = new JsonObj();
            JsonObj controls = new JsonObj();

            MonoBehaviour behaviour = GameAccess.Aircraft as MonoBehaviour;
            Transform transform = GameAccess.Transform;

            object aircraft = GameAccess.Aircraft;
            object player = GameAccess.Player;

            // ── 位置与速度 ────────────────────────────────────────────────
            Vector3? velocity = FirstVector(aircraft, player);
            Vector3 position = transform != null ? transform.position : new Vector3();
            bool hasPosition = transform != null;

            if (hasPosition)
            {
                flight.Set("altitude_msl", position.y);
            }

            float? agl = null;
            float? terrainHeight = null;
            if (hasPosition)
            {
                float ground = SampleTerrainHeight(position);
                if (!float.IsNaN(ground))
                {
                    terrainHeight = ground;
                    agl = position.y - ground;
                    flight.Set("altitude_agl", agl.Value);
                    flight.Set("terrain_height", ground);
                    derived.Set("altitude_agl", true);
                }
            }

            if (velocity.HasValue)
            {
                Vector3 v = velocity.Value;
                flight.Set("ground_speed", v.magnitude);
                flight.Set("vertical_speed", v.y);
            }

            // ── 空速：优先读游戏字段，读不到才用速度模长推算 ────────────────
            float? airspeed = Reflect.GetFloat(aircraft, GameAccess.AirspeedNames);
            bool airspeedDerived = false;
            if (!airspeed.HasValue && velocity.HasValue)
            {
                airspeed = velocity.Value.magnitude;
                airspeedDerived = true;
            }
            if (airspeed.HasValue)
            {
                flight.SetIfNotNull("airspeed", airspeed.Value);
                if (airspeedDerived)
                {
                    derived.Set("airspeed", true);
                }
            }

            float? mach = Reflect.GetFloat(aircraft, GameAccess.MachNames);
            if (!mach.HasValue && airspeed.HasValue)
            {
                float sound = SpeedOfSound(Reflect.GetFloat(GameAccess.Scene, "Temperature", "AirTemperature"));
                mach = airspeed.Value / sound;
                derived.Set("mach", true);
            }
            flight.SetIfNotNull("mach", mach);

            // ── 姿态 ─────────────────────────────────────────────────────
            if (transform != null)
            {
                Vector3 forward = transform.forward;
                Vector3 right = transform.right;
                Vector3 up = transform.up;

                float pitch = Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;
                // 右倾为正：右翼下沉时 right.y 变负，所以取反。
                float roll = -Mathf.Atan2(right.y, up.y) * Mathf.Rad2Deg;
                float heading = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
                if (heading < 0f)
                {
                    heading += 360f;
                }

                flight.Set("pitch", pitch);
                flight.Set("roll", roll);
                flight.Set("heading", heading);
                derived.Set("attitude", true);
            }

            // ── 迎角 / 侧滑：由速度与机体朝向算出来 ────────────────────────
            if (transform != null && velocity.HasValue)
            {
                Vector3 local = transform.InverseTransformDirection(velocity.Value);
                if (local.sqrMagnitude > 0.01f)
                {
                    float forwardAxis = local.z;
                    if (Mathf.Abs(forwardAxis) > 0.001f)
                    {
                        float aoa = Mathf.Atan2(-local.y, forwardAxis) * Mathf.Rad2Deg;
                        float sideslip = Mathf.Atan2(local.x, forwardAxis) * Mathf.Rad2Deg;
                        flight.Set("aoa", aoa);
                        flight.Set("sideslip", sideslip);
                        derived.Set("aoa", true);
                        derived.Set("sideslip", true);
                    }
                }
            }

            // ── 过载 G：由速度变化率推算（法向分量）────────────────────────
            float? gField = Reflect.GetFloat(aircraft, GameAccess.GForceNames);
            if (gField.HasValue)
            {
                flight.Set("g_force", gField.Value);
            }
            else
            {
                float? g = EstimateG(velocity, transform);
                if (g.HasValue)
                {
                    flight.Set("g_force", g.Value);
                    derived.Set("g_force", true);
                }
            }

            // ── 失速 / 接地 / 坠毁 ────────────────────────────────────────
            bool? stalled = Reflect.GetBool(aircraft, GameAccess.StalledNames);
            if (stalled.HasValue)
            {
                flight.Set("stalled", stalled.Value);
            }
            bool? stallWarn = Reflect.GetBool(aircraft, GameAccess.StallWarnNames);
            if (stallWarn.HasValue)
            {
                flight.Set("stall_warning", stallWarn.Value);
            }
            bool? crashed = Reflect.GetBool(aircraft, GameAccess.CrashedNames);
            if (crashed.HasValue)
            {
                flight.Set("crashed", crashed.Value);
            }

            bool? onGround = Reflect.GetBool(aircraft, GameAccess.OnGroundNames);
            if (!onGround.HasValue)
            {
                onGround = FirstBool(player, GameAccess.OnGroundNames);
            }
            if (!onGround.HasValue && agl.HasValue && velocity.HasValue)
            {
                // 兜底：离地不到 3 米且几乎不上升，就当在地面。
                onGround = agl.Value <= 3.0f && velocity.Value.y < 1.5f;
                derived.Set("on_ground", true);
            }
            if (onGround.HasValue)
            {
                flight.Set("on_ground", onGround.Value);
            }

            // ── 操纵输入 ─────────────────────────────────────────────────
            object controlsObject = GameAccess.Controls;
            if (controlsObject != null)
            {
                SetControlAxis(controls, "throttle", controlsObject,
                    "Throttle", "ThrottleInput", "ThrottlePosition");
                SetControlAxis(controls, "pitch_input", controlsObject, "Pitch", "PitchInput", "Elevator");
                SetControlAxis(controls, "roll_input", controlsObject, "Roll", "RollInput", "Aileron");
                SetControlAxis(controls, "yaw_input", controlsObject, "Yaw", "YawInput", "Rudder");
                SetControlAxis(controls, "brake", controlsObject, "Brake", "BrakeInput");
                SetControlAxis(controls, "flaps", controlsObject, "Flaps", "Flap", "FlapsInput");
                SetControlAxis(controls, "airbrake", controlsObject, "Airbrake", "AirBrake", "AirbrakeInput");
                SetControlAxis(controls, "spoiler", controlsObject, "Spoiler", "Spoilers");
                SetControlAxis(controls, "trim", controlsObject, "Trim", "TrimInput", "PitchTrim");
                SetControlAxis(controls, "vtol", controlsObject, "Vtol", "VTOL");

                bool? gear = Reflect.GetBool(controlsObject, GameAccess.GearDownNames);
                if (gear.HasValue)
                {
                    controls.Set("gear_down", gear.Value);
                }
                bool? autopilot = Reflect.GetBool(controlsObject, "Autopilot", "AutopilotEnabled", "IsAutopilot");
                if (autopilot.HasValue)
                {
                    controls.Set("autopilot", autopilot.Value);
                }
                string apMode = Reflect.GetString(controlsObject, "AutopilotMode", "ApMode");
                if (apMode != null)
                {
                    controls.Set("autopilot_mode", apMode);
                }
            }
            if (controlsObject == null)
            {
                bool? gear = Reflect.GetBool(aircraft, GameAccess.GearDownNames);
                if (gear.HasValue)
                {
                    controls.Set("gear_down", gear.Value);
                }
                float? throttle = Reflect.GetFloat(aircraft, "Throttle", "ThrottleInput");
                if (throttle.HasValue)
                {
                    controls.Set("throttle", throttle.Value);
                }
                float? flaps = Reflect.GetFloat(aircraft, "Flaps", "Flap");
                if (flaps.HasValue)
                {
                    controls.Set("flaps", flaps.Value);
                }
            }

            // ── 机体摘要 ─────────────────────────────────────────────────
            JsonObj summary = new JsonObj();
            string craftName = Reflect.GetString(aircraft, GameAccess.NameNames);
            if (craftName == null && behaviour != null)
            {
                craftName = behaviour.name;
            }
            if (craftName != null)
            {
                summary.Set("name", craftName);
            }
            summary.Set("type", GuessType(behaviour));
            root.Set("aircraft", summary);

            float? missionTime = Reflect.GetFloat(GameAccess.Scene, "MissionTime", "ElapsedTime", "TimeSinceStart", "Time");
            if (missionTime.HasValue)
            {
                root.Set("mission_time", missionTime.Value);
            }

            root.Set("flight", flight);
            root.Set("controls", controls);
            root.Set("derived", derived);
            return root;
        }

        // ══════════════════════════════════════════════════════════════════
        // /aircraft
        // ══════════════════════════════════════════════════════════════════

        private static JsonObj BuildAircraft()
        {
            JsonObj root = Begin();
            object aircraft = GameAccess.Aircraft;
            if (aircraft == null)
            {
                root.Set("ok", false);
                root.Set("error", GameAccess.InFlightScene ? "当前没有驾驶载具" : "不在飞行场景");
                return root;
            }

            MonoBehaviour behaviour = aircraft as MonoBehaviour;
            string craftName = Reflect.GetString(aircraft, GameAccess.NameNames);
            if (craftName == null && behaviour != null)
            {
                craftName = behaviour.name;
            }
            root.SetIfNotNull("name", craftName);
            root.Set("type", GuessType(behaviour));

            float? mass = Reflect.GetFloat(aircraft, GameAccess.MassNames);
            root.SetIfNotNull("mass_kg", mass);

            // ── 油量 ─────────────────────────────────────────────────────
            JsonObj fuel = new JsonObj();
            float? fuelCurrent = Reflect.GetFloat(aircraft, GameAccess.FuelNames);
            float? fuelCapacity = Reflect.GetFloat(aircraft, GameAccess.FuelCapacityNames);
            float? fuelPercent = Reflect.GetFloat(aircraft, GameAccess.FuelPercentNames);
            if (!fuelPercent.HasValue && fuelCurrent.HasValue && fuelCapacity.HasValue && fuelCapacity.Value > 0f)
            {
                fuelPercent = fuelCurrent.Value / fuelCapacity.Value * 100f;
            }
            fuel.SetIfNotNull("current_kg", fuelCurrent);
            fuel.SetIfNotNull("capacity_kg", fuelCapacity);
            fuel.SetIfNotNull("percent", fuelPercent);
            root.Set("fuel", fuel);

            // ── 发动机 ───────────────────────────────────────────────────
            List<JsonObj> engines = BuildEngines(behaviour);
            root.Set("engines", engines);

            // ── 起落架 / 操纵面 ──────────────────────────────────────────
            JsonObj gear = new JsonObj();
            bool? gearDown = Reflect.GetBool(GameAccess.Controls, GameAccess.GearDownNames);
            if (!gearDown.HasValue)
            {
                gearDown = Reflect.GetBool(aircraft, GameAccess.GearDownNames);
            }
            if (gearDown.HasValue)
            {
                gear.Set("down", gearDown.Value);
                gear.Set("state", gearDown.Value ? "down" : "up");
            }
            root.Set("gear", gear);

            JsonObj surfaces = new JsonObj();
            float? flaps = Reflect.GetFloat(GameAccess.Controls, "Flaps", "Flap");
            surfaces.SetIfNotNull("flaps_percent", ToPercent(flaps));
            float? airbrake = Reflect.GetFloat(GameAccess.Controls, "Airbrake", "AirBrake");
            surfaces.SetIfNotNull("airbrake_percent", ToPercent(airbrake));
            float? spoiler = Reflect.GetFloat(GameAccess.Controls, "Spoiler", "Spoilers");
            surfaces.SetIfNotNull("spoiler_percent", ToPercent(spoiler));
            root.Set("surfaces", surfaces);

            // ── 损伤 ─────────────────────────────────────────────────────
            JsonObj damage = new JsonObj();
            float? integrity = Reflect.GetFloat(aircraft, GameAccess.IntegrityNames);
            damage.SetIfNotNull("integrity_percent", integrity);
            bool? onFire = Reflect.GetBool(aircraft, "OnFire", "IsOnFire", "Burning", "IsBurning");
            damage.SetIfNotNull("on_fire", onFire);
            root.Set("damage", damage);

            // ── 零件数 ───────────────────────────────────────────────────
            float? partCount = Reflect.GetFloat(aircraft, "PartCount", "PartsCount", "NumberOfParts");
            if (partCount.HasValue)
            {
                root.Set("part_count", (int)partCount.Value);
            }
            else if (behaviour != null)
            {
                int counted = CountParts(behaviour);
                if (counted > 0)
                {
                    root.Set("part_count", counted);
                }
            }

            // ── 限速：机体没给就**不给**，绝不编 ────────────────────────────
            // 协议规定：消费方只在 limits 存在时才做超速告警。
            return root;
        }

        // ══════════════════════════════════════════════════════════════════
        // /navigation
        // ══════════════════════════════════════════════════════════════════

        private static JsonObj BuildNavigation()
        {
            JsonObj root = Begin();
            Transform transform = GameAccess.Transform;
            if (transform == null)
            {
                root.Set("ok", false);
                root.Set("error", "不在飞行场景");
                return root;
            }

            Vector3 position = transform.position;
            JsonObj pos = new JsonObj();
            pos.Set("x", position.x);
            pos.Set("y", position.y);
            pos.Set("z", position.z);
            root.Set("position", pos);

            float heading = Mathf.Atan2(transform.forward.x, transform.forward.z) * Mathf.Rad2Deg;
            if (heading < 0f)
            {
                heading += 360f;
            }
            root.Set("heading", heading);

            // 目标点与最近机场：SP2 的内部接口尚未确认，宁缺勿假。
            // 一旦在实机上确认了类型名，在这里补上即可（见 docs/TROUBLESHOOTING.md）。
            root.Set("target", null);
            root.Set("nearest_airport", null);

            JsonObj wind = BuildWind();
            if (wind.Count > 0)
            {
                root.Set("wind", wind);
            }
            return root;
        }

        // ══════════════════════════════════════════════════════════════════
        // /environment
        // ══════════════════════════════════════════════════════════════════

        private static JsonObj BuildEnvironment()
        {
            JsonObj root = Begin();
            object scene = GameAccess.Scene;
            if (scene == null)
            {
                root.Set("ok", false);
                root.Set("error", "不在飞行场景");
                return root;
            }

            string tod = Reflect.GetString(scene, "TimeOfDay", "TimeOfDayText", "ClockText");
            root.SetIfNotNull("time_of_day", tod);

            string weather = Reflect.GetString(scene, "Weather", "WeatherName", "CurrentWeather");
            root.SetIfNotNull("weather", weather);

            float? temp = Reflect.GetFloat(scene, "Temperature", "AirTemperature", "AmbientTemperature");
            root.SetIfNotNull("temperature_c", temp);

            float? pressure = Reflect.GetFloat(scene, "Pressure", "AirPressure", "BarometricPressure");
            root.SetIfNotNull("pressure_hpa", pressure);

            float? visibility = Reflect.GetFloat(scene, "Visibility", "VisibilityMeters");
            root.SetIfNotNull("visibility_m", visibility);

            JsonObj wind = BuildWind();
            if (wind.Count > 0)
            {
                root.Set("wind", wind);
            }
            return root;
        }

        private static JsonObj BuildWind()
        {
            JsonObj wind = new JsonObj();
            object scene = GameAccess.Scene;
            if (scene == null)
            {
                return wind;
            }
            Vector3? w = Reflect.GetVector3(scene, "Wind", "WindVelocity", "WindVector");
            if (w.HasValue)
            {
                Vector3 vector = w.Value;
                wind.Set("speed_mps", vector.magnitude);
                float direction = Mathf.Atan2(vector.x, vector.z) * Mathf.Rad2Deg;
                if (direction < 0f)
                {
                    direction += 360f;
                }
                wind.Set("direction_deg", direction);
                return wind;
            }
            float? speed = Reflect.GetFloat(scene, "WindSpeed", "WindMagnitude");
            if (speed.HasValue)
            {
                wind.Set("speed_mps", speed.Value);
                float? direction = Reflect.GetFloat(scene, "WindDirection", "WindHeading");
                if (direction.HasValue)
                {
                    wind.Set("direction_deg", direction.Value);
                }
            }
            return wind;
        }

        // ══════════════════════════════════════════════════════════════════
        // 辅助
        // ══════════════════════════════════════════════════════════════════

        private static JsonObj Begin()
        {
            JsonObj obj = new JsonObj();
            obj.Set("ok", true);
            obj.Set("protocol", 1);
            return obj;
        }

        private static Vector3? FirstVector(object aircraft, object player)
        {
            if (aircraft != null)
            {
                Vector3? v = Reflect.GetVector3(aircraft, "Velocity", "WorldVelocity", "VelocityVector");
                if (v.HasValue)
                {
                    return v;
                }
            }
            if (player != null)
            {
                return Reflect.GetVector3(player, "Velocity", "WorldVelocity", "VelocityVector");
            }
            return null;
        }

        private static bool? FirstBool(object target, string[] names)
        {
            return target == null ? null : Reflect.GetBool(target, names);
        }

        private static void SetControlAxis(JsonObj controls, string key, object controlsObject, params string[] names)
        {
            float? value = Reflect.GetFloat(controlsObject, names);
            controls.SetIfNotNull(key, value);
        }

        private static float? ToPercent(float? value)
        {
            if (!value.HasValue)
            {
                return null;
            }
            // 0-1 的当比例，-1..1 的操纵面取绝对值当放出量，其余原样。
            float v = value.Value;
            if (v >= -1.0001f && v <= 1.0001f)
            {
                return v * 100f;
            }
            return v;
        }

        private static float SpeedOfSound(float? temperatureC)
        {
            float kelvin = 288.15f;
            if (temperatureC.HasValue)
            {
                kelvin = temperatureC.Value + 273.15f;
            }
            double value = Math.Sqrt(1.4 * 287.05 * kelvin);
            return (float)value;
        }

        private static float? EstimateG(Vector3? velocity, Transform transform)
        {
            if (!velocity.HasValue)
            {
                return null;
            }
            float now = Time.realtimeSinceStartup;
            Vector3 v = velocity.Value;
            if (!hasPrev)
            {
                prevVelocity = v;
                prevTime = now;
                hasPrev = true;
                return 1.0f;
            }
            float dt = now - prevTime;
            if (dt <= 0.0005f || dt > 1.0f)
            {
                prevVelocity = v;
                prevTime = now;
                return smoothedG;
            }

            Vector3 acceleration = (v - prevVelocity) / dt;
            prevVelocity = v;
            prevTime = now;

            // 过载 = （加速度 - 重力）/ g，沿机体法向的分量
            Vector3 specific = acceleration - new Vector3(0f, -Gravity, 0f);
            float normal = transform != null ? Vector3.Dot(specific, transform.up) : specific.y;
            float g = normal / Gravity;
            if (float.IsNaN(g) || float.IsInfinity(g))
            {
                return smoothedG;
            }
            // 轻平滑，避免单帧抖动把告警刷爆
            smoothedG = Mathf.Lerp(smoothedG, g, 0.35f);
            return smoothedG;
        }

        private static float SampleTerrainHeight(Vector3 position)
        {
            try
            {
                float now = Time.realtimeSinceStartup;
                if (now - terrainsRefreshedAt > 5.0f)
                {
                    terrainsRefreshedAt = now;
                    terrains = Terrain.activeTerrains;
                    if (terrains == null)
                    {
                        terrains = new Terrain[0];
                    }
                }
                for (int i = 0; i < terrains.Length; i++)
                {
                    Terrain terrain = terrains[i];
                    if (terrain == null)
                    {
                        continue;
                    }
                    // 只有落在地形块范围内的采样才可信
                    Vector3 origin = terrain.transform.position;
                    Vector3 size = terrain.terrainData != null ? terrain.terrainData.size : new Vector3(0, 0, 0);
                    if (position.x < origin.x || position.x > origin.x + size.x ||
                        position.z < origin.z || position.z > origin.z + size.z)
                    {
                        continue;
                    }
                    return terrain.SampleHeight(position) + origin.y;
                }

                // 兜底：向下打一条射线。起点抬高一点，免得被自己的起落架挡住。
                RaycastHit hit;
                Vector3 from = position + new Vector3(0f, 5f, 0f);
                if (Physics.Raycast(from, Vector3.down, out hit, 30000f))
                {
                    return hit.point.y;
                }
            }
            catch
            {
            }
            return float.NaN;
        }

        private static List<JsonObj> BuildEngines(MonoBehaviour root)
        {
            List<JsonObj> list = new List<JsonObj>();
            if (root == null)
            {
                return list;
            }

            float now = Time.realtimeSinceStartup;
            if (!ReferenceEquals(engineOwner, root) || now - enginesRefreshedAt > RefineInterval)
            {
                engineOwner = root;
                enginesRefreshedAt = now;
                List<MonoBehaviour> found = new List<MonoBehaviour>();
                MonoBehaviour[] all = root.GetComponentsInChildren<MonoBehaviour>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    MonoBehaviour component = all[i];
                    if (component == null)
                    {
                        continue;
                    }
                    string typeName = component.GetType().Name;
                    if (typeName.IndexOf("Engine", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        found.Add(component);
                    }
                }
                engineComponents = found.ToArray();
            }

            for (int i = 0; i < engineComponents.Length; i++)
            {
                MonoBehaviour component = engineComponents[i];
                if (component == null)
                {
                    continue;
                }
                JsonObj engine = new JsonObj();
                engine.Set("index", i);

                string name = Reflect.GetString(component, "Name", "DisplayName", "PartName");
                engine.Set("name", name != null ? name : component.name);
                engine.Set("type", GuessEngineType(component.GetType().Name));

                bool? running = Reflect.GetBool(component, "Running", "IsRunning", "IsOn", "Ignited", "Enabled");
                engine.SetIfNotNull("running", running);
                float? throttle = Reflect.GetFloat(component, "Throttle", "ThrottleInput", "ThrottlePercent");
                engine.SetIfNotNull("throttle", throttle);
                float? rpm = Reflect.GetFloat(component, "Rpm", "RPM", "Revolutions");
                engine.SetIfNotNull("rpm", rpm);
                float? power = Reflect.GetFloat(component, "Power", "PowerPercent", "Output", "ThrustPercent");
                engine.SetIfNotNull("power_percent", power);
                bool? damaged = Reflect.GetBool(component, "Damaged", "IsDamaged", "Broken", "IsBroken");
                engine.SetIfNotNull("damaged", damaged);
                bool? onFire = Reflect.GetBool(component, "OnFire", "IsOnFire", "Burning");
                engine.SetIfNotNull("on_fire", onFire);
                list.Add(engine);
            }
            return list;
        }

        private static int CountParts(MonoBehaviour root)
        {
            int count = 0;
            try
            {
                MonoBehaviour[] all = root.GetComponentsInChildren<MonoBehaviour>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] == null)
                    {
                        continue;
                    }
                    if (all[i].GetType().Name.EndsWith("PartScript", StringComparison.Ordinal))
                    {
                        count++;
                    }
                }
            }
            catch
            {
            }
            return count;
        }

        private static string GuessType(MonoBehaviour behaviour)
        {
            string typeName = behaviour != null ? behaviour.GetType().Name : "";
            if (typeName.IndexOf("Ground", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "car";
            }
            if (typeName.IndexOf("Boat", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("Ship", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("Sea", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "boat";
            }
            if (typeName.IndexOf("Aircraft", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "plane";
            }
            return "other";
        }

        private static string GuessEngineType(string typeName)
        {
            string lower = typeName.ToLowerInvariant();
            if (lower.Contains("jet") || lower.Contains("turbine"))
            {
                return "jet";
            }
            if (lower.Contains("prop"))
            {
                return "prop";
            }
            if (lower.Contains("piston") || lower.Contains("combustion"))
            {
                return "piston";
            }
            if (lower.Contains("electric") || lower.Contains("motor"))
            {
                return "electric";
            }
            if (lower.Contains("rocket"))
            {
                return "rocket";
            }
            return "other";
        }
    }
}
