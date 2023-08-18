# Overcooked! 2 TAS Development Framework

See https://hpmv.dev/docs/tas/ for an overview.

## How to Use

### Compiling

You will need .NET Framework 3.5 as well as .NET 7.0. The former is needed to plug into Unity, the latter is just the latest .NET version for the controller part.

You can then use either Visual Studio (recommended) or VSCode to compile, but before doing that:

* Download BepInEx 5 (not 6!).
* Follow the instructions in `patch/Libs/README.md` to copy the listed DLLs from the game to the `patch/Libs` directory.

### Installation

* First, install BepInEx 5, which is as simple as extracting it to the game's directory. You should have a `BepInEx` directory alongside `Overcooked2_Data`, and inside the `BepInEx` directory there should be `core`, `config`, `plugins`. If `plugins` is not there, create that directory.
* Compile the `patch` project in *Release* mode. There should now be a `SuperchargedPatch.dll` in the patch/bin/Release folder, as well as some dependency DLLs.
* Copy `Thrift.dll` and `Newtonsoft.Json.dll` to the `BepInEx/plugins` directory. These are the dependency DLLs.
* You now have two choices for installing `SuperchargedPatch.dll`:
  * If you just want to use the TAS framework, copy `SuperchargedPatch.dll` also into the `BepInEx/plugins` directory.
  * If you also want to develop the TAS framework and don't want to restart the game every time you recompile the `patch`, install `ScriptEngine.dll` (from [BepInEx.Debug](https://github.com/BepInEx/BepInEx.Debug)) into `BepInEx/plugins` and then make a symbolic link of `SuperchargedPatch.dll` at **`BepInEx/scripts`**. This way, whenever you recompile the dll, you only need to hit F6 in the game to reload it.

### Using the TAS Framework
You will need to run the game as well as the controller. To run the controller, either run the controller project in Visual Studio (in Release mode), or type `dotnet run --release` in the controller directory. This will spin up a webserver at `localhost:5000`. You can then navigate to the TAS tool by visiting `localhost:5000/studio`.

You may begin by initializing a new level using the button that looks like a plus. Right now, only carnival 3-2 four player is correct; the other levels are outdated. To start orchestrating the chefs, you must first connect to the game using the link icon, wait a few seconds and then restart (or just enter) the level. The game will start tracking the level. You can then pause and start adding actions to a chef by clicking "Click to insert here".

#### Adding a new level
It's not currently trivial to add support for a new level to the TAS engine. There are a few issues:
* The level's geometry needs to be captured. This can be done with `localhost:5000/capture_map`, but it's very very manual because all it does is plot a dot for each position any chef has been in the game. The way to use this is to load up a level in the game and then walk around along all the edges, and then hover over the plotted points and manually write down the polygons that represent walkable areas of the map.
* All the entities of the level need to be annotated correctly. The `localhost:5000/capture_map` will print to the console some code to start with, but additional code is needed to mark each entity with an appropriate prefab. See controller/Data/Levels for examples.
  * A future improvement is to remove the need to do this. The game already provides enough information, we just need to automatically extract it, e.g. an entity is an ingredient container if it has the `ServerIngredientContainer` component.
* There are some mechanisms in the game for which warping is not implemented, preventing proper interactive TAS orchestration. Examples of such mechanisms include: moving platforms, teleportals, and levels that have multiple stages. These are all implementable in theory, but some may be very difficult; for example, implementing warping for the cannon involved rewriting its entire code.


## Development

### Some Pointers to Get Started With:

Projects:

* controller: the web UI where TAS actions are orchestrated. It's responsible for capturing game state and determining what inputs to send to the game. It's also responsible for pausing/resuming the game as well as warping to specific frames.
  * [controller/Pages/Studio.razor](controller/Pages/Studio.razor): the main UI component.
  * [controller/Data/RealGameSimulator.cs](controller/Data/RealGameSimulator.cs): the main class responsible for interpreting messages sent from the game to reconstruct the game state.
  * [controller/Data/GameAction.cs](controller/Data/GameAction.cs) and subclasses: high-level actions that determine game inputs based on game state (e.g. GotoAction finds a path and navigates a chef to a given position).
  * [controller/Data/Levels/*](controller/Data/Levels): Level setups for each level supported by the tool - more can be added using the /map_capture endpoint (not very easy yet).
* patch: BepInEx plugin that augments the game to capture game state, inject controller inputs, pause/resume the game, and implement warping.
  * [patch/TASPatcher.cs](patch/TASPatcher.cs): Main entry point of the BepInEx plugin.
  * [patch/Injection/InjectorServer.cs](patch/Injection/InjectorServer.cs): Connection protocol that communicates with the controller. At each frame, the game state is sent to the controller, and at the beginning of the next frame, the input is awaited on to ensure it is available.
  * [patch/ControllerHandler.cs](patch/ControllerHandler.cs): Implements what exactly happens at the end of every frame: capture game state, handle pausing/resuming, handle warping, etc.
  * [patch/WarpHandler.cs](patch/WarpHandler.cs): **This is what I'm proud of** 😀. Implements the logic to instantly change the game state to any desired state sent by the controller.
  * [patch/AlteredComponents/*](patch/AlteredComponents): Various patches to make the game more friendly for TAS.
  * [patch/Extensions/*](patch/Extensions): Whenever private functions or fields need to be accessed from the game code, we create extension functions so that the code remains clean despite the extensive use of reflection.
  * Look for classes annotated with `HarmonyPatch` to find all functions that are altered from the game.
* common:
  * [common/game.thrift](common/game.thrift): Thrift schema for communication between controller and patch.
