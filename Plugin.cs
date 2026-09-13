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
#if SBG_DEV
        public const string Name    = "SBG Shields TEST";   // the dev build says so in its title
#else
        public const string Name    = "SBG Shields";
#endif
        public const string Version = "0.7.26";

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
        internal static ConfigEntry<float> BreakBounceSpeed;
        internal static ConfigEntry<float> BreakStunMultiplier;
        internal static ConfigEntry<float> BreakSoundCarry;
        internal static ConfigEntry<bool>  RefundPipsOnRefusedKnockout;
        internal static ConfigEntry<bool>  AbsorbedHitsCancelKnockback;
        internal static ConfigEntry<float> ReflectionSearchMargin;
        internal static ConfigEntry<string> CostOverrides;

        // Percent
        internal static ConfigEntry<bool>  PercentEnabled;
        internal static ConfigEntry<float> MaxPercent;
        internal static ConfigEntry<bool>  RageVisual;
        internal static ConfigEntry<float> RageVisualMinPercent;
        // Parry
        internal static ConfigEntry<bool>  PerfectParry;
        internal static ConfigEntry<float> ParryReach;
        internal static ConfigEntry<float> ParryReadTime;
        internal static ConfigEntry<float> ParryReadRange;
        internal static ConfigEntry<float> ParryArmTime;
        internal static ConfigEntry<bool>  AimedAtParry;   // experimental, see AimedAt.cs
        internal static ConfigEntry<float> AimedAtRange;
        internal static ConfigEntry<bool>  PerfectParryBeatsFullBreak;
        internal static ConfigEntry<bool>  PerfectParryBeatsUnblockable;
        internal static ConfigEntry<bool>  PerfectParryRefundsUse;
        internal static ConfigEntry<bool>  PerfectParrySound;
        internal static ConfigEntry<string> ParrySoundFile;
        internal static ConfigEntry<float> ParrySoundVolume;
        internal static ConfigEntry<float> ParryLinger;
        internal static ConfigEntry<bool>  ParryGlow;
        internal static ConfigEntry<float> ParryGlowDuration;
        internal static ConfigEntry<float> ParryGlowBoost;
        internal static ConfigEntry<bool>  ParryBurst;
        internal static ConfigEntry<float> ParryBurstSize;
        internal static ConfigEntry<bool>  ParryShake;

        internal static ConfigEntry<float> PercentForMaxScaling;
        internal static ConfigEntry<float> ForceMultiplierAtMax;
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
        internal static ConfigEntry<float> HorizontalMultiplierAtMax;
        internal static ConfigEntry<float> LaunchDrag;
        internal static ConfigEntry<float> LaunchVerticalDragFactor;
        internal static ConfigEntry<float> LaunchDragDuration;
        internal static ConfigEntry<float> LaunchDragAboveSpeed;
        internal static ConfigEntry<float> LaunchHangTime;
        internal static ConfigEntry<float> LaunchHangWindow;
        internal static ConfigEntry<float> LaunchHangDuration;
        internal static ConfigEntry<float> CloudHitMinPercent;
        internal static ConfigEntry<float> HangFullPercent;
        internal static ConfigEntry<float> RecoveryImmunity;
        internal static ConfigEntry<bool>  StayDownUntilLanding;
        internal static ConfigEntry<float> StayDownMaxTime;
        internal static ConfigEntry<bool>  TumbleGravityUntilLanding;
        // Hit categories
        internal static ConfigEntry<float> ExplosiveForceScale;
        internal static ConfigEntry<float> BulletForceScale;
        internal static ConfigEntry<float> MeleeForceScale;
        internal static ConfigEntry<float> ExplosiveAngleFloorScale;
        internal static ConfigEntry<float> BulletAngleFloorScale;
        internal static ConfigEntry<float> MeleeAngleFloorScale;
        internal static ConfigEntry<float> BulletMaxElevation;
        // Directional influence
        internal static ConfigEntry<bool>  DirectionalInfluence;
        internal static ConfigEntry<float> DIWindow;
        internal static ConfigEntry<float> DIMaxYaw;
        // Teching
        internal static ConfigEntry<bool>  TechEnabled;
        internal static ConfigEntry<float> TechWindow;
        internal static ConfigEntry<float> TechImmunity;
        internal static ConfigEntry<float> TechLockout;
        internal static ConfigEntry<float> TechRecovery;
        internal static ConfigEntry<bool>  TechSelfInflicted;
        // Percent by speed
        internal static ConfigEntry<bool>  PercentScalesWithSpeed;
        internal static ConfigEntry<float> PercentReferenceSpeed;
        internal static ConfigEntry<float> PercentSpeedFactorMin;
        internal static ConfigEntry<float> PercentSpeedFactorMax;
        internal static ConfigEntry<float> LandingStun;
        internal static ConfigEntry<float> BreakLandingStun;
        internal static ConfigEntry<float> MinStunAfterHit;
        internal static ConfigEntry<float> BreakMinStun;
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
        internal static ConfigEntry<bool>  PipWarning;
        internal static ConfigEntry<bool>  BubbleReflects;
        internal static ConfigEntry<float> BubbleWornWhiteness;
        internal static ConfigEntry<float> BubbleWornAlpha;
        internal static ConfigEntry<float> BubbleGlow;
        internal static ConfigEntry<bool>  BubbleHalo;
        internal static ConfigEntry<float> BubbleHaloSize;
        internal static ConfigEntry<float> BubbleHaloStrength;
        internal static ConfigEntry<float> BitsGlow;

        // Immunity look (the game's comeback shield)
        internal static ConfigEntry<bool>  ImmunityFlickerEnabled;
        internal static ConfigEntry<float> ImmunityFlickerRate;
        internal static ConfigEntry<float> ImmunityFlickerWash;
        internal static ConfigEntry<bool>  HideGameBubble;


        // HUD
        internal static ConfigEntry<bool>   ShowHud;
        internal static ConfigEntry<float>  HudScale;
        internal static ConfigEntry<float>  HudBottomMargin;
        internal static ConfigEntry<float>  HudHorizontalOffset;
        internal static ConfigEntry<float>  BubbleHudSize;
        internal static ConfigEntry<float>  BubbleHudGap;
        internal static ConfigEntry<bool>   BubbleHudOnLeft;
        internal static ConfigEntry<float>  HudGlow;
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

        /// <summary>Where the DLL lives (the r2modman package folder). Sound files ship next to it.</summary>
        internal static string PluginDirectory
        {
            get
            {
                try
                {
                    string loc = _instance?.Info?.Location;
                    if (!string.IsNullOrEmpty(loc)) return System.IO.Path.GetDirectoryName(loc);
                }
                catch { }
                try { return System.IO.Path.GetDirectoryName(typeof(Plugin).Assembly.Location); } catch { return null; }
            }
        }

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
        /// The bubble's body outlives the keypress by ParryLinger, cosmetically. WeActivated
        /// goes false the instant you let go -- rooting, jumping and swinging come straight
        /// back -- and ResolveKnockout treats a lingering body as no bubble at all. It exists
        /// so a tap plays intro -> full -> dissolve instead of the game's full -> dissolve.
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
            ShieldFlagPatches.ApplyAll(_harmony);   // by hand: each target degrades to a warning if the game renamed it

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
            MaxPips = Config.Bind("Shield", "MaxPips", 10,
                "Bubble HP. Drawn as five circles of two pips each, so a 1-pip hit takes half a circle. Stray ball 1, guns 2, " +
                "homing ball 2, explosions and carts 3, swings break it outright, the penalty stroke goes straight through. " +
                "No regeneration.");
            UseCooldown = Config.Bind("Shield", "UseCooldown", 3.0f,
                "Seconds after releasing the shield before it can be raised again. Every raise is a commitment; a parry refunds it.");
            BreakCooldown = Config.Bind("Shield", "BreakCooldown", 15.0f,
                "Seconds the bubble is unavailable after it breaks. With the bounce being mild, this and the boom are the punish.");
            RestoreAfterBreakCooldown = Config.Bind("Shield", "RestoreAfterBreakCooldown", true,
                "When the break cooldown ends, restore the shield to full pips. Off = no shield until the next hole.");
            BreakBounceSpeed = Config.Bind("Shield", "BreakBounceSpeed", 10f,
                "When a hit breaks the bubble, the hit itself is cancelled and you pop straight up at this speed (m/s), tumble, land, " +
                "and get up. 10 is about a two-second hop. 0 = you drop where you stand and take the stun there.");
            BreakStunMultiplier = Config.Bind("Shield", "BreakStunMultiplier", 1.3f,
                "The break's knockout timer is the game's normal one times this, so a break stuns a little longer than a hit. " +
                "A break cannot be teched; you sit through it. Immunity afterwards is the game's own.");
            BreakSoundCarry = Config.Bind("Shield", "BreakSoundCarry", 60f,
                "Metres out to which a bubble break is heard by other players (the game's own sound fades much sooner). " +
                "Everyone in that range hears the boom from the right direction. 0 = the game's sound only.");
            RefundPipsOnRefusedKnockout = Config.Bind("Shield", "RefundPipsOnRefusedKnockout", true,
                "If the game refuses the knockout after your shield spent pips on the hit (comeback immunity, team protection, " +
                "frozen, self hit), give the pips back. The bubble still pops, but a hit that could never stun you does not cost you the shield.");
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
                "ElectromagnetShieldExplosion. Defaults (of 10 pips): freeze bomb 0; stray/returned balls 2; pistol 3; homing ball 4; " +
                "elephant gun 5; explosions, carts, vehicles, rocket driver 6; swings/giant = full; laser/thunder/railgun direct = unblockable.");

            // The three master switches. Each layer comes off cleanly on its own:
            //   Shield.ShieldAbsorbsHits  - pips. Off = vanilla shield: blocks everything, never breaks.
            //   Percent.PercentEnabled    - percent. Off = no number, no scaling, vanilla knockback, no kill.
            //   Percent.KillZoneEnabled   - death. Off = percent climbs to MaxPercent and nothing more.
            PercentEnabled = Config.Bind("Percent", "PercentEnabled", true,
                "The percent layer. Off = no percent is ever gained or shown, knockback and stun are exactly the game's, and " +
                "the kill zone cannot fire. The shield and its pips keep working (see Shield.ShieldAbsorbsHits for that layer).");
            MaxPercent = Config.Bind("Percent", "MaxPercent", 300f, "Percent cap.");
            RageVisual = Config.Bind("Percent", "RageVisual", true,
                "Embers rise off your body once you are past RageVisualMinPercent, thicker and redder the closer to the kill line. " +
                "Only you see it: nobody else's client knows your percent yet.");
            RageVisualMinPercent = Config.Bind("Percent", "RageVisualMinPercent", 100f, "Percent at which the embers start.");
            PerfectParry = Config.Bind("Parry", "PerfectParry", true,
                "Letting go of the shield with a threat in reach is a perfect parry: that hit is fully absorbed, no pips spent, no " +
                "percent gained. Holding the shield out and getting hit is an ordinary block; letting go into the hit is the read.");
            ParryReach = Config.Bind("Parry", "ParryReach", 1.5f,
                "Metres past the edge of your bubble that count as 'in reach' when you let go, whatever the thing is doing: a rocket " +
                "drifting past, a golfer winding up. The catch. For things coming AT you see ParryReadTime; a fast ball is never " +
                "'in reach' for long enough to time by distance.");
            ParryReadTime = Config.Bind("Parry", "ParryReadTime", 0.35f,
                "A projectile that would reach your bubble within this many seconds counts as in reach when you let go, however far " +
                "away it still is. The read: see it coming, let go, take it for free. This is the difficulty knob for projectiles. " +
                "Nothing coming = nothing armed, so tapping the bubble cannot fish for a parry.");
            ParryReadRange = Config.Bind("Parry", "ParryReadRange", 15f,
                "Metres out to which incoming projectiles are looked for. Just a search radius; ParryReadTime decides.");
            ParryArmTime = Config.Bind("Parry", "ParryArmTime", 0.5f,
                "Seconds an armed parry stays live, so what was in reach has time to actually arrive. Not a timing window you aim for: " +
                "with nothing in reach at release it never opens at all.");
            AimedAtParry = Config.Bind("Parry", "AimedAtParry", true,
                "EXPERIMENTAL. Guns are instant, so nothing is ever in reach to arm a parry against them. Instead: another player " +
                "aiming an item along a line through your bubble when you let go arms a parry, the same as a projectile in reach. " +
                "Reads the shooter's aim yaw only (pitch is not networked), so it is a horizontal corridor. Off = guns cannot be parried.");
            AimedAtRange = Config.Bind("Parry", "AimedAtRange", 50f,
                "Metres out to which a player aiming at you counts. The log prints the game's real pistol and elephant gun ranges " +
                "the first time this runs; match it to the longer of the two.");
            PerfectParryBeatsFullBreak = Config.Bind("Parry", "PerfectParryBeatsFullBreak", true,
                "A perfect parry stops the hits that normally break the shield outright (swings, carts, targeted balls). " +
                "This is the point of the mechanic: timing beats a hit that pips cannot.");
            PerfectParryBeatsUnblockable = Config.Bind("Parry", "PerfectParryBeatsUnblockable", false,
                "Whether a perfect parry also stops railgun/orbital/thunderstorm direct hits. Off: unblockable stays unblockable.");
            PerfectParryRefundsUse = Config.Bind("Parry", "PerfectParryRefundsUse", true,
                "Clear the use cooldown on a parry, so reading a hit correctly does not cost you the next shield.");
            PerfectParrySound = Config.Bind("Parry", "PerfectParrySound", true,
                "Play a sound when a parry lands, heard by everyone near it: ParrySoundFile if it exists, otherwise the game's own " +
                "blocked-knockout sting.");
            ParrySoundFile = Config.Bind("Parry", "ParrySoundFile", "parry.wav",
                "Sound file for a parry, looked for in the mod's folder and its sounds\\ subfolder (wav, ogg or mp3). " +
                "Missing file = the game's sting. Everyone hears their own copy, so it should ship with the mod.");
            ParrySoundVolume = Config.Bind("Parry", "ParrySoundVolume", 1f, "Volume of ParrySoundFile, 0..2.");
            ParryBurst = Config.Bind("Parry", "ParryBurst", true,
                "A ring of light and sparks bursts out of the bubble when a parry lands, in the parrier's colour, on every screen.");
            ParryBurstSize = Config.Bind("Parry", "ParryBurstSize", 5f, "How wide the parry ring grows, in metres.");
            ParryShake = Config.Bind("Parry", "ParryShake", true,
                "A short camera kick for anyone close to a parry, so it lands like a hit rather than a fizzle.");

            ParryLinger = Config.Bind("Parry", "ParryLinger", 0.3f,
                "Seconds the bubble is still DRAWN after you let go, so a tap shows its intro before the dissolve instead of " +
                "snapping from full to gone. Cosmetic only: a lingering bubble absorbs nothing and bounces nothing; a hit that " +
                "arrives in that moment is parried if you read it and lands in full if you did not. Movement is yours the instant " +
                "the key is up. 0 = the bubble vanishes with the keypress.");
            ParryGlow = Config.Bind("Parry", "ParryGlow", true,
                "Flash the shield bright when a parry lands.");
            ParryGlowDuration = Config.Bind("Parry", "ParryGlowDuration", 0.45f,
                "How long the parry flash lasts, in seconds. The bubble's body is kept drawn for this long after a parry so the flash is seen.");
            ParryGlowBoost = Config.Bind("Parry", "ParryGlowBoost", 3f,
                "How much brighter the flash is than your normal shield colour. The tint pipeline scales by the material's own " +
                "intensity, so this multiplies rather than washing out to white.");

            PercentForMaxScaling = Config.Bind("Percent", "PercentForMaxScaling", 100f,
                "Percent at which everything percent-driven reaches its max: knockback, angle floor, hang time, hitstun, break " +
                "immunity, HUD shake. Past this point hits do not get any bigger; the only thing left to climb to is the kill line.");
            ForceMultiplierAtMax = Config.Bind("Percent", "ForceMultiplierAtMax", 2.3f,
                "Knockback speed multiplier at PercentForMaxScaling and above. 1.0x at 0%, curve between set by KnockbackExponent. " +
                "Defaults give roughly: 40% 1.3x, 60% 1.6x, 80% 1.9x, 100%+ 2.3x. This is the tallest an ordinary launch ever gets; " +
                "only the kill boost goes higher, so a player flying off screen is always a dead one.");
            KnockbackExponent = Config.Bind("Percent", "KnockbackExponent", 1.5f,
                "Shape of the knockback curve. 1 = straight line to PercentForMaxScaling. Higher = flatter early, steeper late. 2 is very back-loaded.");
            HitstunMultiplierAtMax = Config.Bind("Percent", "HitstunMultiplierAtMax", 0.8f,
                "Knockout duration multiplier at PercentForMaxScaling, interpolated from 1.0x at 0%. BELOW 1 by default: the hit that " +
                "sends you furthest is also the one whose timer ends soonest. With StayDownUntilLanding on, that is when the comeback " +
                "bubble comes up mid-air while you keep tumbling; with it off, that is when you wake up in the air. Above 1 gives " +
                "longer stun the more beaten up you are, which reads as being juggled. Set 1.0 for vanilla stun at every percent.");
            PercentPerHitBase = Config.Bind("Percent", "PercentPerHitBase", 5f,
                "Percent gained by any chip-class hit, before the per-pip part.");
            PercentPerPip = Config.Bind("Percent", "PercentPerPip", 4f,
                "Extra percent per pip of the hit's cost (a 3-pip rocket = base + 3*this). Costs are in half-circles; a rocket is 5 + 12 = 17%.");
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
            HorizontalMultiplierAtMax = Config.Bind("Launch", "HorizontalMultiplierAtMax", 1.25f,
                "EXTRA horizontal multiplier at PercentForMaxScaling and above, on top of the force multiplier, on the same curve. " +
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
            LaunchHangTime = Config.Bind("Launch", "LaunchHangTime", 0.35f,
                "How much gravity is cancelled at the top of a launch's arc at full strength (0.35 = 35% at the very apex, tapering to " +
                "none as you speed up). The flight is the stun, and this is how the stun grows with percent: nothing below " +
                "CloudHitMinPercent, ramping to full at HangFullPercent. 0 = off.");
            HangFullPercent = Config.Bind("Launch", "HangFullPercent", 150f,
                "Percent at which hang time reaches its full LaunchHangTime. Between CloudHitMinPercent and this it ramps up.");
            LaunchHangWindow = Config.Bind("Launch", "LaunchHangWindow", 7f,
                "Vertical speed (m/s) within which hang time applies. Only near the apex, where vertical speed is small, so the rise and fall keep their shape.");
            LaunchHangDuration = Config.Bind("Launch", "LaunchHangDuration", 3f,
                "Seconds after a launch during which hang time can apply.");
            CloudHitMinPercent = Config.Bind("Launch", "CloudHitMinPercent", 65f,
                "Percent at or above which a launch starts to hang at its apex. Below this you fall like a body.");
            // Hit categories. Everything the mod does to a launch is a multiplier on the
            // game's own number, so the ordering explosive > bullet > melee holds only if
            // the game's base speeds do not invert it. The verbose "Hit <type>" line prints
            // the category and |v| in and out; tune from that.
            ExplosiveForceScale = Config.Bind("Launch", "ExplosiveForceScale", 1.0f,
                "Multiplier on explosive launches (rockets, mines, back blast, bombs, laser/thunder peripheral). The biggest hits.");
            BulletForceScale = Config.Bind("Launch", "BulletForceScale", 0.65f,
                "Multiplier on bullet launches (pistol, elephant gun, deflected shots, railgun). The game's own gun knockback is the " +
                "STRONGEST there is (elephant gun 60 m/s, rocket 40, swing 30), so this has to sit well under 1 for explosives to " +
                "out-launch bullets. 0.65 puts the elephant gun just under a rocket and the pistol level with a swing.");
            MeleeForceScale = Config.Bind("Launch", "MeleeForceScale", 0.7f,
                "Multiplier on everything else: swings, balls, carts, vehicles. The smallest hits.");
            ExplosiveAngleFloorScale = Config.Bind("Launch", "ExplosiveAngleFloorScale", 1.0f,
                "How much of the percent-scaled elevation floor (MinLaunchAngleAtZero..AtMax) explosions get. 1 = all of it: blasts lift.");
            BulletAngleFloorScale = Config.Bind("Launch", "BulletAngleFloorScale", 0f,
                "Same for bullets. 0 = no floor: a shot shoves you along the ground in the direction it came from.");
            MeleeAngleFloorScale = Config.Bind("Launch", "MeleeAngleFloorScale", 0.5f,
                "Same for melee. Half the floor: a club pops you a little, not into the sky.");
            BulletMaxElevation = Config.Bind("Launch", "BulletMaxElevation", 15f,
                "Ceiling in degrees on a bullet launch's elevation, so getting shot never reads as taking off. 90 = no ceiling.");

            DirectionalInfluence = Config.Bind("Launch", "DirectionalInfluence", true,
                "DI. As you are launched, push the stick toward where on the screen you want to drift and the launch bends that " +
                "way, by up to DIMaxYaw. W is into the screen, S toward the camera, A and D screen left and right: the same " +
                "camera-relative stick you walk with. Speed and height are unchanged, so it is where you land, not how far. " +
                "Read once, from the first input inside DIWindow.");
            DIWindow = Config.Bind("Launch", "DIWindow", 0.15f, "Seconds after the hit during which your stick is read for DI.");
            DIMaxYaw = Config.Bind("Launch", "DIMaxYaw", 20f, "Degrees the launch can be turned toward the stick, at full stick.");

            TechEnabled = Config.Bind("Launch", "TechEnabled", true,
                "Press the shield key just before your tumbling body hits the ground and you tech: up and actionable instantly, " +
                "no lie-down, no get-up animation. The trade: a missed tech gives you the game's full comeback bubble through " +
                "the get-up; a tech gives you only TechImmunity. Break stuns and death launches cannot be teched.");
            TechWindow = Config.Bind("Launch", "TechWindow", 0.2f, "Seconds before landing in which the press counts.");
            TechImmunity = Config.Bind("Launch", "TechImmunity", 0.2f,
                "Seconds of comeback bubble after a tech. 0 = none at all; you are up and fully hittable.");
            TechLockout = Config.Bind("Launch", "TechLockout", 0.4f,
                "A press that did not tech locks the key out for this long after its window closes. One press is one attempt; " +
                "mashing gets you one badly-timed attempt, not a guaranteed tech.");
            TechRecovery = Config.Bind("Launch", "TechRecovery", 0.3f,
                "Seconds you are rooted after a tech: up, but not yet moving, jumping, swinging or using items. Without this a tech " +
                "off your own rocket was a free dash.");
            TechSelfInflicted = Config.Bind("Launch", "TechSelfInflicted", false,
                "Whether a knockout you caused yourself (own rocket, own back-blast) can be teched. Off: you eat the landing you bought.");
            RecoveryImmunity = Config.Bind("Launch", "RecoveryImmunity", 1f,
                "Seconds of the game's blue comeback shield after you get up from a launch. The game's own rule is 3 s (a match " +
                "setting), tuned for a game where the hit itself cost 3 s; with the flight as the stun that is far too generous and " +
                "it crowds out the bubble. The gold shield for repeated knockouts is untouched. 0 = the game's rule.");

            PercentScalesWithSpeed = Config.Bind("Percent", "PercentScalesWithSpeed", true,
                "Percent gain scales with how hard the hit was: the game's own knockback speed over PercentReferenceSpeed. A point-blank " +
                "elephant gun (60 m/s) gives twice a full swing's percent; a pistol at range (15 m/s) gives half. Explosions already " +
                "lose speed with distance, so this replaces ExplosionPercentFalloff while it is on.");
            PercentReferenceSpeed = Config.Bind("Percent", "PercentReferenceSpeed", 30f,
                "The knockback speed (m/s) that gives exactly the listed percent. 30 is a full-power golf swing.");
            PercentSpeedFactorMin = Config.Bind("Percent", "PercentSpeedFactorMin", 0.5f, "Floor on the speed factor, so a graze still counts.");
            PercentSpeedFactorMax = Config.Bind("Percent", "PercentSpeedFactorMax", 2f, "Ceiling on the speed factor, so a rocket driver (90 m/s) is not five swings.");
            LandingStun = Config.Bind("Launch", "LandingStun", 0.25f,
                "The flight is the stun. When your tumbling body lands, the get-up starts this many seconds later instead of after " +
                "whatever is left of the game's own 3 s knockout timer, which is why a short launch used to leave you lying there " +
                "longer than a huge one. 0 = get up the moment you land. A tech skips it entirely.");
            BreakLandingStun = Config.Bind("Shield", "BreakLandingStun", 0.75f,
                "Same, after the break bounce: a break keeps you down a little longer than a hit. Cannot be teched.");
            MinStunAfterHit = Config.Bind("Launch", "MinStunAfterHit", 2f,
                "Floor on the whole stun, hit to get-up, in seconds: the flight or this, whichever is longer. A short launch that " +
                "lands in one second still keeps you down until this much has passed since the hit; a long flight has already " +
                "spent it and gets up on landing. The game's own stun is a flat 3. A tech skips whatever ground time is left, which " +
                "is what makes teching worth doing on the hits that need it.");
            BreakMinStun = Config.Bind("Shield", "BreakMinStun", 3f,
                "Same floor after a bubble break, hit to get-up. Longer than a hit on purpose: losing the bubble is the moment that " +
                "costs you, and it cannot be teched.");

            StayDownUntilLanding = Config.Bind("Launch", "StayDownUntilLanding", true,
                "When the knockout timer runs out while you are still in the air, the comeback bubble comes up on the spot but you " +
                "keep tumbling and falling at knockout speed until you hit the ground, then get up as normal. " +
                "Off = vanilla: the game wakes you in mid-air the instant the timer ends, and from then on you fall at walking-state " +
                "gravity, which reads as floating down.");
            StayDownMaxTime = Config.Bind("Launch", "StayDownMaxTime", 3f,
                "Longest the stay-down hold lasts after the stun timer ended, in seconds. A body that has not touched ground by then " +
                "is stuck on something; wake it rather than wait for the game's 10 s time-out.");
            TumbleGravityUntilLanding = Config.Bind("Launch", "TumbleGravityUntilLanding", true,
                "If anything wakes you in mid-air (the hold's cap, the game's time-out), keep falling at knockout gravity until the " +
                "launch lands instead of drifting down at walking gravity.");
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
                "A hit that breaks your bubble knocks you out (the bounce) even if the game's comeback shield is up. Off = vanilla " +
                "behaviour, where comeback protection refuses the knockout and the break costs you only the bubble.");
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
            PipWarning = Config.Bind("Bubble", "PipWarning", true,
                "Blink the bubble when it is down to its last circle (two pips or fewer). Needs TintVanillaShield.");
            BubbleWornWhiteness = Config.Bind("Bubble", "BubbleWornWhiteness", 0.3f,
                "How much lighter the bubble gets with almost nothing left (0 = none, 1 = white). Kept small so every colour stays " +
                "recognisable at one pip; the main tell is BubbleWornAlpha. It never darkens. A hot flash of the skin colour marks " +
                "each pip lost.");
            BubbleWornAlpha = Config.Bind("Bubble", "BubbleWornAlpha", 0.4f,
                "How opaque the bubble is with almost nothing left, as a fraction of full (1 = no change). It thins as pips go, on " +
                "every screen, so an attacker can see it weaken.");
            BubbleGlow = Config.Bind("Bubble", "BubbleGlow", 1.5f,
                "How much brighter than its plain skin colour the bubble is drawn (1 = as the game draws it). Above 1 the colour is " +
                "pushed past white-point, which the game's bloom turns into a glow. Applies to the sparks it throws too.");
            BubbleHalo = Config.Bind("Bubble", "BubbleHalo", true,
                "A soft ring of the bubble's colour drawn around it, on every screen, so it glows even where there is no bloom. " +
                "Follows the bubble's state: thins as pips go, flares on a parry and the last-circle blink.");
            BubbleHaloSize = Config.Bind("Bubble", "BubbleHaloSize", 1.6f, "Halo width as a multiple of the bubble's diameter.");
            BubbleHaloStrength = Config.Bind("Bubble", "BubbleHaloStrength", 0.7f, "Halo brightness, 0..2.");
            BitsGlow = Config.Bind("Bubble", "BitsGlow", 1.6f,
                "Brightness of the mod's own particles (parry ring and sparks, star flash, rage embers, the halo) past white-point, " +
                "for bloom. Read when each effect is first built; restart the game to change it.");
            BubbleReflects = Config.Bind("Bubble", "BubbleReflects", false,
                "Off: a held bubble absorbs. Balls, rockets and bombs pass into you and cost pips; nothing bounces back. " +
                "On: the game's own behaviour, where every shield is a wall that reflects whatever touches it. " +
                "MUST BE THE SAME FOR EVERYONE IN THE LOBBY: which machine simulates a projectile decides what it hits, so a mixed " +
                "lobby gets mixed results. Applies to the magnet item's shield as well; from another machine the two are the same thing.");

            ImmunityFlickerEnabled = Config.Bind("Immunity", "Flicker", true,
                "Smash-style invulnerability: a player's body flickers washed-out white while the game's comeback shield is up. " +
                "Drawn on every client for every player, from the same networked state the game uses to refuse hits.");
            ImmunityFlickerRate = Config.Bind("Immunity", "FlickerRate", 10f, "Flickers per second.");
            ImmunityFlickerWash = Config.Bind("Immunity", "FlickerWash", 0.75f,
                "How far toward white the body goes on the bright phase (0 = no change, 1 = pure white).");
            HideGameBubble = Config.Bind("Immunity", "HideGameBubble", false,
                "Also hide the game's own blue/orange/red bubble while the flicker runs, so the flicker is the only tell. " +
                "Off until it has been seen in play; the two together may be too much or just right.");

            ShowHud = Config.Bind("HUD", "ShowHud", true, "Show the percent + bubble readiness overlay.");
            HudScale = Config.Bind("HUD", "HudScale", 1.0f, "Overall HUD scale.");
            HudBottomMargin = Config.Bind("HUD", "HudBottomMargin", 150f, "Pixels from the bottom of the screen. Raised so the percent clears the golf-cart Exit prompt.");
            HudHorizontalOffset = Config.Bind("HUD", "HudHorizontalOffset", 0f, "Pixels to shift the percent left/right from centre.");
            BubbleHudSize = Config.Bind("HUD", "BubbleHudSize", 64f, "Bubble readiness icon size in pixels at HudScale 1.");
            BubbleHudGap = Config.Bind("HUD", "BubbleHudGap", 22f, "Gap between the percent and the icon.");
            BubbleHudOnLeft = Config.Bind("HUD", "BubbleHudOnLeft", true, "Icon left of the percent (away from the item bar). Off = right of it.");
            ShowPipDots = Config.Bind("HUD", "ShowPipDots", true, "Show pip dots under the icon. Debug aid; the shield itself should carry this in the final build.");
            HudGlow = Config.Bind("HUD", "HudGlow", 1.0f,
                "Glow behind the bubble icon and the pip circles, in your skin colour: soft when the bubble is ready, breathing while " +
                "it is up, a white flare on a parry, and almost nothing on cooldown. 0 = off, 2 = twice as strong.");
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
            // 0.7.8: knockback now saturates at PercentForMaxScaling. ForceMultiplierAtKill and
            // HorizontalMultiplierAtKill were replaced by *AtMax with new defaults (new keys, no reset needed).
            // The parry is armed by reach now, so the linger is off; PerfectParryWindow no longer exists.
            "Parry.ParryLinger",
            // Also 0.7.8: PartialBreakLaunches deleted (it was the instant-wake-up bug behind a toggle).
            // New entries: PercentEnabled, hit categories, DI, tech, PipWarning, RageVisual.
            // Break rework: stun-in-place and its six knobs replaced by BreakBounceSpeed / BreakStunMultiplier /
            // BreakSoundCarry; the cooldown is now the punish and went 8 -> 10.
            "Shield.BreakCooldown",
            // 0.7.9: no defaults changed. Parry scan fix, bright pip blink, handshake nudge + diagnostics, HUD reason line.
            // 0.7.14: the game's real knockback table (settings dump) showed guns are its strongest hits; bullets 0.85 -> 0.65.
            "Launch.BulletForceScale",
            // 0.7.16: ten pips drawn as five circles; PercentPerPip halved to keep percent the same.
            "Shield.MaxPips", "Percent.PercentPerPip",
            // 0.7.17: hang time on, ramping from 65% (was off, gated at 125%).
            "Launch.LaunchHangTime", "Launch.CloudHitMinPercent",
            // 0.7.19: the linger is cosmetic now and back on, so a tap shows the bubble's intro.
            "Parry.ParryLinger",
            // 0.7.22: worn bubbles thin instead of whitening; whiteness 0.75 -> 0.3.
            "Bubble.BubbleWornWhiteness",
            // 0.7.23: costs halved back to the user's numbers (a rocket is a circle and a half); per-pip percent 2 -> 4
            // so a hit is worth the same. Parry flash 0.35 -> 0.45 now that the body stays for it.
            "Percent.PercentPerPip", "Parry.ParryGlowDuration",
            // 0.7.24: use cooldown 1 -> 3, break cooldown 10 -> 15 (user). Glow entries are new keys.
            "Shield.UseCooldown", "Shield.BreakCooldown",
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
            try { ParryFx.DestroyAll(); }    catch (Exception e) { Log.LogWarning("Unload: " + e.Message); }
            try { ImmunityFlicker.ClearAll(); } catch (Exception e) { Log.LogWarning("Unload: " + e.Message); }
            try { BubbleColliderPatch.RestoreAll(); } catch (Exception e) { Log.LogWarning("Unload: " + e.Message); }
            try { SbgNet.Shutdown(); }           catch (Exception e) { Log.LogWarning("Unload: " + e.Message); }
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
            SettingsDump.Tick();
