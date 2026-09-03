# Super Battle Golf — Shields Mod, Handoff v4

**Version:** 0.7.0. Source is 11 files, ~4,900 lines, plus the csproj and a `.user.example`. Always ship the full set as a zip.
**Build:** `dotnet build -c DevDebug` for the developer's own copy, `dotnet build -c Release` for anyone else. See §3.
**Environment:** Windows, .NET SDK, BepInEx 5 via r2modman, ModConfig (AtomicStudio) for in-game settings.

---

## 0. Read this first: how the last session went wrong, and the rules that follow

The previous session wasted several rounds on a single bug ("shield breaks, but there is no stun, the comeback bubble appears immediately"). It was fixed in the end, but only after the assistant repeatedly asserted causes it had not verified. The user was explicit that this must not happen again. The specific failures, so they are recognisable:

1. **Assumed a scenario instead of asking.** The assistant decided the user must be testing in multiplayer with a friend on an old build, then that the user was hitting themselves with a rocket, then that a swung item was homing on a target. None of these were stated by the user; all were wrong. The user had said, unprompted, that they would state explicitly if a finding came from multiplayer or from a non-host session. **Take that at face value: unless the user says otherwise, the finding is solo, on their own machine, as host.**
2. **Reasoned about the game's code without reading the path actually taken.** The stun bug was defended for three rounds by re-reading the game's recovery and immunity code, which was fine. The real cause was in *the mod's own* partial-break branch (`ShieldState.ResolveKnockout`), written in an earlier session, which scaled hitstun *down* by the uncovered fraction. Nobody re-read it. **When a symptom persists after a fix, the next thing to read is the mod's own code on that path, not the game's again.**
3. **Applied an edit that silently did not land.** A `str.replace` in Python matched nothing and wrote nothing; the assistant grepped afterwards but not for the specific line. The result was the handshake gate not actually blocking activation for two versions. **Every scripted edit must `assert` that it changed the file, and the specific inserted line must be grepped afterwards.** This is now the habit in the session and must stay so.
4. **Shipped diagnostics gated behind a setting that defaults off.** The trace that would have found the stun bug in one round was verbose-only, and verbose defaults to off. It is now always-on for break events (§6). **Diagnostics for a reported bug must be on by default and cheap.**
5. **Trusted a fragmentary read of the game code.** Several early claims came from `grep -A N` output that had been truncated. **If a claim depends on what a game method does, read the whole method.**

Also true and worth holding onto: the user's descriptions were accurate every time. "Blue shield wakes you up" was a correct description of *what they saw*; the assistant's job was to find *why*, and it kept translating the description into a mechanism prematurely. When the user says something is happening, it is happening.

---

## 1. Environment

