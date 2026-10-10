// N.E.K.O-SP2-Bridge — 反射读取游戏对象
//
// 为什么全是反射：
//   《简单飞机2》还在抢先体验，官方 modding 支持尚未上线，Game.dll 的内部
//   类型没有任何稳定保证。直接硬引用类型会在每次游戏更新后编译不过；
//   用反射 + 候选名列表，则「名字变了就少读几个字段」，不会整体崩掉。
//
// 已确认存在的类型（来自社区开源 SP2 模组的实测，见 README 的致谢）
//   Assets.Scripts.Flight.FlightSceneScript  .Instance / .LocalPlayer / .FlightUI
//   Assets.Scripts.Craft.AircraftScript      .Position / .Velocity / .Controls
//   FlightScenePlayer                        .Aircraft / .Velocity / .Position
//   AircraftScript.Controls                  .Flaps / .Roll / .Pitch / .Yaw / .Brake
//
// 其余字段（空速、迎角、油量、起落架、发动机……）名字尚未确认，
// 这里用候选名逐个试；试不到就返回 null，由 Telemetry 或探针补。
//
// ⚠️ 只读：本文件里不存在任何 SetValue / 方法调用去改变游戏状态。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace NekoSp2Bridge
{
    /// <summary>通用反射取值工具。全部带缓存，避免每帧 GetProperty 的开销。</summary>
    public static class Reflect
    {
        private const BindingFlags Flags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        private static readonly Dictionary<string, Type> TypeCache = new Dictionary<string, Type>();
        private static readonly Dictionary<string, MemberInfo> MemberCache = new Dictionary<string, MemberInfo>();
        private static readonly Dictionary<MemberInfo, bool> BadMembers = new Dictionary<MemberInfo, bool>();

        public static Type FindType(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
            {
                return null;
            }
            lock (TypeCache)
            {
                Type cached;
                if (TypeCache.TryGetValue(fullName, out cached))
                {
                    return cached;
                }
            }

            Type found = null;
            // 先从已加载程序集里按全名找（最快）
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length && found == null; i++)
            {
                try
                {
                    found = assemblies[i].GetType(fullName, false, false);
                }
                catch
                {
                }
            }
            // 兜底：按名字尾部匹配（命名空间在版本间挪动过）
            if (found == null)
            {
                string shortName = fullName;
                int dot = fullName.LastIndexOf('.');
                if (dot >= 0)
                {
                    shortName = fullName.Substring(dot + 1);
                }
                for (int i = 0; i < assemblies.Length && found == null; i++)
                {
                    Type[] types;
                    try
                    {
                        types = assemblies[i].GetTypes();
                    }
                    catch
                    {
                        continue;
                    }
                    for (int j = 0; j < types.Length; j++)
                    {
                        if (types[j].Name == shortName)
                        {
                            found = types[j];
                            break;
                        }
                    }
                }
            }

            lock (TypeCache)
            {
                TypeCache[fullName] = found;
            }
            return found;
        }

        private static MemberInfo FindMember(Type type, string name)
        {
            string key = type.FullName + "::" + name;
            lock (MemberCache)
            {
                MemberInfo cached;
                if (MemberCache.TryGetValue(key, out cached))
                {
                    return cached;
                }
            }

            MemberInfo member = null;
            try
            {
                member = type.GetProperty(name, Flags);
            }
            catch
            {
            }
            if (member == null)
            {
                try
                {
                    member = type.GetField(name, Flags);
                }
                catch
                {
                }
            }

            lock (MemberCache)
            {
                MemberCache[key] = member;
            }
            return member;
        }

        /// <summary>按顺序尝试候选名，返回第一个能取到的值；全都没有则 null。</summary>
        public static object Get(object target, params string[] names)
        {
            if (target == null || names == null)
            {
                return null;
            }
            Type type = target as Type;
            bool isStatic = type != null;
            if (!isStatic)
            {
                type = target.GetType();
            }

            for (int i = 0; i < names.Length; i++)
            {
                string name = names[i];
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }
                MemberInfo member = FindMember(type, name);
                if (member == null)
                {
                    continue;
                }
                lock (BadMembers)
                {
                    if (BadMembers.ContainsKey(member))
                    {
                        continue;
                    }
                }
                try
                {
                    PropertyInfo prop = member as PropertyInfo;
                    if (prop != null)
                    {
                        if (prop.GetIndexParameters().Length != 0)
                        {
                            continue;
                        }
                        return prop.GetValue(isStatic ? null : target, null);
                    }
                    FieldInfo field = member as FieldInfo;
                    if (field != null)
                    {
                        return field.GetValue(isStatic ? null : target);
                    }
                }
                catch
                {
                    lock (BadMembers)
                    {
                        BadMembers[member] = true;
                    }
                }
            }
            return null;
        }

        public static float? GetFloat(object target, params string[] names)
        {
            object value = Get(target, names);
            if (value == null)
            {
                return null;
            }
            try
            {
                float f = Convert.ToSingle(value, CultureInfo.InvariantCulture);
                if (float.IsNaN(f) || float.IsInfinity(f))
                {
                    return null;
                }
                return f;
            }
            catch
            {
                return null;
            }
        }

        public static bool? GetBool(object target, params string[] names)
        {
            object value = Get(target, names);
            if (value == null)
            {
                return null;
            }
            try
            {
                return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return null;
            }
        }

        public static string GetString(object target, params string[] names)
        {
            object value = Get(target, names);
            return value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        public static Vector3? GetVector3(object target, params string[] names)
        {
            object value = Get(target, names);
            if (value is Vector3)
            {
                return (Vector3)value;
            }
            return null;
        }

        /// <summary>列出一个对象的属性和字段（探针用）。</summary>
        public static List<MemberInfo> ListMembers(object target)
        {
            List<MemberInfo> list = new List<MemberInfo>();
            if (target == null)
            {
                return list;
            }
            Type type = target as Type;
            if (type == null)
            {
                type = target.GetType();
            }
            try
            {
                list.AddRange(type.GetProperties(Flags));
            }
            catch
            {
            }
            try
            {
                list.AddRange(type.GetFields(Flags));
            }
            catch
            {
            }
            return list;
        }

        public static object ReadMember(object target, MemberInfo member)
        {
            try
            {
                PropertyInfo prop = member as PropertyInfo;
                if (prop != null)
                {
                    if (prop.GetIndexParameters().Length != 0)
                    {
                        return null;
                    }
                    return prop.GetValue(target, null);
                }
                FieldInfo field = member as FieldInfo;
                if (field != null)
                {
                    return field.GetValue(target);
                }
            }
            catch
            {
            }
            return null;
        }
    }

    /// <summary>当前帧能拿到的游戏对象。全部在主线程上刷新，HTTP 线程只读。</summary>
    public static class GameAccess
    {
        // ── 确认过的类型名 ──────────────────────────────────────────────
        // 以下四个都已用 /probe 在 SP2 v0.7.8.103 实机确认（见 docs/probe-*.json）
        public const string SceneTypeName = "Assets.Scripts.Flight.FlightSceneScript";
        public const string AircraftTypeName = "Assets.Scripts.Craft.AircraftScript";

        // ── 候选名 ──────────────────────────────────────────────────────
        //
        // 排在前面的都是**实机 probe 确认过的真名**（SP2 v0.7.8.103），
        // 后面的旧候选名保留着，用于兼容其它版本。
        //
        // ⚠️ 这里踩过一个坑：Pitch/Roll 这类"想当然"的名字在 AircraftScript 上
        // 并不存在，真名是 PitchAngle/RollAngle。候选名写错不会报错，
        // 只会静默返回 null，然后被兜底逻辑填成 0 —— 外面看起来像"读到了 0"。
        // 所以以后加字段，**一定先用 /probe 核对真名**，不要凭直觉写。
        public static readonly string[] AltitudeNames = { "Altitude", "AltitudeMsl", "AltitudeMSL", "SeaLevelAltitude", "AltitudeASL" };
        public static readonly string[] AltitudeAglNames = { "AltitudeAgl", "AltitudeAGL", "AltitudeAboveGround", "RadarAltitude" };
        public static readonly string[] AirspeedNames = { "IAS", "AirSpeed", "Airspeed", "IndicatedAirspeed", "IndicatedAirSpeed", "Ias", "TrueAirspeed", "TrueAirSpeed", "TAS" };
        public static readonly string[] GroundSpeedNames = { "GS", "GroundSpeed", "Speed" };
        public static readonly string[] MachNames = { "Mach", "MachNumber", "MachSpeed" };
        public static readonly string[] AoANames = { "AngleOfAttack", "Aoa", "AOA", "Alpha", "AttackAngle" };
        public static readonly string[] SideslipNames = { "AngleOfSlip", "Sideslip", "SideSlip", "Beta", "SideslipAngle" };
        public static readonly string[] PitchNames = { "PitchAngle", "Pitch", "PitchDeg", "AngleOfPitch" };
        public static readonly string[] RollNames = { "RollAngle", "Roll", "BankAngle", "AngleOfRoll" };
        public static readonly string[] HeadingNames = { "Heading", "HeadingAngle", "Yaw", "Compass" };
        public static readonly string[] PitchRateNames = { "PitchRate", "PitchSpeed" };
        public static readonly string[] RollRateNames = { "RollRate", "RollSpeed" };
        public static readonly string[] YawRateNames = { "YawRate", "YawSpeed" };
        public static readonly string[] LatitudeNames = { "Latitude", "Lat" };
        public static readonly string[] LongitudeNames = { "Longitude", "Lon", "Long" };
        public static readonly string[] GForceNames = { "GForce", "GForceZ", "GLoad", "LoadFactor", "AccelerationG" };
        public static readonly string[] VerticalGNames = { "VerticalG", "GForceVertical" };
        public static readonly string[] StalledNames = { "Stalled", "IsStalled", "Stalling", "IsStalling" };
        public static readonly string[] StallWarnNames = { "StallWarning", "IsStallWarning", "StallWarn" };
        public static readonly string[] OnGroundNames = { "OnGround", "IsGrounded", "Grounded", "IsOnGround", "WheelsOnGround", "TouchingGround" };
        public static readonly string[] CrashedNames = { "Crashed", "IsCrashed", "IsDestroyed", "Destroyed", "IsDead" };
        public static readonly string[] FuelNames = { "Fuel", "FuelRemaining", "FuelMass", "FuelLevel", "RemainingFuel" };
        public static readonly string[] FuelCapacityNames = { "FuelCapacity", "MaxFuel", "FuelCapacityMass", "TotalFuel" };
        // FuelProportion 是实机确认的真名（0-1）。
        public static readonly string[] FuelPercentNames = { "FuelProportion", "FuelPercent", "FuelPercentage", "FuelFraction", "FuelLevelPercent" };
        public static readonly string[] GearDownNames = { "GearDown", "IsGearDown", "LandingGearDown", "GearDeployed", "IsGearDeployed" };
        public static readonly string[] MassNames = { "Mass", "TotalMass", "Weight" };
        public static readonly string[] IntegrityNames = { "Integrity", "IntegrityPercent", "Health", "HealthPercent", "Durability" };
        public static readonly string[] NameNames = { "Name", "DisplayName", "CraftName", "VehicleName" };
        public static readonly string[] TargetDistanceNames = { "TargetDistance", "DistanceToTarget" };
        public static readonly string[] TargetLockedNames = { "TargetLocked", "IsTargetLocked", "Locked" };
        public static readonly string[] TargetSelectedNames = { "TargetSelected", "HasTarget" };

        private const float RefreshInterval = 0.5f;
        private static float nextResolve;

        /// <summary>当前场景脚本；不在飞行场景时为 null。</summary>
        public static object Scene { get; private set; }
        /// <summary>本地玩家对象。</summary>
        public static object Player { get; private set; }
        /// <summary>玩家当前驾驶的飞行器（AircraftScript）。</summary>
        public static object Aircraft { get; private set; }
        /// <summary>飞行器的操纵状态对象（Controls）。</summary>
        public static object Controls { get; private set; }
        /// <summary>Aircraft 的 Transform；用于姿态与位置的兜底计算。</summary>
        public static Transform Transform { get; private set; }

        public static bool HasAircraft
        {
            get { return Aircraft != null; }
        }

        public static string LastError { get; private set; }

        /// <summary>每帧调用；内部自带节流（重载解析不必每帧做）。</summary>
        public static void Tick()
        {
            try
            {
                LastError = null;
                // Unity 对象会被销毁：每次都重新确认一遍，别抱着野引用。
                if (!IsAlive(Aircraft) || !IsAlive(Transform))
                {
                    Aircraft = null;
                    Controls = null;
                    Transform = null;
                }
                if (!IsAlive(Player))
                {
                    Player = null;
                }
                if (!IsAlive(Scene))
                {
                    Scene = null;
                }

                float now = Time.realtimeSinceStartup;
                if (now < nextResolve && Aircraft != null)
                {
                    return;
                }
                nextResolve = now + RefreshInterval;

                Resolve();
            }
            catch (Exception ex)
            {
                LastError = BridgeLog.Safe(ex);
            }
        }

        private static void Resolve()
        {
            Type sceneType = Reflect.FindType(SceneTypeName);
            if (sceneType == null)
            {
                Scene = null;
                Player = null;
                Aircraft = null;
                return;
            }

            Scene = Reflect.Get(sceneType, "Instance", "instance", "_instance");
            if (Scene == null)
            {
                return;
            }

            Player = Reflect.Get(Scene, "LocalPlayer", "Player", "localPlayer");
            if (Player == null)
            {
                return;
            }

            object aircraft = Reflect.Get(Player, "Aircraft", "CurrentOrPreviousAircraft", "CurrentAircraft", "Vehicle");
            // 玩家在地面上走路时没有载具，这不算错误。
            if (!IsAlive(aircraft))
            {
                Aircraft = null;
                Controls = null;
                Transform = null;
                return;
            }

            Aircraft = aircraft;
            Controls = Reflect.Get(Aircraft, "Controls", "AircraftControls", "ControlState");
            MonoBehaviour behaviour = Aircraft as MonoBehaviour;
            Transform = behaviour != null ? behaviour.transform : null;
        }

        /// <summary>
        /// Unity 对象被销毁后，C# 引用仍然非 null —— 必须用 Unity 自己重载过的
        /// == 才能识别。踩过一次：抱着一个已销毁的 AircraftScript 读成员，
        /// 每个字段都抛，日志瞬间被刷爆。
        /// </summary>
        public static bool IsAlive(object unityObject)
        {
            if (unityObject == null)
            {
                return false;
            }
            UnityEngine.Object uo = unityObject as UnityEngine.Object;
            // 不是 UnityEngine.Object（例如纯 C# 的单例），按普通对象算活着。
            if (ReferenceEquals(uo, null))
            {
                return true;
            }
            // 这里**刻意**用 Unity 的 != 重载：已销毁时它返回 false。
            return uo != null;
        }

        /// <summary>当前是否在飞行场景里（有场景脚本实例即视为在世界中）。</summary>
        public static bool InFlightScene
        {
            get { return Scene != null; }
        }
    }
}
