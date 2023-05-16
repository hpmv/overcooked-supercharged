using BepInEx.Harmony;
using BitStream;
using HarmonyLib;
using Hpmv;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Team17.Online.Multiplayer;
using Team17.Online.Multiplayer.Messaging;
using Team17.Online.Multiplayer.Connection;
using UnityEngine;

namespace SuperchargedPatch
{
    public static class ServerInterceptionPatches
    {

        public static void LateUpdate()
        {
            if (Helpers.IsPaused())
            {
                WarpHandler.HandleWarpRequestIfAny();
            }
            var data = Injector.Server.OpenCurrentFrameDataForWrite();
            if (!Helpers.IsPaused())
            {
                if (Injector.Server.CurrentInput.__isset.nextFrame)
                {
                    if (Injector.Server.CurrentInput.NextFrame <= frameNumber)
                    {
                        Console.WriteLine($"Restarting at frame {Injector.Server.CurrentInput.NextFrame}; was at frame {frameNumber}");
                        ClearCacheAfterWarp(Injector.Server.CurrentInput.NextFrame);
                    }
                    else
                    {
                        frameNumber = Injector.Server.CurrentInput.NextFrame;
                    }
                }
            }
            data.FrameNumber = frameNumber;
            CollectPhysicsDataForFrame(data);
            data.LastFramePaused = Helpers.IsPaused();
            if (Injector.Server.CurrentInput.RequestPause)
            {
                Helpers.Pause();
            } else if (Injector.Server.CurrentInput.RequestResume)
            {
                Helpers.Resume();
            }

            data.NextFramePaused = TimeManager.IsPaused(TimeManager.PauseLayer.Main);
            Injector.Server.CommitFrameIfNotCommitted();
        }

        private static readonly Dictionary<int, Vector3> prevCachedLocation = new Dictionary<int, Vector3>();
        private static readonly Dictionary<int, UnityEngine.Quaternion> prevCachedRotation = new Dictionary<int, UnityEngine.Quaternion>();
        private static readonly Dictionary<int, Vector3> prevCachedVelocity = new Dictionary<int, Vector3>();
        private static readonly Dictionary<int, Vector3> prevCachedAngularVelocity = new Dictionary<int, Vector3>();
        private static readonly Dictionary<int, EntityPathReference> prevCachedEntityPathReference = new Dictionary<int, EntityPathReference>();
        private static int frameNumber = 0;

        public static bool EnableInputInjection = true;

        public static void ClearCacheAfterWarp(int frame)
        {
            frameNumber = frame;
            prevCachedLocation.Clear();
            prevCachedRotation.Clear();
            prevCachedVelocity.Clear();
            prevCachedAngularVelocity.Clear();
            prevCachedEntityPathReference.Clear();
        }

