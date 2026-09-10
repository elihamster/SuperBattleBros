# Super Battle Golf — Shields Mod ("Super Battle Bros"), Handoff v6

**Version:** 0.7.22 (test build). 14 `.cs` files, csproj, `package.ps1`, `thunderstore/`.
**Build:** plain `dotnet build` is the dev build (DevDebug, `SBG_DEV`, title "SBG Shields TEST"). `dotnet build -c Release` is the public build. `.\package.ps1` builds DevDebug (while testing) and zips `dist\SuperBattleBros-<ver>.zip` for Thunderstore. See §1 and §3.
**Environment:** Windows, .NET SDK, BepInEx 5 via r2modman, ModConfig (AtomicStudio). Repo at github.com/elihamster/SuperBattleBros (public). Claude Code runs in the repo; every game-code claim comes from the decompiled assemblies in `decomp/` (gitignored) and the runtime settings dump `decomp/SbgSettingsDump.txt`.

Read §0, §10 and §11 first. §5 is the mechanics as they stand. §8 is what to do next.

---

## 0. How the v5 → v6 session went wrong, and the rules that follow

The session (8–10 Sep 2026) shipped 0.7.8 through 0.7.21: thirteen versions in three days, the first multiplayer tests, the first custom network message. Most of what went wrong came from one habit: assuming a rule held on every machine when it held on one.

1. **Design decided per machine.** Reflection (who simulates the ball), remote velocity (interpolated bodies report zero), which player's shield the tint knew about, whose percent anyone could see: each produced a "friend doesn't see X" bug. The two answers are (a) the message layer (§7) and (b) "every client applies the rule to every player" (the bubble collider). **Before shipping anything visible, ask: which machine decides this, and does every other machine agree?**
2. **The same visual mistake twice, then a third variant.** Darkening the bubble to show "low pips" read as a black stain in 0.7.8 (pip blink dimmed to 25%) and again in 0.7.16 (brightness fell with pips). 0.7.20 paled it toward white instead, and the user pointed out every colour then ends up the same. 0.7.22 makes a worn bubble THINNER (alpha, 40% at one pip) with only slight lightening. Rules, now in `ShieldTint` and §10: never darker, never all the way to white, the colour must stay recognisable at one pip. The user's screenshots of "the black tint" on 10 Sep were from 0.7.19, which still dimmed; check the load line before assuming a report is about the current build.
3. **A network gate with a hole.** 0.7.12's message layer sent while the gameplay gate was open, and the gameplay gate grants a newcomer 12 s of grace. A vanilla joiner would have received a custom message and been disconnected. Found in the self-review, fixed in 0.7.13 with a strict send gate (`ModHandshake.AllPeersConfirmed`). **Review your own day's work against the game code before the user finds it.**
4. **Reach in metres against a ball at 20 m/s.** The first reach-based parry could never arm on a ball: 1.5 m is 75 ms. The user's logs showed zero arms all session. Time-to-impact fixed it. **Sanity-check a tuning number against the game's real speeds (they are in the dump now).**
5. **The cost table met a world it was not written for.** Once bubbles absorbed instead of reflecting, "targeted ball = full break" meant any homing ball popped a full bubble on contact, and the user reported it as a bug. When a rule changes, walk the table that depends on it.
6. **Deploy confusion.** AutoDeploy copied the dev DLL into the r2modman package folder; r2modman's update copied the Thunderstore DLL back over it; a dump build ran once with the wrong file. Rule now: the user tests the live Thunderstore build, AutoDeploy is OFF, and any one-off local DLL is deployed by hand and announced.
7. **The decompiled folder got compiled.** Moving `decomp/` into the project pulled 700 game files into the build. `DefaultItemExcludes` now excludes it.
8. **`sed` edits that did not land.** One multi-line `sed` matched nothing; the exact-match editor (fails loudly) is the tool for code.
9. **Reading the log before the code, and the code before guessing.** Every real cause this session came from a log line plus a decompiled method: the 3 s knockout timer, the instant recovery for `InAir`, the gravity factor swap, the raycast guns, the predicted rigidbodies, the host's elimination flag, the zero-alpha skin colours. When the user says something is happening, it is; the job is the mechanism.

The user's descriptions were accurate every time, including "I don't die at 250%", "the stun stacks until the gold shield", "the black tint is still active" (it was the new dimming) and "the friend sees no icon" (zero-alpha skin).

---

## 1. Environment and workflow

