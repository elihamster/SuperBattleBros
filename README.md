# Super Battle Bros

Hold **Left Shift** to raise a bubble. It absorbs hits, costs pips, and breaks if you ask too much of it. On top of that sits an optional Smash-style layer: a percent that climbs as you take hits, knockback that grows with it, directional influence, teching, and a star KO if it gets high enough.

**This mod is in active testing.** Numbers change between versions. If something feels wrong, it probably is — tell me.

## Rules you need to know

- **Everyone in the lobby needs the same version.** The mod checks over text chat when you join. If anyone is missing it or on a different version, the mod switches itself off for you and says so on screen. Nothing breaks; you just play vanilla until it's sorted.
- **Private lobbies only.** In a Public lobby the mod does nothing at all, silently. This is not configurable.
- **Use at your own risk.** Everything happens on your own machine. The game ships an anti-cheat assembly; what it inspects beyond activation rate limits is not known. This has been used in private lobbies among friends without issue, but that's the extent of it.

## What's in it

**Bubble.** 5 pips. Small hits cost 1, bigger hits cost more, some hits break it outright. It blinks when it's down to its last pip. Rooted while it's up by default — no moving, jumping, swinging or items.

**Break.** A hit that breaks the bubble is cancelled; you pop straight up, tumble, land and get up. Everyone nearby hears it, and the bubble is gone for 10 seconds. That cooldown is the real cost.

**Parry.** Let go of the bubble with a threat in reach — a ball, rocket, cart or bomb within a metre or two, or a golfer winding up next to you — and the next hit from it is absorbed for free: no pips, no percent, and it stops hits that would normally break the bubble. Letting go with nothing near you arms nothing, so it cannot be fished for. Someone aiming a gun along a line through your bubble counts too (experimental).

**Percent and knockback.** Every hit that gets through adds percent. Knockback grows with it up to 100% and then stops growing; only a death launch goes higher, so anyone flying off the top of the map is dead. Explosions lift, bullets shove you along the ground, clubs and carts sit in between. At 250% a knockout is a star KO. Percent resets each hole.

**DI.** As you're launched, W steepens the arc, S flattens it, A and D curve it. Where you land, not how far.

**Teching.** Press the bubble key just before your tumbling body hits the ground and you're up instantly, no lie-down. A missed tech gets you the game's normal comeback shield; a tech gets almost none. Breaks and death launches can't be teched.

**Invulnerability look.** While the game's comeback shield is up, the body flickers washed-out white, Smash-style, on every client. The game's own bubble can be hidden alongside it in the config.

## Turning things off

Three master switches, each layer coming off cleanly on its own (`com.sbg.shields.cfg`, or in game with ModConfig):

```
[Shield]  ShieldAbsorbsHits = false   -> vanilla bubble: blocks everything, never breaks
[Percent] PercentEnabled    = false   -> no percent, no scaling, vanilla knockback, no death
[Percent] KillZoneEnabled   = false   -> percent climbs, nobody dies
```

`RootWhileShielded = false` lets you move with the bubble up. DI, teching, the flicker and the rage embers each have their own switch under `[Launch]`, `[Immunity]` and `[Percent]`.

## Recommended

ModConfig, so you can change settings in game rather than editing the file.

## Report a bug

Open an issue at https://github.com/elihamster/SuperBattleBros/issues and include:

- which version you and everyone else in the lobby were on,
- whether you were the host,
- the lines from `BepInEx\LogOutput.log` that contain `SBG Shields` (in r2modman: Settings > Browse profile folder). The mod logs what it did and why; those lines are usually the whole answer.

## Building from source

Needs the .NET SDK, the game installed through Steam, and BepInEx in an r2modman profile. `dotnet build` produces the developer build; `dotnet build -c Release` the one that ships. If your Steam or profile paths differ from the defaults, copy `SbgShields.csproj.local.example` to `SbgShields.csproj.local` and set them there. `.\package.ps1` builds Release and zips a Thunderstore package into `dist\`.