        public static void CollectPhysicsDataForFrame(OutputData currentFrameData)
        {
            // Debug.Log("DEBUG 1");
            if (frameNumber < 0)
            {
                return;
            }
            // Debug.Log("DEBUG 3");
            if (currentFrameData.Items == null)
            {
                currentFrameData.Items = new Dictionary<int, ItemData>();
            }
            // Debug.Log("DEBUG 4");
            FastList<EntitySerialisationEntry> mEntitiesList = EntitySerialisationRegistry.m_EntitiesList;
            // Debug.Log("DEBUG 5");
            for (int i = 0; i < mEntitiesList.Count; i++)
            {
                // Debug.Log("DEBUG 6");
                var t = mEntitiesList._items[i];

                // Debug.Log("DEBUG 7");
                Vector3 position = t.m_GameObject.transform.position;
                // Debug.Log("DEBUG 8");
                UnityEngine.Quaternion rotation = t.m_GameObject.transform.rotation;
                Vector3 velocity = Vector3.zero;
                Vector3 angularVelocity = Vector3.zero;
                if (t.m_GameObject.GetPhysicsContainerIfExists() is Rigidbody container) {
                    position = container.transform.position;
                    rotation = container.transform.rotation;
                    velocity = container.velocity;
                    angularVelocity = container.angularVelocity;
                    // TODO: isKinematic, etc?
                }
                // Debug.Log("DEBUG 9");
                EntityPathReference entityPathReference = null;
                if (t.m_GameObject.GetComponent<EntityPathReferenceMarker>() is EntityPathReferenceMarker marker)
                {
                    entityPathReference = marker.EntityPath;
                }
                // Debug.Log("DEBUG 10");
                int mUEntityID = (int)t.m_Header.m_uEntityID;
                Dictionary<int, ItemData> items = currentFrameData.Items;
                if (!prevCachedLocation.ContainsKey(mUEntityID) || prevCachedLocation[mUEntityID] != position)
                {
                    prevCachedLocation[mUEntityID] = position;
                    if (!items.ContainsKey(mUEntityID))
                    {
                        items[mUEntityID] = new ItemData();
                    }
                    items[mUEntityID].Pos = position.ToThrift();
                }
                // Debug.Log("DEBUG 11");
                if (!prevCachedRotation.ContainsKey(mUEntityID) || prevCachedRotation[mUEntityID] != rotation)
                {
                    prevCachedRotation[mUEntityID] = rotation;
                    if (!items.ContainsKey(mUEntityID))
                    {
                        items[mUEntityID] = new ItemData();
                    }
                    items[mUEntityID].Rotation = rotation.ToThrift();
                }
                // Debug.Log("DEBUG 12");
                if (!prevCachedVelocity.ContainsKey(mUEntityID) || prevCachedVelocity[mUEntityID] != velocity)
                {
                    prevCachedVelocity[mUEntityID] = velocity;
                    if (!items.ContainsKey(mUEntityID))
                    {
                        items[mUEntityID] = new ItemData();
                    }
                    items[mUEntityID].Velocity = velocity.ToThrift();
                }
                // Debug.Log("DEBUG 13");
                if (!prevCachedAngularVelocity.ContainsKey(mUEntityID) || prevCachedAngularVelocity[mUEntityID] != angularVelocity)
                {
                    prevCachedAngularVelocity[mUEntityID] = angularVelocity;
                    if (!items.ContainsKey(mUEntityID))
                    {
                        items[mUEntityID] = new ItemData();
                    }
                    items[mUEntityID].AngularVelocity = angularVelocity.ToThrift();
                }
                // Debug.Log("DEBUG 14");
                if (!prevCachedEntityPathReference.ContainsKey(mUEntityID) ||  prevCachedEntityPathReference[mUEntityID] != entityPathReference)
                {
                    prevCachedEntityPathReference[mUEntityID] = entityPathReference;
                    if (!items.ContainsKey(mUEntityID))
                    {
                        items[mUEntityID] = new ItemData();
                    }
                    items[mUEntityID].EntityPathReference = entityPathReference?.ToThrift();
                }
                // Debug.Log("DEBUG 15");
            }
            // Debug.Log("DEBUG 16");
        }

        private static Type t_ClientMessenger = typeof(ClientTime).Assembly.GetType("ClientMessenger");
        private static MethodInfo f_ClientMessenger_ChefEventMessageGameObject = t_ClientMessenger.GetMethod(
            "ChefEventMessage", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, Type.DefaultBinder,
            new Type[] { typeof(ChefEventMessage.ChefEventType), typeof(GameObject), typeof(GameObject) },
            null);
        private static MethodInfo f_ClientMessenger_ChefEventMessageMonoBehavior = t_ClientMessenger.GetMethod(
            "ChefEventMessage", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, Type.DefaultBinder,
            new Type[] { typeof(ChefEventMessage.ChefEventType), typeof(GameObject), typeof(MonoBehaviour) },
            null);