| Thing | Path / value |
|---|---|
| Game | `C:\Program Files (x86)\Steam\steamapps\common\Super Battle Golf` |
| Managed DLLs | `<Game>\Super Battle Golf_Data\Managed` |
| Repo | `C:\Users\elija\Documents\Coding and Projects\Super Battle Bros\` — remote `origin` = github.com/elihamster/SuperBattleBros, branch `master` |
| BepInEx profile | `%APPDATA%\r2modmanPlus-local\SuperBattleGolf\profiles\Super Battle Bros\BepInEx` |
| Plugin install | `<profile>\plugins\hamsterbby-SuperBattleBros\SbgShields.dll` (r2modman package folder; no loose copy) |
| Config | `<profile>\config\com.sbg.shields.cfg` |
| Log | `<profile>\LogOutput.log` (overwritten per session) |
| Settings dump | `<profile>\SbgSettingsDump.txt`, copied to `decomp\SbgSettingsDump.txt` |
| Decompiled game | `decomp\GameAssembly\` (705 files), `decomp\SharedAssembly\` (68); `ilspycmd -p -o <dir> -r <Managed> <dll>`; gitignored |
| Local build overrides | `SbgShields.csproj.local` (gitignored; example checked in). NOT `.csproj.user`: MSBuild auto-imports that name late and double-imports. Currently `AutoDeploy=false`. |
| Thunderstore | package `hamsterbby/SuperBattleBros`; deps `BepInEx-BepInExPack-5.4.2305`, `AtomicStudio-ModConfig-0.1.3`; `manifest.json` `name` must never change |
| GitHub CLI | installed (`gh`), logged in as elihamster |

**Release routine.** Bump `Plugin.Version` → `.\package.ps1` (stamps the manifest from `Plugin.cs`, zips `dist\SuperBattleBros-<ver>.zip`) → Claude commits and pushes and drops the zip into the chat → the user uploads on thunderstore.io → both players update in r2modman (the index lags a few minutes). Thunderstore refuses a reused version number. Commits carry no Claude attribution trailer (user's request).

**Session-specific temp folder** may vanish; anything worth keeping lives in `decomp/` or the repo.

---

## 2. Files (0.7.21)

| File | Owns |
|---|---|
| `Plugin.cs` | BepInEx entry; every `ConfigEntry`; Shift loop; `TryActivate` / `ReleaseShield` / `CancelLingeringShield`; `ActivationBlockedByState(..., out why)`; `OpenMenuName`; `InTeeOffCountdown`; `RetunedThisVersion`; tech input capture; dev polling. `Name` is "SBG Shields TEST" under `SBG_DEV`. |
| `ShieldState.cs` | Economy: pips (`LosePips`, `PipFraction`), percent, cooldowns, cost table (§5), `InPlayableHole`, `ResolveKnockout`, absorb sparks, `QueueBreakBounce`, `BounceAfterBreak`, parry arming (`ArmParryOnRelease`, reach + time-to-impact + aimed-at), `IsPerfectParry`, `Parry`, hit categories, `ShapeLaunch`, `SpeedFactor`, `GetPercentGain`, teching (`NoteShieldPress`, `OnTumbleLanded`, `TryExecuteTech`), landing stun + floor. |
| `Patches.cs` | Harmony: rooting (incl. tech root), `KnockoutEconomyPatch` (captures responsible player), `HitstunPatch` (+`ClampRecoveryTimer`), `BreakTrace`, trace patches, `TumbleLandingPatch`, `RecoveryHoldPatch` (kill hold + `AirHold`), `TechImmunityPatch` (tech + `RecoveryImmunity`, gold falls through), `VelocityCorrectionPatch` (corrections, tech, DI tick, drag, hang), `TumbleGravityPatch`, `ReflectionChargePatch` (only if `BubbleReflects`), `BreakSoundCarryPatch`, `BubbleColliderPatch` (shield collider = trigger), `InstantKillIgnoresBubblePatch`, `RespawnPercentPatch`, aim/item/spring-boots blocks, `BreakBypassesImmunityPatch`, audio suppression. |
| `Launch.cs` | Per-launch: active until LANDED (not until recovered), cloud-hit flag for hang, camera-relative DI. |
| `KillZone.cs` | Star KO; `PlayStar` shared by local and `PlayRemoteStar`; sends `StarKo` over SbgNet. |
| `ShieldTint.cs` | Tints EVERY player's shield (magnet item included) to `BubbleColour(p)`: skin → pale white by worn pips, hot flash on loss; pip-warning blink; parry flash; per-owner material handoff; `AnyShieldActive` restore. |
| `ModHandshake.cs` | Chat handshake; `GameplayEnabled` (grace for newcomers) and `AllPeersConfirmed` (strict, for sending); nudge re-announce at half timeout; always-on announce line. |
| `SbgNet.cs` | The mod's one Mirror message: `Percent`, `StarKo`, `Parry`, `Pips`. Hand-registered writer/reader. Send only when `AllPeersConfirmed`. Server accepts only from the owning connection, clamps, `SendToAll`. Caches remote percent and pip fraction. |
| `AimedAt.cs` | Experimental hitscan read: another player aiming any item along a corridor through your bubble arms the projectile parry. Yaw only. Delete file + one call + two entries to remove. |
| `ImmunityFlicker.cs` | Smash-style washed-out flicker on every player's body while their comeback shield is up (property blocks on `_Color`/`_BaseColor`); optional `HideGameBubble` postfix. |
| `LaunchVfx.cs` | Smoke trails (remote speed from `SyncedVelocity`), rage embers per player (percent from SbgNet for remotes), shared shader/material helpers. |
| `Hud.cs` | IMGUI: percent, bubble icon, five circles of two pips (half circle for odd), loss flash, stand-down panel, always-on "icon hidden (reason)" line. |
| `ChatCommands.cs` | `#if SBG_DEV`. `/give`, `/pct`, `/pips`, `/sbg`. |
| `SettingsDump.cs` | `#if SBG_DEV`. Appends every `GameManager.*Settings` object and the local hittable's settings + shield collider to `BepInEx\SbgSettingsDump.txt` as they appear. |
| `SbgShields.csproj` | Plain build → DevDebug; `DefaultItemExcludes` for `decomp\**`, `dist\**`; `DeployDir`; AutoDeploy only for DevDebug. |
| `package.ps1` | Builds (`-Configuration`, default DevDebug), stamps manifest, zips README/CHANGELOG/icon/manifest/DLL. |