| Thing | Path / value |
|---|---|
| Game | `C:\Program Files (x86)\Steam\steamapps\common\Super Battle Golf` |
| Managed DLLs | `<Game>\Super Battle Golf_Data\Managed` |
| Source project | `C:\Users\elija\Documents\Coding and Projects\Super Battle Bros\` |
| BepInEx profile | `C:\Users\elija\AppData\Roaming\r2modmanPlus-local\SuperBattleGolf\profiles\Super Battle Bros\BepInEx` |
| Plugin install | `<profile>\plugins\SbgShields.dll` |
| Config | `<profile>\config\com.sbg.shields.cfg` |
| Log | `<profile>\LogOutput.log` |
| High-score sound | `<profile>\plugins\sbg_highscore.ogg` (user supplies; not yet placed) |

The csproj no longer hardcodes these. It derives Steam and AppData paths with a `ProfileName` property, and a gitignored `SbgShields.csproj.user` (copy from the `.example`) overrides them. It also warns at build start if the Managed folder or BepInEx is not where it expects.

**Compiling in the assistant's sandbox:** there are no game DLLs available. Mono's `mcs` was installed via apt and used to parse every file (`mcs -target:library -langversion:latest *.cs`). A clean result is *only* `CS0246` (missing type) errors and zero warnings. Any other error code is a real syntax/semantic problem. tree-sitter was also used; it reports one false positive at the `_suppressed(p.Movement) = false;` ref-delegate assignment in `Patches.cs`, which is valid C#. The user can also upload the Managed folder zipped, which would allow real compiles.

---

## 2. Files (0.7.0)

| File | Owns |
|---|---|
| `Plugin.cs` | BepInEx entry; every `ConfigEntry`; Shift input loop; `TryActivate` / `ReleaseShield`; `ActivationBlockedByState` (the single gate the input AND the HUD use); selective config reset; emote pose; `RunCoroutine` host for statics; debug polling and keys (`#if SBG_DEV`). |
| `ShieldState.cs` | The economy. Pips, percent, cooldowns, cost table + overrides, `InPlayableHole`, `ResolveKnockout` (the prefix logic), `ShapeLaunch`, `Break`, `ChargeReflection`, `StunInPlaceAfterBreak`, knockback curve, refund-on-refusal, respawn/hole/match resets. |
| `Patches.cs` | Harmony patches for rooting, knockout economy, hitstun, break-stun hold, break immunity, immunity bypass on break, velocity correction + drag + hang, reflection charging, respawn, emote fix, audio suppression, item/aim/spring-boots blocks, and the `BreakTrace`. |
| `Launch.cs` | Per-launch tracker: apex detection, apex wake-up, distance sent + session best, `HighScoreSound` (Unity AudioSource from a file), and the patch that skips immunity after a wake-up. |
| `KillZone.cs` | Star KO at/above `KillPercent`: upward boost, apex/timeout trigger, flash + boom + shake, hide-and-linger, respawn. Patch that holds visibility during the linger. |
| `ShieldTint.cs` | `Skin.Of`; gradient-preserving retint of the vanilla shield/dissolve/hit/break VFX, armed around the game's own `SetTeam`; `BubbleMaterialTintPatch`; material lifetime + sweeps. |
| `ModHandshake.cs` | Version handshake over text chat; public-lobby refusal (constant); gate that stands the mod down; `ChatBypass` (sends past the mute check); chat receive patch. |
| `ChatCommands.cs` | `#if SBG_DEV` only. `/give`, `/pct`, `/pips`, `/sbg`. Intercepts `TextChatManager.SendChatMessage`. |
| `Hud.cs` | IMGUI: percent (with rumble, punch, motion blur), bubble icon + pips, "sent N m" readout, stand-down panel. Repaint-only, cached measurements. |
| `LaunchVfx.cs` | Smoke trail emitter; shared `FindShader` / `MakeUnlitMaterial`. |
| `SbgShields.csproj` | Machine-independent paths, `DevDebug` config defining `SBG_DEV`, optional `AutoDeploy`. Audio module references added in 0.6.9. |

Deleted in earlier sessions and must not exist in the source folder: `BubbleVisual.cs`, `ModMenu.cs`. A stale copy breaks the build with CS0117 errors that look like missing config entries.

---

## 3. Build configurations

- **DevDebug** defines `SBG_DEV`. Compiles in: `ChatCommands.cs` entirely; F6 (+25%), F7 (−1 pip), F8 (reset), F10 (repeat last `/give`); config entries `Debug.DebugKeys`, `Debug.SetPercent`, `Debug.ChatCommands`, `Debug.GiveItem`. All default **off** even in this build; `Debug.ChatCommands` must be switched on in ModConfig before `/give` works.
- **Release** contains none of that. A Release DLL has no item-spawning code to find or re-enable.
- The load line in the log says which you are running: `... loaded (DEV build: ...)` or `... loaded (RELEASE build: ...)`. **Check this first when something "does nothing".** In the last session the user built 0.6.3 (which failed) and was still running an older Release DLL for two rounds.
- `/give` is host-only: `PlayerInventory.ServerTryAddItem` is a `[Server]` method that logs internally and returns `false` on a client. There is no client-side way around it. The command now says so in chat.

---

## 4. Verified game architecture (read in full this session; file:line in the decompiled source)

**Authority.** `PlayerMovement.syncDirection = SyncDirection.ClientToServer` (PlayerMovement.cs:751). The owning client is authoritative over its own knockout state, visibility, immunity and position. Every write the mod makes is guarded by `Local.Is`, so it changes only the local player's own state, which then replicates normally. An unmodded observer sees your modded launches and stuns correctly. This is why local-only design is consistent, and why *nothing* the mod does is a desync.

**Item inventories are server-authoritative.** That is the layer the mod genuinely violates: `TryActivate` fabricates an `ItemUseId` for a magnet you do not own. The server rate-checks activations at 0.5 s (hence `ActivationCooldown` floor 0.6). The project references an `AntiCheat` assembly; what it inspects beyond rate checks is unknown.

