using System;
using System.Collections;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SbgShields
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid    = "com.sbg.shields";
        public const string Name    = "SBG Shields";
        public const string Version = "0.7.7";

        internal static ManualLogSource Log;

        // Audio
        internal static ConfigEntry<bool>  SuppressMuffle;
        internal static ConfigEntry<bool>  SuppressHum;

        // Shield
        internal static ConfigEntry<float> ActivationCooldown;
        internal static ConfigEntry<bool>  ShieldAbsorbsHits;
        internal static ConfigEntry<int>   MaxPips;
        internal static ConfigEntry<float> UseCooldown;
        internal static ConfigEntry<float> BreakCooldown;
        internal static ConfigEntry<bool>  RestoreAfterBreakCooldown;
        internal static ConfigEntry<float> BreakStunDuration;
        internal static ConfigEntry<bool>  BreakStunScalesWithPercent;
        internal static ConfigEntry<float> BreakStunDurationAtMax;
        internal static ConfigEntry<bool>  PartialBreakLaunches;
        internal static ConfigEntry<bool>  RefundPipsOnRefusedKnockout;
        internal static ConfigEntry<bool>  BreakImmunityScalesWithPercent;
        internal static ConfigEntry<float> BreakImmunityAtZeroPercent;
        internal static ConfigEntry<float> BreakImmunityAtMaxPercent;
        internal static ConfigEntry<bool>  AbsorbedHitsCancelKnockback;
        internal static ConfigEntry<float> ReflectionSearchMargin;
        internal static ConfigEntry<string> CostOverrides;

        // Percent
        internal static ConfigEntry<float> MaxPercent;
        // Parry
        internal static ConfigEntry<bool>  PerfectParry;
        internal static ConfigEntry<float> PerfectParryWindow;
        internal static ConfigEntry<bool>  PerfectParryBeatsFullBreak;
        internal static ConfigEntry<bool>  PerfectParryBeatsUnblockable;
        internal static ConfigEntry<bool>  PerfectParryRefundsUse;
        internal static ConfigEntry<bool>  PerfectParrySound;
        internal static ConfigEntry<float> ParryLinger;
        internal static ConfigEntry<bool>  ParryGlow;
        internal static ConfigEntry<float> ParryGlowDuration;
        internal static ConfigEntry<float> ParryGlowBoost;

        internal static ConfigEntry<float> PercentForMaxScaling;
        internal static ConfigEntry<float> ForceMultiplierAtKill;
        internal static ConfigEntry<float> KnockbackExponent;
        internal static ConfigEntry<float> HitstunMultiplierAtMax;
        internal static ConfigEntry<float> PercentPerHitBase;
        internal static ConfigEntry<float> PercentPerPip;
        internal static ConfigEntry<float> PercentPerFullBreakHit;
        internal static ConfigEntry<float> PercentPerUnblockableHit;
        internal static ConfigEntry<bool>  ExplosionPercentFalloff;
        internal static ConfigEntry<float> ExplosionFalloffRadius;
        internal static ConfigEntry<float> ExplosionPercentAtEdge;
        internal static ConfigEntry<float> ExplosionForceAtEdge;
        internal static ConfigEntry<bool>  ExplosionRadialLaunch;
        internal static ConfigEntry<float> ExplosionRadialWeight;
        internal static ConfigEntry<float> PercentGainOnFullBreak;
        internal static ConfigEntry<float> PercentReductionBetweenHoles;
        internal static ConfigEntry<float> PercentLostOnRespawn;
        internal static ConfigEntry<float> RespawnFatigueWindow;
        internal static ConfigEntry<bool>  KillZoneEnabled;
        internal static ConfigEntry<float> KillPercent;
        internal static ConfigEntry<float> PercentAfterKillZoneDeath;
        internal static ConfigEntry<bool>  KillZoneFlash;
        internal static ConfigEntry<float> KillZoneFlashSize;
        internal static ConfigEntry<float> KillZoneUpwardBoost;
        internal static ConfigEntry<float> KillZoneMaxRiseTime;
        internal static ConfigEntry<float> KillZoneDeathLinger;
        internal static ConfigEntry<bool>  KillZoneBoom;

        // Launch
        internal static ConfigEntry<bool>  ShapeLaunches;
        internal static ConfigEntry<float> MinLaunchAngleAtZero;
        internal static ConfigEntry<float> MinLaunchAngleAtMax;
        internal static ConfigEntry<float> MaxHorizontalLaunchSpeed;
        internal static ConfigEntry<float> HorizontalMultiplierAtKill;
        internal static ConfigEntry<float> LaunchDrag;
        internal static ConfigEntry<float> LaunchVerticalDragFactor;
        internal static ConfigEntry<float> LaunchDragDuration;
        internal static ConfigEntry<float> LaunchDragAboveSpeed;
        internal static ConfigEntry<float> LaunchHangTime;
        internal static ConfigEntry<float> LaunchHangWindow;
        internal static ConfigEntry<float> LaunchHangDuration;
        internal static ConfigEntry<float> CloudHitMinPercent;
        internal static ConfigEntry<bool>  LaunchTrail;
        internal static ConfigEntry<float> LaunchTrailStartSpeed;
        internal static ConfigEntry<float> LaunchTrailMinPercent;
        internal static ConfigEntry<float> LaunchTrailStopSpeed;
        internal static ConfigEntry<float> LaunchTrailRate;
        internal static ConfigEntry<float> LaunchTrailRatePerMetre;
        internal static ConfigEntry<float> LaunchTrailSize;
        internal static ConfigEntry<float> LaunchTrailLifetime;
        internal static ConfigEntry<float> LaunchTrailAlpha;

        // Rooting
        internal static ConfigEntry<bool>  RootWhileShielded;
        internal static ConfigEntry<bool>  BlockJumpWhileShielded;
        internal static ConfigEntry<bool>  BlockSwingWhileShielded;
        internal static ConfigEntry<bool>  BlockDiveWhileShielded;
        internal static ConfigEntry<bool>  AllowMidAirActivation;
        internal static ConfigEntry<bool>  BlockActivationDuringSwing;
        internal static ConfigEntry<bool>  BlockActivationDuringSpringBoots;
        internal static ConfigEntry<bool>  BlockActivationInMenus;
        internal static ConfigEntry<bool>  BlockActivationDuringImmunity;
        internal static ConfigEntry<bool>  BlockItemUseWhileShielded;
        internal static ConfigEntry<bool>  BlockAimWhileShielded;
        internal static ConfigEntry<bool>  BreakStunIgnoresComebackImmunity;
        internal static ConfigEntry<bool>  RequireAllPlayersModded;
        internal static ConfigEntry<float> HandshakeTimeout;
        /// <summary>
        /// Deliberately NOT a config entry. "Do not run in public lobbies" is the one
        /// rule that protects people who never agreed to play with this, and a checkbox
        /// anyone can untick is not a rule. Editing the DLL defeats it, but that is a
        /// different act from flipping a setting.
        /// </summary>
        internal const bool PrivateLobbiesOnly = true;
        internal static ConfigEntry<float> MismatchPopupDuration;