Deleted this session: `PartialBreakLaunches`, break stun-in-place and its six knobs, `PerfectParryWindow`, `DIMaxPitch`, `BubbleMinBrightness`, `ForceMultiplierAtKill`/`HorizontalMultiplierAtKill` (→ `*AtMax`). Handoff v4 deleted; v5 replaced by this file.

---

## 3. Build configurations

- **DevDebug** (`dotnet build`): `SBG_DEV`, chat commands, F-keys, settings dump, title "SBG Shields TEST". This is what ships on Thunderstore while the mod is in testing (`package.ps1` default), so friend and developer run identical DLLs.
- **Release** (`-c Release`): none of that, title "SBG Shields". Build it too before every drop to be sure it compiles.
- The load line names the build and the pip count: `SBG Shields TEST 0.7.21 loaded (DEV build ...). Pips=10`.

---

## 4. Verified game architecture (decompiled source + settings dump)

Everything in v4/v5 §4 still holds. Added this session, all read in full:

**Knockout recovery.** `UpdateKnockOutState` → timer ≤ 0 or `KnockoutTimeOutDuration` → `RecoverFromKnockout` → `ShouldRecoverInstantly()` is TRUE for any state that is not `OnGround`, so a mid-air timer expiry flips straight to `None` with no landing check. On `None`, `UpdatePhysicsParameters` swaps `KnockOutGravityFactor` (1.325) for 1: the slow float. `SetKnockOutState(Recovering|None)` calls `StartKnockoutImmunity`. `KnockoutRecoveryDuration` (get-up animation) 0.5 s.

**Constants (dump).** `KnockoutDuration` 3.0, `KnockoutTimeOutDuration` 10, `PostKnockoutImmunityDefaultDuration` 3 (match rule), long 7 after 3 knockouts in 30 s, `BaseGravityFactor` 2.87, `DefaultTerminalFallingSpeed` 50, `KnockOutAirLinearDamping` 0.6.

**Per-hit knockback speeds (hittable settings, dump).** Elephant gun 45–60 (far–near), rocket 25–40 (+min upwards 5–15), magnet explosion 25–40, laser 60–75, railgun 40, thunderstorm 30, pistol 15–30, landmine 20–30, full-power swing 30 (rocket driver 50–90), ball hit 10–20, back-blast 20. Guns are the game's strongest ordinary hits. Explosions scale by distance to the blast; guns by range; swings by charge; balls by ball speed; carts by vehicle speed.

**Guns are hitscan.** `PlayerInventory` shoots a raycast from the barrel on `GunHittablesMask` (`QueryTriggerInteraction.Ignore`). A shield collider on the ray → `ReflectShotOffElectromagnetShield` on the shooter's client, victim never sees a hit. Otherwise `HitWithItem` → victim's `OnLocalPlayerWillApplyItemHitPhysics` → `TryKnockOut`. Ranges: pistol/elephant gun `MaxShotDistance` 750.

**Knockback is applied regardless of refusal.** `Hittable.HitWithGolfSwingInternal` and `HitWithItemInternal` add the velocity (or `AddForceAtPosition`) AFTER the `WillApply...` event that runs `TryKnockOut`, whatever it returned. A refused knockout still shoves.

**Balls and carts are Mirror-predicted.** `Entity.IsPredicted`; prediction moves the physics body onto a separate object with no `Entity`/`Hittable`. Use `collider.attachedRigidbody`, never `GetComponentInParent<Entity>()`, to reason about a moving ball on a client.

