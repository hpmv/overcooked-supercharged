# Overcooked! 2 TAS Development Framework

See https://hpmv.dev/docs/tas/

## Some Pointers to Get Started With

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
