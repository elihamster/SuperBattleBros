# Changelog

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