**How a hit reaches the victim.** Attacker's `PlayerGolfer` overlaps the swing box → `Hittable.HitWithGolfSwing` → runs `HitWithGolfSwingInternal` locally *and* sends `CmdHitWithGolfSwing` → server forwards `RpcHitWithGolfSwing` to every other client (no shield filtering, Hittable.cs:2725) → each client runs `HitWithGolfSwingInternal`; only the one whose rigidbody it simulates (`CanApplyPhysics`, Hittable.cs:2300) applies physics → fires `WillApplyGolfSwingHitPhysics` → `PlayerMovement.OnLocalPlayerWillApplyGolfSwingHitPhysics` (5006) → `TryKnockOut(..., ElectromagnetShieldHitBlockType.FullyBlocked, ...)` → then `Rigidbody.linearVelocity += knockback` (Hittable.cs:912). Items go the same way through `HitWithItemInternal` (1314) → `WillApplyItemHitPhysics` → `TryKnockOut` with real `localOrigin` and `distance` → `AddForceAtPosition(..., VelocityChange)` (1423).

**So `TryKnockOut` is reached on the victim with the shield up**, for swings, items, projectiles and collisions. Our prefix runs first, cancels the shield if it breaks, and the game's own `CanBeKnockedOutBy` then sees no shield. This is why the shield must be spent in the prefix and refunded in the postfix if refused.

**Reflections do NOT go through `TryKnockOut`.** A projectile that bounces off the shield produces `PlayerInfo.PlayElectromagnetShieldHitInternal` on the owner and nothing else. `ReflectionChargePatch` charges pips from that. A break on that path has no knockout to hold, so `StunInPlaceAfterBreak` calls the game's `TryKnockOut` ourselves with zero velocity and `RequestingBreakStun` set, and `ResolveKnockout` turns that request into the in-place stun.

**The game's knockout state machine.** `TryKnockOut` (1548) → `CanKnockOut` local fn → `CanBeKnockedOutBy` (1635) — checks team (`excludeSelf: true`, so self hits are never team-blocked), then comeback immunity with `suppressKnockoutImmunity` **hardcoded false** by `CanKnockOut`, then `IsKnockoutProtectedFromPlayer` (domination, red), then frozen, then shield block type → `SetKnockOutState(InAir)` (3881) which on the None→knocked-out transition sets `timeUntilKnockoutRecovery = KnockoutDuration`. `UpdateKnockOutState` (2801) ticks the timer; when it hits 0 or `KnockoutTimeOutDuration` elapses, calls `RecoverFromKnockout` (3820) — the **only** path into `Recovering` (3853) and the only normal path to `None`. Direct `SetKnockOutState(None)` also happens on teleport, respawn, elimination, invisibility, and freeze. `StartKnockoutImmunity` (3932) is called from `SetKnockOutState` on the transition to Recovering/None, i.e. **immunity is the consequence of recovery, never its cause**.

**The three comeback bubbles.** Blue = normal post-knockout immunity. Orange/gold = long immunity after repeated knockouts (`recentKnockoutImmunityTimestamps`). Red = domination protection against one specific player (`IsKnockoutProtectedFromPlayer`, match rule `DominationProtection`). All three only ever refuse *incoming* knockouts.

**Explosion knockback in the game** (Hittable.cs:1340-1420): per item type, speed lerps between Min/Max by distance (`InverseLerpClamped(MaxKnockbackDistance, MinKnockbackDistance, distance)`), direction is radial from the blast, and then `knockback.y = max(y, MinUpwardsKnockbackSpeed)` forces a vertical floor. That floor is why explosions felt "not position-relative" once multiplied; `ExplosionRadialLaunch` rebuilds the direction from `localOrigin`.

**Lobby privacy** is a SyncVar on `MatchSetupMenu.lobbyMode` (Public/Friends/InviteOnly); clients can read the host's value.

**Text chat** is `TextChatManager.SendChatMessage` (mute check, profanity filter) → private `CmdSendMessageInternal` (rate-limited on the server) → `RpcMessage` to everyone → `UserCode_RpcMessage__String__PlayerInfo` displays. The handshake sends via the Cmd directly (skipping the mute check) and swallows its own token in a prefix on the display method.

**Mirror and custom messages:** a peer that receives a message id it has no handler for is disconnected. That is why the handshake is over chat and not a custom message.

---

## 5. Mechanics as of 0.7.0

**Activation.** Hold Left Shift. Blocked when: standing down (§7), in a menu (pause/scoreboard/chat/emote wheel), knockout immunity up, in a golf cart, spring boots active, knocked out / respawning / diving, charging or mid-swing, mid-air if `AllowMidAirActivation` false. Shield drops if a cart, spring boots or immunity begins while holding. While up: rooted, no jump, no dive, no swing, no item use, no aiming (weapon swap allowed).

