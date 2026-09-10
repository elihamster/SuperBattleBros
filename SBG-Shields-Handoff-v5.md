# Super Battle Golf — Shields Mod, Handoff v5

**Version:** 0.7.7. Source is 11 `.cs` files plus the csproj and a `.user.example`.
**Build:** `dotnet build` (Debug) or `dotnet build -c DevDebug` / `-c Release`. See §3.
**Environment:** Windows, .NET SDK, BepInEx 5 via r2modman, ModConfig (AtomicStudio) for in-game settings. **New since v4:** the source folder is a git repo and the user runs Claude Code in it. The build now runs against the real game assemblies on every change; the `mcs` parse-check described in v4 is no longer the ceiling.

Read §0, §10 and §11 first. Everything else is reference.

---

## 0. How previous sessions went wrong, and the rules that follow

### 0.1 From the session before v4 (still true)

1. **Assumed a scenario instead of asking.** Unless the user says otherwise, a finding is solo, own machine, host.
2. **Reasoned about the game's code without reading the mod's own code on the path.** When a symptom persists after a fix, read the mod's code on that path next, not the game's again.
3. **An edit silently did not land.** Every scripted edit asserts that it changed the file; the specific inserted line is grepped afterwards. With git this is now `git diff` before committing.
4. **Diagnostics gated behind a default-off setting.** Diagnostics for a reported bug are on by default and cheap.
5. **Trusted a fragmentary read.** If a claim depends on what a game method does, read the whole method.

The user's descriptions were accurate every time. When they say something is happening, it is happening.

### 0.2 From the v4 → v5 session (new)

6. **A previous session invented a working agreement and wrote it into the handoff as the user's instruction.** "Do not remove features unasked; put replaced behaviour behind a toggle" was never said by the user. It then shaped decisions for several versions — most visibly `PartialBreakLaunches`, a toggle that re-enables the instant-wake-up bug, kept because of a rule nobody had actually given. **Handoff entries describing the user's preferences must record where they came from.** §10 now does this. If a rule cannot be traced to something the user said, it is the assistant's inference and must be marked as such.
7. **A review from a separate chat was useful precisely because it had no stake in the decisions.** Keep doing that. Most of its findings were about code that had already been deleted by the time it arrived — check version before acting on external review.
8. **The assistant made a design case it later had to walk back.** The parry linger was described as "legible feedback" when it is visually identical to holding the shield. The user caught it. When arguing for a mechanic, say what it looks like from the player's chair, not what it does in the code.

---

## 1. Environment

| Thing | Path / value |
|---|---|
| Game | `C:\Program Files (x86)\Steam\steamapps\common\Super Battle Golf` |
| Managed DLLs | `<Game>\Super Battle Golf_Data\Managed` |
| Source / repo | `C:\Users\elija\Documents\Coding and Projects\Super Battle Bros\` |
| BepInEx profile | `C:\Users\elija\AppData\Roaming\r2modmanPlus-local\SuperBattleGolf\profiles\Super Battle Bros\BepInEx` |
| Plugin install | `<profile>\plugins\SbgShields.dll` |
| Config | `<profile>\config\com.sbg.shields.cfg` |
| Log | `<profile>\LogOutput.log` |

The csproj derives Steam and AppData paths; `dotnet build` succeeded on the user's machine with no `.csproj.user`, so the defaults are correct for this install. Repo has `.gitignore` (bin/obj) and `.gitattributes` (`* text=auto`).

**Compiling from the assistant's sandbox** (if ever needed again): no game DLLs; `mcs -target:library -langversion:latest *.cs` with only `CS0246` errors and zero warnings is the best available check. It does not catch wrong game API names. Prefer the real build.

---

## 2. Files (0.7.7)

| File | Owns |
|---|---|
| `Plugin.cs` | BepInEx entry; every `ConfigEntry`; Shift input loop; `TryActivate` / `ReleaseShield` / `CancelLingeringShield`; `ActivationBlockedByState`; selective config reset (`RetunedThisVersion`); the parry linger timer; `RunCoroutine` host; debug polling and keys (`#if SBG_DEV`). |
| `ShieldState.cs` | The economy. Pips, percent, cooldowns, cost table + overrides, `InPlayableHole`, `ResolveKnockout`, `IsPerfectParry` / `Parry`, `ShapeLaunch`, `Break`, `ChargeReflection`, `StunInPlaceAfterBreak`, `BreakStun` and `BreakImmunityDuration` (both percent-scaled), knockback curve, refund-on-refusal, resets. |
| `Patches.cs` | Harmony patches for rooting, knockout economy, hitstun, break-stun hold, break immunity, immunity bypass on break, velocity correction + drag + hang, reflection charging, respawn, audio suppression, item/aim/spring-boots blocks, `BreakTrace`, and the once-per-session game-constants log. |
| `Launch.cs` | Per-launch tracker. **Now only gates hang time** (`IsCloudHit`). Apex detection, wake-up and distance are gone. |
| `KillZone.cs` | Star KO at/above `KillPercent`. Unchanged. |
| `ShieldTint.cs` | Skin retint of vanilla shield VFX; `BubbleMaterialTintPatch`; **`ParryFlash`** (bright flash on parry, restored from `Tick`). |
| `ModHandshake.cs` | Version handshake over chat; gate; `ChatBypass`. Unchanged. |
| `ChatCommands.cs` | `#if SBG_DEV` only. Unchanged. |
| `Hud.cs` | IMGUI percent + bubble + pips + stand-down. Distance readout removed. |
| `LaunchVfx.cs` | Smoke trail. One rule for everyone now: tumbling and above `LaunchTrailStopSpeed`. |
| `SbgShields.csproj` | Audio / UnityWebRequest references removed in 0.7.3. |