        [HarmonyPatch(typeof(ClientPlayerControlsImpl_Default), "Update_Carry")]
        public static class Update_CarryPatch
        {
            [HarmonyPrefix]
            public static bool Prefix(ref PlayerControls ___m_controls, ref ICarrier ___m_iCarrier, ref float ___m_lastPickupTimestamp, ClientPlayerControlsImpl_Default __instance)
            {
                if (!EnableInputInjection)
                {
                    return true;
                }
                int id = (int)EntitySerialisationRegistry.GetId(___m_controls.gameObject);
                bool overridden = (Injector.Server.CurrentInput.Input == null || !Injector.Server.CurrentInput.Input.ContainsKey(id) ? false : Injector.Server.CurrentInput.Input[id].PickDown);
                if (___m_controls.ControlScheme.m_pickupButton.JustPressed() | overridden)
                {
                    IClientHandlePickup mIHandlePickup = ___m_controls.CurrentInteractionObjects.m_iHandlePickup;
                    if (___m_iCarrier.InspectCarriedItem() != null)
                    {
                        PlayerControlsHelper.PlaceHeldItem_Client(___m_controls);

                    }
                    else if (mIHandlePickup != null && mIHandlePickup.CanHandlePickup(___m_iCarrier))
                    {
                        float single = ClientTime.Time();
                        if (single >= ___m_lastPickupTimestamp)
                        {
                            f_ClientMessenger_ChefEventMessageGameObject.Invoke(null, new object[] { ChefEventMessage.ChefEventType.PickUp, __instance.gameObject, ___m_controls.CurrentInteractionObjects.m_TheOriginalHandlePickup });
                            ___m_lastPickupTimestamp = single + ___m_controls.m_pickupDelay;

                        }
                    }
                    else if (___m_controls.CurrentInteractionObjects.m_interactable != null && ___m_controls.CurrentInteractionObjects.m_interactable.UsePlacementButton)
                    {
                        f_ClientMessenger_ChefEventMessageMonoBehavior.Invoke(null, new object[] { ChefEventMessage.ChefEventType.TriggerInteract, __instance.gameObject, ___m_controls.CurrentInteractionObjects.m_interactable });
                    }
                }
                return false;
            }
        }

        private static MethodInfo f_Update_Carry = typeof(ClientPlayerControlsImpl_Default).GetMethod("Update_Carry", BindingFlags.NonPublic | BindingFlags.Instance);
        private static MethodInfo f_Update_Interact = typeof(ClientPlayerControlsImpl_Default).GetMethod("Update_Interact", BindingFlags.NonPublic | BindingFlags.Instance);
        private static MethodInfo f_Update_Throw = typeof(ClientPlayerControlsImpl_Default).GetMethod("Update_Throw", BindingFlags.NonPublic | BindingFlags.Instance);
        private static MethodInfo f_Update_Aim = typeof(ClientPlayerControlsImpl_Default).GetMethod("Update_Aim", BindingFlags.NonPublic | BindingFlags.Instance);
        private static MethodInfo f_Update_Movement = typeof(ClientPlayerControlsImpl_Default).GetMethod("Update_Movement", BindingFlags.NonPublic | BindingFlags.Instance);
        private static MethodInfo f_Update_Falling = typeof(ClientPlayerControlsImpl_Default).GetMethod("Update_Falling", BindingFlags.NonPublic | BindingFlags.Instance);
        private static MethodInfo f_Update_Rotation = typeof(ClientPlayerControlsImpl_Default).GetMethod("Update_Rotation", BindingFlags.NonPublic | BindingFlags.Instance);
        private static MethodInfo f_Update_MovementSuppression = typeof(ClientPlayerControlsImpl_Default).GetMethod("Update_MovementSuppression", BindingFlags.NonPublic | BindingFlags.Instance);
        private static MethodInfo f_ApplyGravityForce = typeof(ClientPlayerControlsImpl_Default).GetMethod("ApplyGravityForce", BindingFlags.NonPublic | BindingFlags.Instance);
        private static MethodInfo f_ApplyWindForce = typeof(ClientPlayerControlsImpl_Default).GetMethod("ApplyWindForce", BindingFlags.NonPublic | BindingFlags.Instance);
        private static MethodInfo f_ProgressVelocityWrtFriction = typeof(ClientPlayerControlsImpl_Default).GetMethod("ProgressVelocityWrtFriction", BindingFlags.NonPublic | BindingFlags.Instance);
        private static MethodInfo f_GetSurfaceData = typeof(ClientPlayerControlsImpl_Default).GetMethod("GetSurfaceData", BindingFlags.NonPublic | BindingFlags.Instance);
        private static MethodInfo f_ApplyGroundMovement = typeof(ClientPlayerControlsImpl_Default).GetMethod("ApplyGroundMovement", BindingFlags.NonPublic | BindingFlags.Instance);
        private static MethodInfo f_DoDash = typeof(ClientPlayerControlsImpl_Default).GetMethod("DoDash", BindingFlags.NonPublic | BindingFlags.Instance);
        private static MethodInfo f_OnDashCollision = typeof(ClientPlayerControlsImpl_Default).GetMethod("OnDashCollision", BindingFlags.NonPublic | BindingFlags.Instance);

