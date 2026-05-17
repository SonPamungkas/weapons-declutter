using System;
using System.Collections.Generic;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace WeaponSkipMod
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public static BepInEx.Logging.ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            Harmony harmony = new Harmony(PluginInfo.PLUGIN_GUID);
            harmony.PatchAll();
            Log.LogInfo("WeaponSkipMod initialized. Refined skipping logic active.");
        }
    }

    [HarmonyPatch(typeof(WeaponManager), "NextWeaponStation")]
    public class NextWeaponStation_Patch
    {
        private static bool _isSkipping = false;

        static void Postfix(WeaponManager __instance)
        {
            if (_isSkipping) return;

            try
            {
                var aircraft = Traverse.Create(__instance).Field("aircraft").GetValue<Aircraft>();
                if (aircraft == null || !GameManager.IsLocalAircraft(aircraft)) return;
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

                if (count >= total && __instance.currentWeaponStation != null && __instance.currentWeaponStation.Ammo <= 0)
                {
                    __instance.SetActiveStation(255);
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
                if (aircraft == null || !GameManager.IsLocalAircraft(aircraft)) return;
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
            try
            {
                if (__instance.Ammo <= 0)
                {
                    if (__instance.Weapons != null && __instance.Weapons.Count > 0)
                    {
                        var aircraft = __instance.Weapons[0].attachedUnit as Aircraft;
                        if (aircraft != null && GameManager.IsLocalAircraft(aircraft) && aircraft.weaponManager != null)
                        {
                            if (aircraft.weaponManager.currentWeaponStation == __instance)
                            {
                                aircraft.weaponManager.NextWeaponStation();
                            }
                        }
                    }
                }
            }
            catch { }
        }
    }

    public static class PluginInfo
    {
        public const string PLUGIN_GUID = "com.user.weaponskipmod";
        public const string PLUGIN_NAME = "WeaponSkipMod";
        public const string PLUGIN_VERSION = "1.0.0";
    }
}
