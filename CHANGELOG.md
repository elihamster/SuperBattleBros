# Changelog

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