        [HarmonyPatch(typeof(ClientPlayerControlsImpl_Default), "Update_Impl")]
        public static class Update_ImplPatch
        {
            public static bool Prefix(
                ClientPlayerControlsImpl_Default __instance,
                ref PlayerControls ___m_controls,
                ref PlayerIDProvider ___m_playerIDProvider,
                ref PlayerControls.ControlSchemeData ___m_controlScheme
                )
            {
                if (!EnableInputInjection)
                {
                    return true;
                }
                float deltaTime = TimeManager.GetDeltaTime(__instance.gameObject);
                bool flag = ___m_playerIDProvider.IsLocallyControlled();
                if (TimeManager.IsPaused(TimeManager.PauseLayer.Network) & flag)
                {
                    if (___m_controlScheme != null)
                    {
                        f_Update_Movement.Invoke(__instance, new object[] { deltaTime, true });
                    }
                    return false;
                }
                if (TimeManager.IsPaused(TimeManager.PauseLayer.Main) & flag)
                {
                    return false;
                }
                if (___m_controlScheme != null)
                {
                    int id = (int)EntitySerialisationRegistry.GetId(___m_controls.gameObject);
                    bool flag1 = (Injector.Server.CurrentInput.Input == null || !Injector.Server.CurrentInput.Input.ContainsKey(id) ? false : Injector.Server.CurrentInput.Input[id].ChopDown);
                    bool flag2 = (Injector.Server.CurrentInput.Input == null || !Injector.Server.CurrentInput.Input.ContainsKey(id) ? false : Injector.Server.CurrentInput.Input[id].ThrowDown);
                    bool flag3 = (Injector.Server.CurrentInput.Input == null || !Injector.Server.CurrentInput.Input.ContainsKey(id) ? false : Injector.Server.CurrentInput.Input[id].ThrowUp);
                    bool flag4 = ___m_controlScheme.IsUseSuppressed();
                    bool flag5 = ___m_controlScheme.IsUseDown() | flag1;
                    bool flag6 = ___m_controlScheme.IsUseJustPressed() | flag2;
                    bool flag7 = ___m_controlScheme.IsUseJustReleased() | flag3;
                    if (flag)
                    {
                        var currentFrameData = Injector.Server.OpenCurrentFrameDataForWrite();
                        ___m_controls.UpdateNearbyObjects();
                        if (currentFrameData.CharPos == null)
                        {
                            currentFrameData.CharPos = new Dictionary<int, CharPositionData>();
                        }
                        if (!currentFrameData.CharPos.ContainsKey(id))
                        {
                            currentFrameData.CharPos[id] = new CharPositionData();
                        }
                        currentFrameData.CharPos[id].HighlightedForPickup = (___m_controls.CurrentInteractionObjects.m_TheOriginalHandlePickup != null ? (int)EntitySerialisationRegistry.GetId(___m_controls.CurrentInteractionObjects.m_TheOriginalHandlePickup) : -1);
                        currentFrameData.CharPos[id].HighlightedForUse = (___m_controls.CurrentInteractionObjects.m_interactable != null ? (int)EntitySerialisationRegistry.GetId(___m_controls.CurrentInteractionObjects.m_interactable.gameObject) : -1);
                        currentFrameData.CharPos[id].HighlightedForPlacement = (___m_controls.CurrentInteractionObjects.m_iHandlePlacement is Component ? (int)EntitySerialisationRegistry.GetId(((Component)___m_controls.CurrentInteractionObjects.m_iHandlePlacement).gameObject) : -1);
                        f_Update_Carry.Invoke(__instance, new object[0]);
                        f_Update_Interact.Invoke(__instance, new object[] { deltaTime, flag5, flag6 });
                        f_Update_Throw.Invoke(__instance, new object[] { deltaTime, flag5, flag7, flag4 });
                    }
                    f_Update_Aim.Invoke(__instance, new object[] { deltaTime, flag5 });
                    f_Update_Movement.Invoke(__instance, new object[] { deltaTime, false });
                }
                f_Update_Falling.Invoke(__instance, new object[] { deltaTime });
                return false;
            }
        }