Deleted this session and must not exist: `HighScoreSound` (was inside `Launch.cs`), `ApexWakeUpImmunityPatch`, `EmoteSuppressionFix`, `UpdatePose`. Deleted earlier: `BubbleVisual.cs`, `ModMenu.cs`.

---

## 3. Build configurations

Unchanged from v4. `DevDebug` defines `SBG_DEV` (chat commands, F-keys, debug config entries, all default off). `Release` contains none of it. The load line says which build is running — check it first when something "does nothing".

---

## 4. Verified game architecture

Everything in v4 §4 still holds (authority is client-to-server on `PlayerMovement`; hits reach the victim's `TryKnockOut` with the shield up; reflections bypass `TryKnockOut`; the knockout state machine; the three comeback bubbles; explosion knockback; lobby privacy; text chat; Mirror unknown-id disconnects). Additions verified this session, with file:line in the decompiled source:

**Knockout notification is not broadcast.** `TryKnockOut` (PlayerMovement.cs:1548) ends in `CmdInformKnockedOut`, a Command with `requiresAuthority: true`. The server then calls `responsiblePlayer.RpcInformKnockedOutOtherPlayer(...)` (4117), which is a **TargetRPC** (`SendTargetRPCInternal`) — it goes to the attacker only, so they can get their speed boost. Third parties are told nothing. **Consequence:** even with everyone modded, clients cannot reconstruct each other's pips or percent by observation. Absorbed hits and parries produce no knockout at all and are therefore invisible to the network. Universal mod adoption gives identical *rules*, not shared *state*.

**The attacker does learn what they knocked out.** That TargetRPC carries `knockoutType`, `localOrigin` and `distance`. This is the only existing cross-client channel and the basis for attacker credit (open item 6).

**The shield has no "going down" state.** `PlayerInfo.isElectromagnetShieldActive` (PlayerInfo.cs:52) is a plain SyncVar bool with a change hook. The dissolve VFX and the deactivation sound hang off the flip. Protection ends at the flip; the animation is decoration over an already-unprotected player. There is no interval where the collider persists while the visual winds down. Vanilla resolves the visual ambiguity in the attacker's favour.

**Reflection is a physics collision, not a rule.** `GolfBall` (1466–1473) toggles `Physics.IgnoreCollision` against `player.ElectromagnetShieldCollider`. A projectile bounces because it hits a real collider. No collider at contact time means no bounce, and nothing can bounce it afterwards.

**Custom Mirror messages are safe once the gate is open.** The unknown-id disconnect only bites when a modded peer sends to a vanilla peer. `ModHandshake` over chat exists to bootstrap trust before that is known. After `_gateOpen`, every peer is confirmed modded on the same version and has the handler registered. Mirror is already referenced in the csproj. This is the door for config sync, parry sound, remote pip indicators, KO feed. It is also a new attack surface: validate every incoming message; never let one drive state directly.

---

## 5. Mechanics as of 0.7.7

**Activation / rooting / pips / costs.** Unchanged from v4 §5.

**Absorb / break.** Unchanged, except **break stun now scales with percent**: `ShieldState.BreakStun` lerps `BreakStunDuration` (2.5 s at 0%) → `BreakStunDurationAtMax` (3.5 s at `PercentForMaxScaling`). `BreakStunScalesWithPercent` turns it off (flat 2.5). Deliberately the **opposite slope** to launch hitstun (below): a launch hands control back sooner at high percent, a break holds you longer. Losing the shield when beaten up is meant to be the moment that costs you; `BreakImmunityAtMaxPercent` (3 s) is the compensation.

`PartialBreakLaunches` still exists and still re-enables the instant-wake-up bug. See §8 item 1.

**Percent.** Unchanged.

**Hitstun.** `HitstunMultiplierAtMax` is now **0.8** (was 1.25). Lerps from 1.0 at 0% to 0.8 at `PercentForMaxScaling`. This **replaced** the apex wake-up: the stun timer simply runs out mid-air on big launches, and the game's own rule recovers you instantly in the air with no get-up animation. Same outcome as the wake-up, through the game's timer instead of reaching into the state machine. 0.8 was chosen without knowing `KnockoutDuration`; see §6 for the log line that gives it.

**Knockback curve.** Unchanged, except the horizontal cap fix: `ShapeLaunch` used to recompute the vertical from the *pre-boost* speed when capping, which voided `HorizontalMultiplierAtKill` entirely above the cap — and the cap binds on most hits past ~100%. It now spends the boosted total. Defaults retuned with it: `MaxHorizontalLaunchSpeed` 26 → **34**, `HorizontalMultiplierAtKill` 1.6 → **2.0**. The 28° angle floor is untouched. **User is testing these two knobs and will report.** Note the floor already makes horizontal exceed vertical in raw m/s; what made launches feel vertical was the cap converting reach into altitude, not the floor.

**Launch tracker.** `Launch.cs` now exists solely to gate hang time. `AirRecoveryMinPercent` renamed `CloudHitMinPercent` (125). Apex wake-up, `TrailEndsAtApex`, distance sent, session best, high-score sound: **all removed**. The distance record is shelved until the user designs what it is measured against.

**Perfect parry (new, 0.7.4–0.7.6).** A hit arriving within `PerfectParryWindow` (0.2 s) **after the shield key is released** is fully absorbed: no pips, no percent, knockback cancelled, use cooldown cleared (`PerfectParryRefundsUse`). `PerfectParryBeatsFullBreak` (on) means a parried cart / swing / targeted ball is stopped where it would normally break the shield — that is the mechanic's reason to exist. `PerfectParryBeatsUnblockable` (off). The window is measured from `ShieldState.LoweredAt`, stamped only in `ReleaseShield` (deliberate key-up); a shield that *broke* (`NotifyShieldDropped`) does not open it. The check sits **above** the shielded branch in `ResolveKnockout`, because the shield is already down when the hit lands — inside that branch it could never fire. `ChargeReflection` has its own check because reflections bypass `TryKnockOut`. Sound: the game's `KnockoutImmunityBlockedKnockoutEvent`, local only. Flash: `ShieldTint.ParryFlash` (skin colour × `ParryGlowBoost`, restored after `ParryGlowDuration`), local only. Logs at Info with timing: `PERFECT PARRY on X (87 ms after release, would have cost a break)`.

Release-timed means it is a **read, not a reaction**: you commit to letting go before the hit lands. A missed read leaves you unshielded and on use cooldown. Raise-timed was built first (0.7.4) and moved on the user's correction.

**Parry linger (0.7.6).** `ReleaseShield` no longer cancels the shield immediately. `_weActivated` clears at key-up (rooting, jump, swing come straight back); the actual `LocalPlayerCancelElectromagnetShield` is deferred by `ParryLinger` (0.2 s) and runs from `Update` via `CancelLingeringShield`. While lingering the shield is **genuinely active** — real collider, synced state — so the game itself reflects homing projectiles and plays the shield-hit effect on every client. `ResolveKnockout` checks `!Plugin.ShieldLingering` so a lingering shield is not mistaken for the vanilla magnet item. `NotifyShieldDropped` clears the timer; `ForceReleaseForHandshake` cancels immediately.

**Status of the linger: the user is unsure about it.** It is a free 0.2 s block on every release, and it looks identical to holding the shield. The agreed next step is to test with `ParryLinger = 0` in the cfg (reverts to 0.7.5 behaviour: local sound + glow, no reflection, attacker sees nothing) and decide whether release-timed parrying is fun *at all* before deciding what it deserves visually. Options on the table if it is: shorten to 0.06–0.1 s; fade the tint across the linger so it reads as the shield ending; or leave at 0. Impact frames are available later (`ImpactFrame`, `ImpactFrameController`, `ImpactFrameRenderer` exist in the game).

**Kill zone, HUD, visuals.** Unchanged, minus the distance readout.

**Removed this session, by the user's instruction:** apex wake-up (`AirRecoveryAtApex`, `AirRecoveryGrantsImmunity`, `WakeUp`, `SkipNextImmunity`, `ApexWakeUpImmunityPatch`); `TrailEndsAtApex`; distance sent + session best + `HighScoreSound` + `ShowDistanceSent` / `DistanceMinimum` / `HighScoreSoundFile` / `HighScoreSoundVolume`; shield pose (`PlayShieldEmote`, `ShieldEmote`, `UpdatePose`, `EmoteSuppressionFix`); the three audio csproj references. Kept by the user's decision: hang time (off; may revisit), `ShowPipDots`.

---

## 6. Diagnostics (always on, cheap)

- **Load line.** Unchanged.
- **Break trace.** Unchanged.
- **Game knockout constants** (new, 0.7.1): on the first knockout of any kind each session, one Info line: `Game knockout constants: KnockoutDuration=…s, KnockoutTimeOutDuration=…s, LongImmunity=…s`. It used to be verbose-only and break-only. **This is the number `HitstunMultiplierAtMax` scales; the user has not yet reported it.** Get it before retuning stun.
- **Parry line** (new): `PERFECT PARRY on <type> (<n> ms after release, would have cost <x>). <pips> pips kept.` Info, always on. Use it to calibrate `PerfectParryWindow`.
- **Handshake lines, `VerboseLogging`, `/sbg`.** Unchanged. Verbose now also logs `Launch ended (landed|recovered|cancelled)`, `Shield key released; body lingers`, `Shield down (<why>)`.

---

## 7. Networking and safety posture

Unchanged from v4 §7, with the §4 additions above sharpening the limits: motion syncs (position is client-owned and broadcast), the vanilla shield state syncs (SyncVar), reflections are real for everyone; pips, percent, absorbed hits and parries exist only on the owner's machine and cannot be observed by others.

---

## 8. Open items (priority order)

1. **Delete `PartialBreakLaunches`.** It preserves the instant-wake-up bug behind a toggle, kept only because of the invented rule (§0.2). With hitstun now 0.8 it would be worse than the version originally reported. The user asked "why is the bug toggleable" but did not yet say delete; confirm and remove the entry, the branch at `ShieldState.ResolveKnockout` (the `fraction = excess` path), and any dead `_pendingReleasesBreakStun` handling that only it used. Good first Claude Code job.
2. **Test 0.7.7:** `ParryLinger = 0` first; horizontal knobs; grab the `KnockoutDuration` line.
3. **Config sync over Mirror (host authority).** Agreed design, not built: after `_gateOpen`, host broadcasts its config; clients apply it as an **override layer read at point of use** (never written into their cfg), revert on disconnect; host rebroadcasts on change; apply on lobby/hole boundaries only, queue mid-hole. **Each `ConfigEntry` must be classified as shared (fairness: costs, parry window, knockback, percent on/off) or personal (tint, HUD, verbose, glow) — only shared ones travel.** Validate ranges on receipt. This is the foundation the parry sound, remote pip indicators and KO feed all need. First real Claude Code project.
4. **Presets / config cleanup for the average user.** The user wants toggles like "no percent", "shields only", "always move while shielded", "no pips", "no death". Most are reachable today by combination (`ShapeLaunches false`, `ForceMultiplierAtKill 1`, `HitstunMultiplierAtMax 1`, `BreakStunScalesWithPercent false`, `KillZoneEnabled false`, `RootWhileShielded false`). Two need real work: stop percent accruing at all, and stop pips depleting. **Presets must be read at point of use, not written into individual keys** — same trap as `RetunedThisVersion`.
5. **Damage → knockback coupling.** Not built. Percent gain and knockback are parallel outputs of the same inputs and never multiplied together; a 15%-damage and a 25%-damage hit at the same percent differ only if the game's own velocity change differs. Fix is ~5 lines in `ResolveKnockout`: compute the gain first, normalise against 25 (full-break value), fold into `forceMult` behind a config defaulting off. User wants to go through pip costs per item first.
6. **Impact-speed cart cost.** User believes a tap costs 1 pip and a ram breaks; the mod's cost table is a flat 3 for `GolfCart`, so what they saw is probably pips-remaining or a different `KnockoutType`. Test: `VerboseLogging` on, compare `|v|` in the `Hit <type>` lines for a tap vs a ram. If the game varies `incomingVelocityChange` with speed, variable cost is easy; if not, it needs relative velocity at collision. User will gather data.
7. **Attacker credit** via the TargetRPC (§4). **KO feed** via the message layer (3).
8. **Publishing.** Name `SuperBattleBros` (no spaces in Thunderstore names; display name can have them). Internal token `SBGSHIELDS` and BepInEx GUID stay as-is (no handshake break). User makes the thumbnail. Needs `manifest.json`, 256×256 `icon.png`, README (private lobbies only, same version required, use at own risk, anti-cheat assembly note). BepInExPack as a hard dependency; the config-menu mod as recommended, probably not required. Publish when parry and knockback settle; a modpack can pin the version for friends. Versions are immutable once uploaded.
9. **Kinetic vs explosive categories, break warning at ≤1 pip, hang time decision.** Carried from v4, unchanged.
10. **Distance record.** Shelved until the user decides what it is measured against and over what span.

---

## 9. Config reference (0.7.7 defaults; changes from v4 marked ★, removed struck)

`[Shield]` ActivationCooldown 0.6, ShieldAbsorbsHits true, MaxPips 5, UseCooldown 1, BreakCooldown 8, RestoreAfterBreakCooldown true, BreakStunDuration 2.5, ★BreakStunScalesWithPercent true, ★BreakStunDurationAtMax 3.5, PartialBreakLaunches false, RefundPipsOnRefusedKnockout true, BreakImmunityScalesWithPercent true, BreakImmunityAtZeroPercent 0.5, BreakImmunityAtMaxPercent 3, AbsorbedHitsCancelKnockback true, ReflectionSearchMargin 1.5.
`[Costs]` Overrides "".
`[Parry]` ★PerfectParry true, ★PerfectParryWindow 0.2, ★PerfectParryBeatsFullBreak true, ★PerfectParryBeatsUnblockable false, ★PerfectParryRefundsUse true, ★PerfectParrySound true, ★ParryLinger 0.2, ★ParryGlow true, ★ParryGlowDuration 0.35, ★ParryGlowBoost 3.
`[Percent]` MaxPercent 300, PercentForMaxScaling 100, ForceMultiplierAtKill 6, KnockbackExponent 1.5, ★HitstunMultiplierAtMax 0.8, PercentPerHitBase 5, PercentPerPip 4, PercentPerFullBreakHit 25, PercentPerUnblockableHit 30, PercentGainOnFullBreak 0, ExplosionPercentFalloff true, ExplosionFalloffRadius 8, ExplosionPercentAtEdge 0.3, PercentReductionBetweenHoles 1, PercentLostOnRespawn 25, RespawnFatigueWindow 45, KillZone* unchanged.
`[Launch]` ShapeLaunches true, MinLaunchAngleAtZero 8, MinLaunchAngleAtMax 28, ★HorizontalMultiplierAtKill 2.0, ★MaxHorizontalLaunchSpeed 34, ExplosionForceAtEdge 0.25, ExplosionRadialLaunch true, ExplosionRadialWeight 0.8, LaunchDrag 0.8, LaunchVerticalDragFactor 0.5, LaunchDragDuration 1.5, LaunchDragAboveSpeed 14, LaunchHangTime 0, LaunchHangWindow 7, LaunchHangDuration 3, ★CloudHitMinPercent 125 (was AirRecoveryMinPercent), ~~AirRecoveryAtApex~~, ~~AirRecoveryGrantsImmunity~~, ~~TrailEndsAtApex~~, LaunchTrail* unchanged.
`[Rooting]` all true, unchanged.
`[Bubble]` TintVanillaShield true.
~~`[Pose]`~~ removed.
`[HUD]` unchanged minus ~~ShowDistanceSent~~, ~~DistanceMinimum~~.
`[Audio]` SuppressHum true, SuppressMuffle true; ~~HighScoreSoundFile~~, ~~HighScoreSoundVolume~~.
`[Network]`, `[Debug]`, `[Meta]` unchanged.

`RetunedThisVersion` currently lists: `Launch.LaunchHangTime` (0.7.0), `Percent.HitstunMultiplierAtMax` (0.7.1), `Launch.MaxHorizontalLaunchSpeed`, `Launch.HorizontalMultiplierAtKill` (0.7.4).

---

## 10. Working agreements — with provenance

Each entry is marked **[user]** (the user said it) or **[inferred]** (an assistant concluded it from how a session went). Inferred entries are defaults, not instructions; the user can override any of them without it being a change of mind.

- **[user]** The user builds and tests. Ask for the `[break …]` lines or the load line when a report is ambiguous; do not guess the scenario.
- **[user]** Solo, own machine, host, unless the user says otherwise.
- **[user]** No handoff in every drop; only when asked.
- **[user]** When the user asks to take something out, take it out. Do not preserve it behind a toggle. (The opposite rule in v4 was invented; see §0.2.)
- **[user]** The shield alone is a complete, fun mod. Percent and knockback are a layer on top and must be cleanly switchable off.
- **[user]** Explain design changes in terms of what the player sees and feels, not what the code does.
- **[inferred]** Bump `Version` on every drop that changes behaviour; list retuned defaults in `RetunedThisVersion`. (Now: commit at every such point.)
- **[inferred]** Every scripted edit asserts it landed; grep the inserted line. (Now: `git diff` before commit; `dotnet build` after every change.)
- **[inferred]** Diagnostics for a reported bug default on and cheap.
- **[inferred]** Read the whole game method before claiming what it does.
- **[inferred]** One agent owns a change end to end. Parallelise across files that do not touch (README vs gameplay logic), not within `ResolveKnockout`.

---

## 11. Decisions made this session, with reasons (so they are not re-litigated)

- **Air recovery removed; hitstun inverted instead.** The wake-up reached into the state machine; a stun that shrinks with percent gets the same result through the game's own timer. User's call.
- **Break stun scales up with percent while launch stun scales down.** Intentional asymmetry: launched is survivable, shield lost at high percent is the punishing moment. User's call.
- **Parry is release-timed, not raise-timed.** User's correction. A read, not a reaction.
- **Linger built, then doubted.** It delivers reflection + shared sound via vanilla, but is a free block that looks like holding. Undecided; test at 0 first.
- **Distance record shelved** until the user defines the reference. Not deleted from the design, deleted from the code.
- **Cart impact scaling left alone** until the user gathers `|v|` data.
- **Horizontal fix is a bug fix, not just a retune.** The cap was voiding the multiplier. Values 34 / 2.0 are a first pass; user tuning.
- **Multi-agent: one agent for now**, subagents later for genuinely parallel files. User's call after research.
- **Name: SuperBattleBros package, SBGSHIELDS internal.** No handshake break. User's call on takedown risk.
- **Publish after parry/knockback settle.** README first because versions are immutable.
