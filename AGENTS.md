# Repository Guidelines

## Project Structure & Module Organization
`patch/` contains the BepInEx plugin that patches Overcooked! 2 and talks to the controller. Key areas are `Injection/` for controller I/O, `AlteredComponents/` and `Extensions/` for Harmony-based game changes, and `Libs/` for copied game/BepInEx DLLs. `controller/` is the Blazor-based TAS UI and simulator; most logic lives in `Data/`, UI routes in `Pages/`, shared layout in `Shared/`, and static assets in `wwwroot/`. `common/game.thrift` defines the shared wire protocol. `patch/gen-csharp/` and `controller/gen-netstd/` are generated; do not edit them by hand.

## Build, Test, and Development Commands
- `dotnet build Supercharged.sln -c Release`: builds both projects when dependencies are present.
- `dotnet build patch/SuperchargedPatch.csproj -c Release`: builds the game patch; requires the DLLs listed in `patch/Libs/README.md`.
- `cd controller && env -u PROTOBUF_PROTOC PROTOBUF_TOOLS_OS=macosx PROTOBUF_TOOLS_CPU=x64 dotnet build -c Release`: reliable macOS controller build on Apple Silicon.
- `cd controller && env -u PROTOBUF_PROTOC PROTOBUF_TOOLS_OS=macosx PROTOBUF_TOOLS_CPU=x64 ASPNETCORE_URLS=http://localhost:5050 dotnet run -c Release --no-launch-profile`: starts the local web UI on an alternate port when `localhost:5000` is already in use.

## Coding Style & Naming Conventions
Use 4-space indentation and existing C# conventions: `PascalCase` for types/methods/properties, `camelCase` for locals/parameters, and descriptive names for game-specific patch classes. Keep braces and line breaks consistent with the repo’s compact style; `controller/omnisharp.json` disables extra brace newlines. Prefer small targeted patches over broad refactors, and keep generated code out of manual edits.

## Testing Guidelines
There is no standalone automated test suite in this repository today. Validate changes by building the affected project, then smoke-test the controller UI and, for patch changes, verify in-game behavior with BepInEx loaded. When changing protocol or serialization code, rebuild both `patch/` and `controller/` so regenerated Thrift/Protobuf outputs stay in sync.

## Commit & Pull Request Guidelines
Recent history uses short, imperative commit subjects such as `Fix PlateReturnStation warping` and `Clean up code warnings.` Keep the first line concise, describe the behavior change, and reference a PR or issue when relevant. PRs should include: affected area (`patch`, `controller`, or protocol), setup notes for reviewers, manual verification steps, and screenshots for UI changes.

## Configuration Notes
Do not commit copied proprietary game DLLs from `patch/Libs/`. Treat `bin/`, `obj/`, generated artifacts, and local saves as build outputs unless a change explicitly requires them.

For the controller on macOS, keep `PROTOBUF_PROTOC` unset. Pointing it at Homebrew `protoc` generates code that is too new for this repository’s pinned `Google.Protobuf` version. The controller currently targets `.NET 6`, so contributors need the `.NET 6` SDK/runtime installed even if newer SDKs are present. `localhost:5000` may already be owned by macOS Control Centre (`ControlCe`), so prefer an alternate port such as `5050` when running locally.

For the patch project, checked-in files under `patch/gen-csharp/` are the source of truth for normal builds. `patch/SuperchargedPatch.csproj` now skips Thrift regeneration by default because Homebrew `thrift 0.23.x` removed the legacy `-gen csharp` target that these sources were generated with. Only enable `RunThriftCodegen=true` if you intentionally change `common/game.thrift` and have an older Thrift compiler that still supports the classic C# generator.