        [HarmonyPatch(typeof(ClientPlayerControlsImpl_Default), "Update_Movement")]

        public static class UpdateMovementPatch
        {
            public static bool Prefix(
                float _deltaTime,
                bool _netPaused,
                ClientPlayerControlsImpl_Default __instance,
                ref float ___m_LeftOverTime,
                ref PlayerControlsHelper.ControlAxisData ___m_controlAxisData,
                ref PlayerControls ___m_controls,
                ref bool ___m_movementInputSuppressed,
                ref float ___m_dashTimer,
                ref GameObject ___m_controlObject,
                ref float ___m_impactTimer,
                ref float ___m_impactStartTime,
                ref Vector3 ___m_lastVelocity,
                ref Vector3 ___m_impactVelocity,
                ref PlayerControls.ControlSchemeData ___m_controlScheme,
                ref PlayerControlsImpl_Default ___m_controlsImpl,
                ref float ___m_attemptedDistanceCounter,
                ref CollisionRecorder ___m_collisionRecorder)
            {
                if (!EnableInputInjection)
                {
                    return true;
                }
                ___m_LeftOverTime += _deltaTime;
                _deltaTime = 0.0166666675f;
                Vector3 _zero = Vector3.zero;
                PlayerControlsHelper.BuildControlAxisData(___m_controls, ref ___m_controlAxisData);
                float value = ___m_controlAxisData.MoveX.GetValue();
                float y = ___m_controlAxisData.MoveY.GetValue();
                bool dashDown = false;
                int id = (int)EntitySerialisationRegistry.GetId(___m_controls.gameObject);
                Dictionary<int, OneInputData> input = Injector.Server.CurrentInput.Input;
                if (input != null && input.ContainsKey(id))
                {
                    OneInputData item = input[id];
                    if (item.Pad != null)
                    {
                        value += (float)item.Pad.X;
                        y += (float)item.Pad.Y;
                    }
                    dashDown = item.DashDown;
                }
                if (!_netPaused)
                {
                    _zero = PlayerControlsHelper.GetControlAxis(value, y, ___m_controlAxisData.XAxisAllignment, ___m_controlAxisData.YAxisAllignment);
                }
                while (___m_LeftOverTime >= 0.0166666675f)
                {
                    ___m_LeftOverTime -= 0.0166666675f;
                    f_Update_Rotation.Invoke(__instance, new object[] { _deltaTime, value, y, ___m_controlAxisData.XAxisAllignment, ___m_controlAxisData.YAxisAllignment });
                    f_Update_MovementSuppression.Invoke(__instance, new object[] { _deltaTime, _zero });
                    f_ApplyGravityForce.Invoke(__instance, new object[0]);
                    f_ApplyWindForce.Invoke(__instance, new object[] { _deltaTime });
                    PlayerControls.MovementData movement = ___m_controls.Movement;
                    float movementScale = ___m_controls.MovementScale;
                    Vector3 runSpeed = (movementScale * _zero) * movement.RunSpeed;
                    if (___m_movementInputSuppressed)
                    {
                        runSpeed = Vector3.zero;
                    }
                    if (___m_dashTimer > 0f)
                    {
                        Vector3 _forward = (movementScale * ___m_controlObject.transform.forward) * movement.DashSpeed;
                        float single = MathUtils.SinusoidalSCurve(___m_dashTimer / movement.DashTime);
                        runSpeed = ((1f - single) * runSpeed) + (single * _forward);
                        Debug.Log($"Dash timer is {___m_dashTimer} at frame {Time.frameCount}");
                    }
                    ___m_dashTimer -= _deltaTime;
                    if (___m_impactTimer > 0f)
                    {
                        float single1 = MathUtils.SinusoidalSCurve(___m_impactTimer / ___m_impactStartTime);
                        runSpeed = ((1f - single1) * runSpeed) + (single1 * ___m_impactVelocity);
                    }
                    ___m_impactTimer -= _deltaTime;
                    runSpeed = Vector3.ClampMagnitude(runSpeed, movement.MaxSpeed);
                    Vector3 vector3 = (Vector3)f_ProgressVelocityWrtFriction.Invoke(__instance, new object[] { ___m_lastVelocity, ___m_controls.Motion.GetVelocity(), runSpeed, f_GetSurfaceData.Invoke(__instance, new object[0]) });
                    ___m_controls.Motion.SetVelocity(vector3);
                    ___m_lastVelocity = vector3;
                    f_ApplyGroundMovement.Invoke(__instance, new object[] { _deltaTime });
                    if (!_netPaused && ___m_controlScheme.m_dashButton.JustPressed() | dashDown && movement.DashTime - ___m_dashTimer >= movement.DashCooldown && ___m_impactTimer < 0f)
                    {
                        ___m_dashTimer = movement.DashTime;
                        if (___m_controlsImpl.m_serverImpl == null)
                        {
                            f_DoDash.Invoke(__instance, new object[0]);
                        }
                        else
                        {
                            ___m_controlsImpl.m_serverImpl.StartDash();
                        }
                        List<Collision> recentCollisions = ___m_collisionRecorder.GetRecentCollisions();
                        for (int i = 0; i < recentCollisions.Count; i++)
                        {
                            Collision collision = recentCollisions[i];
                            PlayerControls playerControl = collision.gameObject.RequestComponent<PlayerControls>();
                            if (playerControl != null)
                            {
                                Vector3 _relativeVelocity = collision.relativeVelocity + ((movementScale * ___m_controlObject.transform.forward) * movement.DashSpeed);
                                f_OnDashCollision.Invoke(__instance, new object[] { playerControl, collision.contacts[0].point, _relativeVelocity });
                            }
                        }
                    }
                    ___m_attemptedDistanceCounter = ___m_attemptedDistanceCounter + runSpeed.magnitude * _deltaTime;

                    if (___m_attemptedDistanceCounter < movement.FootstepLength)
                    {
                        continue;
                    }
                    ___m_attemptedDistanceCounter %= movement.FootstepLength;
                }
                return false;
            }
        }