**Remote velocity.** `PlayerMovement.Velocity` is the local rigidbody's; for remote players read `SyncedVelocity` (SyncVar).

**Protective state and elimination.** `Hittable.HitWithItemInternal` builds `ProtectiveState` from `IsElectromagnetShieldActive` / giant form on each client. On the server, `PlayerGolfer.OnServerWasHitByItem(itemUser, itemType, itemUseId, direction, distance, isReflected, protectiveState)` → `itemType.TryGetEliminationReason(...)` → `ServerSetPotentialEliminationReason` → `CanBeEliminatedByHit` (only `OrbitalLaserCenter`, `ThunderstormDirectHit`, `RailgunDirectHit`) → `ServerEliminate`. With the shield flag the reason is the `*ElectromagnetShield*` variant and nobody is eliminated.

**Shield collider.** `PlayerInfo.ElectromagnetShieldCollider`: `SphereCollider`, radius 1.25, own layer 22 "Electromagnet shield". Vanilla reflection = physics collision with it (`GolfBall` toggles `IgnoreCollision` only for teammate protection). Making it a trigger on every client removes all reflection and lets projectiles reach the body.

**Chat on the host.** `TextChatManager.CmdSendMessageInternal` on a host client runs the server user code directly with a null sender, which `GetPlayerFromConnection` maps to the local player. Same path as typed host chat.

**Skin colours.** `PlayerCosmeticsSettings.skinColors[].baseColor` carries arbitrary alpha (most are 0); the game forces `a = 1` before use. `Skin.Of` now does too.

**Entity classification.** `Entity.IsPlayer / IsGolfBall / IsItem / IsGolfCart / IsTrafficVehicle`, `Entity.Rigidbody`. `ItemData` has no "firearm" flag; the game's own aim code switches on `ItemType`. Replicated aim: `PlayerInfo.NetworkedIsAimingItem`, `NetworkedEquippedItem`, `AnimatorIo.AimingYawOffset` (pitch is not).

**Renderers.** `PlayerCosmeticsSwitcher.bodyRenderer/headRenderer/shoesRenderer`; skin colour written to material `_Color`. `PlayerVfx` gathers all mesh + skinned renderers under the player.

**Immunity VFX.** `PlayerMovement.knockoutImmunityVfx` (pooled particle) is (re)played from `UpdateKnockoutImmunityVfx` on every status change; stopping it in a postfix hides the bubble without touching bookkeeping.

**Match state.** The tee-off countdown IS `MatchState.TeeOff`; `Ongoing` begins at zero. `CourseManager.MatchState` throws where there is no match (lobby hub): treat as "not a hole". Driving range = `DrivingRangeManager.HasInstance`. Menus with static flags: `PauseMenu.IsPaused`, `Scoreboard.IsVisible`, `TextChatUi.IsOpen`, `RadialMenu.IsVisible`, `PlayerCustomizationMenu.IsActive` (the shop), `VoteKickUi.IsShown`, `LoadingScreen.IsVisible`, `MatchSetupMenu.IsActive`.

**Input.** `PlayerMovement.rawMoveVector2d` (public, stick/WASD) and private `rawWorldMoveVector3d` (camera-yaw-rotated), both written every frame even while movement is suppressed.

**Teams.** `enum Team { None, Red, Blue }`; 103 red/blue-specific sites in 21 files, mostly UI (scoreboard has two score fields, progress bar two lanes, announcer lines, auto-balance, setup entries); rules are generic by team equality and `TeamSettings.TryGetTeamData(team)`.

**Match rules.** `MatchSetupRules.Rule` includes PlayerSpeed, CartSpeed, SwingPower, HomingShots, Wind, Knockouts, RecoveryProtectionDuration, RepeatRecoveryProtection, DominationProtection, teammate protections; item spawn chances are a host-owned `SyncDictionary<ItemPoolId,float>` reapplied by `ServerUpdateSpawnChanceValue`. Host-driven "events" are feasible on this.