#endif
            ModHandshake.Tick();
            if (_lingerUntil > double.MinValue && Time.timeAsDouble >= _lingerUntil) CancelLingeringShield("linger over");
            ShieldState.Tick();
            ShieldTint.Tick();
            LaunchVfx.Tick();
            KillZone.Tick();
            ImmunityFlicker.Tick();
            SbgNet.Tick();

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
            if (keyboard.leftShiftKey.wasPressedThisFrame) ShieldState.NoteShieldPress(player);   // tech input

            // Comeback bubble came up while shielding: drop it, the pips are wasted otherwise.
            if (_weActivated && BlockActivationDuringImmunity.Value && HasKnockoutImmunity(player))
            {
                if (VerboseLogging.Value) Log.LogInfo("Comeback immunity is up; dropping the shield.");
                ReleaseShield();
            }

            // Climbing into a cart, springing, or a new hole's countdown starting with the shield up drops it.
            if (_weActivated && (IsInGolfCart(player) || InTeeOffCountdown() ||
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
        internal static bool ActivationBlockedByState(PlayerInfo player) => ActivationBlockedByState(player, false, out _);
        internal static bool ActivationBlockedByState(PlayerInfo player, bool forHud) => ActivationBlockedByState(player, forHud, out _);

        /// <param name="forHud">
        /// The HUD passes true so the icon does not blink out for the second or two of
        /// comeback immunity after every recovery. Input still respects it.
        /// </param>
        /// <param name="why">The first reason that blocked, for the HUD's diagnostic line. Null when not blocked.</param>
        internal static bool ActivationBlockedByState(PlayerInfo player, bool forHud, out string why)
        {
            why = null;
            if (player == null) { why = "no local player"; return true; }
            // The gate is checked HERE, not only in the HUD: without this the shield is
            // still raisable while standing down, which made the whole stand-down a lie.
            if (!ModHandshake.GameplayEnabled) { why = "standing down"; return true; }
            if (!forHud && BlockActivationDuringImmunity.Value && HasKnockoutImmunity(player)) { why = "comeback shield up"; return true; }
            if (BlockActivationInMenus.Value)
            {
                string menu = OpenMenuName();
                if (menu != null) { why = menu + " open"; return true; }
            }
            if (InTeeOffCountdown()) { why = "tee-off countdown"; return true; }
            if (KillZone.IsLingering) { why = "dead"; return true; }
            if (IsInGolfCart(player)) { why = "in a cart"; return true; }
            var movement = player.Movement;
            if (movement != null)
            {
                if (!AllowMidAirActivation.Value && !movement.IsGrounded) { why = "in the air"; return true; }
                if (movement.IsKnockedOutOrRecovering) { why = "knocked out"; return true; }
                if (movement.IsRespawningOrDrowning) { why = "respawning"; return true; }
                if (movement.DivingState != DivingState.None) { why = "diving"; return true; }
            }
            if (BlockActivationDuringSpringBoots.Value && IsUsingSpringBoots(player)) { why = "spring boots"; return true; }
            var golfer = player.AsGolfer;
            if (BlockActivationDuringSwing.Value && golfer != null && (golfer.IsChargingSwing || golfer.IsSwinging)) { why = "swinging"; return true; }
            return false;
        }

        /// <summary>Which full-screen or modal UI is eating gameplay input, or null.</summary>
        internal static string OpenMenuName()
        {
            try { if (PauseMenu.IsPaused) return "pause menu"; } catch { }
            try { if (Scoreboard.IsVisible) return "scoreboard"; } catch { }
            try { if (TextChatUi.IsOpen) return "text chat"; } catch { }
            try { if (RadialMenu.IsVisible) return "emote wheel"; } catch { }
            try { if (PlayerCustomizationMenu.IsActive) return "customization shop"; } catch { }
            try { if (VoteKickUi.IsShown) return "vote"; } catch { }
            try { if (LoadingScreen.IsVisible) return "loading"; } catch { }
            return null;
        }

        /// <summary>
        /// The 3-2-1 before a hole starts. In the game's state machine that IS
        /// MatchState.TeeOff: CourseManager shows the countdown on entering TeeOff and
        /// flips to Ongoing the frame it reaches zero. Nobody can be hit yet, so a
        /// shield here only burns the use cooldown and looks odd.
        /// </summary>
        internal static bool InTeeOffCountdown()
        {
            try { return CourseManager.MatchState == MatchState.TeeOff; } catch { return false; }
        }

        /// <summary>Any full-screen or modal UI that eats gameplay input.</summary>
        internal static bool AnyMenuOpen() => OpenMenuName() != null;

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

        private static string _lastRefusal;
        private static double _lastRefusalAt = double.MinValue;

        /// <summary>
        /// Always on, cheap: one line per distinct reason (and at most one every two
        /// seconds) when Shift does nothing. "My shield stopped working" is otherwise
        /// invisible in the log, because the HUD's own hidden-reason line deliberately
        /// leaves out comeback immunity and the cooldowns.
        /// </summary>
        private static void LogRefusal(string why, PlayerInfo player)
        {
            double now = Time.timeAsDouble;
            if (why == _lastRefusal && now - _lastRefusalAt < 2.0) return;
            _lastRefusal = why; _lastRefusalAt = now;
            string extra = "";
            try
            {
                if (why == "comeback shield up" && player?.Movement != null)
                    extra = $" (immunity: hasImmunity={player.Movement.KnockoutImmunityStatus.hasImmunity})";
                else if (why == "no pips" || why == "break cooldown" || why == "use cooldown")
                    extra = $" (pips={ShieldState.Pips}, ready in {ShieldState.SecondsUntilReady:0.0}s)";
            }
            catch { }
            Log.LogInfo($"Shift ignored: {why}{extra}.");
        }

        private void TryActivate(PlayerInfo player)
        {
            double since = Time.timeAsDouble - _lastActivationTime;
            if (since < ActivationCooldown.Value) return;
            if (!ShieldState.CanActivate(out string cdWhy)) { LogRefusal(cdWhy, player); return; }
            if (ActivationBlockedByState(player, false, out string stateWhy)) { LogRefusal(stateWhy, player); return; }

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
            ShieldState.LoweredAt = Time.timeAsDouble;
            ShieldState.ArmParryOnRelease(player);       // looks around the bubble while its collider still exists
            ShieldState.OnShieldReleased();

            float linger = Mathf.Max(0f, ParryLinger.Value);
            if (linger > 0f && ModHandshake.GameplayEnabled)
            {
                _lingerUntil = Time.timeAsDouble + linger;
                if (VerboseLogging.Value) Log.LogInfo($"Shield key released; body lingers {linger:0.00}s.");
                return;
            }

            CancelLingeringShield("released");
        }

        /// <summary>
        /// The bubble simply goes away, held or lingering: no pop, no break, no cooldown,
        /// no parry arming. For hits that are not the bubble's business (the penalty
        /// stroke) and for a hit that lands during the linger, where the game's own
        /// shield check would otherwise refuse it.
        /// </summary>
        internal static void DropShieldQuietly(string why)
        {
            _weActivated = false;
            CancelLingeringShield(why);
        }

        /// <summary>
        /// Keep the lingering body drawn a little longer (a parry just lit it up). Only
        /// ever extends a linger that is already running or begins one for a body the
        /// key has let go of; it never holds a bubble that is actually up.
        /// </summary>
        internal static void ExtendLinger(float seconds)
        {
            if (_weActivated || seconds <= 0f) return;
            var player = GameManager.LocalPlayerInfo;
            if (player == null || !player.IsElectromagnetShieldActive) return;
            double until = Time.timeAsDouble + seconds;
            if (until > _lingerUntil) _lingerUntil = until;
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