        private static FieldInfo m_ClientPlayerControlsImpl_Default_dashTimer = typeof(ClientPlayerControlsImpl_Default).GetField("m_dashTimer", BindingFlags.Instance | BindingFlags.NonPublic);

        [HarmonyPatch(typeof(PlayerControls), "Update")]
        public static class PlayerControlsUpdatePatch
        {
            [HarmonyPrefix]
            public static void Prefix(PlayerControls __instance, PlayerControlsImpl_Default ___m_impl_default)
            {
                var currentFrameData = Injector.Server.OpenCurrentFrameDataForWrite();
                if (currentFrameData.CharPos == null)
                {
                    currentFrameData.CharPos = new Dictionary<int, CharPositionData>();
                }
                int id = (int)EntitySerialisationRegistry.GetId(__instance.gameObject);
                if (!currentFrameData.CharPos.ContainsKey(id))
                {
                    currentFrameData.CharPos[id] = new CharPositionData();
                }
                currentFrameData.CharPos[id].ForwardDirection = new Point()
                {
                    X = __instance.gameObject.transform.forward.x,
                    Y = __instance.gameObject.transform.forward.y,
                    Z = __instance.gameObject.transform.forward.z
                };
                currentFrameData.CharPos[id].DashTimer = (float)m_ClientPlayerControlsImpl_Default_dashTimer.GetValue(___m_impl_default.m_clientImpl);
            }
        }

