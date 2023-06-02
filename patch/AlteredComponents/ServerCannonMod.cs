using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace SuperchargedPatch.AlteredComponents
{
    public class ServerCannonMod : ServerSynchroniserBase, ITriggerReceiver
    {
        // Token: 0x06001433 RID: 5171 RVA: 0x0006E500 File Offset: 0x0006C900
        public override void StartSynchronising(Component synchronisedObject)
        {
            base.StartSynchronising(synchronisedObject);
            m_cannon = (Cannon)synchronisedObject;
            m_pilotRotation = gameObject.RequestComponentRecursive<PilotRotation>();
            m_interactable = GetComponent<ServerInteractable>();
            m_interactable.RegisterTriggerCallbacks(this.OnInteractTrigger);
            m_interactable.RegisterCanInteractCallbacks(this.CanInteract);
            m_placementInteractable = GetComponent<ServerPlacementInteractable>();
            m_serverCosmetics = GetComponent<ServerCannonCosmeticDecisionsMod>();
            m_clientCosmetics = GetComponent<ClientCannonCosmeticDecisionsMod>();
        }

        public override void OnDestroy()
        {
            base.OnDestroy();

        }

        // Token: 0x06001434 RID: 5172 RVA: 0x0006E598 File Offset: 0x0006C998
        public override void UpdateSynchronising()
        {
            if (m_message.m_state == CannonModMessage.CannonState.Loaded)
            {
                var playerControls = m_message.m_loadedObject.GetComponent<PlayerControls>();
                if (playerControls != null)
                {
                    playerControls.ControlScheme.IsUseJustReleased();
                }
                if (playerControls == null || playerControls.ControlScheme.m_dashButton.JustPressed())
                {
                    OnExit();
                    m_message.m_loadedObject.transform.position = m_message.m_exitPosition;
                    m_message.m_loadedObject.transform.rotation = m_message.m_exitRotation;
                    m_message.m_loadedObject.transform.SetParent(null, true);
                    m_message.m_loadedObject = null;
                    m_message.m_state = CannonModMessage.CannonState.NotLoaded;
                    m_message.ClearDataOnExit();
                    SendServerEvent(m_message);
                }
            }
            else if (m_message.m_state == CannonModMessage.CannonState.Loading)
            {
                if (m_serverCosmetics.IsReadyToLaunch())
                {
                    m_message.m_state = CannonModMessage.CannonState.Loaded;
                    m_message.m_loadingTime = 0;
                    OnReady();
                    SendServerEvent(m_message);
                }
                else
                {
                    m_message.m_loadingTime = m_serverCosmetics.GetLoadAnimationProgress();
                    SendServerEvent(m_message);
                }
            } else if (m_message.m_state == CannonModMessage.CannonState.BeginLaunching)
            {
                OnFlying();
                var chef = m_message.m_loadedObject;
                var target = m_cannon.m_target;
                Quaternion startingRotation = chef.transform.rotation;
                if (m_cannon.m_animation.m_bUseStartRotation)
                {
                    startingRotation = Quaternion.Euler(m_cannon.m_animation.m_StartRotationValue);
                }
                Vector3 targetPosition = target.position;
                Quaternion targetRotation = Quaternion.Euler(target.eulerAngles + m_cannon.m_animation.m_TargetRotationValue);
                Vector3 startingPosition = chef.transform.position;
                float startingHeight = chef.transform.position.y;
                float maxHeight = startingHeight + m_cannon.m_animation.m_MaxHeightValue;
                m_message.m_startingPosition = startingPosition;
                m_message.m_targetPosition = targetPosition;
                m_message.m_startingRotation = startingRotation;
                m_message.m_targetRotation = targetRotation;
                m_message.m_startingHeight = startingHeight;
                m_message.m_maxHeight = maxHeight;
                m_message.m_state = CannonModMessage.CannonState.Flying;
                m_message.m_flyingTime = 0;
                SendServerEvent(m_message);
            } else if (m_message.m_state == CannonModMessage.CannonState.Flying)
            {
                var chef = m_message.m_loadedObject;
                if (m_message.m_flyingTime < m_cannon.m_animation.m_CurveTime)
                {
                    float position = 1f - (m_cannon.m_animation.m_CurveTime - m_message.m_flyingTime) / m_cannon.m_animation.m_CurveTime;
                    chef.transform.rotation = Quaternion.Lerp(m_message.m_startingRotation, m_message.m_targetRotation, m_cannon.m_animation.m_RotationCurve.Evaluate(position));
                    Vector3 newPosition = Vector3.Lerp(m_message.m_startingPosition, m_message.m_targetPosition, position);
                    if (position < 0.5f)
                    {
                        newPosition.y = Mathf.Lerp(m_message.m_startingHeight, m_message.m_maxHeight, m_cannon.m_animation.m_HeightCurve.Evaluate(position));
                    }
                    else
                    {
                        newPosition.y = Mathf.Lerp(m_message.m_targetPosition.y, m_message.m_maxHeight, m_cannon.m_animation.m_HeightCurve.Evaluate(position));
                    }
                    chef.transform.position = newPosition;
                    var deltaTime = TimeManager.GetDeltaTime(chef);
                    m_message.m_flyingTime += deltaTime;
                    if (deltaTime > 0)
                    {
                        SendServerEvent(m_message);
                    }
                }
                else
                {
                    chef.transform.rotation = m_message.m_targetRotation;
                    chef.transform.position = m_message.m_targetPosition;
                    chef.GetComponent<ClientPlayerControlsImpl_Default>().ApplyImpact(chef.transform.forward.XZ() * 2f, 0.2f);
                    OnExit();
                    m_message.m_loadedObject = null;
                    m_message.m_state = CannonModMessage.CannonState.NotLoaded;
                    m_message.ClearDataOnExit();

                    SendServerEvent(m_message);
                }
            }
        }

        // Token: 0x06001435 RID: 5173 RVA: 0x0006E62E File Offset: 0x0006CA2E
        public override EntityType GetEntityType()
        {
            return EntityType.Cannon;
        }

        // Token: 0x06001437 RID: 5175 RVA: 0x0006E668 File Offset: 0x0006CA68
        private void OnLoad()
        {
            var chef = m_message.m_loadedObject;
            // From original ServerCannonSessionInteractable.StartSession
            m_placementInteractable.SetInteractionSurpressed(true);
            m_interactable.SetInteractionSuppressed(true);
            var playerControls = chef.GetComponent<PlayerControls>();
            playerControls.enabled = false;
            var playerLayer = LayerMask.NameToLayer("Players");
            chef.GetComponent<CollisionRecorder>().SetFilter(_collision =>
            {
                return _collision != null && _collision.gameObject != null && _collision.rigidbody != null && _collision.gameObject.layer == playerLayer && _collision.rigidbody.velocity.sqrMagnitude > 0.001f;
            });
            chef.GetComponent<Rigidbody>().isKinematic = true;
            playerControls.ControlScheme.ClearEvents();

            // From original ClientCannonPlayerHandler.Load
            playerControls.AllowSwitchingWhenDisabled = true;
            playerControls.ThrowIndicator.Hide();
            var dynamicLandscapeParenting = chef.GetComponent<DynamicLandscapeParenting>();
            if (dynamicLandscapeParenting != null)
            {
                dynamicLandscapeParenting.enabled = false;
            }

            // From original ClientCannon.Load
            chef.transform.position = m_cannon.m_attachPoint.position;
            chef.transform.rotation = m_cannon.m_attachPoint.rotation;
            chef.transform.SetParent(m_cannon.m_attachPoint, true);

            m_clientCosmetics.Load(chef);
        }

        private void OnReady()
        {
            m_cannon.m_button.SendTrigger(m_cannon.m_enableTrigger);
        }

        // Token: 0x06001438 RID: 5176 RVA: 0x0006E6C4 File Offset: 0x0006CAC4
        private void OnExit()
        {
            var _obj = m_message.m_loadedObject;

            // From original ServerCannonSessionInteractable.OnSessionEnded
            m_placementInteractable.SetInteractionSurpressed(false);
            m_interactable.SetInteractionSuppressed(false);

            var playerControls = _obj.GetComponent<PlayerControls>();
            playerControls.enabled = true;
            _obj.GetComponent<CollisionRecorder>().SetFilter(null);
            _obj.GetComponent<Rigidbody>().isKinematic = false;

            // From original ClientCannonPlayerHandler.Unload and ExitCannonRoutine
            playerControls.AllowSwitchingWhenDisabled = false;
            var dynamicLandscapeParenting = _obj.GetComponent<DynamicLandscapeParenting>();
            if (dynamicLandscapeParenting != null)
            {
                dynamicLandscapeParenting.enabled = true;
            }

            _obj.transform.SetParent(null, true);

            // Something to reset weird animation results from warping
            _obj.transform.localScale = new Vector3(1, 1, 1);

            // From original ServerCannon.ExitCannonRoutine
            _obj.GetComponent<GroundCast>()?.ForceUpdateNow();

            // From ClientCannontPlayerHandler.Land (OK to do even if did not launch)
            _obj.GetComponent<Collider>().enabled = true;

            // Logic we added ourselves, so we can do this instead of updating every frame.
            m_cannon.m_button.SendTrigger(m_cannon.m_disableTrigger);

            m_clientCosmetics.Unload(_obj);
        }

        private void OnLaunch()
        {
            m_message.m_loadedObject.GetComponent<Collider>().enabled = false;
            m_cannon.m_button.SendTrigger(m_cannon.m_disableTrigger);
        }

        private void OnFlying()
        {
            m_message.m_loadedObject.transform.SetParent(null, true);
            m_clientCosmetics.Launch(m_message.m_loadedObject);
        }

        public void Warp(CannonModMessage msg)
        {
            if (m_message.m_state != CannonModMessage.CannonState.NotLoaded)
            {
                OnExit();
            }
            m_message = msg;
            if (m_message.m_state != CannonModMessage.CannonState.NotLoaded)
            {
                OnLoad();
                if (m_message.m_state == CannonModMessage.CannonState.Loading)
                {
                    m_serverCosmetics.SetLoadAnimationProgress(m_message.m_loadingTime);
                }
                else
                {
                    OnReady();
                    if (m_message.m_state != CannonModMessage.CannonState.Loaded)
                    {
                        OnLaunch();
                        if (m_message.m_state != CannonModMessage.CannonState.BeginLaunching)
                        {
                            OnFlying();
                        }
                    }
                }
            }
        }

        // Token: 0x0600143B RID: 5179 RVA: 0x0006E7AC File Offset: 0x0006CBAC
        public void OnTrigger(string _trigger)
        {
            if (_trigger == m_cannon.m_launchTrigger && m_message.m_state == CannonModMessage.CannonState.Loaded)
            {
                m_message.m_state = CannonModMessage.CannonState.BeginLaunching;
                OnLaunch();
                if (m_pilotRotation != null)
                {
                    m_message.m_angle = m_pilotRotation.m_transformToRotate.eulerAngles.y;
                }

                SendServerEvent(m_message);
            }
        }

        private void OnInteractTrigger(GameObject _interacter, Vector2 _directionXZ)
        {
            m_message.m_loadedObject = _interacter;
            m_message.m_state = CannonModMessage.CannonState.Loading;
            OnLoad();

            // From original ClientCannon.Load
            m_message.m_exitPosition = _interacter.transform.position;
            m_message.m_exitRotation = _interacter.transform.rotation;

            SendServerEvent(m_message);
        }

        private bool CanInteract(GameObject _interacter)
        {
            return m_message.m_state == CannonModMessage.CannonState.NotLoaded;
        }

        // Token: 0x04000FAF RID: 4015
        private Cannon m_cannon;

        // Token: 0x04000FB0 RID: 4016
        private CannonModMessage m_message = new CannonModMessage();

        // Token: 0x04000FB5 RID: 4021
        private PilotRotation m_pilotRotation;

        private ServerInteractable m_interactable;
        private ServerPlacementInteractable m_placementInteractable;

        private ServerCannonCosmeticDecisionsMod m_serverCosmetics;
        private ClientCannonCosmeticDecisionsMod m_clientCosmetics;
    }

}