#if SBG_DEV
        internal static ConfigEntry<bool>  EnableChatCommands;
#endif

        // Bubble visual
        internal static ConfigEntry<bool>  TintVanillaShield;


        // HUD
        internal static ConfigEntry<bool>   ShowHud;
        internal static ConfigEntry<float>  HudScale;
        internal static ConfigEntry<float>  HudBottomMargin;
        internal static ConfigEntry<float>  HudHorizontalOffset;
        internal static ConfigEntry<float>  BubbleHudSize;
        internal static ConfigEntry<float>  BubbleHudGap;
        internal static ConfigEntry<bool>   BubbleHudOnLeft;
        internal static ConfigEntry<bool>   ShowPipDots;
        internal static ConfigEntry<string> ConfigVersion;
        internal static ConfigEntry<string> PercentFontName;
        internal static ConfigEntry<int>    PercentFontSize;
        internal static ConfigEntry<float>  PercentShakeDuration;
        internal static ConfigEntry<float>  PercentShakePixels;
        internal static ConfigEntry<string> PercentShakeDurationTable;
        internal static ConfigEntry<float>  PercentShakeSpeed;
        internal static ConfigEntry<float>  PercentBlurSamples;
        internal static ConfigEntry<float>  PercentBlurLength;
        internal static ConfigEntry<float>  PercentShakePunch;
        internal static ConfigEntry<float>  PercentVerticalOffset;

        // Debug
        internal static ConfigEntry<bool>  VerboseLogging;
#if SBG_DEV
        internal static ConfigEntry<bool>   DebugKeys;
        internal static ConfigEntry<float>  DebugSetPercent;
        internal static ConfigEntry<string> DebugGiveItem;