        private static Type t_ServerMessenger = typeof(ServerTime).Assembly.GetType("ServerMessenger");
        private static FieldInfo m_m_LocalServer = t_ServerMessenger.GetField("m_LocalServer", BindingFlags.Static | BindingFlags.NonPublic);

        //[HarmonyPrefix]
        //[HarmonyPatch(typeof(Server), "BroadcastMessageToAll")]
        //public static void ServerBroadcastMessageToAllPatch(MessageType type, Serialisable message) {
        //    // Only intercept server messages if we are the server. 
        //    if (m_m_LocalServer.GetValue(null) == null) {
        //        return;
        //    }

        //    if (Injector.Server.CurrentFrameData.ServerMessages == null) {
        //        Injector.Server.CurrentFrameData.ServerMessages = new List<ServerMessage>();
        //    }
        //    FastList<byte> fastList = new FastList<byte>();
        //    message.Serialise(new BitStreamWriter(fastList));
        //    Injector.Server.CurrentFrameData.ServerMessages.Add(new ServerMessage() {
        //        Type = (int)type,
        //        Message = fastList.ToArray()
        //    });
        //}

        private static FieldInfo m_m_spawnables = typeof(SpawnableEntityCollection).GetField("m_spawnables", BindingFlags.NonPublic | BindingFlags.Instance);

        [HarmonyPatch(typeof(EntitySerialisationRegistry), "StartSynchronisingEntry")]
        public static class EntitySerialisationRegistryStartSynchronisingEntryPatch
        {
            [HarmonyPrefix]
            public static void Prefix(EntitySerialisationEntry entry)
            {
                try
                {
                    var currentFrameData = Injector.Server.OpenCurrentFrameDataForWrite();
                    GameObject mGameObject = entry.m_GameObject;
                    if (currentFrameData.EntityRegistry == null)
                    {
                        currentFrameData.EntityRegistry = new List<EntityRegistryData>();
                    }
                    EntityRegistryData entityRegistryDatum = new EntityRegistryData()
                    {
                        EntityId = (int)EntitySerialisationRegistry.GetId(mGameObject),
                        Name = mGameObject.name,
                        Pos = mGameObject.transform.position.ToThrift(),  // TODO: needed?
                        SyncEntityTypes = new List<int>(),
                        Components = (
                            from c in mGameObject.GetComponents<Component>()
                            select c.GetType().Name).ToList<string>()
                    };
                    if (mGameObject.GetComponent<SpawnableEntityCollection>() != null)
                    {
                        SpawnableEntityCollection component = mGameObject.GetComponent<SpawnableEntityCollection>();
                        entityRegistryDatum.SpawnNames = (
                            from s in (List<GameObject>)m_m_spawnables.GetValue(component)
                            select s.name).ToList<string>();
                    }
                    for (int i = 0; i < entry.m_ServerSynchronisedComponents.Count; i++)
                    {
                        entityRegistryDatum.SyncEntityTypes.Add((int)entry.m_ServerSynchronisedComponents._items[i].GetEntityType());
                    }
                    currentFrameData.EntityRegistry.Add(entityRegistryDatum);
                }
                catch (Exception exception)
                {
                    Console.WriteLine(exception.Message);
                }
            }
        }

        private static Type t_RoundInstanceData = typeof(RoundData).GetNestedType("RoundInstanceData", BindingFlags.NonPublic);
        private static FieldInfo m_RecipeCount = t_RoundInstanceData.GetField("RecipeCount", BindingFlags.Instance | BindingFlags.Public);
        private static FieldInfo m_CumulativeFrequencies = t_RoundInstanceData.GetField("CumulativeFrequencies", BindingFlags.Instance | BindingFlags.Public);
        private static MethodInfo f_GetWeight = typeof(RoundData).GetMethod("GetWeight", BindingFlags.Instance | BindingFlags.NonPublic);

