# Changelog

## 0.7.27 (test build)

- No bubble during the victory dance. Scoring puts you in the dance and Shift does nothing until it ends; a bubble that was up when you scored drops. Read from the game's own reaction state, not the emote, so a mod that lets you keep playing after scoring gets the bubble back the moment the game says you are playing again.

## 0.7.26 (test build)

- Handshake: the host announces again after every scene change. The lobby reads as public for a moment while a course loads, and that moment was wiping the host's pending announce, so every client timed the host out at the first tee-off ("Hamster does not have SBG Shields installed"), and again on the way back to the range. Each player now also announces a second time five seconds after a reset, and answers a repeat announce once, so a client that loads slower than the host still hears it.
- The bubble's glow no longer compounds. Every state change re-tinted the already-tinted material, multiplying the glow into itself, so a spammed bubble got brighter and brighter and its dissolve lingered. The tint now always starts from the game's own material.
- The log now says why a Shift press did nothing (`Shift ignored: <reason>`), including comeback immunity and the cooldowns, which the HUD's own hidden-reason line leaves out.

## 0.7.25 (test build)

- The HUD glows: a soft light in your skin colour behind the bubble icon and behind every pip circle that still has something in it. Soft when the bubble is ready, breathing while it is up, a white flare on a parry and on a circle that just lost a pip, nearly out on cooldown. `HUD.HudGlow` (0 = off).

## 0.7.24 (test build)

- The bubble glows. Its colour is pushed past white-point (`Bubble.BubbleGlow` 1.5) so the game's bloom lights it up, and a soft halo of the same colour is drawn around it on every screen (`Bubble.BubbleHalo`), thinning with the pips and flaring with a parry or the last-circle blink. The mod's own particles (parry ring and sparks, star flash, embers) are pushed the same way (`Bubble.BitsGlow`).
- Use cooldown 1 s to 3 s, break cooldown 10 s to 15 s.
- The load log says whether bloom is on, so the glow numbers can be tuned against what the game actually renders.

## 0.7.23 (test build)

