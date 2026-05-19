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

        // Toggle states
        public static bool WheelbrakeToggled = false;
        public static bool AirbrakeToggled = false;
        public static bool BothBrakesToggled = false;

        // Effective states (Dynamically resolves Hold Mode vs Toggle Mode)
        public static bool IsWheelbrakeActive
        {
            get
            {
                bool wb = WheelbrakeHoldMode.Value ? WheelbrakeKey.Value.IsPressed() : WheelbrakeToggled;
                bool bb = BothBrakesHoldMode.Value ? BothBrakesKey.Value.IsPressed() : BothBrakesToggled;
                return wb || bb;
            }
        }

        public static bool IsAirbrakeActive
        {
            get
            {
                bool ab = AirbrakeHoldMode.Value ? AirbrakeKey.Value.IsPressed() : AirbrakeToggled;
                bool bb = BothBrakesHoldMode.Value ? BothBrakesKey.Value.IsPressed() : BothBrakesToggled;
                return ab || bb;
            }
        }

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

            // Setup Harmony manual patching (using silent reflection to prevent console warnings)
            try
            {
                Harmony harmony = new Harmony("com.BothBrakesMod");

                // Patch Landing Gear for Wheelbrakes
                PatchMethodByName(harmony, "LandingGear", "FixedUpdate", typeof(LandingGear_Patch));

                // Patch Pilot Player input states for throttle overriding
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

                // Use silent standard C# GetMethod to avoid Harmony console warnings on missing prefixes/postfixes
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
            // Reset toggles if not inside a local aircraft (e.g. menu, spectator, dead, ejected)
            Aircraft localAircraft;
            if (!GameManager.GetLocalAircraft(out localAircraft) || localAircraft == null)
            {
                if (WheelbrakeToggled || AirbrakeToggled || BothBrakesToggled)
                {
                    WheelbrakeToggled = AirbrakeToggled = BothBrakesToggled = false;
                    Log.LogInfo("Not in local aircraft, resetting brake toggles.");
                }
                return;
            }

            // Prevent toggle input when in chat
            bool inChat = false;
            try { inChat = CursorManager.GetFlag(CursorFlags.Chat); } catch {}
            if (inChat) return;

            // Check shortcuts (Only trigger if the respective key is in Toggle Mode)
            if (!WheelbrakeHoldMode.Value && WheelbrakeKey.Value.IsDown())
            {
                WheelbrakeToggled = !WheelbrakeToggled;
                BothBrakesToggled = WheelbrakeToggled && AirbrakeToggled;
                Log.LogInfo($"Wheelbrakes toggled: {WheelbrakeToggled}");
            }

            if (!AirbrakeHoldMode.Value && AirbrakeKey.Value.IsDown())
            {
                AirbrakeToggled = !AirbrakeToggled;
                BothBrakesToggled = WheelbrakeToggled && AirbrakeToggled;
                Log.LogInfo($"Airbrakes toggled: {AirbrakeToggled}");
            }

            if (!BothBrakesHoldMode.Value && BothBrakesKey.Value.IsDown())
            {
                BothBrakesToggled = !BothBrakesToggled;
                WheelbrakeToggled = AirbrakeToggled = BothBrakesToggled;
                Log.LogInfo($"Both brakes toggled: {BothBrakesToggled}");
            }
        }
    }

    // ==========================================
    // WHEELBRAKE PATCH
    // ==========================================
    public static class LandingGear_Patch
    {
        public static void Prefix(Component __instance)
        {
            if (!Plugin.IsWheelbrakeActive || __instance == null) return;

            Aircraft aircraft = __instance.GetComponentInParent<Aircraft>();
            Aircraft localAircraft;
            if (GameManager.GetLocalAircraft(out localAircraft) && aircraft == localAircraft && aircraft.controlInputs != null)
            {
                aircraft.controlInputs.brake = 1f; // Force brake in FixedUpdate
            }
        }
    }

    // ==========================================
    // PILOT PLAYER STATE PATCH
    // ==========================================
    public static class PilotPlayerState_Patch
    {
        public static void Postfix(PilotPlayerState __instance)
        {
            if (!Plugin.IsAirbrakeActive || __instance == null || __instance.controlInputs == null) return;

            Aircraft localAircraft;
            if (GameManager.GetLocalAircraft(out localAircraft) && localAircraft != null)
            {
                __instance.controlInputs.throttle = 0f; // Force throttle to 0f
            }
        }
    }
}