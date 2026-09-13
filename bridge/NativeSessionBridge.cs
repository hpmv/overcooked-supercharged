using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using HarmonyLib;
using Hpmv;
using OrderController;
using SuperchargedPatch.AlteredComponents;
using SuperchargedPatch.Extensions;
using Team17.Online;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace SuperchargedPatch.Bridge
{
    // Session/diagnostic adapter only. Supercharged remains the sole owner of
    // clocks, frame exchange, input interpretation and optional warp operations.
    public static class NativeSessionBridge
    {
        private static readonly TransportQueue queue = new TransportQueue();
        private static readonly object socketGate = new object();
        private static TcpListener listener;
        private static TcpClient currentClient;
        private static Thread server;
        private static volatile bool running;
        private static Harmony harmony;
        private static TransportConnection controlOwner;
        private static bool neutralFence = true, holdPause, loading, loadComplete;
        private static string root, lastError = "", lastRelease = "startup", lastCommand = "";
        private static int port, mainThread, readyUnityFrame = -1;
        private static float loadingDeadline;
        private static Action<InputData> immediateInputConsumer;
        private static int[] chefIds = new int[0];
        private static InputData neutral = MakeNeutral(new int[0]);
        private static SuperchargedPatch.Authoring.AuthoringModuleHost authoringModules;

        public static bool InputBlocked { get { return neutralFence; } }
        public static bool KitchenReady { get { return loadComplete && !loading; } }

        // The framework wires this to its actual logical-button cache owner.
        // It is invoked on Unity's thread, never from the diagnostic socket.
        public static void SetImmediateInputConsumer(Action<InputData> consumer)
        {
            RequireMainThread();
            immediateInputConsumer = consumer;
            if (neutralFence && consumer != null) consumer(neutral);
        }

        public static void Awake()
        {
            if (running) throw new InvalidOperationException("Native session bridge is already installed.");
            mainThread = Thread.CurrentThread.ManagedThreadId;
            string configured = Environment.GetEnvironmentVariable("OC2SC_ROOT");
            if (String.IsNullOrEmpty(configured))
                throw new InvalidOperationException("OC2SC_ROOT must explicitly identify the isolated workspace profile/artifact root.");
            root = Path.GetFullPath(configured);
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.Combine(root, "artifacts"));
            Directory.CreateDirectory(Path.Combine(root, "modules"));
            authoringModules = new SuperchargedPatch.Authoring.AuthoringModuleHost(Path.Combine(root, "modules"), RequireAuthoringBoundary);
            harmony = new Harmony("dev.hpmv.overcooked.supercharged.native-session-bridge");
            SaveIsolation.Install(harmony, root);
            ValidateJsonRuntime();
            port = 17636;
            string configuredPort = Environment.GetEnvironmentVariable("OC2SC_BRIDGE_PORT");
            if (configuredPort != null && !Int32.TryParse(configuredPort, out port) || port < 1 || port > 65535)
                throw new ArgumentException("OC2SC_BRIDGE_PORT must be an integer from1 through65535.");
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            Screen.SetResolution(1280, 720, false);
            listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            running = true;
            server = new Thread(ServerLoop); server.IsBackground = true; server.Start();
            Debug.Log("[Supercharged bridge] loopback port=" + port + "; isolated root=" + root);
        }

        // Called by InjectorServer when accepting each input frame, BEFORE its
        // logical-button caches consume it. The incoming frame remains untouched
        // during ordinary framework execution. While blocked, neither a stale
        // resume directive nor a warp may escape the diagnostic/session fence.
        public static InputData FilterInput(InputData input)
        {
            RequireMainThread();
            RefreshChefIds();
            return neutralFence ? neutral : input ?? neutral;
        }

        public static void ForceNeutral(string reason)
        {
            RequireMainThread();
            neutralFence = true;
            lastRelease = reason ?? "explicit";
            RefreshChefIds();
            if (immediateInputConsumer != null) immediateInputConsumer(neutral);
        }

        public static void LateUpdate()
        {
            if (!running) return;
            RequireMainThread();
            // The game's graphics preferences are applied again during loading.
            // Keep the user's requested normal window throughout those transitions.
            if (Screen.fullScreen) Screen.SetResolution(1280, 720, false);
            RefreshChefIds();
            // A bounded number of diagnostics cannot starve the framework's
            // own LateUpdate/frame exchange. Queue ownership cancels dead clients.
            for (int i = 0; i < 16 && queue.DispatchNext(Disconnected, Handle); i++) { }
            if (loading)
            {
                if (SessionSetup.Failed)
                    FinishLoad(false, SessionSetup.LastError);
                else if (SessionSetup.KitchenReady && AllUsersInLevel())
                    FinishLoad(true, "");
                else if (Time.realtimeSinceStartup >= loadingDeadline)
                    FinishLoad(false, "Native session did not reach four synchronized InLevel users within150 seconds.");
            }
            if (holdPause && Helpers.CurrentTimeManager != null) Helpers.Pause();
            else if (loading && Helpers.CurrentTimeManager != null) Helpers.Resume();
        }

        private static void FinishLoad(bool success, string error)
        {
            if (success) NativeSceneMetadata.Refresh();
            loading = false; loadComplete = success; lastError = error;
            readyUnityFrame = success ? Time.frameCount : -1;
            ForceNeutral(success ? "native-load-complete" : "native-load-failed");
            holdPause = true;
            if (Helpers.CurrentTimeManager != null) Helpers.Pause();
            Debug.Log("[Supercharged bridge] load " + (success ? "ready" : "failed: " + error));
        }

        private static bool AllUsersInLevel()
        {
            if (ServerUserSystem.m_Users.Count != 4 || ClientUserSystem.m_Users.Count != 4) return false;
            for (int i = 0; i < 4; i++)
                if (ServerUserSystem.m_Users._items[i].GameState != GameState.InLevel ||
                    ClientUserSystem.m_Users._items[i].GameState != GameState.InLevel) return false;
            return chefIds.Length == 4;
        }

        private static void Handle(Envelope envelope)
        {
            bool quit = false;
            try
            {
                var request = JsonRead.Object(envelope.json);
                if (JsonRead.Integer(request, "version", 1) != 1)
                    throw new ArgumentException("Expected bridge protocol version1.");
                string command = (JsonRead.Text(request, "command", "status") ?? "status").Trim().ToLowerInvariant();
                string message = "";
                object detail = null;
                switch (command)
                {
                    case "status": case "inspect": break;
                    case "hot-status": detail = authoringModules.Describe(); break;
                    case "hot-load": case "hot-call": case "hot-unload":
                        RequireAuthoringBoundary();
                        try
                        {
                            string slot = JsonRead.Text(request, "slot", null);
                            if (command == "hot-load")
                            {
                                string coreHash=JsonRead.Text(request,"coreSha256",null);
                                if(coreHash==null||coreHash.Length!=64)throw new ArgumentException("Module manifest coreSha256 required.");
                                detail = authoringModules.Load(slot, JsonRead.Text(request,"path",null), JsonRead.Text(request,"type",null), JsonRead.Text(request,"sha256",null),coreHash);
                            }
                            else if (command == "hot-unload") detail = authoringModules.Unload(slot);
                            else
                            {
                                object arguments;
                                var args = request.TryGetValue("args",out arguments) ? NormalizeModuleArguments(arguments) as Dictionary<string,object> : new Dictionary<string,object>();
                                if(args==null)throw new ArgumentException("Module args must be an object.");
                                detail = authoringModules.Invoke(slot,JsonRead.Text(request,"operation",null),args);
                            }
                            WriteAuthoringReceipt(request, true, detail);
                        }
                        catch(Exception error) { WriteAuthoringReceipt(request,false,error.ToString()); throw; }
                        lastCommand = command;
                        break;
                    case "food": detail = NativeFoodSnapshot.Capture(); break;
                    case "window":
                        string mode = JsonRead.Text(request, "mode", "");
                        if (mode != "minimize" && mode != "show") throw new ArgumentException("Window mode must be minimize or show.");
                        NativeWindowControl.SetMinimized(mode == "minimize");
                        break;
                    case "render":
                        int requestedFps = JsonRead.Integer(request, "fps", 60);
                        if (requestedFps < 0 || requestedFps > 1000) throw new ArgumentException("Render fps must be0 (unlimited) or1..1000.");
                        QualitySettings.vSyncCount = 0;
                        Application.targetFrameRate = requestedFps == 0 ? -1 : requestedFps;
                        Screen.SetResolution(1280, 720, false);
                        break;
                    case "load": case "restart":
                        if (loading) throw new InvalidOperationException("Native load is already in progress.");
                        if (immediateInputConsumer == null)
                            throw new InvalidOperationException("Framework logical-input neutralization callback has not been wired.");
                        ForceNeutral("native-" + command);
                        holdPause = false; loadComplete = false; lastError = ""; readyUnityFrame = -1;
                        if (Helpers.CurrentTimeManager != null) Helpers.Resume();
                        // Clear only framework observation cache. No native clock,
                        // food, transform, round timer or random state is restored.
                        ActiveStateCollector.ClearCacheAfterWarp(0);
                        StateInvalidityManager.InvalidReason = "";
                        WarpableRoundData.ConfigureSeedForNextRound(JsonRead.Integer(request, "seed", 0));
                        SessionSetup.Execute(command);
                        loading = true; loadingDeadline = Time.realtimeSinceStartup + 150f;
                        controlOwner = envelope.Owner; lastCommand = command;
                        message = "Native load queued; poll status until loadComplete or lastError.";
                        break;
                    case "pause":
                        ForceNeutral("bridge-pause"); holdPause = true;
                        if (Helpers.CurrentTimeManager != null) Helpers.Pause();
                        controlOwner = envelope.Owner; lastCommand = command;
                        break;
                    case "arm":
                        if (loading || !loadComplete || Helpers.CurrentTimeManager == null || !Helpers.IsPaused())
                            throw new InvalidOperationException("Arm requires a completed native load which is still paused.");
                        if (immediateInputConsumer == null) throw new InvalidOperationException("Framework logical-input callback is not wired.");
                        ForceNeutral("bridge-arm-boundary");
                        neutralFence = false; holdPause = false;
                        controlOwner = envelope.Owner; lastCommand = command;
                        message = "Framework input armed; native pause is retained until the framework requests resume.";
                        break;
                    case "resume":
                        if (loading) throw new InvalidOperationException("Cannot resume framework control during native loading.");
                        if (Helpers.CurrentTimeManager == null) throw new InvalidOperationException("Native TimeManager is not initialized.");
                        if (immediateInputConsumer == null) throw new InvalidOperationException("Framework logical-input callback is not wired.");
                        // Force an explicit neutral cache boundary before the
                        // next newly accepted framework input can regain control.
                        ForceNeutral("bridge-resume-boundary");
                        neutralFence = false; holdPause = false; Helpers.Resume();
                        controlOwner = envelope.Owner; lastCommand = command;
                        break;
                    case "quit":
                        ForceNeutral("bridge-quit"); holdPause = true;
                        controlOwner = envelope.Owner; lastCommand = command; quit = true;
                        break;
                    case "screenshot":
                        string screenshot = ScreenshotPath(JsonRead.Text(request, "path", null));
                        ScreenCapture.CaptureScreenshot(screenshot);
                        message = "Native screenshot queued: " + screenshot;
                        break;
                    default: throw new ArgumentException("Bridge command must be status, food, window, load, restart, pause, arm, resume, render, screenshot, hot-status, hot-load, hot-call, hot-unload or quit.");
                }
                envelope.result = JsonText.Serialize(Map("version", 1, "ok", true, "message", message, "bridge", Capture(), "detail", detail));
            }
            catch (Exception ex)
            {
                envelope.result = JsonText.Serialize(Map("version", 1, "ok", false, "error", ex.ToString(), "bridge", Capture()));
            }
            finally { envelope.complete.Set(); }
            if (quit) Application.Quit();
        }

        private static void Disconnected(TransportConnection owner)
        {
            // A short-lived read-only status connection does not acquire input
            // ownership and cannot interrupt a running framework session.
            if (!object.ReferenceEquals(owner, controlOwner)) return;
            controlOwner = null;
            ForceNeutral("bridge-control-connection-disconnected");
            holdPause = !loading; // complete an in-flight native load neutrally
            if (holdPause && Helpers.CurrentTimeManager != null) Helpers.Pause();
        }

        private static void RequireAuthoringBoundary()
        {
            RequireMainThread();
            if (!KitchenReady || Helpers.CurrentTimeManager == null || !Helpers.IsPaused() || !neutralFence || !holdPause)
                throw new InvalidOperationException("Live authoring commands require a ready kitchen paused by the bridge with inputs fenced. Issue pause first.");
        }

        private static object NormalizeModuleArguments(object value)
        {
            var number = value as JsonRead.Number;
            if(number!=null)
            {
                long integer;
                if(Int64.TryParse(number.Token,System.Globalization.NumberStyles.AllowLeadingSign,System.Globalization.CultureInfo.InvariantCulture,out integer))return integer;
                return number.Value;
            }
            var map = value as Dictionary<string,object>;
            if(map!=null) { var result = new Dictionary<string,object>(); foreach(var pair in map)result.Add(pair.Key,NormalizeModuleArguments(pair.Value)); return result; }
            var array=value as System.Collections.ArrayList;
            if(array!=null) { var result=new object[array.Count]; for(int i=0;i<result.Length;i++)result[i]=NormalizeModuleArguments(array[i]);return result; }
            return value;
        }

        private static void WriteAuthoringReceipt(Dictionary<string,object> request,bool success,object result)
        {
            using(var writer = new StreamWriter(Path.Combine(root,"artifacts/authoring-modules.jsonl"),true,Encoding.UTF8))
                writer.WriteLine(JsonText.Serialize(Map("utc",DateTime.UtcNow.ToString("o"),"processId",System.Diagnostics.Process.GetCurrentProcess().Id,
                    "unityFrame",Time.frameCount,"request",request,"ok",success,"result",result,"modules",authoringModules.Describe())));
        }

        private static string ScreenshotPath(string requested)
        {
            if (String.IsNullOrEmpty(requested)) throw new ArgumentException("Screenshot requires an explicit PNG path inside OC2SC_ROOT.");
            string path = Path.GetFullPath(Path.IsPathRooted(requested) ? requested : Path.Combine(root, requested));
            string prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                !String.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Screenshot path must be a PNG file below OC2SC_ROOT.");
            if (File.Exists(path)) throw new IOException("Screenshot already exists; refusing to overwrite it.");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            return path;
        }

        public static Dictionary<string, object> Capture()
        {
            RequireMainThread();
            Dictionary<string, object> session;
            try { session = JsonRead.Object(SessionSetup.Status()); }
            catch (Exception ex) { session = Map("statusError", ex.Message); }
            var chefs = new List<object>();
            foreach (PlayerIDProvider provider in UnityEngine.Object.FindObjectsOfType<PlayerIDProvider>())
            {
                if (provider.GetComponent<PlayerControls>() == null) continue;
                chefs.Add(Map("player", (int)provider.GetID(), "entity", (int)EntitySerialisationRegistry.GetId(provider.gameObject),
                    "local", provider.IsLocallyControlled()));
            }
            return Map("root", root, "port", port, "profile", SaveIsolation.Root, "session", session,
                "loading", loading, "loadComplete", loadComplete, "lastError", lastError, "readyUnityFrame", readyUnityFrame,
                "inputBlocked", neutralFence, "paused", Helpers.CurrentTimeManager != null && Helpers.IsPaused(), "holdPause", holdPause,
                "unityFrame", Time.frameCount, "unityTime", Time.time, "fixedTime", Time.fixedTime,
                "fixedDeltaTime", Time.fixedDeltaTime, "captureFramerate", Time.captureFramerate,
                "logicalRealtime", UnrealTimePatch.LogicalRealtime(), "authoringClockRestores", UnrealTimePatch.AuthoringClockRestores,
                "logicalClock", UnrealTimePatch.Diagnostics(),
                "targetFrameRate", Application.targetFrameRate,
                "captureMetrics", Map("samples", ControllerHandler.CaptureCount,
                    "lastMilliseconds", ControllerHandler.LastCaptureTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency,
                    "totalMilliseconds", ControllerHandler.CaptureTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency),
                "sceneMetadataRefreshes", NativeSceneMetadata.Refreshes,
                "nativeSyncCalls", Map("worldUpdates", NativeSyncCallCounters.WorldUpdates,
                    "worldEvents", NativeSyncCallCounters.WorldEvents, "chefMessages", NativeSyncCallCounters.ChefMessages,
                    "meshPositions", NativeSyncCallCounters.MeshPositions, "basicEvents", NativeSyncCallCounters.BasicEvents),
                "nativeCheckpoints", NativeKitchenCheckpoint.Diagnostics(),
                "authoringModules", authoringModules == null ? null : authoringModules.Describe(),
                "inputExchange", Injector.Server.Diagnostics(),
                "screenWidth", Screen.width, "screenHeight", Screen.height, "fullScreen", Screen.fullScreen,
                "windowedOptionCommits", WindowedModePatches.InterceptedCommits,
                "nativeWindow", NativeWindowControl.Observe(),
                "applicationFocused", Application.isFocused, "backgroundTasInput", BackgroundTasInputFocus.Enabled,
                "unfocusedVirtualInputChecks", BackgroundTasInputFocus.UnfocusedVirtualChecks,
                "unfocusedLogicalInputChecks", BackgroundTasInputFocus.UnfocusedLogicalChecks,
                "runInBackground", Application.runInBackground, "lastCommand", lastCommand, "lastRelease", lastRelease,
                "chefs", chefs, "transport", queue.Capture(), "nativeRound", CaptureNativeRound(),
                "nativePhysics", NativePhysicsObservation.CaptureRegistry(),
                "frameworkAssembly", typeof(NativeSessionBridge).Assembly.Location);
        }

        private static Dictionary<string, object> Map(params object[] pairs)
        {
            if (pairs.Length % 2 != 0) throw new ArgumentException("JSON key/value pairs are incomplete.");
            var result = new Dictionary<string, object>();
            for (int i = 0; i < pairs.Length; i += 2) result.Add((string)pairs[i], pairs[i + 1]);
            return result;
        }

        // CLR-only round-trip smoke check; no Newtonsoft/System.Xml/System.Data
        // dependency, and no Unity serialization of nested sealed DTOs.
        public static void ValidateJsonRuntime()
        {
            var request = JsonRead.Object("{\"version\":1,\"command\":\"status\",\"values\":[true,7,1.25,null]}");
            var result = Map("version", 1, "ok", true, "request", request,
                "fields", Map("score", 0, "timer", 270f), "values", new object[] { "native", true, 4, 1.25f, null });
            var roundTrip = JsonRead.Object(JsonText.Serialize(result));
            var fields = (Dictionary<string, object>)roundTrip["fields"];
            var echoed = (Dictionary<string, object>)roundTrip["request"];
            if (JsonRead.Integer(fields, "score", -1) != 0 || JsonRead.Integer(fields, "timer", -1) != 270 ||
                JsonRead.Integer(echoed, "version", -1) != 1 || ((System.Collections.ArrayList)roundTrip["values"]).Count != 5)
                throw new InvalidOperationException("Bridge JSON runtime warmup did not round-trip its diagnostic data.");
        }

        // Compact authoritative server getters, sampled only on request. These
        // fields are observations; no score, clock, order or food field is set.
        internal static object CaptureNativeRound()
        {
            try
            {
                var flow = UnityEngine.Object.FindObjectOfType<ServerKitchenFlowControllerBase>();
                if (flow == null) return Map("available", false);
                var stateField = AccessTools.Field(flow.GetType(), "m_State");
                string gameState = stateField == null ? "unknown" : Convert.ToString(stateField.GetValue(flow));
                IServerRoundTimer timer = flow.RoundTimer;
                float? timeLimit = null, elapsed = null, remaining = null;
                bool? timerSuppressed = null;
                if (timer != null)
                {
                    var limitField = AccessTools.Field(timer.GetType(), "m_timeLimit");
                    if (limitField != null) timeLimit = Convert.ToSingle(limitField.GetValue(timer));
                    elapsed = timer.TimeElapsed; timerSuppressed = timer.IsSuppressed;
                    if (timeLimit.HasValue) remaining = Math.Max(0f, timeLimit.Value - elapsed.Value);
                }
                var monitor = flow.GetMonitorForTeam(TeamID.One);
                object ledger = null, recipeRandom = null;
                float? configuredDuration = null;
                var orders = new List<object>();
                if (monitor != null)
                {
                    TeamMonitor.TeamScoreStats score = monitor.Score;
                    ledger = Map("total", score.GetTotalScore(), "baseScore", score.TotalBaseScore,
                        "tips", score.TotalTipsScore, "multiplier", score.TotalMultiplier, "combo", score.TotalCombo,
                        "deliveries", score.TotalSuccessfulDeliveries, "deductions", score.TotalTimeExpireDeductions);
                    var controller = monitor.OrdersController;
                    if (controller != null)
                    {
                        var roundData = controller.GetRoundData();
                        if (roundData != null) configuredDuration = roundData.m_roundTimer;
                        var wrapper = roundData as WarpableRoundData;
                        var instance = controller.GetRoundInstanceData();
                        if (wrapper != null && instance != null) recipeRandom = wrapper.GetDiagnostics(instance);
                        var activeField = AccessTools.Field(controller.GetType(), "m_activeOrders");
                        var active = activeField == null ? null : activeField.GetValue(controller) as List<ServerOrderData>;
                        if (active != null) foreach (var order in active)
                        {
                            var entry = order.RecipeListEntry;
                            orders.Add(Map("id", order.ID.m_id, "remaining", order.Remaining, "lifetime", order.Lifetime,
                                "recipeId", entry != null && entry.m_order != null ? (int?)entry.m_order.m_uID : null,
                                "recipe", entry != null && entry.m_order != null ? entry.m_order.name : null,
                                "baseValue", entry == null ? (int?)null : entry.m_scoreForMeal));
                        }
                    }
                }
                return Map("available", true, "gameState", gameState, "configuredDuration", configuredDuration,
                    "timeLimit", timeLimit, "elapsed", elapsed, "remaining", remaining,
                    "timerSuppressed", timerSuppressed, "ledger", ledger, "orders", orders, "recipeRandom", recipeRandom);
            }
            catch (Exception ex) { return Map("available", false, "diagnosticError", ex.ToString()); }
        }

        private static void RefreshChefIds()
        {
            var ids = new List<int>();
            var registry = EntitySerialisationRegistry.m_EntitiesList;
            for (int i = 0; i < registry.Count; i++)
            {
                GameObject item = registry._items[i].m_GameObject;
                if (item != null && item.GetComponent<PlayerIDProvider>() != null && item.GetComponent<PlayerControls>() != null)
                    ids.Add((int)registry._items[i].m_Header.m_uEntityID);
            }
            ids.Sort();
            bool changed = ids.Count != chefIds.Length;
            for (int i = 0; !changed && i < ids.Count; i++) changed = ids[i] != chefIds[i];
            if (!changed) return;
            chefIds = ids.ToArray(); neutral = MakeNeutral(chefIds);
            if (neutralFence && immediateInputConsumer != null) immediateInputConsumer(neutral);
        }

        private static InputData MakeNeutral(int[] ids)
        {
            var result = new InputData { Input = new Dictionary<int, OneInputData>() };
            foreach (int id in ids) result.Input[id] = new OneInputData
                { Pad = new PadDirection(), Pickup = new ButtonInput(), Interact = new ButtonInput(), Dash = new ButtonInput() };
            return result;
        }

        private static void RequireMainThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThread)
                throw new InvalidOperationException("Native session bridge operations require Unity's main thread.");
        }

        private static void ServerLoop()
        {
            while (running)
            {
                TcpClient client = null; TransportConnection owner = null;
                try
                {
                    client = listener.AcceptTcpClient(); client.NoDelay = true;
                    lock (socketGate) currentClient = client;
                    owner = queue.Open();
                    NetworkStream stream = client.GetStream();
                    while (running)
                    {
                        int length = BitConverter.ToInt32(ReadExactly(stream, 4), 0);
                        if (length < 2 || length > 1024 * 1024) throw new IOException("Invalid bridge request length.");
                        Envelope e = queue.Enqueue(owner, Encoding.UTF8.GetString(ReadExactly(stream, length)));
                        while (running && !e.complete.WaitOne(100))
                            if (client.Client.Poll(0, SelectMode.SelectRead) && client.Client.Available == 0)
                                throw new IOException("Bridge controller disconnected.");
                        if (!running) break;
                        byte[] data = Encoding.UTF8.GetBytes(e.result);
                        byte[] prefix = BitConverter.GetBytes(data.Length);
                        stream.Write(prefix, 0, 4); stream.Write(data, 0, data.Length); stream.Flush();
                        e.complete.Close();
                    }
                }
                catch (Exception ex) { if (running) Console.WriteLine("[Supercharged bridge] " + ex.Message); }
                finally
                {
                    queue.Disconnect(owner);
                    lock (socketGate) { if (object.ReferenceEquals(currentClient, client)) currentClient = null; }
                    if (client != null) client.Close();
                }
            }
        }

        private static byte[] ReadExactly(Stream stream, int count)
        {
            byte[] data = new byte[count]; int offset = 0;
            while (offset < count)
            {
                int read = stream.Read(data, offset, count - offset);
                if (read == 0) throw new EndOfStreamException();
                offset += read;
            }
            return data;
        }

        public static void Destroy()
        {
            RequireMainThread();
            ForceNeutral("bridge-destroy"); running = false;
            if (listener != null) listener.Stop();
            lock (socketGate) { if (currentClient != null) currentClient.Close(); }
            if (server != null && server.IsAlive) server.Join(500);
            // Save-path protection deliberately remains until process exit.
            // Removing it during teardown could redirect a final save to originals.
        }
    }
}