**Pips and costs.** 5 pips. Costs: 1 (pistols, deflected shots, returned ball, untargeted swing projectiles), 2 (back blast, thunderstorm/laser peripheral, magnet explosion), 3 (cart, traffic, rocket driver swings, landmine), full break (swing, rocket, freeze bomb, targeted swing projectiles, everything unlisted), unblockable (laser/thunderstorm/railgun direct hits; shield drops, hit lands in full). Overridable per type via `Costs.Overrides`.

**Absorb / break.** Cost < pips: absorbed, knockback cancelled, no percent. Cost ≥ pips or full-break type: **the shield breaks and you are stunned in place for `BreakStunDuration` (2.5 s), always.** Percent gain on a break is the uncovered fraction only (`1 − pips/cost`; full-break types use `PercentGainOnFullBreak`, default 0). The old behaviour — partial breaks launching at the fraction with proportionally shortened hitstun — is behind `PartialBreakLaunches` (off) and was the "instant wake-up" bug. Break through comeback immunity is forced by `BreakBypassesImmunityPatch`. If the game refuses the knockout for another reason (team, frozen), pips are refunded.

**Break stun hold.** `HitstunPatch` sets the recovery timer and `BreakStunUntil`; `BreakStunHoldPatch` refuses `RecoverFromKnockout` until then. Immunity after a break stun is percent-scaled (`BreakImmunityAtZeroPercent`→`AtMaxPercent`). **Verified in play from the trace:** `stun applied 2.50s → hold expired +2.52s → Recovering → immunity → None +3.03s`.

**Percent.** Only inside a real hole (`InPlayableHole`: TeeOff/Ongoing/CountingDownToEnd/Overtime, not the driving range); the shield still works elsewhere for testing. Full reset on match start, full reset each hole (`PercentReductionBetweenHoles` = 1), flat −25 on respawn (45 s fatigue window), 25 after a star KO. Explosion percent scales with distance (`ExplosionFalloffRadius` 8 m, 30% at the edge). Hits taken while already knocked out use vanilla knockback and still add percent.

**Knockback curve.** `mult = 1 + (ForceMultiplierAtKill − 1) × (percent / KillPercent)^KnockbackExponent`. Defaults 6× and 1.5 give ≈ 40% 1.4×, 80% 1.9×, 100% 2.3×, 150% 3.3×, 200% 4.6×, 250% 6×. Horizontal gets `HorizontalMultiplierAtKill` (1.6×) on the same curve. `PercentForMaxScaling` (100) is now only the saturation point for angle floor, hang, hitstun, break immunity and HUD shake. Explosions: the percent force bonus is also scaled by distance (`ExplosionForceAtEdge` 0.25) and the direction is rebuilt radially from the blast (`ExplosionRadialWeight` 0.8). Drag only acts on speed above `LaunchDragAboveSpeed` (14 m/s); the old all-speed drag removed ~85% of horizontal travel and was why "horizontal knockback isn't felt".

**Launch tracker (`Launch.cs`).** Starts at a launch. Apex = vertical speed crosses zero. **Apex wake-up** ends the knockout in the air, only for launches taken at ≥ `AirRecoveryMinPercent` (125), never on a kill, no comeback bubble (`AirRecoveryGrantsImmunity` off). The smoke trail ends at the apex for the local player. When the launch ends (grounded / OnGround) the horizontal distance from the hit is shown ("sent N m", gold "NEW BEST" on a session best ≥ `DistanceMinimum`), and a new best plays `Audio.HighScoreSoundFile` through a Unity `AudioSource`. Hang time (`LaunchHangTime`) is **off** by default and cloud-hit-only if enabled; the user may want it removed entirely.

**Kill zone.** A knockout committing percent ≥ 250 arms it: +45 m/s up on the killing blow, no drag; at the apex (or 2.5 s) a star flash + shield-explosion boom + rocket screenshake, hide via `LocalPlayerUpdateVisibilityPatch`, linger 5 s with the camera holding, then `TryBeginRespawn`. Local only; no KO feed, no attacker credit (needs a host-side message layer).

**HUD.** Percent bottom-centre (`HudBottomMargin` 150, `PercentVerticalOffset` 14), bubble icon to its left, pips under the icon (hidden during break cooldown, pop in left→right when they return). Percent rumbles on a hit and decays to still; settle time from `ShakeDurationTable` ("0:0.8, 30:2, 60:3, 90:5", interpolated); centred zoom punch; ghost-copy motion blur. Icon hides when the shield cannot be raised unless a break cooldown is counting. Percent hidden outside a hole. Stand-down panel replaces the HUD when the gate is closed.

