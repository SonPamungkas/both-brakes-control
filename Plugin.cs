using System;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace BothBrakesMod
{
    [BepInPlugin("com.BothBrakesMod", "BothBrakesMod", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public static BepInEx.Logging.ManualLogSource Log = null!;

        // Config Entries for Keyboard Shortcuts
        public static ConfigEntry<KeyboardShortcut> WheelbrakeKey = null!;
        public static ConfigEntry<KeyboardShortcut> AirbrakeKey = null!;
        public static ConfigEntry<KeyboardShortcut> BothBrakesKey = null!;

        // Config Entries for Hold Mode
        public static ConfigEntry<bool> WheelbrakeHoldMode = null!;
        public static ConfigEntry<bool> AirbrakeHoldMode = null!;
        public static ConfigEntry<bool> BothBrakesHoldMode = null!;

        // Toggle/Active states (controlled dynamically by Update)
        public static bool WheelbrakeToggled = false;
        public static bool AirbrakeToggled = false;
        public static bool BothBrakesToggled = false;

        // Effective states
        public static bool IsWheelbrakeActive => WheelbrakeToggled || BothBrakesToggled;
        public static bool IsAirbrakeActive => AirbrakeToggled || BothBrakesToggled;

        private void Awake()
        {
            Log = Logger;

            // Bind configurations with beautiful, helpful descriptions
            WheelbrakeKey = Config.Bind(
                "Keybinds", 
                "Toggle Wheel Brakes", 
                new KeyboardShortcut(KeyCode.B, KeyCode.LeftAlt), 
                "Key shortcut to toggle/hold the wheel brakes."
            );

            AirbrakeKey = Config.Bind(
                "Keybinds", 
                "Toggle Airbrakes", 
                new KeyboardShortcut(KeyCode.G, KeyCode.LeftAlt), 
                "Key shortcut to toggle/hold the airbrakes (deploys even with throttle active)."
            );

            BothBrakesKey = Config.Bind(
                "Keybinds", 
                "Toggle Both Brakes", 
                new KeyboardShortcut(KeyCode.H, KeyCode.LeftAlt), 
                "Key shortcut to toggle/hold both wheel brakes and airbrakes simultaneously."
            );

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

            // Setup Harmony manual patching
            try
            {
                Harmony harmony = new Harmony("com.BothBrakesMod");

                // We removed the LandingGear patch and centralized everything in PilotPlayerState.
                // Patch Pilot Player input states for overriding (Keyboard, Mouse, HOTAS)
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
            // Reset states if not inside a local aircraft (e.g. menu, spectator, dead, ejected)
            Aircraft localAircraft;
            if (!GameManager.GetLocalAircraft(out localAircraft) || localAircraft == null)
            {
                if (WheelbrakeToggled || AirbrakeToggled || BothBrakesToggled)
                {
                    WheelbrakeToggled = AirbrakeToggled = BothBrakesToggled = false;
                    Log.LogInfo("Not in local aircraft, resetting brake states.");
                }
                return;
            }

            // Prevent toggle input when in chat
            bool inChat = false;
            try { inChat = CursorManager.GetFlag(CursorFlags.Chat); } catch {}
            if (inChat) return;

            // --- BOTH BRAKES KEY ---
            if (BothBrakesHoldMode.Value)
            {
                BothBrakesToggled = BothBrakesKey.Value.IsPressed();
                if (BothBrakesToggled)
                {
                    WheelbrakeToggled = AirbrakeToggled = true;
                }
                else
                {
                    WheelbrakeToggled = AirbrakeToggled = false;
                }
            }
            else if (BothBrakesKey.Value.IsDown())
            {
                BothBrakesToggled = !BothBrakesToggled;
                WheelbrakeToggled = AirbrakeToggled = BothBrakesToggled;
                Log.LogInfo($"Both brakes toggled: {BothBrakesToggled}");
            }

            // --- WHEELBRAKE KEY ---
            if (WheelbrakeHoldMode.Value)
            {
                WheelbrakeToggled = WheelbrakeKey.Value.IsPressed();
            }
            else if (WheelbrakeKey.Value.IsDown())
            {
                WheelbrakeToggled = !WheelbrakeToggled;
                Log.LogInfo($"Wheelbrakes toggled: {WheelbrakeToggled}");
            }

            // --- AIRBRAKE KEY ---
            if (AirbrakeHoldMode.Value)
            {
                AirbrakeToggled = AirbrakeKey.Value.IsPressed();
            }
            else if (AirbrakeKey.Value.IsDown())
            {
                AirbrakeToggled = !AirbrakeToggled;
                Log.LogInfo($"Airbrakes toggled: {AirbrakeToggled}");
            }
        }
    }

    // ==========================================
    // PILOT PLAYER STATE PATCH (Handles BOTH Brakes)
    // ==========================================
    public static class PilotPlayerState_Patch
    {
        public static void Postfix(PilotPlayerState __instance)
        {
            // If neither brake is active, do nothing
            if ((!Plugin.IsAirbrakeActive && !Plugin.IsWheelbrakeActive) || __instance == null || __instance.controlInputs == null) return;

            Aircraft localAircraft;
            if (GameManager.GetLocalAircraft(out localAircraft) && localAircraft != null)
            {
                // Inject the Wheelbrake logic
                if (Plugin.IsWheelbrakeActive)
                {
                    __instance.controlInputs.brake = 1f; // Force brake input to 100%
                }

                // Inject the Airbrake logic
                if (Plugin.IsAirbrakeActive)
                {
                    __instance.controlInputs.throttle = 0f; // Force throttle to 0f
                }
            }
        }
    }
}