**Mirror.** `Writer<T>.write` / `Reader<T>.read` can be assigned by hand for an unweaved assembly; `NetworkServer/Client.ReplaceHandler<T>`; a peer that receives an unknown message id is disconnected (handoff v4; not re-verified in Mirror's own code).

---

## 5. Mechanics as of 0.7.21

Terminology (user's): **bubble** = the mod's Shift shield; **shield** = the game's comeback protection.

**Bubble.** Hold Left Shift. Ten pips, drawn as five circles of two (a 1-pip hit is half a circle; odd counts show a half). Blocked during: stand-down, menus (incl. the shop), tee-off countdown, dead, cart, knocked out, respawning, diving, spring boots, swinging, immunity up (input only). Rooted while up. Tapped bubbles show intro → full → dissolve via a cosmetic 0.3 s linger (`ParryLinger`) that absorbs nothing.

**Nothing reflects.** Every active shield collider is a trigger on every client (`BubbleReflects` off, must match across the lobby; applies to the magnet item too). Projectiles reach the body; the hit arrives as a knockout and is absorbed for pips; the absorb plays the game's shield sparks itself.

**Costs (of 10).** Freeze bomb 0 (holder spared, everyone else in range still freezes). Stray/returned ball 2. Pistol 3. Homing ball 4. Elephant gun 5. Explosions (rocket, mine, back-blast, magnet blast, laser/thunder edge), carts, vehicles, rocket driver 6. Golf swing, giant: full break. Laser/thunderstorm/railgun DIRECT: unblockable AND lethal — the host strips the shield flag so elimination applies. Overrides via `Costs.Overrides`.

**Break.** The hit is cancelled; you pop straight up at 10 m/s (`BreakBounceSpeed`), tumble, land (0.75 s floor), get up with vanilla immunity. Stun timer ×1.3. Cannot be teched. The boom is re-played within 60 m on every client (`BreakSoundCarry`). Bubble gone for 10 s (`BreakCooldown`). Break through comeback immunity forced (`BreakStunIgnoresComebackImmunity`).

**Parry.** Armed at RELEASE by what is coming: anything moving that would reach the bubble within 0.35 s (`ParryReadTime`, searched to 15 m) or is within 1.5 m of its edge (`ParryReach`, the catch), or a golfer within club reach winding up / swinging, or (experimental) a player aiming any item along a corridor through the bubble within 50 m. Arming lasts ≥ 0.5 s and long enough for the farthest thing to arrive. The next hit of the armed class (swing vs projectile, read from the KnockoutType name) is free: no pips, no percent, knockback cancelled, use cooldown cleared, sound for the attacker over SbgNet. Nothing coming = nothing armed. Beats full breaks, not unblockables.

**Percent.** Only in a real hole (`InPlayableHole`: TeeOff/Ongoing/CountingDownToEnd/Overtime, not range, not lobby, not stand-down). Gain = base 5 + 2/pip (full break 25, unblockable 30) × speed factor (hit speed / 30 m/s, clamped 0.5–2; replaces explosion distance falloff while on). Reset per hole, −25 on respawn (45 s fatigue), 25 after a star KO. Master switch `PercentEnabled`.

**Knockback.** Multiplier 1 → 2.3 at `PercentForMaxScaling` 100 (exponent 1.5) then FLAT; horizontal 1 → 1.25 on the same curve; cap 34 m/s (excess → height). Category scale: explosive 1.0, bullet 0.65, melee 0.7; elevation floor 8°→28° scaled 1.0 / 0 / 0.5 by category; bullets capped at 15° elevation. Explosions: radial direction rebuilt from the blast (0.8), force bonus scaled by distance. Drag above 14 m/s for 1.5 s. Hang time 0.35 at the apex, ramping from 65% to 150%.

**DI.** First real stick input within 0.15 s of the launch turns the horizontal direction toward where the stick points on screen (camera-relative), up to 20°. Speed and height unchanged.

**The flight is the stun.** Mid-air timer expiry does NOT wake you: `AirHold` raises the comeback shield and keeps you tumbling until landing (cap 3 s, `StayDownMaxTime`); hits refused while down have their shove cancelled; any mid-air wake-up keeps knockout gravity until the launch lands (`TumbleGravityPatch`). On landing the get-up starts after `LandingStun` 0.25 s, floored so the total is at least `MinStunAfterHit` 2 s since the hit (`BreakMinStun` 3 s after a break): the flight or the floor, whichever is longer. User (10 Sep): "air = stun" alone was silly at low percent and defeated teching; the floor is the middle ground, and the numbers are untested. Comeback shield after an ordinary get-up: `RecoveryImmunity` 1 s (gold repeat shield untouched).

**Teching.** Shift within 0.2 s before landing → instant get-up, `TechImmunity` 0.2 s, rooted `TechRecovery` 0.3 s. One press per attempt (`TechLockout` 0.4 s after a miss). Not on breaks, not on self-inflicted launches, not on death launches.

**Kill zone.** Knockout at ≥ 250%: +45 m/s up, knockout held to the apex, star flash + boom + shake on EVERY screen (SbgNet), hide 5 s, respawn.

**Shared visuals.** Everyone's bubble tinted (skin colour, thinning to 40% opacity and lightening slightly as pips go, hot flash on loss, blink on the last circle); other players' percent drives their embers (from 100%) and trail gate; immunity flicker on every body; smoke trails for everyone.

**HUD.** Percent (hidden outside a hole / percent off), bubble icon (hidden with a logged reason), five circles + half, loss flash, cooldown timer, stand-down panel.

---

## 6. Diagnostics (always on, cheap)

Load line; `Handshake announced: ... (as host|client)`; `<name> is running SBG Shields X` / `does not have ...`; `No announce from X after Ns; announcing once more`; `SbgNet: server/client handler registered` / `SbgNet (...) failed`; `HUD: bubble icon hidden (<reason>)` / `shown`; `Bubble released: nothing armed. N collider(s) ...: <rejects>` / `Parry armed for Xs: ...`; `PERFECT PARRY on ...`; `Bubble: 10 -> 7 pips (-3); circles 3 full + a half`; `Last circle (N pips): bubble blinking`; `[break +t] ...` trace for ~4.5 s after a break; `Stun ended mid-air ...: bubble up, staying down until landing` / `Air hold released (...)`; `Death launch: holding the knockout until the apex`; `Landed Xs after the hit: get-up in Ys (the game's timer had Zs left)`; `TECH: pressed N ms before landing ...`; `DI: stick ... turned ±N°`; `Shield radius is 1.25 m ...`; `Game gun ranges ...`; `Game knockout constants ...`; `<item> hit a bubbled player: bubble ignored, elimination rules apply`. `VerboseLogging` adds per-hit shaping lines (`Hit <type> [category] at N%: force x.. x.., |v| ...`).

Dev build only: `Settings dump: wrote <object>` lines and the dump file.

---

## 7. Networking and safety posture

- Handshake over chat as before. Two gates: `GameplayEnabled` (mod active; newcomers get `HandshakeTimeout` 12 s of grace) and `AllPeersConfirmed` (every remote player announced this exact version; the ONLY gate that permits sending).
- `SbgNet.Msg { byte Kind; uint NetId; float A }`. Client → server → `SendToAll`. Server validates: kind range, finite value, `FindPlayer(netId).connectionToClient == conn` (you may only speak about yourself), clamp 0–1000. Receivers drive visuals and caches only. Handlers registered every frame if Mirror is active and not yet registered; unregistered on unload.
- Percent sent on change (≤10/s) and every 2 s; pips likewise; StarKo and Parry as events.
- Still local-only and unsynced: the knockout economy itself (who can be hit, costs), KO feed / attacker credit, config values (config sync is open item 1).
- Bubble collider trigger state is applied by every client to every player, so it is consistent only if every client has the same `BubbleReflects`.

---

## 8. Open items (priority order)

1. **Verify in play (both machines on 0.7.22):** half circles after an odd cost (`Bubble:` line vs HUD); last-circle blink (`Last circle` line vs screen); worn-bubble legibility (does alpha actually thin the bubble's shader? if not, `BubbleWornAlpha` does nothing and the next tell is intensity, never darkness); "sometimes white during the intro" = the last-circle blink's hot phase or the parry flash, check the log; laser/thunder/railgun kill through a bubble (host log line + elimination); friend's icon (zero-alpha fix) — the `HUD: bubble icon hidden (...)` line settles it; parry read at 0.35 s; hang time reads as weight not braking; `MinStunAfterHit` feel (user: "I get up rather quickly" at 0.25; 1.5 is a first guess; 3 = vanilla).
2. **Config sync (host authority).** Design agreed since v5, unbuilt: after `AllPeersConfirmed`, host broadcasts SHARED entries (costs, parry timings, knockback, percent on/off, `BubbleReflects`, hang, DI, tech) as an override layer read at point of use; personal entries (tint, HUD, verbose) never travel. Foundation for perks/tiers, events, rubber-banding.
3. **Aimed-at parry**: keep, tune or delete after the friend tests it (yaw-only corridor).
4. **Cost table review**: cart cost by impact speed (the game passes it); elephant gun 5 vs pistol 3 by feel; whether the parry should keep beating full breaks now that only swings are full breaks.
5. **Parked: percent by difficulty for guns** (longer/harder shots worth more) — user's idea, not built (memory `parked-gun-range-percent`).
6. **Modes (user's wishlist, memory `future-ideas`)**: pre-match modifier VOTE; roguelike PERKS with tiers picked between holes (hole overview window); events at TEE-OFF (host sets item pool + rules, banner over SbgNet); stocks FFA/team; coins (new networked pickup); rubber-banding; two more TEAMS built from the game (values 3/4; "Neither" skin colours green/yellow/purple/pink); kart drift (host-side physics); small isolated mods (click sounds, wind arrow position).
7. **Simplify-knockback proposal** (if the layer feels like a main mechanic): three rules — hits add percent, higher percent flies further (cap at 100%), 250% is death — delete shaping/drag/hang/categories/landing stun, keep DI and tech as hidden depth. Not chosen yet.
8. Polish: death-off-map flames/SFX; custom break sound; tech visual; screen-KO variant; README updates each drop.
9. Remove `SettingsDump.cs` once no longer useful (dev only, harmless).

---

## 9. Config reference (0.7.21 defaults; ★ new or changed since v5)

`[Audio]` SuppressMuffle true, SuppressHum true.
`[Shield]` ActivationCooldown 0.6, ShieldAbsorbsHits true (master: pips), MaxPips ★10, UseCooldown 1, BreakCooldown ★10, RestoreAfterBreakCooldown true, ★BreakBounceSpeed 10, ★BreakStunMultiplier 1.3, ★BreakSoundCarry 60, ★BreakLandingStun 0.75, ★BreakMinStun 3, RefundPipsOnRefusedKnockout true, AbsorbedHitsCancelKnockback true, ReflectionSearchMargin 1.5.
`[Costs]` Overrides "".
`[Parry]` PerfectParry true, ★ParryReach 1.5, ★ParryReadTime 0.35, ★ParryReadRange 15, ★ParryArmTime 0.5, ★AimedAtParry true, ★AimedAtRange 50, PerfectParryBeatsFullBreak true, PerfectParryBeatsUnblockable false, PerfectParryRefundsUse true, PerfectParrySound true, ParryLinger ★0.3 (cosmetic), ParryGlow true, ParryGlowDuration 0.35, ParryGlowBoost 3.
`[Percent]` ★PercentEnabled true (master), MaxPercent 300, ★RageVisual true, ★RageVisualMinPercent 100, PercentForMaxScaling 100, ★ForceMultiplierAtMax 2.3, KnockbackExponent 1.5, HitstunMultiplierAtMax 0.8, PercentPerHitBase 5, PercentPerPip ★2, PercentPerFullBreakHit 25, PercentPerUnblockableHit 30, PercentGainOnFullBreak 0, ★PercentScalesWithSpeed true, ★PercentReferenceSpeed 30, ★PercentSpeedFactorMin 0.5, ★PercentSpeedFactorMax 2, ExplosionPercentFalloff true (inactive while speed scaling), ExplosionFalloffRadius 8, ExplosionPercentAtEdge 0.3, PercentReductionBetweenHoles 1, PercentLostOnRespawn 25, RespawnFatigueWindow 45, KillZoneEnabled true (master: death), KillPercent 250, PercentAfterKillZoneDeath 25, KillZoneFlash true, KillZoneFlashSize 9, KillZoneUpwardBoost 45, KillZoneMaxRiseTime 2.5, KillZoneDeathLinger 5, KillZoneBoom true.
`[Launch]` ShapeLaunches true, MinLaunchAngleAtZero 8, MinLaunchAngleAtMax 28, ★HorizontalMultiplierAtMax 1.25, MaxHorizontalLaunchSpeed 34, ExplosionForceAtEdge 0.25, ExplosionRadialLaunch true, ExplosionRadialWeight 0.8, LaunchDrag 0.8, LaunchVerticalDragFactor 0.5, LaunchDragDuration 1.5, LaunchDragAboveSpeed 14, LaunchHangTime ★0.35, LaunchHangWindow 7, LaunchHangDuration 3, CloudHitMinPercent ★65, ★HangFullPercent 150, ★ExplosiveForceScale 1, ★BulletForceScale 0.65, ★MeleeForceScale 0.7, ★ExplosiveAngleFloorScale 1, ★BulletAngleFloorScale 0, ★MeleeAngleFloorScale 0.5, ★BulletMaxElevation 15, ★DirectionalInfluence true, ★DIWindow 0.15, ★DIMaxYaw 20, ★TechEnabled true, ★TechWindow 0.2, ★TechImmunity 0.2, ★TechLockout 0.4, ★TechRecovery 0.3, ★TechSelfInflicted false, ★RecoveryImmunity 1, ★LandingStun 0.25, ★MinStunAfterHit 2, ★StayDownUntilLanding true, ★StayDownMaxTime 3, ★TumbleGravityUntilLanding true, LaunchTrail true, LaunchTrailStartSpeed 12, LaunchTrailMinPercent 75, LaunchTrailStopSpeed 5, LaunchTrailRate 45, LaunchTrailRatePerMetre 5, LaunchTrailSize 2.2, LaunchTrailLifetime 1.7, LaunchTrailAlpha 1.
`[Rooting]` RootWhileShielded, BlockJumpWhileShielded, BlockSwingWhileShielded, BlockDiveWhileShielded, AllowMidAirActivation, BlockActivationDuringSwing, BlockActivationDuringSpringBoots, BlockActivationInMenus, BlockItemUseWhileShielded, BlockAimWhileShielded, BreakStunIgnoresComebackImmunity, BlockActivationDuringImmunity — all true.
`[Bubble]` TintVanillaShield true, ★PipWarning true, ★BubbleReflects false (must match lobby), ★BubbleWornWhiteness 0.3, ★BubbleWornAlpha 0.4.
`[Immunity]` ★Flicker true, ★FlickerRate 10, ★FlickerWash 0.75, ★HideGameBubble false.
`[Pose]` PlayShieldEmote false, ShieldEmote "HandsUp". `[HUD]` unchanged from v5. `[Network]` RequireAllPlayersModded true, HandshakeTimeout 12, MismatchPopupDuration 12. `[Debug]` VerboseLogging false; dev: DebugKeys, SetPercent, ChatCommands, GiveItem. `[Meta]` ConfigVersion.

`RetunedThisVersion` (Plugin.cs) lists every default change since 0.5.8; keys reset on version change only if listed.

---

## 10. Working agreements — with provenance

**[user]** = the user said it. **[inferred]** = an assistant's default; overridable.

- **[user]** The user tests in the game (now with a friend, both on the live Thunderstore build). Ask for the relevant log lines when a report is ambiguous; do not guess the scenario. Solo, own machine, host, unless stated.
- **[user]** Never guess at game behaviour: read the method in `decomp/`, and the numbers in the settings dump.
- **[user]** When asked to take something out, take it out. No toggles for removed behaviour.
- **[user]** The bubble alone is a complete mod; percent/knockback/death are layers with master switches.
- **[user]** Explain design in terms of what the player sees and feels.
- **[user]** Terminology: "bubble" = the mod's shield; "shield" = the game's comeback protection.
- **[user]** No Claude attribution trailers in commits.
- **[user]** Release routine: Claude commits, pushes, and drops the zip in the chat; the user uploads to Thunderstore; both update in r2modman. Dev build ships while "TEST" is in the title.
- **[user]** No handoff in every drop; only when asked (this one was asked for on 10 Sep).
- **[user]** Events roll at tee-off. Perks have tiers. The modifier vote is once, before the match. Coins are worth building even though they need a new networked object.
- **[inferred]** Bump `Version` on every behaviour change; list retuned defaults; commit at every version; `git diff` before committing; exact-match edits for code.
- **[inferred]** Diagnostics for a reported bug default on and cheap; one line per event, never per frame.
- **[inferred]** Before shipping anything visible, ask which machine decides it and whether every other machine agrees.
- **[inferred]** A worn bubble goes thinner and a little lighter, or hot on a flash; never darker, never fully white, never the same for every colour.
- **[inferred]** Nothing is sent over the wire unless `AllPeersConfirmed`.
- **[inferred]** One agent owns a change end to end; parallelise only across files that do not touch.

---

## 11. Decisions made this session, with reasons

- **Stay down until landing, not wake mid-air.** User's call ("proc the shield, don't wake the player"); later reinforced by "the flight is the stun". The gravity patch then fixed the float at its root so the hold is no longer the only defence.
- **Knockback saturates at 100%.** Kill line is adjustable, launches went too high, camera lost players, death launch had to be distinct. User's call.
- **Break = bounce + boom + cooldown**, not stun-in-place. User's rehaul.
- **Parry armed by what is coming, at release.** User's idea; time-to-impact was the fix for fast balls. Never lucky: nothing coming = nothing armed.
- **Held bubbles absorb; nothing reflects.** User's expectation; consistent only via "every client makes every shield a trigger". Parry-reflect needs the message layer and is NOT built.
- **Message layer built** because "we can't see each other get hit" was the core complaint; strict gate added after review.
- **Ten pips as five half-circles.** User's idea over the assistant's ring.
- **Bubble state visible to everyone** (pales, flashes). Twice attempted as dimming; dimming is banned.
- **Categories explosive > bullet > melee**, bullets 0.65 after the dump showed guns are the game's strongest hits.
- **Costs**: freeze 0, guns 2→(3/5 at ten pips), explosions 3→6, balls 1/2→2/4. User's numbers, then doubled with the pip count; pistol/elephant split by the assistant from the speed table.
- **Percent scales with hit speed** (user: "yes"). Gun-range reversal parked.
- **Teching tightened** after "too forgiving": lockout, recovery root, no self-tech.
- **Landing stun 0.25 + floors 2 / 3 (break)** — added after "I get up rather quickly" and "air = stun is silly, a broken shield gets up immediately, this defeats teching". Numbers untested.
- **Hang time on from 65%**, comeback shield 1 s after a get-up, gold untouched. Assistant's proposal, user agreed.
- **DI camera-relative**, pitch steer removed. User: "feels more intuitive".
- **Instant kills go through the bubble.** User's rule; host-side flag strip.
- **Cosmetic linger 0.3 s** so a tap plays its intro. User asked whether to leave the abrupt version; this keeps the visual without changing commitment.
- **Teams from the game, not from Team Chaos.** Team Chaos predates the vanilla team mode. Bubble colour follows team in team mode.
- **Simplify-knockback**: proposed, not chosen.