#endif

        private Harmony _harmony;
        private static Plugin _instance;

        /// <summary>The plugin is a MonoBehaviour; lend that to static helpers that need a coroutine.</summary>
        internal static void RunCoroutine(IEnumerator routine)
        {
            if (_instance != null && routine != null) _instance.StartCoroutine(routine);
        }

        // Direct access to PlayerInfo's private activation timestamp. Keeping this
        // fresh is what makes ShouldBeActive() keep returning true, so we never
        // have to patch the local function itself.
        private static AccessTools.FieldRef<PlayerInfo, double> _activationTimestamp;

        private static double _lastActivationTime = double.MinValue;
        private static bool   _weActivated;
        internal static bool  WeActivated => _weActivated;
        internal static double LastActivationTime => _lastActivationTime;
        internal static double LastOurShieldReleaseTime = double.MinValue;

        /// <summary>
        /// The shield's body outlives the keypress by ParryLinger. WeActivated goes false
        /// the instant you let go -- rooting, jumping and swinging come straight back --
        /// but the collider stays, so vanilla reflection and the shield-hit effects still
        /// have something to work with while the parry window is open.
        /// </summary>
        private static double _lingerUntil = double.MinValue;

        internal static bool ShieldLingering => Time.timeAsDouble < _lingerUntil;

        private void Awake()
        {
            Log = Logger;
            BindConfig();
            ResetConfigIfVersionChanged();

            try
            {
                _activationTimestamp = AccessTools.FieldRefAccess<PlayerInfo, double>(
                    "localPlayerElectromagnetShieldActivationTimestamp");
            }
            catch (Exception e)
            {
                Log.LogError("Could not bind the shield activation timestamp field. " +
                             "The game probably updated and renamed it. " + e);
                return;
            }

            _instance = this;
            _harmony = new Harmony(Guid);
            _harmony.PatchAll();

            ShieldState.Pips = MaxPips.Value;
            Hud.Init();

            ModHandshake.Reset("startup");
            try { CourseManager.MatchStateChanged += OnMatchStateChanged; }
            catch (Exception e) { Log.LogWarning("Could not subscribe to MatchStateChanged; pips will not reset per hole. " + e.Message); }

#if SBG_DEV
            const string buildKind = "DEV build: chat commands and debug keys compiled in";
#else
            const string buildKind = "RELEASE build: no debug tooling";
#endif
            Log.LogInfo($"{Name} {Version} loaded ({buildKind}). Hold Left Shift to shield. Pips={MaxPips.Value}, break cooldown={BreakCooldown.Value}s.");
        }

        private void BindConfig()
        {
            SuppressMuffle = Config.Bind("Audio", "SuppressMuffle", true,
                "Stop the underwater/lowpass snapshot when the shield goes up. The activation sound still plays.");
            SuppressHum = Config.Bind("Audio", "SuppressHum", true,
                "Stop the looping shield hum for the Shift shield. The vanilla magnet item always keeps its hum.");

            ActivationCooldown = Config.Bind("Shield", "ActivationCooldown", 0.6f,
                "Hard minimum seconds between activations. Keep at or above 0.5 -- the server's " +
                "'Activate electromagnet' rate checker flags activations closer than 0.5s apart.");
            ShieldAbsorbsHits = Config.Bind("Shield", "ShieldAbsorbsHits", true,
                "Master switch for the pip economy. Off = vanilla shield behaviour (blocks everything, never breaks).");
            MaxPips = Config.Bind("Shield", "MaxPips", 5,
                "Shield HP. Chip hits cost 1-3, big hits break it outright. No regeneration.");
            UseCooldown = Config.Bind("Shield", "UseCooldown", 1.0f,
                "Seconds after releasing the shield before it can be raised again. Anti-flicker.");
            BreakCooldown = Config.Bind("Shield", "BreakCooldown", 8.0f,
                "Seconds the shield is unavailable after it breaks. The punish.");
            RestoreAfterBreakCooldown = Config.Bind("Shield", "RestoreAfterBreakCooldown", true,
                "When the break cooldown ends, restore the shield to full pips. Off = no shield until the next hole.");
            BreakStunDuration = Config.Bind("Shield", "BreakStunDuration", 2.5f,
                "Seconds you are stunned in place when a hit breaks the shield, at 0%.");
            BreakStunScalesWithPercent = Config.Bind("Shield", "BreakStunScalesWithPercent", true,
                "Break stun grows with percent, from BreakStunDuration to BreakStunDurationAtMax. Turn this off with the rest of the " +
                "percent rules and every break costs the same flat BreakStunDuration.");
            BreakStunDurationAtMax = Config.Bind("Shield", "BreakStunDurationAtMax", 3.5f,
                "Break stun at PercentForMaxScaling. Note this runs OPPOSITE to HitstunMultiplierAtMax on purpose: a launch gives you " +
                "the air back sooner the more beaten up you are, but a break pins you longer. Getting launched is survivable; losing " +
                "the shield at high percent should be the moment that costs you. BreakImmunityAtMaxPercent is the compensation.");
            PartialBreakLaunches = Config.Bind("Shield", "PartialBreakLaunches", false,
                "Off (default): EVERY break stuns in place, and only percent takes the uncovered fraction of the hit. " +
                "On: a hit that beat your remaining pips launches you at that fraction with a shortened knockdown instead of stunning. " +
                "This was the old behaviour and is why partial breaks felt like an instant wake-up.");
            RefundPipsOnRefusedKnockout = Config.Bind("Shield", "RefundPipsOnRefusedKnockout", true,
                "If the game refuses the knockout after your shield spent pips on the hit (comeback immunity, team protection, " +
                "frozen, self hit), give the pips back. The bubble still pops, but a hit that could never stun you does not cost you the shield.");
            BreakImmunityScalesWithPercent = Config.Bind("Shield", "BreakImmunityScalesWithPercent", true,
                "After a break stun, the knockout-immunity bubble (the blue one) lasts a percent-scaled time instead of the match rule. " +
                "Every other knockout keeps the vanilla immunity, including the orange repeat protection.");
            BreakImmunityAtZeroPercent = Config.Bind("Shield", "BreakImmunityAtZeroPercent", 0.5f,
                "Seconds of immunity after a break stun at 0%. Swap the two values to make high percent LESS protected instead.");
            BreakImmunityAtMaxPercent = Config.Bind("Shield", "BreakImmunityAtMaxPercent", 3.0f,
                "Seconds of immunity after a break stun at PercentForMaxScaling. Default gives more breathing room the more beaten up you are.");
            AbsorbedHitsCancelKnockback = Config.Bind("Shield", "AbsorbedHitsCancelKnockback", true,
                "Chip hits that the shield absorbs also cancel the pushback. Off = vanilla shove-while-shielded.");
            ReflectionSearchMargin = Config.Bind("Shield", "ReflectionSearchMargin", 1.5f,
                "Metres beyond the shield radius to search for the projectile that just bounced off, to identify it.");

            CostOverrides = Config.Bind("Costs", "Overrides", "",
                "Per-type pip cost overrides, comma separated. Values: a number, 'full', or 'unblockable'. " +
                "Example: Landmine=2, Rocket=4, GolfCart=full. Type names are KnockoutType enum names: " +
                "Swing, SwingProjectile, ReturnedBall, DuelingPistol, ElephantGun, GolfCart, Rocket, RocketBackBlast, " +
                "Landmine, ReflectedSwingProjectile, ReflectedRocket, DeflectedDuelingPistolShot, DeflectedElephantGunShot, " +
                "OrbitalLaserPeripheralHit, RocketDriverSwing, RocketDriverSwingPostHitSpin, RocketDriverSwingProjectile, " +
                "FreezeBomb, ReflectedFreezeBomb, ThunderstormPeripheralHit, ThunderstormDirectHit, OrbitalLaserDirectHit, " +
                "RailgunDirectHit, TrafficVehicle, JumboBurgerGiantSwing, JumboBurgerGiantSwingProjectile, JumboBurgerGiantCollision, " +
                "ElectromagnetShieldExplosion. Defaults: pistols/untargeted balls 1; backblast/peripheral 2; carts/vehicles/rocket driver/mines 3; " +
                "swings/targeted balls/rockets/freeze/giant = full; laser/thunder/railgun direct = unblockable.");

            MaxPercent = Config.Bind("Percent", "MaxPercent", 300f, "Percent cap.");
            PerfectParry = Config.Bind("Parry", "PerfectParry", true,
                "A hit that lands just as you DROP the shield is a perfect parry: fully absorbed, no pips spent, no percent gained. " +
                "Holding the shield out and getting hit is an ordinary block; letting go into the hit is the read.");
            PerfectParryWindow = Config.Bind("Parry", "PerfectParryWindow", 0.2f,
                "Seconds after you release the shield key during which a hit counts as a perfect parry. This is the whole difficulty " +
                "knob: 0.2 is forgiving, 0.1 asks for a real read. Only a deliberate release opens it -- a shield that broke does not.");
            PerfectParryBeatsFullBreak = Config.Bind("Parry", "PerfectParryBeatsFullBreak", true,
                "A perfect parry stops the hits that normally break the shield outright (swings, carts, targeted balls). " +
                "This is the point of the mechanic: timing beats a hit that pips cannot.");
            PerfectParryBeatsUnblockable = Config.Bind("Parry", "PerfectParryBeatsUnblockable", false,
                "Whether a perfect parry also stops railgun/orbital/thunderstorm direct hits. Off: unblockable stays unblockable.");
            PerfectParryRefundsUse = Config.Bind("Parry", "PerfectParryRefundsUse", true,
                "Clear the use cooldown on a parry, so reading a hit correctly does not cost you the next shield.");
            PerfectParrySound = Config.Bind("Parry", "PerfectParrySound", true,
                "Play the game's own blocked-knockout sound on a parry, over the normal shield hit.");

            ParryLinger = Config.Bind("Parry", "ParryLinger", 0.2f,
                "Seconds the shield stays physically up after you let go of the key. You get your movement back immediately -- only the " +
                "shield's body lingers. This is what makes a parry visible to everyone else: while the shield exists the game does its " +
                "own work, so homing balls bounce back at whoever threw them and the shield-hit sound plays on every client. " +
                "0 = the shield vanishes with the keypress and parries are silent to everyone but you.");
            ParryGlow = Config.Bind("Parry", "ParryGlow", true,
                "Flash the shield bright when a parry lands.");
            ParryGlowDuration = Config.Bind("Parry", "ParryGlowDuration", 0.35f, "How long the parry flash lasts, in seconds.");
            ParryGlowBoost = Config.Bind("Parry", "ParryGlowBoost", 3f,
                "How much brighter the flash is than your normal shield colour. The tint pipeline scales by the material's own " +
                "intensity, so this multiplies rather than washing out to white.");

            PercentForMaxScaling = Config.Bind("Percent", "PercentForMaxScaling", 100f,
                "Percent at which the things that SATURATE (angle floor, hang time, hitstun, break immunity, HUD shake) reach their max. " +
                "Knockback itself does not use this -- it keeps climbing to KillPercent.");
            ForceMultiplierAtKill = Config.Bind("Percent", "ForceMultiplierAtKill", 6f,
                "Knockback speed multiplier at the kill line. 1.0x at 0%, and the curve between is set by KnockbackExponent. " +
                "Defaults give roughly: 40% 1.4x, 80% 1.9x, 100% 2.3x, 150% 3.3x, 200% 4.6x, 250% 6x.");
            KnockbackExponent = Config.Bind("Percent", "KnockbackExponent", 1.5f,
                "Shape of the knockback curve. 1 = straight line to the kill line. Higher = flatter early, steeper late. 2 is very back-loaded.");
            HitstunMultiplierAtMax = Config.Bind("Percent", "HitstunMultiplierAtMax", 0.8f,
                "Knockout duration multiplier at PercentForMaxScaling, interpolated from 1.0x at 0%. BELOW 1 by default: the hit that " +
                "sends you furthest is also the one that gives you back the earliest, so on a big launch the stun runs out mid-air and " +
                "you dive where you want to land. Above 1 gives the old behaviour (longer stun the more beaten up you are), which " +
                "reads as being juggled. Set 1.0 for vanilla stun at every percent.");
            PercentPerHitBase = Config.Bind("Percent", "PercentPerHitBase", 5f,
                "Percent gained by any chip-class hit, before the per-pip part.");
            PercentPerPip = Config.Bind("Percent", "PercentPerPip", 4f,
                "Extra percent per pip of the hit's cost (a 3-pip hit = base + 3*this).");
            PercentPerFullBreakHit = Config.Bind("Percent", "PercentPerFullBreakHit", 25f,
                "Percent gained from a full-break-class hit landing unshielded.");
            ExplosionPercentFalloff = Config.Bind("Percent", "ExplosionPercentFalloff", true,
                "Scale percent from explosions by how far you were from the blast, the way the game already scales the knockback.");
            ExplosionFalloffRadius = Config.Bind("Percent", "ExplosionFalloffRadius", 8f,
                "Distance in metres at which an explosion does its minimum percent. Closer than this scales between full and minimum.");
            ExplosionPercentAtEdge = Config.Bind("Percent", "ExplosionPercentAtEdge", 0.3f,
                "Fraction of the normal percent for a hit at the edge of the blast (0.3 = 30%).");
            ExplosionForceAtEdge = Config.Bind("Launch", "ExplosionForceAtEdge", 0.25f,
                "How much of the PERCENT force bonus applies at the edge of a blast (0.25 = a quarter). At the centre it is the full bonus. " +
                "This is why 100% does not mean 100% knockback from a distant explosion.");
            ExplosionRadialLaunch = Config.Bind("Launch", "ExplosionRadialLaunch", true,
                "Launch explosions away from where the blast actually was, instead of the game's forced-upward direction.");
            ExplosionRadialWeight = Config.Bind("Launch", "ExplosionRadialWeight", 0.8f,
                "0 = the game's direction, 1 = purely away from the blast. 0.8 keeps a little of the vanilla lift so a blast at your feet still pops you up.");
            PercentPerUnblockableHit = Config.Bind("Percent", "PercentPerUnblockableHit", 30f,
                "Percent gained from an unblockable hit.");
            PercentGainOnFullBreak = Config.Bind("Percent", "PercentGainOnFullBreak", 0f,
                "Fraction of the hit's percent you still take when the shield breaks and you are stunned in place.");
            PercentReductionBetweenHoles = Config.Bind("Percent", "PercentReductionBetweenHoles", 1f,
                "Fraction of percent removed at the start of each hole. 1 = full wipe.");
            PercentLostOnRespawn = Config.Bind("Percent", "PercentLostOnRespawn", 25f,
                "Flat percent removed when you respawn (not a fraction). Small enough that dying on purpose is not attractive.");
            RespawnFatigueWindow = Config.Bind("Percent", "RespawnFatigueWindow", 45f,
                "Seconds after a respawn during which another respawn gives NO percent reduction. Stops suicide-resetting.");
            KillZoneEnabled = Config.Bind("Percent", "KillZoneEnabled", true,
                "At or above KillPercent, the apex of the launch that got you there is a star KO: flash, respawn.");
            KillPercent = Config.Bind("Percent", "KillPercent", 250f, "The kill line.");
            PercentAfterKillZoneDeath = Config.Bind("Percent", "PercentAfterKillZoneDeath", 25f,
                "Percent you respawn with after a star KO. The normal respawn halving does not apply on top.");
            KillZoneFlash = Config.Bind("Percent", "KillZoneFlash", true, "Star flash + screenshake on a star KO (local only for now).");
            KillZoneFlashSize = Config.Bind("Percent", "KillZoneFlashSize", 9f, "Star flash size in metres.");
            KillZoneUpwardBoost = Config.Bind("Percent", "KillZoneUpwardBoost", 45f,
                "Extra upward speed (m/s) added to the killing blow. You are already dead, so this is pure showmanship: you rocket off screen.");
            KillZoneMaxRiseTime = Config.Bind("Percent", "KillZoneMaxRiseTime", 2.5f,
                "Seconds to wait for the apex before killing anyway, so a boost off a ceiling cannot stall the death.");
            KillZoneDeathLinger = Config.Bind("Percent", "KillZoneDeathLinger", 5f,
                "Seconds you stay gone after the star flash before respawning. The camera holds on the spot.");
            KillZoneBoom = Config.Bind("Percent", "KillZoneBoom", true,
                "Play the shield-explosion boom and a heavy screenshake on a star KO. Placeholder until custom SFX.");

            ShapeLaunches = Config.Bind("Launch", "ShapeLaunches", true,
                "Raise the launch angle with percent and cap horizontal speed. Vertical launches stop juggling.");
            MinLaunchAngleAtZero = Config.Bind("Launch", "MinLaunchAngleAtZero", 8f,
                "Minimum launch elevation in degrees at 0%. Low = the hit direction decides.");
            MinLaunchAngleAtMax = Config.Bind("Launch", "MinLaunchAngleAtMax", 28f,
                "Minimum launch elevation in degrees at PercentForMaxScaling. 90 = straight up. Kept low so launches stay punishable.");
            HorizontalMultiplierAtKill = Config.Bind("Launch", "HorizontalMultiplierAtKill", 2.0f,
                "EXTRA horizontal multiplier at the kill line, on top of the force multiplier, on the same curve. " +
                "This is what makes late hits go away rather than up. The cap below still applies afterwards.");
            MaxHorizontalLaunchSpeed = Config.Bind("Launch", "MaxHorizontalLaunchSpeed", 34f,
                "Horizontal speed cap (m/s); excess becomes height. 0 = no cap. This is the knob that decides how much of a big launch " +
                "reads as distance versus altitude: lower it and high-percent hits turn into elevator rides. Mostly a lake guard now.");
            LaunchDrag = Config.Bind("Launch", "LaunchDrag", 0.8f,
                "Extra air drag per second on the part of your speed above LaunchDragAboveSpeed, while tumbling after a launch. 0 = off.");
            LaunchVerticalDragFactor = Config.Bind("Launch", "LaunchVerticalDragFactor", 0.5f,
                "How much of LaunchDrag applies to upward speed (0-1). Lower = higher, floatier arc.");
            LaunchDragDuration = Config.Bind("Launch", "LaunchDragDuration", 1.5f,
                "Seconds after a hit that LaunchDrag applies.");
            LaunchDragAboveSpeed = Config.Bind("Launch", "LaunchDragAboveSpeed", 14f,
                "Drag only touches speed ABOVE this (m/s). Everything below travels freely, so a launch keeps its sideways distance; " +
                "only the extreme burst of a huge hit gets tamed.");
            LaunchHangTime = Config.Bind("Launch", "LaunchHangTime", 0f,
                "How much gravity is cancelled at the top of a cloud-hit arc (0.55 = 55% at the very apex, tapering to none as you speed up). " +
                "OFF by default: it reads as slowing down mid-air then speeding up, because that is what it is. Try 0.3-0.5 on cloud hits only.");
            LaunchHangWindow = Config.Bind("Launch", "LaunchHangWindow", 7f,
                "Vertical speed (m/s) within which hang time applies. Only near the apex, where vertical speed is small, so the rise and fall keep their shape.");
            LaunchHangDuration = Config.Bind("Launch", "LaunchHangDuration", 3f,
                "Seconds after a launch during which hang time can apply.");
            CloudHitMinPercent = Config.Bind("Launch", "CloudHitMinPercent", 125f,
                "Percent at or above which a launch counts as a cloud hit. Hang time is the only thing left that reads it.");
            LaunchTrail = Config.Bind("Launch", "LaunchTrail", true,
                "Skin-colored smoke trail on any player flying fast while knocked out.");
            LaunchTrailStartSpeed = Config.Bind("Launch", "LaunchTrailStartSpeed", 12f,
                "Speed (m/s) needed to start the trail. Small shoves do not get one.");
            LaunchTrailMinPercent = Config.Bind("Launch", "LaunchTrailMinPercent", 75f,
                "Percent you must be at before your launches smoke. Other players' percent is not synced yet, so " +
                "their trails still use the speed threshold alone.");
            LaunchTrailStopSpeed = Config.Bind("Launch", "LaunchTrailStopSpeed", 5f,
                "Speed (m/s) below which the trail stops.");
            LaunchTrailRate = Config.Bind("Launch", "LaunchTrailRate", 45f, "Smoke puffs per second, on top of the per-metre rate.");
            LaunchTrailRatePerMetre = Config.Bind("Launch", "LaunchTrailRatePerMetre", 5f,
                "Extra puffs per metre travelled, so a fast launch leaves a continuous cloud rather than dots.");
            LaunchTrailSize = Config.Bind("Launch", "LaunchTrailSize", 2.2f, "Puff size in metres at its largest.");
            LaunchTrailLifetime = Config.Bind("Launch", "LaunchTrailLifetime", 1.7f, "Seconds each puff lingers.");
            LaunchTrailAlpha = Config.Bind("Launch", "LaunchTrailAlpha", 1.0f, "Puff opacity at spawn.");

            RootWhileShielded = Config.Bind("Rooting", "RootWhileShielded", true,
                "Shielding pins you in place: no walking. Existing momentum carries until ground drag stops you.");
            BlockJumpWhileShielded = Config.Bind("Rooting", "BlockJumpWhileShielded", true,
                "Disallow jumping while the shield is up.");
            BlockSwingWhileShielded = Config.Bind("Rooting", "BlockSwingWhileShielded", true,
                "Cannot start charging a swing while the shield is up.");
            BlockDiveWhileShielded = Config.Bind("Rooting", "BlockDiveWhileShielded", true,
                "Cannot dive while the shield is up. Forced dives are unaffected.");
            AllowMidAirActivation = Config.Bind("Rooting", "AllowMidAirActivation", true,
                "Shield can be raised in the air. Momentum carries; you are rooted on landing.");
            BlockActivationDuringSwing = Config.Bind("Rooting", "BlockActivationDuringSwing", true,
                "Shield cannot be raised while charging or mid-swing.");
            BlockActivationDuringSpringBoots = Config.Bind("Rooting", "BlockActivationDuringSpringBoots", true,
                "Shield cannot be raised while spring boots are active, and goes down if you spring while holding it.");
            BlockActivationInMenus = Config.Bind("Rooting", "BlockActivationInMenus", true,
                "Shift does nothing while the pause menu, scoreboard, text chat or emote wheel is open, so you cannot burn pips by accident.");
            BlockItemUseWhileShielded = Config.Bind("Rooting", "BlockItemUseWhileShielded", true,
                "No using items (or spring boots) while the shield is up.");
            BlockAimWhileShielded = Config.Bind("Rooting", "BlockAimWhileShielded", true,
                "No aiming while the shield is up. Swapping weapons is still allowed.");
            BreakStunIgnoresComebackImmunity = Config.Bind("Rooting", "BreakStunIgnoresComebackImmunity", true,
                "A hit that breaks your shield stuns you even if the game's comeback bubble is up. Off = vanilla behaviour, " +
                "where comeback protection refuses the knockout and you get no stun at all.");
            RequireAllPlayersModded = Config.Bind("Network", "RequireAllPlayersModded", true,
                "Stand down (vanilla rules, no shield, no percent) unless every other player in the lobby is running this exact version. " +
                "Players announce themselves over the game's own chat channel; anyone without the mod sees one line of plain text saying who is modded. " +
                "NOTE: this runs on your machine, so it stops accidents and version drift, not a determined cheater -- only a server-side check could do that.");
            MismatchPopupDuration = Config.Bind("Network", "MismatchPopupDuration", 12f,
                "Seconds the big version-mismatch panel stays up before shrinking to a single line at the top of the screen.");
            HandshakeTimeout = Config.Bind("Network", "HandshakeTimeout", 12f,
                "Seconds to wait for a player to announce before treating them as unmodded. Raise it on slow connections.");
            BlockActivationDuringImmunity = Config.Bind("Rooting", "BlockActivationDuringImmunity", true,
                "Shift does nothing while the game's own comeback bubble (blue recovery protection or gold repeat protection) is up. " +
                "Nothing can knock you out then, so a shield would only waste pips.");

            TintVanillaShield = Config.Bind("Bubble", "TintVanillaShield", true,
                "Recolor the game's own shield particle (hold, dissolve, hit sparks, break) to your skin color for the Shift shield. The magnet item stays team-colored.");

            ShowHud = Config.Bind("HUD", "ShowHud", true, "Show the percent + bubble readiness overlay.");
            HudScale = Config.Bind("HUD", "HudScale", 1.0f, "Overall HUD scale.");
            HudBottomMargin = Config.Bind("HUD", "HudBottomMargin", 150f, "Pixels from the bottom of the screen. Raised so the percent clears the golf-cart Exit prompt.");
            HudHorizontalOffset = Config.Bind("HUD", "HudHorizontalOffset", 0f, "Pixels to shift the percent left/right from centre.");
            BubbleHudSize = Config.Bind("HUD", "BubbleHudSize", 64f, "Bubble readiness icon size in pixels at HudScale 1.");
            BubbleHudGap = Config.Bind("HUD", "BubbleHudGap", 22f, "Gap between the percent and the icon.");
            BubbleHudOnLeft = Config.Bind("HUD", "BubbleHudOnLeft", true, "Icon left of the percent (away from the item bar). Off = right of it.");
            ShowPipDots = Config.Bind("HUD", "ShowPipDots", true, "Show pip dots under the icon. Debug aid; the shield itself should carry this in the final build.");
            PercentFontName = Config.Bind("HUD", "PercentFontName", "DFGothic-EB",
                "OS-installed font to use for the percent. Matched loosely (spaces/dashes ignored). " +
                "Unity can only load fonts installed on the system, so install DF Gothic first. Falls back to the default font.");
            PercentFontSize = Config.Bind("HUD", "PercentFontSize", 72, "Percent font size in pixels at HudScale 1.");
            PercentShakeDurationTable = Config.Bind("HUD", "ShakeDurationTable", "0:0.8, 30:2, 60:3, 90:5",
                "How long the shake takes to settle, as percent:seconds thresholds. Values between two thresholds interpolate, " +
                "so with the default a hit at 45% settles over 2.5s. The shake always decays to nothing -- the number is still when you are not being hit.");
            PercentShakeDuration = Config.Bind("HUD", "PercentShakeDuration", 2f,
                "Fallback settle time if ShakeDurationTable cannot be read.");
            PercentShakePixels = Config.Bind("HUD", "PercentShakePixels", 5f,
                "Rumble size in pixels immediately after a hit, decaying to zero. This is a rattle, not a bounce; keep it small.");
            PercentShakeSpeed = Config.Bind("HUD", "PercentShakeSpeed", 90f,
                "How fast the rumble moves. Higher is a tighter buzz.");
            PercentShakePunch = Config.Bind("HUD", "PercentShakePunch", 0.3f,
                "Momentary size pop on a hit (0.3 = 30% bigger), scaled about the number's centre. This is what gives the impact.");
            PercentBlurSamples = Config.Bind("HUD", "MotionBlurSamples", 3f,
                "Ghost copies drawn behind the number while it moves, to fake motion blur. 0 = off.");
            PercentBlurLength = Config.Bind("HUD", "MotionBlurLength", 2.5f,
                "How far the ghosts trail behind the movement. Higher smears more.");
            PercentVerticalOffset = Config.Bind("HUD", "PercentVerticalOffset", 14f,
                "Pixels to push the percent down so its digits line up with the middle of the bubble icon. " +
                "Text sits above its own baseline, so a bottom-aligned number reads high next to a circle.");

            ConfigVersion = Config.Bind("Meta", "ConfigVersion", "",
                "Internal. When the mod version changes, all settings are reset to the new defaults so retunes actually land.");

            // Off by default in every build. In a Release build the cheat tooling below
            // is not merely disabled, it is not compiled in at all -- see SBG_DEV.
            VerboseLogging = Config.Bind("Debug", "VerboseLogging", false,
                "Log every activation, absorb, break and percent change. Noisy; turn on when reporting a problem.");
#if SBG_DEV
            DebugKeys = Config.Bind("Debug", "DebugKeys", false,
                "DEV BUILD. F6 = +25 percent, F7 = -1 pip, F8 = reset, F10 = repeat last /give.");
            DebugSetPercent = Config.Bind("Debug", "SetPercent", -1f,
                "DEV BUILD. Set the percent you want; applied at once, then springs back to -1. Only works during a hole.");
            EnableChatCommands = Config.Bind("Debug", "ChatCommands", false,
                "DEV BUILD. Slash commands in the chat box: /give rocketlauncher, /pct 150, /pips 3, /sbg. " +
                "Caught before sending, so nobody else sees them.");
            DebugGiveItem = Config.Bind("Debug", "GiveItem", "",
                "DEV BUILD. Type an item name to drop it into your inventory; clears itself. HOST ONLY. " +
                "Coffee, DuelingPistol, ElephantGun, Airhorn, SpringBoots, GolfCart, RocketLauncher, Landmine, Electromagnet, " +
                "OrbitalLaser, RocketDriver, FreezeBomb, SmokeBomb, FlashCamera, Thunderstorm, Railgun, JumboBurger.");
#endif

            // Re-parse caches when something is edited in game.
            CostOverrides.SettingChanged   += (_, __) => ShieldState.InvalidateOverrides();
            PercentFontName.SettingChanged += (_, __) => Hud.InvalidateFont();
            PercentShakeDurationTable.SettingChanged += (_, __) => Hud.InvalidateShakeTable();
        }

        /// <summary>
        /// Entries whose DEFAULT changed in this version. Only these are reset when the
        /// version changes; everything the user tuned in game is left alone. Clear the
        /// list on a drop that changes no defaults, fill it on one that does.
        /// </summary>
        private static readonly string[] RetunedThisVersion =
        {
            // Keep this list in step with Version above whenever a default changes.
            // 0.5.8: percent economy, death timing, the HUD shake block.
            "Percent.PercentGainOnFullBreak", "Percent.PercentReductionBetweenHoles",
            "Percent.PercentAfterKillZoneDeath", "Percent.KillZoneDeathLinger",
            "HUD.PercentShakeDuration", "HUD.PercentShakePixels", "HUD.PercentShakePunch",
            // 0.6.1: debug tooling now ships off (and out of Release builds entirely).
            "Debug.VerboseLogging",
            // 0.6.3: shake rework replaced the old idle/hit split.
            "HUD.PercentShakeDuration", "HUD.PercentShakePixels", "HUD.PercentShakePunch",
            "HUD.PercentShakeSpeed",
            // 0.6.6: drag was killing horizontal travel.
            "Launch.LaunchDrag",
            // 0.6.9: knockback curve now runs to the kill line; the old *AtMax entries are gone.
            "Percent.PercentForMaxScaling",
            // 0.7.0: hang time off by default.
            "Launch.LaunchHangTime",
            // 0.7.1: air recovery is gone; hitstun now SHRINKS with percent instead.
            "Percent.HitstunMultiplierAtMax",
            // 0.7.4: launches carry further before the cap turns reach into altitude.
            "Launch.MaxHorizontalLaunchSpeed", "Launch.HorizontalMultiplierAtKill",
        };

        private void ResetConfigIfVersionChanged()
        {
            if (ConfigVersion.Value == Version) return;
            var reset = new System.Collections.Generic.List<string>();
            foreach (var kv in Config)
            {
                string key = kv.Key.Section + "." + kv.Key.Key;
                if (Array.IndexOf(RetunedThisVersion, key) < 0) continue;
                var e = kv.Value;
                try
                {
                    if (!Equals(e.BoxedValue, e.DefaultValue)) { e.BoxedValue = e.DefaultValue; reset.Add(key); }
                }
                catch { }
            }
            ConfigVersion.Value = Version;
            Config.Save();
            Log.LogInfo(reset.Count == 0
                ? $"Config version now {Version}; no retuned entries needed resetting."
                : $"Config version now {Version}; reset to new defaults: {string.Join(", ", reset)}.");
        }

        private void OnDestroy()
        {
            // Order matters: put the game's own materials back before we unpatch, or
            // the tinted copies stay on pooled prefabs with nothing left to fix them.
            try { ReleaseShield(); }        catch (Exception e) { Log.LogWarning("Unload: " + e.Message); }
            try { ShieldTint.DestroyAll(); } catch (Exception e) { Log.LogWarning("Unload: " + e.Message); }
            try { LaunchVfx.DestroyAll(); }  catch (Exception e) { Log.LogWarning("Unload: " + e.Message); }
            try { KillZone.DestroyAll(); }   catch (Exception e) { Log.LogWarning("Unload: " + e.Message); }
            try { Hud.Shutdown(); }          catch (Exception e) { Log.LogWarning("Unload: " + e.Message); }
            try { CourseManager.MatchStateChanged -= OnMatchStateChanged; } catch { }
            _harmony?.UnpatchSelf();
        }

        private void OnMatchStateChanged(MatchState from, MatchState to)
        {
            if (to != MatchState.TeeOff || from == MatchState.TeeOff) return;

            // Coming from Initializing means a whole new match (or the first hole after
            // the lobby / driving range): start clean rather than carrying anything in.
            if (from == MatchState.Initializing) { ShieldState.FullReset("match start"); ModHandshake.Reset("match start"); Launch.Cancel(); }
            else ShieldState.ResetForNewHole();
        }

        private void Update()
        {
#if SBG_DEV
            PollDebugConfig();
#endif
            ModHandshake.Tick();
            if (_lingerUntil > double.MinValue && Time.timeAsDouble >= _lingerUntil) CancelLingeringShield("linger over");
            ShieldState.Tick();
            ShieldTint.Tick();
            LaunchVfx.Tick();
            KillZone.Tick();

            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            var player = GameManager.LocalPlayerInfo;
            if (player == null)
            {
                _weActivated = false;
                return;
            }

#if SBG_DEV
            if (DebugKeys.Value)
            {
                if (keyboard.f6Key.wasPressedThisFrame) ShieldState.AddPercent(25f);
                if (keyboard.f7Key.wasPressedThisFrame && ShieldState.Pips > 0) ShieldState.Pips--;
                if (keyboard.f8Key.wasPressedThisFrame)
                {
                    ShieldState.Pips = MaxPips.Value;
                    ShieldState.Percent = 0f;
                    ShieldState.UseCooldownUntil = ShieldState.BreakCooldownUntil = double.MinValue;
                    Log.LogInfo("Debug reset.");
                }
                if (keyboard.f10Key.wasPressedThisFrame)
                {
                    // Repeats the last /give, for when retyping mid-match is awkward.
                    if (!string.IsNullOrWhiteSpace(_lastGiveItem)) DebugGiveItem.Value = _lastGiveItem;
                    else Log.LogWarning("F10: use /give <item> once first, then F10 repeats it.");
                }
            }
#endif

            bool held = keyboard.leftShiftKey.isPressed;

            // Comeback bubble came up while shielding: drop it, the pips are wasted otherwise.
            if (_weActivated && BlockActivationDuringImmunity.Value && HasKnockoutImmunity(player))
            {
                if (VerboseLogging.Value) Log.LogInfo("Comeback immunity is up; dropping the shield.");
                ReleaseShield();
            }

            // Climbing into a cart, or springing, with the shield up drops it.
            if (_weActivated && (IsInGolfCart(player) ||
                (BlockActivationDuringSpringBoots.Value && IsUsingSpringBoots(player))))
                ReleaseShield();

            if (held)
            {
                if (!player.IsElectromagnetShieldActive)
                    TryActivate(player);
                else if (_weActivated)
                    _activationTimestamp(player) = Time.timeAsDouble; // refresh, never expires
            }
            else if (_weActivated)
            {
                ReleaseShield();
            }
        }

        private void OnGUI()
        {
            try { Hud.Draw(); }
            catch (Exception e) { if (VerboseLogging.Value) Log.LogWarning("HUD draw error: " + e.Message); }
        }

        internal static bool IsInGolfCart(PlayerInfo player)
        {
            try { return player.ActiveGolfCartSeat.IsValid(); } catch { return false; }
        }

        /// <summary>
        /// The player is in a state where the shield cannot be raised (cart, knocked
        /// out, respawning, diving, swinging, mid-air if disallowed). Shared by the
        /// activation gate and the HUD, so the icon hides exactly when Shift would do nothing.
        /// </summary>
        internal static bool ActivationBlockedByState(PlayerInfo player) => ActivationBlockedByState(player, false);

        /// <param name="forHud">
        /// The HUD passes true so the icon does not blink out for the second or two of
        /// comeback immunity after every recovery. Input still respects it.
        /// </param>
        internal static bool ActivationBlockedByState(PlayerInfo player, bool forHud)
        {
            if (player == null) return true;
            // The gate is checked HERE, not only in the HUD: without this the shield is
            // still raisable while standing down, which made the whole stand-down a lie.
            if (!ModHandshake.GameplayEnabled) return true;
            if (!forHud && BlockActivationDuringImmunity.Value && HasKnockoutImmunity(player)) return true;
            if (BlockActivationInMenus.Value && AnyMenuOpen()) return true;
            if (KillZone.IsLingering) return true;
            if (IsInGolfCart(player)) return true;
            var movement = player.Movement;
            if (movement != null)
            {
                if (!AllowMidAirActivation.Value && !movement.IsGrounded) return true;
                if (movement.IsKnockedOutOrRecovering || movement.IsRespawningOrDrowning) return true;
                if (movement.DivingState != DivingState.None) return true; // no shield out of a dive
            }
            if (BlockActivationDuringSpringBoots.Value && IsUsingSpringBoots(player)) return true;
            var golfer = player.AsGolfer;
            if (BlockActivationDuringSwing.Value && golfer != null && (golfer.IsChargingSwing || golfer.IsSwinging))
                return true;
            return false;
        }

        /// <summary>Any full-screen or modal UI that eats gameplay input.</summary>
        internal static bool AnyMenuOpen()
        {
            try { if (PauseMenu.IsPaused) return true; } catch { }
            try { if (Scoreboard.IsVisible) return true; } catch { }
            try { if (TextChatUi.IsOpen) return true; } catch { }
            try { if (RadialMenu.IsVisible) return true; } catch { }
            return false;
        }

        /// <summary>The game's own knockout-immunity bubble: blue recovery protection or gold repeat protection.</summary>
        internal static bool HasKnockoutImmunity(PlayerInfo player)
        {
            try { return player != null && player.Movement != null && player.Movement.KnockoutImmunityStatus.hasImmunity; }
            catch { return false; }
        }

        internal static bool IsUsingSpringBoots(PlayerInfo player)
        {
            try
            {
                if (player.Inventory != null && player.Inventory.IsUsingSpringBoots) return true;
                return player.Movement != null && player.Movement.IsInSpringBootsJump;
            }
            catch { return false; }
        }

        private void TryActivate(PlayerInfo player)
        {
            double since = Time.timeAsDouble - _lastActivationTime;
            if (since < ActivationCooldown.Value) return;
            if (!ShieldState.CanActivate(out _)) return;
            if (ActivationBlockedByState(player)) return;

            // Nothing validates ItemUseId for the shield path -- no hash, no Cmd.
            // It just needs to pass IsValid(): nonzero guid, non-negative index.
            var useId = new ItemUseId(player.PlayerId.Guid, 0, ItemType.Electromagnet, false);

            // Flag BEFORE activating: the game's shield-changed hook fires synchronously
            // inside this call, and the tint/rooting patches check WeActivated.
            _weActivated = true;
            _lastActivationTime = Time.timeAsDouble;
            player.LocalPlayerActivateElectromagnetShield(useId);

            if (!player.IsElectromagnetShieldActive) { _weActivated = false; return; }

            if (VerboseLogging.Value) Log.LogInfo($"Shield up ({ShieldState.Pips} pips).");
        }

        private static void ReleaseShield()
        {
            if (!_weActivated) return;

            var player = GameManager.LocalPlayerInfo;
            if (player == null || !player.IsElectromagnetShieldActive) { _weActivated = false; return; }

            // explode: false is important. The vanilla cancel-with-explode path fires
            // an AoE HitWithItem on everyone nearby. Cancel BEFORE clearing the flag so
            // the vfx hook still knows this was our shield.
            _weActivated = false;
            LastOurShieldReleaseTime = Time.timeAsDouble;
            ShieldState.LoweredAt = Time.timeAsDouble;   // the parry window starts here
            ShieldState.OnShieldReleased();

            float linger = Mathf.Max(0f, ParryLinger.Value);
            if (linger > 0f && ShieldState.GameplayParryEnabled)
            {
                _lingerUntil = Time.timeAsDouble + linger;
                if (VerboseLogging.Value) Log.LogInfo($"Shield key released; body lingers {linger:0.00}s.");
                return;
            }

            CancelLingeringShield("released");
        }

        /// <summary>Ends the linger: the actual cancel the keypress deferred.</summary>
        private static void CancelLingeringShield(string why)
        {
            _lingerUntil = double.MinValue;

            var player = GameManager.LocalPlayerInfo;
            if (player == null || !player.IsElectromagnetShieldActive) return;

            // explode: false is important. The vanilla cancel-with-explode path fires
            // an AoE HitWithItem on everyone nearby.
            player.LocalPlayerCancelElectromagnetShield(false);
            LastOurShieldReleaseTime = Time.timeAsDouble;

            if (VerboseLogging.Value) Log.LogInfo($"Shield down ({why}).");
        }

        // ---- Debug helpers -------------------------------------------------
        // Polled every frame rather than driven by SettingChanged. The config UI is a
        // separate mod and we cannot be sure it raises that event the way BepInEx's own
        // file watcher does; a string compare per frame costs nothing and always works.

