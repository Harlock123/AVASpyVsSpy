# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls and
what the tests check; this file covers how the code is put together and the rules that keep it working.

## What this is

A split-screen game of searching, booby-trapping and clubbing in the style of First Star Software's 1984
home-computer game Spy vs Spy, in C# on .NET 10 and Avalonia 12, drawn with Avalonia's `DrawingContext`. You play
the white spy against a computer black spy. One self-contained executable per platform (Windows, macOS and Linux,
x64 and ARM). The embassies are generated from seeds, the graphics are drawn in code, and the sounds are
synthesised by `tools/make_sounds.py`.

## Commands

```bash
dotnet build AVASpyVsSpy.slnx                 # the SDK version is in global.json (10.0.400 or later)
dotnet test AVASpyVsSpy.slnx                  # every test runs headless: no window opens
dotnet run --project src/AVASpyVsSpy          # play it; --no-audio for silence
SPY_SHOTS=$PWD/Images dotnet test tests/AVASpyVsSpy.Tests --filter ScreenshotTests   # refresh the README's pictures
SPY_PROBE=1 dotnet test tests/AVASpyVsSpy.Tests --filter PilotProbe --logger "console;verbosity=detailed"
                                              # how the computer spies fare (SPY_PROBE=lazy: a white spy who does nothing)
python3 tools/make_sounds.py                  # rebuild src/AVASpyVsSpy/Sounds (standard-library Python only)
```

`Directory.Build.props` sets `TreatWarningsAsErrors`, nullable reference types and implicit usings for every
project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVASpyVsSpy/Engine/`: the game, with no Avalonia drawing in it.
  - `GameEngine.cs` runs the states (title, demonstration, play, high scores) and the fixed-step loop.
  - `Sim.cs` steps an embassy: two spies walking from room to room, searching furniture, setting and springing
    traps, and fighting.
  - `Embassy.cs` has `Controls`, the screen's measures (`Field`), the items, traps and remedies, and `EmbassyBook`,
    which builds the embassies (each larger than the last) from seeds.
  - `World.cs` holds the world's state and `Dice`, the copyable random source.
  - `Pilot.cs` is the computer spy: it plays the black spy, both spies in the demonstration, and the tests.
  - `InputHandler.cs` (keys) and `ControllerManager.cs` (SDL2 gamepads) produce `Controls`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVASpyVsSpy/Views/`: `GameCanvas` (split over partial files) draws the 1024x768 frame, scaled to the
  window, with the white spy's room above and the black spy's below, and runs the 60 Hz timer.
- `src/AVASpyVsSpy/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `tests/AVASpyVsSpy.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`. `EmbassyTests.cs` checks every room can be
  reached, the items are hidden properly, and the computer spies can play an embassy out. `PilotProbe.cs` is a
  tuning report, not a check.

## Rules that keep it working

- **Fixed steps.** `GameEngine.Update` turns frame time into fixed 1/60 s steps (`Field.Step`), and `Sim.Step`
  advances the world by one. Tests can play whole embassies deterministically from a seed. Draw random numbers
  from the world's seeded `Dice` (`w.Dice`), or a pilot's own `Dice`, never from `Random.Shared`.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the world and draws it.
- **Input goes through `InputHandler`.** Up/down on keys and stick must produce the same `Dy` sign the engine
  expects (negative is up the screen, towards the room's back wall). Test new controls through `InputHandler`,
  not by setting `Controls` directly.
  - Keys are tracked by `PhysicalKey`, because on some Linux setups Avalonia reports the release of Shift as
    CapsLock (Shift sets a trap here). `ShiftReleaseTests` covers this.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up.
- **Tests never touch the player's files.** High scores live in the app-data folder
  (`SpecialFolder.ApplicationData`, `AVASpyVsSpy`); tests load the table from a fresh temporary folder and run
  with `Silent = true`.
- **Line endings.** The sources are LF; `app.manifest` is CRLF. Keep each file's existing line endings so diffs
  show only real changes.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- A change to the embassy builder can strand a room or an item: `EmbassyTests` will say so.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, controls, the test list) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push and pull request. It then publishes self-contained
single-file builds for win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64, kept as workflow artifacts.
Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a proper release.
A release also asks the portfolio site to rebuild, if the `SITE_REFRESH_TOKEN` secret is set.