**Visuals.** Vanilla shield VFX (hold, dissolve, hit sparks, break) retinted to skin colour before `Play()` via the `SetTeam` postfix, gradients preserved key-by-key. Magnet *item* keeps team colour and its hum.

---

## 6. Diagnostics (always on, cheap)

- **Load line:** `SBG Shields <ver> loaded (DEV build: ...)` / `(RELEASE build: ...)`.
- **Break trace:** every shield break logs `[break +0.00s] ...` lines for ~4 s: the break itself, `CanBeKnockedOutBy -> True/False (immunitySuppressed=, team=, hasImmunity=, shieldActive=)`, every `SetKnockOutState A -> B`, `stun applied: timer=`, `RecoverFromKnockout allowed/BLOCKED`, `StartKnockoutImmunity`. If a break produces **no** `[break` lines, the break did not go through a path that begins the trace — check `ChargeReflection` and the `RequestingBreakStun` path.
- **Handshake:** `SBG Shields is inactive: <reason>` / `SBG Shields is active.` at Warning level; per-player version lines at Info.
- **`VerboseLogging`** (off by default) adds: `Hit <type> at N%...` with the shaped velocity, `Shield absorbed/broken`, `Percent set`, `Sent N m`, `Apex wake-up`, tint dumps, handshake announces.
- **`/sbg`** (dev build): version, gate state, pips, percent, host/client.

---

## 7. Networking and safety posture

- `Network.RequireAllPlayersModded` (on): stand down unless every remote player announces the same version over chat within `HandshakeTimeout` (12 s). Announce once on join; re-announce once when a new peer appears; retry every second while there is no chat manager; 3 s minimum interval; no message ceiling. Departed players are forgotten every tick. Own announcement is swallowed but not recorded.
- **`PrivateLobbiesOnly` is a `const true`**, not a config entry: in a Public lobby the mod is fully inert and silent. Unreadable lobby mode is treated as Public. The gate starts **closed** and opens only after a successful evaluation.
- Stand-down means: `ActivationBlockedByState` returns true, `ResolveKnockout` returns before the absorb branch, `InPlayableHole` is false. Verified all three in 0.6.3 after a review found the first two missing.
- Honest limits, unchanged: everything is client-side, so this stops accidents and version drift, not a determined cheater; the fabricated `ItemUseId` and unsynced pips/percent/KO credit remain; only a host-side message layer fixes those.
- Unpatch on unload restores every material, destroys every generated texture/material/GameObject, unsubscribes the HUD from the static `PercentIncreased` event. Prefixes that can return `false` all carry `HarmonyPriority(Priority.Low)`. No transpilers anywhere.

---

## 8. Open items (priority order)

1. **Verify 0.7.0 in play:** cloud hit past 125% → apex wake-up + "sent N m"; sound file placed; the break trace on a landmine break should read like §5.
2. **Kinetic vs explosive categories.** Agreed design, not built: guns (pistol, elephant gun) should take the game's own knockback with a smaller percent multiplier and *no* elevation floor; melee (swing, cart) some floor; explosive keeps the lift. Railgun: leave exactly as vanilla, user's decision.
3. **Decide hang time:** remove if it does not earn its place on cloud hits.
4. **Break warning on the shield** at ≤1 pip, then drop `ShowPipDots`.
5. **Host-side message layer** (custom Mirror message, host advertises first so vanilla peers are never sent an unknown id): pip/percent sync, KO feed + attacker credit on star KO, and the only real fix for the authority problem. This is a project, not a patch.
6. **Attacker credit on reflection breaks** currently goes to the victim (self). Fixable if the reflected projectile carries its owner.
7. **README** for distribution: private lobbies only, same version required, use at own risk.
8. The one-off "swung item slowed then sped up" report: no mod code touches non-player rigidbodies; if it recurs, add a trace on swung `Hittable` items and get item type + host/client.

---

## 9. Config reference (0.7.0 defaults)