- Costs are in half-circles again: stray ball 1, guns 2, homing ball 2, explosions and carts 3. They had been doubled along with the pip count, so a rocket took three whole circles. Percent per pip doubled to match, so a hit is worth the same percent as before.
- The penalty stroke goes straight through the bubble: no pips, no parry, no break, an ordinary hit. `Costs.Overrides` accepts `bypass` for any other type.
- Guns and balls actually cost pips now. The shooter's machine (guns) and the host (balls) were reflecting off the shield *flag* regardless of the bubble being a trigger, so a held bubble bounced both for free and the pip costs never applied.
- The bubble is no longer a body: mines, blasts, the laser and the thunderstorm measure their distance to you, not to the bubble's edge. A held bubble used to pop a mine from a metre away and made you a bigger target for the laser and lightning it can't stop.
- A hit that arrives during the 0.3 s cosmetic linger now actually lands. The game's own shield check was refusing it, which made every release a free block.
- A parry is a moment now, on every screen: the bubble flares and stays for it, a ring of your colour bursts out with sparks, a sound plays at the bubble, and nearby cameras kick. Drop a `parry.wav` next to the DLL (or in `sounds\`) to use your own sound; without one the game's blocked-hit sting plays. `Parry.ParrySoundFile`, `ParryBurst`, `ParryShake`.
- In a public lobby or a mismatched one the magnet item's shield stays a wall, as vanilla has it.
- Dev build: the settings dump prints layer masks as bits and layer names.

## 0.7.22 (test build)

- A worn bubble gets thinner, not darker and not white: it keeps its colour, fades toward 40% opacity at one pip, and lightens only a little. The hot flash on a lost pip is gentler. `Bubble.BubbleWornAlpha`, `BubbleWornWhiteness`.

## 0.7.21 (test build)

- The stun is the flight or a floor, whichever is longer: you never get up sooner than 2 s after the hit that launched you (3 s after a bubble break). Short launches lie there; long flights still get up on landing; a tech skips whatever ground time is left. `Launch.MinStunAfterHit`, `Shield.BreakMinStun`.

## 0.7.20 (test build)

- The bubble pales toward white as pips go instead of darkening. Darkening an additive bubble looked like a black stain; that was the dimming, not a bug elsewhere. `Bubble.BubbleWornWhiteness` replaces `BubbleMinBrightness`. Pip loss and the last-circle blink flash the skin colour hot rather than white.
- Orbital laser, thunderstorm and railgun direct hits kill through a bubble. The host was turning them into non-lethal shield hits.
- No bubble icon while the customization shop, a vote or a loading screen is up.
- Percent no longer accrues or shows in the lobby hub, where the mod could not read a match state and assumed one.
- Every pip loss logs the new count and what the circles should show; the last-circle blink logs when it starts.

## 0.7.19 (test build)

- A tapped bubble now plays its intro before dissolving. The body is drawn for 0.3 s after release, cosmetically only: it absorbs nothing, bounces nothing, and a hit in that moment is parried or lands in full. `Parry.ParryLinger`, 0 to turn it off.

## 0.7.18 (test build)

- DI is camera-relative: push the stick toward where on the screen you want to drift and the launch turns that way, up to 20°. W is into the screen, S toward the camera. The separate up/down steer is gone.

## 0.7.17 (test build)

- Hang time is on: a launch lingers at the top of its arc, nothing below 65% and full at 150%. The flight is the stun, and this is how it grows with percent. `Launch.LaunchHangTime`, `CloudHitMinPercent`, `HangFullPercent`.
- The blue comeback shield after an ordinary get-up is 1 s (`Launch.RecoveryImmunity`, 0 = the game's 3 s). The gold shield for repeated knockouts is untouched.

## 0.7.16 (test build)

- Ten pips, drawn as the same five circles: a 1-pip hit takes half a circle. Costs: balls 2, pistol 3, homing ball 4, elephant gun 5, explosions and carts 6, swings a full break, freeze bomb 0. Percent per pip halved so percent is unchanged.
- The bubble shows its state to everyone: full brightness at full pips, fading toward a faint shell as they go, with a white flash on each pip lost. Pips travel over the mod's network message.
- The HUD circle that just lost a pip swells and flashes; the last-circle blink starts at two pips.

## 0.7.15 (test build)

- Costs: guns 2 pips, explosions 3 (rocket, landmine, back-blast, magnet blast, laser and thunderstorm edge). Freeze bomb stays 0, balls 1 and 2, swings a full break.
- Percent scales with how hard the hit was: the game's knockback speed over a full swing's 30 m/s, between half and double. A point-blank elephant gun is twice a swing; a pistol at range is half. Replaces the explosion distance falloff while on.
- Teching tightened: one press is one attempt and a missed one locks the key out for 0.4 s; a tech roots you for 0.3 s before you can act; a launch you caused yourself cannot be teched.

## 0.7.14 (test build)

- Costs against a held bubble: freeze bomb 0, rocket 2, landmine 2 (were full break, full break, 3). The explosion or the freeze still hits everyone else in range; the holder is spared.
- Bullet launches scaled to 0.65 (was 0.85). The game's own knockback table, read from a settings dump, has guns as its strongest hits: elephant gun 60 m/s against a rocket's 40 and a swing's 30. At 0.85 a shotgun still out-launched a rocket; at 0.65 explosives lead, then bullets, then melee.

## 0.7.13 (test build)

- Network messages are sent only once every player in the lobby has announced this exact version. 0.7.12 could send during a newcomer's grace period, which would have disconnected a vanilla player joining a modded lobby.
- Absorbed hits are no longer charged a second time when the bubble's own spark effect echoes back over a slow connection.
- A mid-air wake-up of any kind now keeps knockout gravity until the launch lands (`Launch.TumbleGravityUntilLanding`). The original slow-float bug fixed at its root.
- Parry search buffer enlarged for busy holes.

## 0.7.12 (test build)

- The mod now has its own network message, sent only once everyone has passed the handshake. Percent is shared, so other players' rage embers and trails run off their real number; a star KO plays on every screen; the player you parried hears it.
- A hit refused while you are already down no longer shoves your body. Being immune and tumbling used to mean being juggled around the sky until the game's 10 s time-out forced a wake-up under the gold shield.
- The stay-down hold has a 3 s cap after the stun ends (`Launch.StayDownMaxTime`).

## 0.7.11 (test build)

- Everyone's bubble is tinted to their skin colour, not just your own. The magnet item's shield too, since from another machine they are the same thing.
- Other players' smoke trails show. The mod was reading a velocity remote players never report.
- Landing ends the stun: the get-up starts 0.25 s after touchdown (0.75 s after a break bounce) instead of after the game's 3 s timer. `Launch.LandingStun`, `Shield.BreakLandingStun`.
- Fixed the "Particle Velocity curves must all be in the same mode" error spam from the rage embers.

## 0.7.10 (test build)

- A held bubble absorbs instead of reflecting. Balls, rockets and bombs pass into you and cost pips; nothing bounces back. `Bubble.BubbleReflects` restores the game's wall, and must match for everyone in the lobby.
- A homing ball is a 2-pip chip, not an instant break.
- The parry reads time-to-impact: anything that would reach you within 0.35 s counts when you let go, however far out it is. Distance alone never worked on a fast ball.
- HUD icon and pips were invisible for skins whose colour carries zero alpha. The game forces alpha to 1; now so does the mod.

## 0.7.9 (test build)

- Parry now arms on balls and carts. They are network-predicted objects and the release scan was looking at the wrong half of them, so only rockets and bombs ever counted.
- Last-pip warning blinks bright instead of dark (dark read as a black stain on the bubble).
- Handshake: your own announce is always logged; a silent peer gets one more announce before being declared unmodded.
- The HUD logs why the bubble icon is hidden whenever that changes.
- Every bubble release logs what was in reach and why it did or did not arm a parry.

## 0.7.8 (test build)

- Stun ending mid-air no longer wakes you; the comeback shield comes up and you keep tumbling until you land. Fixes the slow float-down.
- Star KO at 250% works again (the death launch was recovering before its apex).
- Knockback saturates at 100% instead of climbing to the kill line. `ForceMultiplierAtKill` and `HorizontalMultiplierAtKill` replaced by `ForceMultiplierAtMax` / `HorizontalMultiplierAtMax`.
- Bubble break reworked: a vertical pop instead of a stun in place, a boom that carries, and a 10 s cooldown. Six break-stun and break-immunity settings removed.
- Parry is armed by what is in reach when you let go, not by timing. `PerfectParryWindow` removed, `ParryLinger` off. Experimental aimed-at parry for guns.
- Hit categories: explosive, bullet, melee, each with its own force and elevation.
- Directional influence, teching, last-pip warning, rage embers, invulnerability flicker.
- `PercentEnabled` master switch. `PartialBreakLaunches` removed.
- Bubble cannot be raised during the tee-off countdown.