#if SBG_DEV
        private static string _lastGiveItem;

        private static void PollDebugConfig()
        {
            if (DebugSetPercent.Value >= 0f) ApplyDebugPercent();
            if (!string.IsNullOrWhiteSpace(DebugGiveItem.Value)) ApplyDebugGiveItem();
        }

        private static void ApplyDebugPercent()
        {
            float v = DebugSetPercent.Value;
            DebugSetPercent.Value = -1f; // spring back so the same number can be set twice
            if (!ShieldState.InPlayableHole)
            {
                Log.LogWarning("SetPercent ignored: percent only exists during a hole.");
                return;
            }
            ShieldState.SetPercent(v);
        }

        private static void ApplyDebugGiveItem()
        {
            string raw = DebugGiveItem.Value.Trim();
            DebugGiveItem.Value = "";
            if (raw.Length == 0) return;
            _lastGiveItem = raw;   // F10 repeats it

            if (!Enum.TryParse(raw, true, out ItemType item) || item == ItemType.None)
            {
                Log.LogWarning($"GiveItem: '{raw}' is not an item name. Valid: {string.Join(", ", Enum.GetNames(typeof(ItemType)))}");
                return;
            }

            var player = GameManager.LocalPlayerInfo;
            if (player == null || player.Inventory == null) { Log.LogWarning("GiveItem: no local player yet."); return; }

            try
            {
                bool isServer = Mirror.NetworkServer.active;
                if (!isServer)
                {
                    // ServerTryAddItem is a [Server] method: on a client it logs and
                    // returns without doing anything, which is exactly the silent
                    // nothing you would have seen.
                    Log.LogWarning($"GiveItem: you are not the host, so the game will not grant {item}. " +
                                   "Item inventories are server-authoritative; host the lobby to use this.");
                    return;
                }
                if (!player.Inventory.HasSpaceForItem(out int slot))
                {
                    Log.LogWarning("GiveItem: inventory is full, drop something first.");
                    return;
                }
                int uses = 1;
                if (GameManager.AllItems != null && GameManager.AllItems.TryGetItemData(item, out var data) && data.MaxUses > 0)
                    uses = data.MaxUses;

                bool ok = player.Inventory.ServerTryAddItem(item, uses);
                Log.LogInfo(ok
                    ? $"GiveItem: {item} x{uses} into slot {slot}."
                    : $"GiveItem: the game refused {item}.");
            }
            catch (Exception e) { Log.LogWarning($"GiveItem threw on {item}: {e}"); }
        }
#endif

        /// <summary>The handshake gate closed mid-match: drop the shield we should not have.</summary>
        internal static void ForceReleaseForHandshake()
        {
            // No linger here: the gate closed, the shield should be gone now, not in 0.2s.
            try { if (_weActivated) ReleaseShield(); CancelLingeringShield("handshake"); } catch { }
        }

        /// <summary>Called by ShieldState when the shield is cancelled by a break rather than a release.</summary>
        internal static void NotifyShieldDropped()
        {
            _weActivated = false;
            _lingerUntil = double.MinValue;   // it broke; there is no body left to linger
            LastOurShieldReleaseTime = Time.timeAsDouble;
        }
    }
}
