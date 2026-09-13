using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using OrderController;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oc2Tas
{
    // Public fields deliberately match Unity's JsonUtility serialization rules.
    [Serializable]
    public sealed class WorldState
    {
        public long frame, fixedFrame;
        public long gameplayFrame = -1, gameplayFixedFrame = -1, levelFrameZero = -1, levelFixedFrameZero = -1, introFrameZero = -1;
        public int physicsStepsThisFrame, framesSinceNoPhysics = -1, levelStartPhysicsPhase = -1;
        public float levelClientTimeZero;
        public bool alignStartPhysics, applicationFocused;
        public int screenWidth,screenHeight,targetFrameRate,vSyncCount,captureFramerate;
        public float unityDeltaTime,unityMaximumDeltaTime;
        public int startAlignmentWaitFrames;
        public long startAlignmentReleaseFrame = -1;
        public string scene, gameState;
        public float timer = -1f, logicalTime, clientTime, clientDeltaTime, fixedDeltaTime;
        public int score, baseScore, tips, multiplier, combo, delivered, deductions;
        public bool inLevel, paused, timerSuppressed, levelReady, serverRoundActive, clientRoundActive;
        public string randomState;
        public InstrumentationState instrumentation;
        public EntityRegistrationAuditState entityRegistration;
        public LoadedRoundDurationState roundDuration;
        public ChefState[] chefs = new ChefState[0];
        public EntityState[] entities = new EntityState[0];
        public OrderState[] orders = new OrderState[0];
        public RecipeDrawState[] recipeDraws = new RecipeDrawState[0];
        public RecipeDrawState[] currentRecipeDraws = new RecipeDrawState[0];
        public int currentRecipeRoundInstanceOrdinal = -1;
        public LifecycleMarker[] lifecycle = new LifecycleMarker[0];
        public GameEventState[] gameEvents = new GameEventState[0];
        public bool gameEventsInstalled;
        public int gameEventsDropped;
        public string gameEventsError;
        public string[] warnings = new string[0];
    }

    [Serializable]
    public sealed class ChefState
    {
        public int entityId, playerId;
        public string name;
        public Vector3 position, velocity, forward, lastVelocity, lastMoveInputDirection, impactVelocity;
        public int heldEntityId, pickupTargetId, useTargetId, placementTargetId, interactingEntityId;
        public int clientPredictedInteractionId, serverInteractionId;
        public int trackedThrowableEntityId;
        public float catchAngleMax = -1f, catchDistance = -1f, throwForce = -1f, throwInclination = -1f;
        public float runSpeed = -1f, dashSpeed = -1f, dashDuration = -1f, dashCooldown = -1f, movementScale = -1f, maxSpeed = -1f, turnSpeed = -1f;
        public float surfaceSpeedMultiplier = 1f, surfaceSlippiness, surfaceSlidiness;
        public Vector3 groundNormal, surfaceVelocity, windVelocity;
        public float dashTimer, lastPickupTimestamp, impactTimer, impactStartTime, leftOverTime;
        public bool aimingThrow, inputSuppressed, respawning, canAcceptInput, directlyControlled, controlsEnabled;
        public bool useSuppressed;
    }

    [Serializable]
    public sealed class EntityState
    {
        public int id, layer, observedOrdinal;
        public string name, hierarchyPath, physicsHash;
        public Vector3 position, velocity, angularVelocity;
        public Quaternion rotation;
        public bool active, hasRigidbody, kinematic, sleeping;
        public string[] components = new string[0];
        public ColliderState[] colliders = new ColliderState[0];
        public int attachedEntityId;
        // -1 means this component is absent, rather than zero progress.
        public float cookingProgress = -1f, cookingTime = -1f, mixingProgress = -1f, mixingTime = -1f, workProgress = -1f;
        public int cookingTypeId = -1, mixingTypeId = -1;
        public int workStage, workSubStage;
        public int[] ingredientIds = new int[0];
        public FoodState[] contents = new FoodState[0];
        public FoodState composition;
        public string spawnPrefab;
        public int switchIndex = -1;
        public float cannonAngle, washingProgress = -1f;
        public int cannonLoadedEntityId, plateCount;
        public int washingOutputEntityId, plateStackEntityId;
        public float washingTime = -1f;
        public string plateStackKind;
        public bool throwFlying;
        public float throwFlightTime = -1f;
        public int throwerEntityId, previousThrowerEntityId;
        public bool cannonFlying, cannonReady;
        public string cannonState;
        public int cannonButtonEntityId, controlTargetId;
        public float cannonStartAngle, cannonMinAngle, cannonMaxAngle;
        public float cannonMinRelativeAngle, cannonMaxRelativeAngle;
        public Vector3 cannonTarget, cannonAttachPoint, cannonExitPoint;
        public int portalDestinationId, portalSenderCount, portalReceiverCount;
        public Vector3 portalTeleportPoint;
        public float portalReceiveDelay, portalCooldownTime, portalArc;
        public bool portalTeleporting, portalReceiving;
    }

    [Serializable]
    public sealed class ColliderState
    {
        public string type, hierarchyPath;
        public int layer;
        public bool enabled, trigger, active;
        public Vector3 center, size;
    }

    [Serializable]
    public sealed class FoodState
    {
        public string type, name, state;
        public int id;
        public int cookingStepId = -1, mixingStepId = -1;
        public float progress = -1f;
        public FoodState[] children = new FoodState[0];
    }

    [Serializable]
    public sealed class OrderState
    {
        public int id, recipeId, baseValue;
        public string recipe;
        public int[] ingredientIds = new int[0];
        public FoodState food;
        public float remaining, lifetime;
    }

    public static class Telemetry
    {
        private static readonly Dictionary<int, int> Ordinals = new Dictionary<int, int>();
        private static int nextOrdinal;
        private static string lastScene;

        public static void Reset()
        {
            Ordinals.Clear();
            nextOrdinal = 0;
            lastScene = null;
        }

        // Call on Unity's main thread after the native frame has completed.
        // This method never advances game logic, synchronizes entities, or consumes inputs.
        public static WorldState Capture()
        {
            WorldState world = new WorldState();
            List<string> warnings = new List<string>();
            world.frame = NativeTime.Frame;
            world.fixedFrame = NativeTime.FixedFrame;
            world.gameplayFrame = NativeTime.GameplayFrame;
            world.gameplayFixedFrame = NativeTime.GameplayFixedFrame;
            world.levelFrameZero = NativeTime.LevelFrameZero;
            world.levelFixedFrameZero = NativeTime.LevelFixedFrameZero;
            world.introFrameZero = NativeTime.IntroFrameZero;
            world.physicsStepsThisFrame = NativeTime.PhysicsStepsThisFrame;
            world.framesSinceNoPhysics = NativeTime.FramesSinceNoPhysics;
            world.levelStartPhysicsPhase = NativeTime.LevelStartPhysicsPhase;
            world.levelClientTimeZero = NativeTime.LevelClientTimeZero;
            world.alignStartPhysics = NativeTime.AlignStartPhysics;
            world.startAlignmentWaitFrames = NativeTime.StartAlignmentWaitFrames;
            world.startAlignmentReleaseFrame = NativeTime.StartAlignmentReleaseFrame;
            world.applicationFocused = Application.isFocused;
            world.screenWidth=Screen.width;world.screenHeight=Screen.height;
            world.targetFrameRate=Application.targetFrameRate;world.vSyncCount=QualitySettings.vSyncCount;
            world.captureFramerate=Time.captureFramerate;world.unityDeltaTime=Time.deltaTime;
            world.unityMaximumDeltaTime=Time.maximumDeltaTime;
            world.levelReady = NativeTime.LevelReady;
            world.serverRoundActive = NativeTime.ServerRoundActive;
            world.clientRoundActive = NativeTime.ClientRoundActive;
            world.lifecycle = NativeTime.GetLifecycleMarkers();
            world.gameEvents = GameEvents.Capture();
            world.gameEventsInstalled = GameEvents.Installed;
            world.gameEventsDropped = GameEvents.DroppedEventCount;
            world.gameEventsError = GameEvents.LastError;
            world.logicalTime = (float)(NativeTime.Frame / 60.0);
            world.fixedDeltaTime = Time.fixedDeltaTime;
            world.clientTime = ClientTime.Time();
            world.clientDeltaTime = ClientTime.DeltaTime();
            world.randomState = NativeTime.RandomStateFingerprint(UnityEngine.Random.state);
            world.instrumentation = InstrumentationAudit.Capture();
            world.entityRegistration = EntityRegistrationAudit.Capture();
            world.roundDuration = EntityRegistrationAudit.CaptureRoundDuration();
            world.recipeDraws = NativeTime.GetRecipeDraws();
            world.currentRecipeDraws = NativeTime.GetCurrentRecipeDraws();
            world.currentRecipeRoundInstanceOrdinal = NativeTime.CurrentRecipeRoundInstanceOrdinal;
            world.scene = SceneManager.GetActiveScene().name;
            world.paused = TimeManager.IsPaused(TimeManager.PauseLayer.Main);
            if (lastScene != world.scene)
            {
                Ordinals.Clear();
                nextOrdinal = 0;
                lastScene = world.scene;
            }

            List<EntityState> entities = new List<EntityState>();
            List<ChefState> chefs = new List<ChefState>();
            List<uint> ids = new List<uint>(EntitySerialisationRegistry.m_Entities.Keys);
            ids.Sort();
            foreach (uint id in ids)
            {
                EntitySerialisationEntry entry;
                if (!EntitySerialisationRegistry.m_Entities.TryGetValue(id, out entry) || entry == null || entry.m_GameObject == null)
                    continue;
                GameObject obj = entry.m_GameObject;
                try
                {
                    entities.Add(CaptureEntity((int)id, obj));
                    PlayerIDProvider provider = obj.GetComponent<PlayerIDProvider>();
                    if (provider != null && obj.GetComponent<PlayerControls>() != null)
                        chefs.Add(CaptureChef((int)id, obj, provider));
                }
                catch (Exception ex)
                {
                    warnings.Add("entity " + id + ": " + ex.GetType().Name);
                }
            }
            chefs.Sort(delegate(ChefState a, ChefState b) { return a.playerId.CompareTo(b.playerId); });
            world.entities = entities.ToArray();
            world.chefs = chefs.ToArray();
            try { CaptureKitchen(world); }
            catch (Exception ex) { warnings.Add("kitchen: " + ex.GetType().Name); }
            world.warnings = warnings.ToArray();
            return world;
        }

        private static EntityState CaptureEntity(int id, GameObject obj)
        {
            EntityState result = new EntityState();
            result.id = id;
            result.name = obj.name;
            result.hierarchyPath = HierarchyPath(obj.transform);
            result.layer = obj.layer;
            result.active = obj.activeInHierarchy;
            int instance = obj.GetInstanceID();
            if (!Ordinals.TryGetValue(instance, out result.observedOrdinal))
            {
                result.observedOrdinal = nextOrdinal++;
                Ordinals[instance] = result.observedOrdinal;
            }
            result.position = obj.transform.position;
            result.rotation = obj.transform.rotation;
            Rigidbody body = obj.GetComponent<Rigidbody>();
            if (body != null)
            {
                result.hasRigidbody = true;
                result.velocity = body.velocity;
                result.angularVelocity = body.angularVelocity;
                result.kinematic = body.isKinematic;
                result.sleeping = body.IsSleeping();
            }
            result.physicsHash = PhysicsFingerprint(result);
            Component[] components = obj.GetComponents<Component>();
            List<string> names = new List<string>();
            foreach (Component component in components)
                if (component != null) names.Add(component.GetType().FullName);
            names.Sort(StringComparer.Ordinal);
            result.components = names.ToArray();
            Collider[] colliders = obj.GetComponentsInChildren<Collider>(true);
            result.colliders = new ColliderState[colliders.Length];
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider c = colliders[i];
                result.colliders[i] = new ColliderState { type = c.GetType().Name, enabled = c.enabled,
                    trigger = c.isTrigger, active = c.gameObject.activeInHierarchy, layer = c.gameObject.layer,
                    hierarchyPath = HierarchyPath(c.transform), center = c.bounds.center, size = c.bounds.size };
            }
            ServerAttachStation attach = obj.GetComponent<ServerAttachStation>();
            if (attach != null) result.attachedEntityId = Id(attach.InspectItem());
            ServerCookingHandler cooking = obj.GetComponent<ServerCookingHandler>();
            if (cooking != null)
            {
                result.cookingProgress = cooking.GetCookingProgress();
                CookingHandler config = obj.GetComponent<CookingHandler>();
                if (config != null)
                {
                    result.cookingTime = config.m_cookingtime;
                    if (config.m_cookingType != null) result.cookingTypeId = config.m_cookingType.m_uID;
                }
            }
            ServerMixingHandler mixing = obj.GetComponent<ServerMixingHandler>();
            if (mixing != null)
            {
                result.mixingProgress = mixing.GetMixingProgress();
                MixingHandler config = obj.GetComponent<MixingHandler>();
                if (config != null)
                {
                    result.mixingTime = config.m_mixingTime;
                    if (config.m_mixingType != null) result.mixingTypeId = config.m_mixingType.m_uID;
                }
            }
            ServerWorkableItem workable = obj.GetComponent<ServerWorkableItem>();
            if (workable != null)
            {
                result.workStage = NativeFields.Int(workable, "m_progress");
                result.workSubStage = NativeFields.Int(workable, "m_subProgress");
                WorkableItem config = obj.GetComponent<WorkableItem>();
                result.workProgress = config == null ? -1f : (float)result.workStage / Math.Max(1, config.m_stages - 1);
            }
            ServerIngredientContainer container = obj.GetComponent<ServerIngredientContainer>();
            List<FoodState> contents = new List<FoodState>();
            List<int> ingredients = new List<int>();
            if (container != null)
            {
                for (int i = 0; i < container.GetContentsCount(); i++)
                    contents.Add(CaptureFood(container.GetContentsElement(i), ingredients, 0));
            }
            else
            {
                IngredientPropertiesComponent ingredient = obj.GetComponent<IngredientPropertiesComponent>();
                if (ingredient != null) contents.Add(CaptureFood(ingredient.GetOrderComposition(), ingredients, 0));
            }
            result.contents = contents.ToArray();
            result.ingredientIds = ingredients.ToArray();
            // Some containers have intrinsic food outside their added contents.
            // Read the same composition used by native preparation and delivery.
            IOrderDefinition definition=obj.GetComponent<ServerCookableContainer>();
            if(definition==null)definition=obj.GetComponent<ServerMixableContainer>();
            if(definition==null)definition=obj.GetComponent<ServerPreparationContainer>();
            if(definition==null)definition=obj.GetComponent<ServerPlate>();
            if(definition==null)definition=obj.GetComponent<IngredientPropertiesComponent>();
            if(definition!=null)
            {
                List<int> completeIngredients=new List<int>();
                result.composition=CaptureFood(definition.GetOrderComposition(),completeIngredients,0);
                result.ingredientIds=completeIngredients.ToArray();
            }
            PickupItemSpawner spawner = obj.GetComponent<PickupItemSpawner>();
            if (spawner != null && spawner.m_itemPrefab != null) result.spawnPrefab = spawner.m_itemPrefab.name;
            Component switcher = obj.GetComponent("ServerPlacementItemSwitcher");
            if (switcher == null) switcher = obj.GetComponent("ServerPickupItemSwitcher");
            if (switcher != null) result.switchIndex = NativeFields.Int(switcher, "m_currentItemPrefabIndex");
            Teleportal portal = obj.GetComponent<Teleportal>();
            if (portal != null)
            {
                result.portalDestinationId = Id(portal.m_exitPortal);
                result.portalTeleportPoint = portal.m_teleportPoint == null ? obj.transform.position : portal.m_teleportPoint.position;
                result.portalReceiveDelay = portal.m_receiveDelay;
                result.portalCooldownTime = portal.m_cooldownTime;
                result.portalArc = portal.m_teleportArc;
                ServerTeleportal serverPortal = obj.GetComponent<ServerTeleportal>();
                if (serverPortal != null)
                {
                    Array senders = NativeFields.Get(serverPortal, "m_senders") as Array;
                    Array receivers = NativeFields.Get(serverPortal, "m_selfReceivers") as Array;
                    result.portalSenderCount = senders == null ? 0 : senders.Length;
                    result.portalReceiverCount = receivers == null ? 0 : receivers.Length;
                    result.portalTeleporting = serverPortal.IsTeleporting();
                    result.portalReceiving = serverPortal.IsReceiving();
                }
            }
            ServerCannon cannon = obj.GetComponent<ServerCannon>();
            if (cannon != null)
            {
                Cannon config = obj.GetComponent<Cannon>();
                if (config != null)
                {
                    result.cannonButtonEntityId = Id(config.m_button);
                    if (config.m_target != null) result.cannonTarget = config.m_target.position;
                    if (config.m_attachPoint != null) result.cannonAttachPoint = config.m_attachPoint.position;
                    if (config.m_exitPoint != null) result.cannonExitPoint = config.m_exitPoint.position;
                }
                result.cannonLoadedEntityId = Id(NativeFields.Get(cannon, "m_loadedObject"));
                result.cannonFlying = NativeFields.Bool(cannon, "m_flying");
                result.cannonReady = NativeFields.Bool(cannon, "m_readyToLaunch");
                result.cannonState = Convert.ToString(NativeFields.Get(NativeFields.Get(cannon, "m_message"), "m_state"));
                PilotRotation pilot = NativeFields.Get(cannon, "m_pilotRotation") as PilotRotation;
                if (pilot != null)
                {
                    if (pilot.m_transformToRotate != null) result.cannonAngle = pilot.m_transformToRotate.eulerAngles.y;
                    ServerPilotRotation serverPilot = pilot.GetComponent<ServerPilotRotation>();
                    result.cannonStartAngle = NativeFields.Float(serverPilot, "m_startAngle");
                    result.cannonMinRelativeAngle = pilot.m_minLimitDegrees;
                    result.cannonMaxRelativeAngle = pilot.m_maxLimitDegrees;
                    result.cannonMinAngle = result.cannonStartAngle + pilot.m_minLimitDegrees;
                    result.cannonMaxAngle = result.cannonStartAngle + pilot.m_maxLimitDegrees;
                }
            }
            Terminal terminal = obj.GetComponent<Terminal>();
            if (terminal != null) result.controlTargetId = Id(terminal.m_pilotableObject);
            ServerWashingStation washing = obj.GetComponent<ServerWashingStation>();
            if (washing != null)
            {
                result.washingProgress = NativeFields.Float(washing, "m_cleaningTimer");
                result.plateCount = NativeFields.Int(washing, "m_plateCount");
                result.plateStackKind = "dirty-in-sink";
                WashingStation washingConfig = obj.GetComponent<WashingStation>();
                if (washingConfig != null)
                {
                    result.washingOutputEntityId = Id(washingConfig.m_dryingStation);
                    result.washingTime = washingConfig.m_cleanPlateTime;
                }
            }
            ServerPlateStackBase plateStack = obj.GetComponent<ServerPlateStackBase>();
            ServerPlateReturnStation plateReturn = obj.GetComponent<ServerPlateReturnStation>();
            if (plateReturn != null) plateStack = NativeFields.Get(plateReturn, "m_stack") as ServerPlateStackBase;
            if (plateStack != null)
            {
                result.plateStackEntityId = Id(plateStack);
                // Synchronisation may expose the shell before ServerStack has
                // been assigned. Reading its size is valid only after setup.
                if (NativeFields.Get(plateStack, "m_stack") != null) result.plateCount = plateStack.GetSize();
                result.plateStackKind = plateStack is ServerDirtyPlateStack ? "dirty" : plateStack is ServerCleanPlateStack ? "clean" : "unknown";
            }
            else if (obj.GetComponent<ServerPlate>() != null)
            {
                result.plateCount = 1;
                result.plateStackKind = "single-clean";
            }
            ServerThrowableItem throwable = obj.GetComponent<ServerThrowableItem>();
            if (throwable != null)
            {
                result.throwFlying = throwable.IsFlying();
                result.throwFlightTime = throwable.GetFlightTime();
                result.throwerEntityId = Id(throwable.GetThrower());
                result.previousThrowerEntityId = Id(throwable.GetPreviousThrower());
            }
            return result;
        }

        private static ChefState CaptureChef(int id, GameObject obj, PlayerIDProvider provider)
        {
            ChefState chef = new ChefState();
            chef.entityId = id;
            chef.playerId = (int)provider.GetID();
            chef.name = obj.name;
            chef.position = obj.transform.position;
            chef.forward = obj.transform.forward;
            ClientAttachmentCatcher catcher = obj.GetComponent<ClientAttachmentCatcher>();
            if (catcher != null) chef.trackedThrowableEntityId = Id(catcher.GetTrackedThrowable());
            AttachmentCatcher catchConfig = obj.GetComponent<AttachmentCatcher>();
            if (catchConfig != null) { chef.catchAngleMax = catchConfig.m_catchAngleMax; chef.catchDistance = catchConfig.m_catchDistance; }
            AttachmentThrower throwConfig = obj.GetComponent<AttachmentThrower>();
            if (throwConfig != null) { chef.throwForce = throwConfig.m_throwForce; chef.throwInclination = throwConfig.m_throwInclination; }
            Rigidbody body = obj.GetComponent<Rigidbody>();
            if (body != null) chef.velocity = body.velocity;
            PlayerControls controls = obj.GetComponent<PlayerControls>();
            PlayerControls.MovementData movement = controls.Movement;
            chef.runSpeed = movement.RunSpeed; chef.dashSpeed = movement.DashSpeed; chef.dashDuration = movement.DashTime;
            chef.dashCooldown = movement.DashCooldown; chef.maxSpeed = movement.MaxSpeed; chef.turnSpeed = movement.TurnSpeed;
            chef.movementScale = controls.MovementScale; chef.groundNormal = controls.GroundNormal;
            if (controls.PhysicsSurface != null && controls.PhysicsSurface.Properties != null)
            {
                chef.surfaceSpeedMultiplier = controls.PhysicsSurface.Properties.SpeedMultiplier;
                chef.surfaceSlippiness = controls.PhysicsSurface.Properties.Slippiness;
                chef.surfaceSlidiness = controls.PhysicsSurface.Properties.Slidiness;
            }
            if (controls.SurfaceMovable != null) chef.surfaceVelocity = controls.SurfaceMovable.GetVelocity();
            if (controls.WindReceiver != null) chef.windVelocity = controls.WindReceiver.GetVelocity();
            chef.canAcceptInput = controls.CanButtonBePressed();
            if (controls.ControlScheme != null) chef.useSuppressed = controls.ControlScheme.IsUseSuppressed();
            chef.directlyControlled = controls.GetDirectlyUnderPlayerControl();
            chef.controlsEnabled = controls.enabled;
            chef.respawning = controls.m_bRespawning;
            object nearby = controls.CurrentInteractionObjects;
            chef.pickupTargetId = Id(NativeFields.Get(nearby, "m_TheOriginalHandlePickup"));
            chef.useTargetId = Id(NativeFields.Get(nearby, "m_interactable"));
            chef.placementTargetId = Id(NativeFields.Get(nearby, "m_iHandlePlacement"));
            ClientPlayerControlsImpl_Default impl = obj.GetComponent<ClientPlayerControlsImpl_Default>();
            if (impl != null)
            {
                chef.clientPredictedInteractionId = Id(NativeFields.Get(impl, "m_predictedInteracted"));
                ICarrier carrier = NativeFields.Get(impl, "m_iCarrier") as ICarrier;
                if (carrier != null) chef.heldEntityId = Id(carrier.InspectCarriedItem());
                chef.interactingEntityId = Id(impl.GetCurrentlyInteracting());
                chef.dashTimer = NativeFields.Float(impl, "m_dashTimer");
                chef.lastPickupTimestamp = NativeFields.Float(impl, "m_lastPickupTimestamp");
                chef.aimingThrow = NativeFields.Bool(impl, "m_aimingThrow");
                chef.inputSuppressed = NativeFields.Bool(impl, "m_movementInputSuppressed");
                chef.lastVelocity = NativeFields.Vector(impl, "m_lastVelocity");
                chef.lastMoveInputDirection = NativeFields.Vector(impl, "m_lastMoveInputDirection");
                chef.impactVelocity = NativeFields.Vector(impl, "m_impactVelocity");
                chef.impactTimer = NativeFields.Float(impl, "m_impactTimer");
                chef.impactStartTime = NativeFields.Float(impl, "m_impactStartTime");
                chef.leftOverTime = NativeFields.Float(impl, "m_LeftOverTime");
            }
            ServerPlayerControlsImpl_Default serverImpl = obj.GetComponent<ServerPlayerControlsImpl_Default>();
            if (serverImpl != null) chef.serverInteractionId = Id(NativeFields.Get(serverImpl, "m_lastInteracted"));
            return chef;
        }

        private static void CaptureKitchen(WorldState world)
        {
            ServerKitchenFlowControllerBase flow = UnityEngine.Object.FindObjectOfType<ServerKitchenFlowControllerBase>();
            if (flow == null) { world.gameState = "NoKitchen"; return; }
            world.gameState = Convert.ToString(NativeFields.Get(flow, "m_State"));
            world.inLevel = world.gameState == "InLevel";
            IServerRoundTimer timer = flow.RoundTimer;
            if (timer != null)
            {
                object limit = NativeFields.Get(timer, "m_timeLimit");
                if (limit != null) world.timer = Math.Max(0f, Convert.ToSingle(limit) - timer.TimeElapsed);
                world.timerSuppressed = timer.IsSuppressed;
            }
            ServerTeamMonitor monitor = flow.GetMonitorForTeam(TeamID.One);
            if (monitor == null) return;
            TeamMonitor.TeamScoreStats score = monitor.Score;
            world.score = score.GetTotalScore();
            world.baseScore = score.TotalBaseScore;
            world.tips = score.TotalTipsScore;
            world.multiplier = score.TotalMultiplier;
            world.combo = score.TotalCombo;
            world.delivered = score.TotalSuccessfulDeliveries;
            world.deductions = score.TotalTimeExpireDeductions;
            List<ServerOrderData> active = NativeFields.Get(monitor.OrdersController, "m_activeOrders") as List<ServerOrderData>;
            if (active == null) return;
            List<OrderState> orders = new List<OrderState>();
            foreach (ServerOrderData order in active)
            {
                OrderState state = new OrderState();
                state.id = (int)order.ID.m_id;
                state.remaining = order.Remaining;
                state.lifetime = order.Lifetime;
                RecipeList.Entry entry = order.RecipeListEntry;
                if (entry != null && entry.m_order != null)
                {
                    state.recipe = entry.m_order.name;
                    state.recipeId = entry.m_order.m_uID;
                    state.baseValue = entry.m_scoreForMeal;
                    List<int> ingredients = new List<int>();
                    state.food = CaptureFood(entry.m_order.Convert(), ingredients, 0);
                    state.ingredientIds = ingredients.ToArray();
                }
                orders.Add(state);
            }
            world.orders = orders.ToArray();
        }

        private static FoodState CaptureFood(AssembledDefinitionNode node, List<int> ingredients, int depth)
        {
            FoodState food = new FoodState();
            if (node == null || depth > 20) { food.type = "null"; return food; }
            food.type = node.GetType().Name;
            CookedCompositeAssembledNode cooked = node as CookedCompositeAssembledNode;
            if (cooked != null && cooked.m_cookingStep != null) food.cookingStepId = cooked.m_cookingStep.m_uID;
            // This build's MixedCompositeAssembledNode has no mixing-step field.
            // Keep -1 for absent rather than inventing a recipe distinction.
            IngredientAssembledNode leaf = node as IngredientAssembledNode;
            if (leaf != null && leaf.m_ingriedientOrderNode != null)
            {
                food.id = leaf.m_ingriedientOrderNode.m_uID;
                food.name = leaf.m_ingriedientOrderNode.name;
                ingredients.Add(food.id);
            }
            object state = NativeFields.Get(node, "m_progress");
            if (state != null) food.state = state.ToString();
            object progress = NativeFields.Get(node, "m_recordedProgress");
            if (progress != null) food.progress = Convert.ToSingle(progress);
            CompositeAssembledNode composite = node as CompositeAssembledNode;
            if (composite != null)
            {
                List<FoodState> children = new List<FoodState>();
                if (composite.m_composition != null)
                    foreach (AssembledDefinitionNode child in composite.m_composition)
                        children.Add(CaptureFood(child, ingredients, depth + 1));
                food.children = children.ToArray();
            }
            return food;
        }

        private static int Id(object value)
        {
            GameObject obj = value as GameObject;
            Component component = value as Component;
            if (obj == null && component != null) obj = component.gameObject;
            return obj == null ? 0 : (int)EntitySerialisationRegistry.GetId(obj);
        }

        private static string HierarchyPath(Transform transform)
        {
            string result = transform.name;
            for (Transform parent = transform.parent; parent != null; parent = parent.parent)
                result = parent.name + "/" + result;
            return result;
        }

        private static string PhysicsFingerprint(EntityState entity)
        {
            // Raw IEEE754 bytes distinguish sub-JSON-precision physics drift.
            float[] values = { entity.position.x, entity.position.y, entity.position.z,
                entity.rotation.x, entity.rotation.y, entity.rotation.z, entity.rotation.w,
                entity.velocity.x, entity.velocity.y, entity.velocity.z,
                entity.angularVelocity.x, entity.angularVelocity.y, entity.angularVelocity.z };
            StringBuilder result = new StringBuilder(values.Length * 8);
            foreach (float value in values)
                result.Append(BitConverter.ToUInt32(BitConverter.GetBytes(value), 0).ToString("x8"));
            return result.ToString();
        }
    }

    internal static class NativeFields
    {
        private static readonly Dictionary<string, FieldInfo> Cache = new Dictionary<string, FieldInfo>();
        public static object Get(object target, string name)
        {
            if (target == null) return null;
            Type type = target as Type ?? target.GetType();
            string key = type.FullName + ":" + name;
            FieldInfo field;
            if (!Cache.TryGetValue(key, out field))
            {
                for (Type current = type; current != null && field == null; current = current.BaseType)
                    field = current.GetField(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                Cache[key] = field;
            }
            return field == null ? null : field.GetValue(target is Type ? null : target);
        }
        public static float Float(object target, string name) { object value = Get(target, name); return value == null ? 0f : Convert.ToSingle(value); }
        public static int Int(object target, string name) { object value = Get(target, name); return value == null ? 0 : Convert.ToInt32(value); }
        public static bool Bool(object target, string name) { object value = Get(target, name); return value != null && Convert.ToBoolean(value); }
        public static Vector3 Vector(object target, string name) { object value = Get(target, name); return value is Vector3 ? (Vector3)value : Vector3.zero; }
    }
}
