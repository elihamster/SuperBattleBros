# SBG Shields — working agreements

BepInEx 5 mod for Super Battle Golf. `SBG-Shields-Handoff-v5.md` holds the architecture,
mechanics, config reference, open items and the decisions already made (§11). Read its §0,
§10 and §11 first. These are the rules for working in this repo.

Entries marked **[user]** are things the user said. Entries marked **[inferred]** are defaults an
assistant concluded from how a session went; the user can override any of them at any time.

## Build

- Run `dotnet build` after every code change and report the result (errors, warnings, or clean).
  Do not describe a change as done until the build has run and its outcome is stated.
- Plain `dotnet build` is the developer's build: the csproj maps it to DevDebug (defines `SBG_DEV`, title
  says TEST). With the gitignored `SbgShields.csproj.local` it also copies the DLL into the r2modman package
  folder, so the user only restarts the game. `-c Release` is the public build and never deploys; build it
  too before a drop.

## Testing and bug reports

- **[user]** The user tests in the game. Ask for the `[break …]` trace lines or the load line when a
  report is ambiguous; do not guess the scenario.
- **[user]** Solo, own machine, host, unless the user says otherwise.
- **[inferred]** Diagnostics for a reported bug default on and cheap.
- **[inferred]** Read the whole game method before claiming what it does. When a symptom persists after
  a fix, read the mod's own code on that path next, not the game's again.
- **[user]** Never guess at game behaviour. The game's assemblies are decompiled in `decomp/` (gitignored:
  `decomp/GameAssembly`, `decomp/SharedAssembly`, made with `ilspycmd -p -o <dir> -r <Managed> <dll>`).
  Read the method there. If the folder is missing, regenerate it before reasoning about game code.

## Design

- **[user]** When the user asks to take something out, take it out. Do not preserve it behind a toggle.
- **[user]** The shield alone is a complete, fun mod. Percent and knockback are a layer on top and must be
  cleanly switchable off.
- **[user]** Explain design changes in terms of what the player sees and feels, not what the code does.

## Drops and versioning

- **[user]** No handoff document in every drop; only when asked.
- **[inferred]** Bump `Version` in `Plugin.cs` on every drop that changes behaviour; list retuned defaults
  in `RetunedThisVersion` in the same file. Commit at every such point.
- **[inferred]** Check `git diff` before committing so every intended edit actually landed.

## Working style

- **[inferred]** One agent owns a change end to end. Parallelise only across files that do not touch;
  never within `ResolveKnockout`.