`[Shield]` ActivationCooldown 0.6, ShieldAbsorbsHits true, MaxPips 5, UseCooldown 1, BreakCooldown 8, RestoreAfterBreakCooldown true, BreakStunDuration 2.5, PartialBreakLaunches false, RefundPipsOnRefusedKnockout true, BreakImmunityScalesWithPercent true, BreakImmunityAtZeroPercent 0.5, BreakImmunityAtMaxPercent 3, AbsorbedHitsCancelKnockback true, ReflectionSearchMargin 1.5.
`[Costs]` Overrides "".
`[Percent]` MaxPercent 300, PercentForMaxScaling 100, ForceMultiplierAtKill 6, KnockbackExponent 1.5, HitstunMultiplierAtMax 1.25, PercentPerHitBase 5, PercentPerPip 4, PercentPerFullBreakHit 25, PercentPerUnblockableHit 30, PercentGainOnFullBreak 0, ExplosionPercentFalloff true, ExplosionFalloffRadius 8, ExplosionPercentAtEdge 0.3, PercentReductionBetweenHoles 1, PercentLostOnRespawn 25, RespawnFatigueWindow 45, KillZoneEnabled true, KillPercent 250, PercentAfterKillZoneDeath 25, KillZoneFlash true, KillZoneFlashSize 9, KillZoneUpwardBoost 45, KillZoneMaxRiseTime 2.5, KillZoneDeathLinger 5, KillZoneBoom true.
`[Launch]` ShapeLaunches true, MinLaunchAngleAtZero 8, MinLaunchAngleAtMax 28, HorizontalMultiplierAtKill 1.6, MaxHorizontalLaunchSpeed 26, ExplosionForceAtEdge 0.25, ExplosionRadialLaunch true, ExplosionRadialWeight 0.8, LaunchDrag 0.8, LaunchVerticalDragFactor 0.5, LaunchDragDuration 1.5, LaunchDragAboveSpeed 14, LaunchHangTime 0, LaunchHangWindow 7, LaunchHangDuration 3, AirRecoveryAtApex true, AirRecoveryMinPercent 125, AirRecoveryGrantsImmunity false, TrailEndsAtApex true, LaunchTrail true, LaunchTrailStartSpeed 12, LaunchTrailMinPercent 75, LaunchTrailStopSpeed 5, LaunchTrailRate 45, LaunchTrailRatePerMetre 5, LaunchTrailSize 2.2, LaunchTrailLifetime 1.7, LaunchTrailAlpha 1.
`[Rooting]` RootWhileShielded, BlockJumpWhileShielded, BlockSwingWhileShielded, BlockDiveWhileShielded, AllowMidAirActivation, BlockActivationDuringSwing, BlockActivationDuringSpringBoots, BlockActivationInMenus, BlockItemUseWhileShielded, BlockAimWhileShielded, BreakStunIgnoresComebackImmunity, BlockActivationDuringImmunity — all true.
`[Bubble]` TintVanillaShield true.
`[Pose]` PlayShieldEmote false, ShieldEmote "HandsUp".
`[HUD]` ShowHud true, HudScale 1, HudBottomMargin 150, HudHorizontalOffset 0, BubbleHudSize 64, BubbleHudGap 22, BubbleHudOnLeft true, ShowPipDots true, PercentFontName "DFGothic-EB" (not installed on the user's machine; falls back), PercentFontSize 72, ShakeDurationTable "0:0.8, 30:2, 60:3, 90:5", PercentShakeDuration 2 (fallback), PercentShakePixels 5, PercentShakeSpeed 90, PercentShakePunch 0.3, MotionBlurSamples 3, MotionBlurLength 2.5, PercentVerticalOffset 14, ShowDistanceSent true, DistanceMinimum 5.
`[Audio]` SuppressHum true, SuppressMuffle true, HighScoreSoundFile "sbg_highscore.ogg", HighScoreSoundVolume 1.
`[Network]` RequireAllPlayersModded true, HandshakeTimeout 12, MismatchPopupDuration 12. (PrivateLobbiesOnly is a constant.)
`[Debug]` VerboseLogging false; dev-build only: DebugKeys false, SetPercent −1, ChatCommands false, GiveItem "".
`[Meta]` ConfigVersion (internal). `RetunedThisVersion` in `Plugin.cs` lists which keys reset when the version changes; keep it in step with `Version`.

---

## 10. Working agreements with the user

- No handoff document in every drop; only when asked. Zips are named `SbgShields-<ver>.zip` and contain every file.
- The user builds and tests; the assistant cannot compile against the game. Ask for the `[break …]` lines or the load line when a report is ambiguous — do not guess the scenario.
- Solo, own machine, host, unless the user says otherwise.
- Bump `Version` on every drop that changes behaviour; list retuned defaults in `RetunedThisVersion`.
- Do not remove features unasked; put replaced behaviour behind a toggle (as with `PartialBreakLaunches`) so the user can compare.
