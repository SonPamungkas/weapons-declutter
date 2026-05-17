using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using NuclearOption.Networking;

namespace WeaponSkipMod
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public static BepInEx.Logging.ManualLogSource Log;
        public static ConfigEntry<bool> EnableAutoSkip;

        private void Awake()
        {
            Log = Logger;
            
            EnableAutoSkip = Config.Bind("General", "Enable Auto-Skip", true, "If enabled, weapon stations are automatically skipped when they run out of ammo during firing. If disabled, empty stations are only skipped when manually cycling weapons.");

            Harmony harmony = new Harmony(PluginInfo.PLUGIN_GUID);
            harmony.PatchAll();
            Log.LogInfo("WeaponSkipMod initialized. Client-side multiplayer skipping active.");
        }
    }

    [HarmonyPatch(typeof(WeaponManager), "NextWeaponStation")]
    public class NextWeaponStation_Patch
    {
        private static bool _isSkipping = false;

        static void Postfix(WeaponManager __instance)
        {
            // Prevent infinite recursion during auto-skipping
            if (_isSkipping) return;

            try
            {
                var aircraft = Traverse.Create(__instance).Field("aircraft").GetValue<Aircraft>();
                
                // MULTIPLAYER CHECK: 
                // Ensure this aircraft is actually the one the local player is currently flying
                Aircraft localAircraft;
                if (!GameManager.GetLocalAircraft(out localAircraft) || aircraft != localAircraft) return;
                
                if (aircraft.weaponStations == null || aircraft.weaponStations.Count == 0) return;

                int total = aircraft.weaponStations.Count;
                int count = 0;

                while (__instance.currentWeaponStation != null && __instance.currentWeaponStation.Ammo <= 0 && count < total)
                {
                    _isSkipping = true;
                    try
                    {
                        __instance.NextWeaponStation();
                    }
                    finally
                    {
                        _isSkipping = false;
                    }
                    count++;
                }

                // If we looped through all stations and still landed on an empty one, deselect entirely
                if (count >= total && __instance.currentWeaponStation != null && __instance.currentWeaponStation.Ammo <= 0)
                {
                    __instance.SetActiveStation(255); // 255 is the standard byte index for 'None'
                    Traverse.Create(__instance).Field("currentWeaponStation").SetValue(null);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Error in NextWeaponStation Postfix: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(WeaponManager), "PreviousWeaponStation")]
    public class PreviousWeaponStation_Patch
    {
        private static bool _isSkipping = false;

        static void Postfix(WeaponManager __instance)
        {
            if (_isSkipping) return;

            try
            {
                var aircraft = Traverse.Create(__instance).Field("aircraft").GetValue<Aircraft>();
                
                Aircraft localAircraft;
                if (!GameManager.GetLocalAircraft(out localAircraft) || aircraft != localAircraft) return;

                if (aircraft.weaponStations == null || aircraft.weaponStations.Count == 0) return;

                int total = aircraft.weaponStations.Count;
                int count = 0;

                while (__instance.currentWeaponStation != null && __instance.currentWeaponStation.Ammo <= 0 && count < total)
                {
                    _isSkipping = true;
                    try
                    {
                        __instance.PreviousWeaponStation();
                    }
                    finally
                    {
                        _isSkipping = false;
                    }
                    count++;
                }

                if (count >= total && __instance.currentWeaponStation != null && __instance.currentWeaponStation.Ammo <= 0)
                {
                    __instance.SetActiveStation(255);
                    Traverse.Create(__instance).Field("currentWeaponStation").SetValue(null);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Error in PreviousWeaponStation Postfix: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(WeaponStation), "AccountAmmo")]
    public class WeaponStation_AccountAmmo_Patch
    {
        static void Postfix(WeaponStation __instance)
        {
            if (Plugin.EnableAutoSkip != null && !Plugin.EnableAutoSkip.Value) return;
            try
            {
                Aircraft localAircraft = null;
                if (GameManager.GetLocalAircraft(out localAircraft) && localAircraft != null)
                {
                    if (localAircraft.weaponStations != null && localAircraft.weaponStations.Contains(__instance))
                    {
                        if (__instance.Ammo <= 0)
                        {
                            if (localAircraft.weaponManager != null && localAircraft.weaponManager.currentWeaponStation == __instance)
                            {
                                localAircraft.weaponManager.NextWeaponStation();
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Silently caught error in AccountAmmo Postfix: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(WeaponManager), "Fire")]
    public class WeaponManager_Fire_Patch
    {
        static void Postfix(WeaponManager __instance)
        {
            if (Plugin.EnableAutoSkip != null && !Plugin.EnableAutoSkip.Value) return;
            try
            {
                var aircraft = Traverse.Create(__instance).Field("aircraft").GetValue<Aircraft>();
                Aircraft localAircraft;
                if (aircraft == null || !GameManager.GetLocalAircraft(out localAircraft) || aircraft != localAircraft) return;

                var current = __instance.currentWeaponStation;
                if (current != null)
                {
                    int activeWeaponsCount = 0;
                    if (current.Weapons != null)
                    {
                        foreach (var w in current.Weapons)
                        {
                            if (w != null) activeWeaponsCount++;
                        }
                    }

                    if (current.Ammo <= 0 || activeWeaponsCount == 0)
                    {
                        __instance.NextWeaponStation();
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Error in WeaponManager.Fire Postfix: {ex}");
            }
        }
    }

    [HarmonyPatch]
    public class Unit_RpcSyncAmmoCount_Patch
    {
        [HarmonyTargetMethod]
        static System.Reflection.MethodBase TargetMethod()
        {
            foreach (var m in typeof(Unit).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
            {
                if (m.Name.StartsWith("UserCode_RpcSyncAmmoCount"))
                {
                    return m;
                }
            }
            return null;
        }

        static void Postfix(Unit __instance, byte stationIndex, int ammo)
        {
            if (Plugin.EnableAutoSkip != null && !Plugin.EnableAutoSkip.Value) return;
            try
            {
                var aircraft = __instance as Aircraft;
                Aircraft localAircraft;
                
                if (aircraft != null && GameManager.GetLocalAircraft(out localAircraft) && aircraft == localAircraft)
                {
                    if (ammo <= 0 && aircraft.weaponManager != null)
                    {
                        if (stationIndex < aircraft.weaponStations.Count)
                        {
                            var station = aircraft.weaponStations[stationIndex];
                            bool isCurrent = aircraft.weaponManager.currentWeaponStation == station;
                            if (isCurrent)
                            {
                                aircraft.weaponManager.NextWeaponStation();
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Error in RpcSyncAmmoCount Postfix: {ex}");
            }
        }
    }

    [HarmonyPatch]
    public class Unit_RpcSingleRemoteFire_Patch
    {
        [HarmonyTargetMethod]
        static System.Reflection.MethodBase TargetMethod()
        {
            foreach (var m in typeof(Unit).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
            {
                if (m.Name.StartsWith("UserCode_RpcSingleRemoteFire"))
                {
                    return m;
                }
            }
            return null;
        }

        static void Postfix(Unit __instance, byte stationIndex, int ammo)
        {
            if (Plugin.EnableAutoSkip != null && !Plugin.EnableAutoSkip.Value) return;
            try
            {
                var aircraft = __instance as Aircraft;
                Aircraft localAircraft;

                if (aircraft != null && GameManager.GetLocalAircraft(out localAircraft) && aircraft == localAircraft)
                {
                    if (ammo <= 0 && aircraft.weaponManager != null)
                    {
                        if (stationIndex < aircraft.weaponStations.Count)
                        {
                            var station = aircraft.weaponStations[stationIndex];
                            bool isCurrent = aircraft.weaponManager.currentWeaponStation == station;
                            if (isCurrent)
                            {
                                aircraft.weaponManager.NextWeaponStation();
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Error in RpcSingleRemoteFire Postfix: {ex}");
            }
        }
    }

    public static class PluginInfo
    {
        public const string PLUGIN_GUID = "com.user.weaponskipmod";
        public const string PLUGIN_NAME = "WeaponSkipMod";
        public const string PLUGIN_VERSION = "1.0.0";
    }
}