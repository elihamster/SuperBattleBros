# Super Battle Bros

Hold **Left Shift** to raise a bubble. It absorbs hits, costs pips, and breaks if you ask too much of it. On top of that sits an optional Smash-style layer: a percent that climbs as you take hits, knockback that grows with it, directional influence, teching, and a star KO if it gets high enough.

**This mod is in active testing.** Numbers change between versions. If something feels wrong, it probably is — tell me.

## Rules you need to know

- **Everyone in the lobby needs the same version.** The mod checks over text chat when you join. If anyone is missing it or on a different version, the mod switches itself off for you and says so on screen. Nothing breaks; you just play vanilla until it's sorted.
- **Private lobbies only.** In a Public lobby the mod does nothing at all, silently. This is not configurable.
- **Use at your own risk.** Everything happens on your own machine, plus one small network message the mod sends only once everyone has passed the version check. The game ships an anti-cheat assembly; what it inspects beyond activation rate limits is not known. This has been used in private lobbies among friends without issue, but that's the extent of it.

## The bubble

**Pips.** Ten, drawn as five circles under the icon: a 1-pip hit takes half a circle. The bubble glows in your skin colour with a soft halo around it, thins as pips go and flashes hot on each one lost, on everyone's screen, so an attacker can see it weaken. It blinks when it's down to its last circle. No regeneration.

**What a hit costs, in half-circles.** Stray ball 1, guns 2, homing ball 2, explosions and carts 3, golf swing breaks it outright. A freeze bomb costs nothing: everyone else in range still freezes, you don't. Explosions still go off against everyone else nearby; the bubble only spares its holder. The penalty stroke (your own ball dropped on your head after going out of bounds) ignores the bubble entirely: it can't be absorbed or parried. Costs are editable per hit type in the config.

**Nothing bounces off a held bubble.** Balls, rockets and bombs pass in and cost pips; gun shots land the same way. The bubble is not a body either: a mine, a blast, a laser or a lightning strike measures its distance to you, not to the bubble's edge. Rooted while it's up by default — no moving, jumping, swinging or items.

**Break.** A hit that breaks the bubble is cancelled; you pop straight up, tumble, land, and get up. Everyone nearby hears it, and the bubble is gone for 15 seconds. That cooldown is the real cost. Letting go of a healthy bubble costs 3 seconds before the next one, unless you parried.

**Parry.** Let go of the bubble with a threat coming — anything that would reach you within about a third of a second, or a golfer winding up next to you — and the next hit from it is absorbed for free: no pips, no percent, and it stops a swing that would normally break the bubble. Letting go with nothing coming arms nothing, so it cannot be fished for. Someone aiming a gun along a line through your bubble counts too (experimental). Everyone sees and hears it: the bubble flares, a ring of your colour bursts out of it, and nearby cameras kick.

## The percent layer

**Percent and knockback.** Every hit that gets through adds percent, more for a harder hit: a point-blank elephant gun is worth twice a swing, a pistol at range half. Knockback grows with your percent up to 100% and then stops growing; only a death launch goes higher, so anyone flying off the top of the map is dead. Explosions lift, bullets shove you along the ground, clubs and carts sit in between. At 250% a knockout is a star KO, seen on every screen. Percent resets each hole.

**The flight is the stun.** When you land you get up a quarter second later, whatever the game's timer says. From 65% launches start to hang at the top of the arc, fully by 150%: that is how the stun grows with percent. The comeback shield after a get-up lasts one second instead of three; the gold one for repeated knockouts is the game's own.

**DI.** As you're launched, push toward where on the screen you want to drift and the launch bends that way, up to 20°. Where you land, not how far.

**Teching.** Press the bubble key just before your tumbling body hits the ground and you're up instantly, no lie-down. One press is one attempt, a missed one locks the key out briefly, and a tech roots you for a moment before you can act. A launch you caused yourself, or a break, can't be teched.

**Invulnerability look.** While the game's comeback shield is up, the body flickers washed-out white, Smash-style, on every client. Past 100% embers rise off you, redder toward the kill line.

## Turning things off

Three master switches, each layer coming off cleanly on its own (`com.sbg.shields.cfg`, or in game with ModConfig):

```
[Shield]  ShieldAbsorbsHits = false   -> vanilla bubble: blocks everything, never breaks
[Percent] PercentEnabled    = false   -> no percent, no scaling, vanilla knockback, no death
[Percent] KillZoneEnabled   = false   -> percent climbs, nobody dies
```

`RootWhileShielded = false` lets you move with the bubble up. `Bubble.BubbleReflects = true` restores the game's wall that bounces things back, and must then match for everyone in the lobby. DI, teching, hang time, the flicker and the embers each have their own switch.

## Recommended

ModConfig, so you can change settings in game rather than editing the file.

## Report a bug

Open an issue at https://github.com/elihamster/SuperBattleBros/issues and include:

- which version you and everyone else in the lobby were on,
- whether you were the host,
- the lines from `BepInEx\LogOutput.log` that contain `SBG Shields` (in r2modman: Settings > Browse profile folder). The mod logs what it did and why; those lines are usually the whole answer.

## Building from source

Needs the .NET SDK, the game installed through Steam, and BepInEx in an r2modman profile. `dotnet build` produces the developer build; `dotnet build -c Release` the one that ships. If your Steam or profile paths differ from the defaults, copy `SbgShields.csproj.local.example` to `SbgShields.csproj.local` and set them there. `.\package.ps1` builds and zips a Thunderstore package into `dist\`.