        [HarmonyPatch(typeof(RoundData), "GetNextRecipe")]
        public static class GetNextRecipePatch
        {
            [HarmonyPrefix]
            public static bool Prefix(ref RecipeList.Entry[] __result, RoundInstanceDataBase _data, RecipeList ___m_recipes, RoundData __instance)
            {
                m_RecipeCount.SetValue(_data, (int)m_RecipeCount.GetValue(_data) + 1);
                KeyValuePair<int, RecipeList.Entry> weightedRandomElement =
                    OrderRandom.GetWeightedRandomElement(___m_recipes.m_recipes, (int i, RecipeList.Entry e) => (float)f_GetWeight.Invoke(__instance, new object[] { _data, i }));
                ((int[])m_CumulativeFrequencies.GetValue(_data))[weightedRandomElement.Key]++;
                __result = new RecipeList.Entry[] { weightedRandomElement.Value };
                return false;
            }
        }

        [HarmonyPatch(typeof(WorkstationMessage), "Serialise")]
        public static class WorkstationMessageSerialisePatch
        {
            [HarmonyPrefix]
            public static bool Prefix(BitStreamWriter writer, WorkstationMessage __instance)
            {
                // This patch is needed because WorkstationMessage is not supposed to be reserialized on the client side.
                // Not all messages do this... this is perhaps an exception.
                writer.Write(__instance.m_interacting);
                if (__instance.m_interactor != null)
                {
                    __instance.m_interactor.m_Header.Serialise(writer);
                }
                else
                {
                    __instance.m_interactorHeader.Serialise(writer);
                }
                if (__instance.m_interacting)
                {
                    if (__instance.m_item != null)
                    {
                        __instance.m_item.m_Header.Serialise(writer);
                    }
                    else
                    {
                        __instance.m_itemHeader.Serialise(writer);
                    }
                }
                return false;
            }
        }
    }

    // [HarmonyPatch]
    // public static class PatchOnMessageReceived
    // {
    //     public static MethodBase TargetMethod()
    //     {
    //         return typeof(Server).Assembly.GetType("Mailbox")
    //             .GetMethod("OnMessageReceived", BindingFlags.Instance | BindingFlags.NonPublic);
    //     }

    //     public static void Prefix(MessageType type, Serialisable message)
    //     {
    //         try
    //         {
    //             var currentFrameData = Injector.Server.OpenCurrentFrameDataForWrite();
    //             if (currentFrameData.ServerMessages == null)
    //             {
    //                 currentFrameData.ServerMessages = new List<ServerMessage>();
    //             }
    //             FastList<byte> fastList = new FastList<byte>();
    //             if (message == null)
    //             {
    //                 Console.WriteLine("Message of type " + type + " is null!");
    //                 return;
    //             }
    //             message.Serialise(new BitStreamWriter(fastList));
    //             currentFrameData.ServerMessages.Add(new ServerMessage()
    //             {
    //                 Type = (int)type,
    //                 Message = fastList.ToArray()
    //             });
    //         }
    //         catch (Exception e)
    //         {
    //             Console.WriteLine(e);
    //             throw;
    //         }
    //     }
    // }

    [HarmonyPatch(typeof(Server), "SendMessageToClient", new Type[]{typeof(NetworkConnection), typeof(MessageType), typeof(Serialisable), typeof(bool)})]
    public static class PatchSendMessageToClient {
        public static void Prefix(NetworkConnection connection, MessageType type, Serialisable message, bool bReliable) {
            try
            {
                var currentFrameData = Injector.Server.OpenCurrentFrameDataForWrite();
                if (currentFrameData.ServerMessages == null)
                {
                    currentFrameData.ServerMessages = new List<ServerMessage>();
                }
                FastList<byte> fastList = new FastList<byte>();
                if (message == null)
                {
                    Console.WriteLine("Message of type " + type + " is null!");
                    return;
                }
                message.Serialise(new BitStreamWriter(fastList));
                currentFrameData.ServerMessages.Add(new ServerMessage()
                {
                    Type = (int)type,
                    Message = fastList.ToArray()
                });
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }
    }

}
