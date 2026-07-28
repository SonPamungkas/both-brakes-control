using System;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using Rewired;
using InputFramework;
namespace BothBrakesMod
{
    [BepInPlugin("neutral.both.brakes", "BothBrakesMod", "1.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        public static BepInEx.Logging.ManualLogSource Log = null!;
        public static ConfigEntry<bool> WheelbrakeHoldMode = null!;
        public static ConfigEntry<bool> AirbrakeHoldMode = null!;
        public static ConfigEntry<bool> BothBrakesHoldMode = null!;
        public static ConfigEntry<bool> VerboseLogging = null!;
        public static bool WheelbrakeToggled = false;
        public static bool AirbrakeToggled = false;
        public static bool BothBrakesToggled = false;
        public static bool IsWheelbrakeActive => WheelbrakeToggled || BothBrakesToggled;
        public static bool IsAirbrakeActive => AirbrakeToggled || BothBrakesToggled;
        private void Awake()
        {
            Log = Logger;
            WheelbrakeHoldMode = Config.Bind(
                "Settings", 
                "Wheel Brakes Hold Mode", 
                false, 
                "If true, wheel brakes are only active while holding the hotkey. If false, they function as a toggle."
            );
            AirbrakeHoldMode = Config.Bind(
                "Settings", 
                "Airbrakes Hold Mode", 
                false, 
                "If true, airbrakes are only active while holding the hotkey. If false, they function as a toggle."
            );
            BothBrakesHoldMode = Config.Bind(
                "Settings", 
                "Both Brakes Hold Mode", 
                false, 
                "If true, both brakes are only active while holding the hotkey. If false, they function as a toggle."
            );
            VerboseLogging = Config.Bind(
                "Debug", 
                "Verbose Logging", 
                false, 
                "If true, the mod will output verbose telemetry logs to the BepInEx console."
            );
            ExtraInputManager.LoadPendingActions();
            ExtraInputManager.RegisterAction("ToggleWheelbrake", Rewired.InputActionType.Button);
            ExtraInputManager.RegisterAction("ToggleAirbrake", Rewired.InputActionType.Button);
            ExtraInputManager.RegisterAction("ToggleBothBrakes", Rewired.InputActionType.Button);
            try
            {
                Harmony harmony = new Harmony("com.BothBrakesMod");
                harmony.PatchAll(typeof(RewiredActionInjector));
                PatchMethodByName(harmony, "PilotPlayerState", "PlayerAxisControls", typeof(PilotPlayerState_Patch));
                PatchMethodByName(harmony, "PilotPlayerState", "PlayerControls", typeof(PilotPlayerState_Patch));
                PatchMethodByName(harmony, "PilotPlayerState", "PlayerThrottleAxis1Controls", typeof(PilotPlayerState_Patch));
                Log.LogInfo("BothBrakesMod patches successfully loaded!");
            }
            catch (Exception ex)
            {
                Log.LogError($"Failed to patch BothBrakesMod: {ex}");
            }
        }
        private void PatchMethodByName(Harmony harmony, string className, string methodName, Type patchType)
        {
            try
            {
                Type targetType = AccessTools.TypeByName(className);
                if (targetType == null) return;
                var target = AccessTools.Method(targetType, methodName);
                if (target == null) return;
                var prefix = patchType.GetMethod("Prefix");
                var postfix = patchType.GetMethod("Postfix");
                harmony.Patch(target, 
                    prefix != null ? new HarmonyMethod(prefix) : null, 
                    postfix != null ? new HarmonyMethod(postfix) : null);
                Log.LogInfo($"Successfully patched {className}.{methodName} for brake spoofing.");
            }
            catch (Exception ex)
            {
                Log.LogWarning($"Failed to patch {className}.{methodName}: {ex.Message}");
            }
        }
        private void Update()
        {
            Aircraft localAircraft;
            if (!GameManager.GetLocalAircraft(out localAircraft) || localAircraft == null)
            {
                if (WheelbrakeToggled || AirbrakeToggled || BothBrakesToggled)
                {
                    WheelbrakeToggled = AirbrakeToggled = BothBrakesToggled = false;
                    if (VerboseLogging.Value) Log.LogInfo("Not in local aircraft, resetting brake states.");
                }
                return;
            }
            if (!ExtraInputManager.RewiredInitialized) return;
            bool inChat = false;
            try { inChat = CursorManager.GetFlag(CursorFlags.Chat); } catch {}
            if (inChat) return;
            Rewired.Player localPlayer = ReInput.players.GetPlayer(0);
            if (localPlayer == null) return;
            if (BothBrakesHoldMode.Value)
            {
                bool isHeld = localPlayer.GetButton("ToggleBothBrakes");
                if (isHeld != BothBrakesToggled)
                {
                    BothBrakesToggled = isHeld;
                    WheelbrakeToggled = AirbrakeToggled = isHeld;
                }
            }
            else if (localPlayer.GetButtonDown("ToggleBothBrakes"))
            {
                BothBrakesToggled = !BothBrakesToggled;
                WheelbrakeToggled = AirbrakeToggled = BothBrakesToggled;
                if (VerboseLogging.Value) Log.LogInfo($"Both brakes toggled: {BothBrakesToggled}");
            }
            if (WheelbrakeHoldMode.Value)
            {
                WheelbrakeToggled = localPlayer.GetButton("ToggleWheelbrake");
            }
            else if (localPlayer.GetButtonDown("ToggleWheelbrake"))
            {
                WheelbrakeToggled = !WheelbrakeToggled;
                if (VerboseLogging.Value) Log.LogInfo($"Wheelbrakes toggled: {WheelbrakeToggled}");
            }
            if (AirbrakeHoldMode.Value)
            {
                AirbrakeToggled = localPlayer.GetButton("ToggleAirbrake");
            }
            else if (localPlayer.GetButtonDown("ToggleAirbrake"))
            {
                AirbrakeToggled = !AirbrakeToggled;
                if (VerboseLogging.Value) Log.LogInfo($"Airbrakes toggled: {AirbrakeToggled}");
            }
        }
    }
    public static class PilotPlayerState_Patch
    {
        public static void Postfix(PilotPlayerState __instance)
        {
            if ((!Plugin.IsAirbrakeActive && !Plugin.IsWheelbrakeActive) || __instance == null || __instance.controlInputs == null) return;
            Aircraft localAircraft;
            if (GameManager.GetLocalAircraft(out localAircraft) && localAircraft != null)
            {
                if (Plugin.IsWheelbrakeActive)
                {
                    __instance.controlInputs.brake = 1f; 
                }
                if (Plugin.IsAirbrakeActive)
                {
                    __instance.controlInputs.throttle = 0f; 
                }
            }
        }
    }
}