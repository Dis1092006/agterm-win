# Contributing to agterm-win

## Before you start

If this is your first pull request to agterm-win, open an [issue](https://github.com/Dis1092006/agterm-win/issues) that describes what you plan to build and why. Then wait for a reply before you write code. Some ideas do not fit the project, and it costs less to find that out before the work is done.

Check that what you want does not already exist. agterm-win has a broad control API, custom commands you bind yourself in `keymap.conf`, and six Settings tabs, so a lot is reachable without new code. Look at `agtermctl --help`, the [README](README.md), `docs/PLATFORM-NOTES.md` and the bundled agent skill under `plugins/agterm/skills/agterm/` before you propose anything.

This repository has no Discussions. Use an issue for a bug, a question or a plan.

## Is it worth it?

Weigh what a feature adds against the code it brings with it.

- **Does it help most users, or one setup?** A feature for a few edge cases rarely pays for the work to maintain it.
- **Does it belong here or upstream?** agterm-win is a Windows port of [umputun/agterm](https://github.com/umputun/agterm). It matches upstream's wire contract byte for byte: the `agtermctl` commands, arguments, results and error strings, the keymap grammar, the `BuiltinAction` names, the `{AGT_X}` tokens and the settings field names. The parity pin is upstream `7df70fe`, and `parity/surface.json` is its snapshot. A change to any of these needs an upstream equivalent, or a reason to be a Windows-only extension (see below). Propose it in an issue first.
- **Does it belong in xterm.js?** Terminal emulation and rendering come from xterm.js running in WebView2. Bugs and requests in those areas belong in [xtermjs/xterm.js](https://github.com/xtermjs/xterm.js). A workaround here for an xterm.js problem needs a recorded reason in `docs/PLATFORM-NOTES.md`.
- **Is it a Windows-only divergence?** Some changes have no upstream analogue: the named pipe transport, a macOS feature Windows does not have, a command only this port has. Each one is recorded in `docs/PLATFORM-NOTES.md` with the reason.
- **Does it change what agterm-win is?** It is a terminal with a two-level workspace to session sidebar. It is not a multiplexer, not a session manager for other tools, not an IDE. A PR that pulls it in those directions does not fit.
- **Is the code proportional to the value?** Several hundred lines for something a custom command in `keymap.conf` already covers is too much. Keep it small.

## Development setup

You need:

- Windows 10 version 2004 or later, or Windows 11, with the WebView2 Runtime. Windows 11 ships it.
- The .NET SDK 10.0.400 or a later .NET 10 feature band. `global.json` pins `10.0.400` with `rollForward: latestFeature`.
- Visual Studio 2022 or later with the Desktop development with C++ workload, only to publish with Native AOT through `publish.proj`. Ordinary builds and tests do not need it.

Build and test from the repository root:

```powershell
dotnet build "AgTerm.slnx"
dotnet build "src\AgTerm.App" -p:Platform=x64
dotnet test "AgTerm.slnx" -m:1
```

The first command builds the solution. The second builds the WinUI app project on its own. It needs `-p:Platform=x64`, because the app has no AnyCPU platform. The third runs the test projects one at a time, as CI does. With `-m:1` the live ConPTY and WebView2 suites do not compete for CPU.

All three must pass with 0 warnings before you send a PR. `Directory.Build.props` sets `TreatWarningsAsErrors`, so a warning fails the build. An incremental build can skip a warning, so add `--no-incremental` to see all of them.

The live tests in `tests/AgTerm.Xterm.Tests` start real ConPTY shells and a real WebView2. They run as part of `dotnet test`.

For manual checks, run an isolated instance with its own `AGTERM_STATE_DIR`, so it does not touch your real workspaces. The [Running](README.md#running) section of the README shows how. It also covers publishing and Rider.

## AI-assisted contributions

This project is written with AI tools too. The expectations below apply to every PR.

**Quality expectations**

- Review your own code before you submit it, whether you or a tool wrote it.
- Follow the project's conventions. Read the codebase first, with or without AI help. The binding rules are in `CLAUDE.md`; these are the ones that catch people out:
  - **Module boundary.** `AgTerm.Core` is host-free: it targets plain `net10.0` and has no WinUI, WebView2 or ConPTY dependency. Model, persistence, parsing and dispatch logic go there. `AgTerm.Terminal` holds the ConPTY backend, `AgTerm.Xterm` the WebView2 and xterm.js bridge, and `AgTerm.App` the WinUI 3 shell. When a feature splits, put the host-free half in `AgTerm.Core` and keep the rest a thin adapter for side effects.
  - **TDD, strictly.** Write the failing test first, watch it fail, then write the smallest code that passes it. No production code without a failing test. `CLAUDE.md` names the `superpowers:test-driven-development` skill for this. The `superpowers:*` skills are optional agent tooling, not a requirement.
  - **Tests.** The suites use xUnit, one test project per source project. Add coverage to the suite that already owns the behavior. Tests ported from upstream's Swift suites live in `Ported` folders.
  - **Wire format.** JSON must match the output of Swift `Codable`: camelCase property names exactly as in the Swift structs, and an absent optional is left out of the output, never written as `null`. Wire error strings are part of the contract. Copy them verbatim from `docs/agterm-windows-spec.md` or the upstream Swift source.
  - **Versions.** Every PR raises `<Version>` in `Directory.Build.props` by hand, whatever it changes. A change to the agent skill also raises the plugin `version` in `plugins/agterm/.claude-plugin/plugin.json`, `plugins/agterm/.codex-plugin/plugin.json` and the plugin entry of `.claude-plugin/marketplace.json`. The three must stay equal. The plugin version is separate from the app version.
  - **Documentation.** A change to a wire command, a CLI flag, an `AGTERM_*` variable or the keymap updates the bundled agent skill under `plugins/agterm/skills/agterm/` in the same PR. `tests/AgTerm.Cli.Tests/SkillTextTests.cs` checks that the skill reference names every wire command and that the plugin manifests carry one version. Plans and design specs go into `docs/plans/`, and the PR that finishes a plan moves it to `docs/plans/completed/`.
- Code has to be readable by a person who maintains it a year from now.
- Commit messages and PR descriptions should say something specific. Generic AI output is not enough.
- Keep PRs focused. Unrelated cleanup does not belong in a feature PR, so send it separately.
- Checking AI output for security problems is your job. These tools introduce vulnerabilities that are not obvious on a quick read.
- You must understand and be able to explain every line you submit. If I ask about a change in review, "the AI wrote it" does not answer the question.

**Reviewable scope**

- A PR has to be sized for a human to review.
- Split a large change into focused, logical PRs.
- A PR that touches dozens of files with thousands of lines is not reviewable. Break it down.

**What I cannot accept**

- Unreviewed AI output left for me to clean up.
- Code with no tests, or with a failing build or test run.
- Changes that ignore project conventions after someone pointed at them.
- PRs that do not respond to review feedback.

I maintain this project alone and review every PR myself. If a PR would take more of my time to fix than to write the change again, I close it and say why.

## Adding a user-facing action

You cannot guess this convention from the code: a new user action is not done when the button works.

Anything added to `AppStore` (`src/AgTerm.Core/Model`) for a user to do from the sidebar, a menu, a palette or a keymap chord also needs all of these:

1. a `Command` case and its wire string in `src/AgTerm.Core/Protocol/Command.cs`, with new arguments in `ControlArgs` and new result fields in `ControlResult`
2. handling in `ControlDispatcher`, which owns the host-free part (argument parsing, validation, error text, response shape) and calls the host through an `IControlActions` method for the side effect alone
3. that method in `HeadlessControlActions` (`src/AgTerm.ControlServer`). The live app runs the same class through `LiveControlHost` in `AgTerm.Xterm`, which passes it callbacks for the effects that need a real window or pane.
4. an `agtermctl` subcommand in `src/AgTerm.Cli`, in the `*Commands.cs` file for its group
5. tests: wire round-trip and dispatcher tests in `tests/AgTerm.Core.Tests`, an end-to-end test through `agtermctl` in `tests/AgTerm.E2E`, and a live test in `tests/AgTerm.Xterm.Tests` when the command drives a real pane or window

The UI and the control pipe change the same `AppStore`, and they must not drift apart. A command that sets or changes per-session state also needs a matching read-back field on the `tree` node in `ControlTreeNodes.cs`, so a script can query the value it just wrote.

Record where the command comes from. A command, argument or field taken from upstream goes into `parity/adopted.json` with its upstream sha. One that upstream does not have goes into `parity/windows-only.json` and the "Windows-only extensions" section of `docs/PLATFORM-NOTES.md`. The parity tests compare the code with these lists.

The bundled agent skill under `plugins/agterm/skills/agterm/` describes the control API and changes with it.

The one exemption is a change with nothing to drive, meaning pure rendering or visual polish. Say so in the PR description when you claim it.

## Issues and PRs

Every issue and pull request has to answer two questions:

1. **What is the problem?** What exactly is broken, missing or awkward. Be specific. "It would be nice to have X" is not a problem statement.
2. **How does this solve it?** Why this approach is the right fix, and how it addresses the cause rather than the symptom.

If a PR does not say what problem it solves, I ask for that before I review the code.

## Cookbook recipes

`cookbook/` collects installable `agtermctl` workflows. A recipe is a folder `cookbook/<name>/` with PowerShell scripts and a README, not C#. The README says what the recipe does, what it needs, how to set it up and how to check it. `install.ps1` sets the recipe up, `install.ps1 -Uninstall` removes it, and a second run changes nothing.
