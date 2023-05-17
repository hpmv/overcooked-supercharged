### ClientPlayerControlsImpl_Default

Fields:

* `private PlayerControlsImpl_Default m_controlsImpl;`
  The associated `PlayerControlsImpl_Default` component of the same object.
* `private ClientInteractable m_lastInteracted;`
  The last object interacted with. Set in BeginInteraction and cleared in EndInteraction. Seems to only be queried by code that affects animations?
* `private ClientInteractable m_predictedInteracted;`
  Part of some super complicated interaction logic:
    ```cs
    private void Update_Interact(float _deltaTime, bool isUsePressed, bool justPressed)
    {
        // This is the highlighted object to be interacted.
        ClientInteractable highlighted = this.m_controls.CurrentInteractionObjects.m_interactable;

        // Object already being interacted
        bool alreadyInteracting = this.m_predictedInteracted != null;
        bool hasHighlight = highlighted != null;
        bool isCurrentInteractionSticky = alreadyInteracting && this.m_predictedInteracted.InteractionIsSticky();

        if (isUsePressed && hasHighlight && !alreadyInteracting)
        {
            // If we aren't already interacting with something, and the interaction button is down (doesn't have to
            // be just pressed), and we have a highlighted object to interact with, then start the interaction
            // with that object.
            this.m_predictedInteracted = highlighted;
            ClientMessenger.ChefEventMessage(ChefEventMessage.ChefEventType.Interact, base.gameObject, highlighted);
        }
        else if (!hasHighlight && alreadyInteracting && isCurrentInteractionSticky)
        {
            // If current interaction is sticky (i.e. does not require pressing), but there is no longer a
            // currently highlighted object, then stop interacting.
            this.m_predictedInteracted = null;
            ClientMessenger.ChefEventMessage(ChefEventMessage.ChefEventType.Interact, base.gameObject, null);
        }
        else if (!isUsePressed && alreadyInteracting && !isCurrentInteractionSticky)
        {
            // If current interaction is not sticky (requires continuous pressing) but the key is
            // no longer down, then stop interacting. e.g. fire extingisher, washer jet.
            this.m_predictedInteracted = null;
            ClientMessenger.ChefEventMessage(ChefEventMessage.ChefEventType.Interact, base.gameObject, null);
        }
        else if (hasHighlight && highlighted != this.m_predictedInteracted)
        {
            // If the currently highlighted object is different from the currently interacted object,
            // stop interacting.
            this.m_predictedInteracted = null;
            ClientMessenger.ChefEventMessage(ChefEventMessage.ChefEventType.Interact, base.gameObject, null);
        }

        if (justPressed && highlighted != null)
        {
            // Independently of everything else, if we had *just* pressed the use button and we have a highlighted
            // object to use, trigger the interaction with that object.
            ClientMessenger.ChefEventMessage(ChefEventMessage.ChefEventType.TriggerInteract, base.gameObject, highlighted);
        }
    }
    ```
* `private Transform m_Transform;`
  Transform associated with the chef.
* `private ClientInteractable m_sessionInteraction;`
  This is set while the chef is in an active "Session Interaction", i.e. an interaction that makes the chef
  uncontrollable before it ends; e.g. operating the raft control, or being inside a cannon.
* `private PlayerControls m_controls;`
  The non-synchronizing PlayerControls object.
* `private PlayerControls.ControlSchemeData m_controlScheme;`
  Provides information for the control scheme, i.e. what keys are used to dash, interact, etc.
* `private GameObject m_controlObject;`
  The chef.
* `private CollisionRecorder m_collisionRecorder;`
  Records recent collisions, used to compute dash collision with another player.
* `private PlayerIDProvider m_controlsPlayer;`
  Component on the chef that provides the ID of the player.
* `private ICarrier m_iCarrier;`
  Component on the chef that deals with carrying objects. TODO: this is satisfied by both ServerPlayerAttachmentCarrier and ClientPlayerAttachmentCarrier.
* `private IClientThrower m_iThrower;`
  Component on the chef that deals with throwing objects.
* `private IClientHandleCatch m_iCatcher;`
  Component on the chef that deals with catching objects.
* `private Vector3 m_lastVelocity;`
  Velocity on the last "chef movement" frame. Being used in `Update_Movement` to compute friction:
    ```cs
    Vector3 vector3 = this.ProgressVelocityWrtFriction(this.m_lastVelocity, this.m_controls.Motion.GetVelocity(), vector2, this.GetSurfaceData());
    this.m_controls.Motion.SetVelocity(vector3);
    this.m_lastVelocity = vector3;
    ```
* `private float m_dashTimer = float.MinValue;`
  * Remaining time of a dash action; CAN BE NEGATIVE. When pressing dash, the dash timer is set to PlayerControls.MovementData.DashTime. Then on every "chef movement"
  frame this is decremented by 1/60 of a second, and continues to go below zero.
  * This is also used to compute the dash cooldown (this is why it needs to go below zero).
  * It is also used to compute dash collision impact.
* `private float m_attemptedDistanceCounter;`
  Does not seem to be actually useful. Only read and written here:
  ```cs
  this.m_attemptedDistanceCounter += vector2.magnitude * _deltaTime;
  if (this.m_attemptedDistanceCounter >= movement.FootstepLength)
  {
  	this.m_attemptedDistanceCounter %= movement.FootstepLength;
  }
  ```
* `private bool m_aimingThrow;`
    * If true, the chef is aiming to throw an item.
    * This is used to show/hide the throw indicator.
    * It is also used to suppress movement, see `m_movementInputSuppressed`.
* `private bool m_movementInputSuppressed;`
  If true, the chef does not move with the controls (the control's movement axes are effectively zero); however it will still move with the dash velocity.
* `private Vector3 m_lastMoveInputDirection;`
  * This stores the last movement input direction while the chef is aiming (if input magnitude is >= `m_controls.m_analogEnableDeadzoneThreshold`, which is 0.25).
  * After exiting from aiming mode or a session interaction, the movement suppression is only lifted when either
    * the movement input magnitude is less than `m_controls.m_analogEnableDeadzoneThreshold`
    * the movement input direction is more than `m_controls.m_analogEnableAngleThreshold` (which is 15 degrees) away from the `m_lastMoveInputDirection`.
* `private float m_impactStartTime = float.MaxValue;`
  The description is misleading; this is the total duration of the impact. It is set in ApplyImpact and not changed after.
* `private float m_impactTimer = float.MinValue;`
  This is the remaining time of the impact. It is set to the same value as `m_impactStartTime` at the beginning of ApplyImpact,
  and then it decrements by 1/60 of a second every "movement frame".
* `private Vector3 m_impactVelocity = Vector3.zero;`
  The initial impact velocity. This is only set in ApplyImpact and then used to calculate the chef velocity (along with movement and dash) in Update_Movement.
  The calculation formula is very similar to dash.
* `private float m_timeOffGround;`
  * Increments when the chef is not in collision with ground; resets to zero otherwise.
  * If this value reaches m_controls.m_timeBeforeFalling, ``m_isFalling` is set to true.
  * The only thing that `m_isFalling` controls is a trigger that starts the falling
    animation.
* `private bool m_isFalling;`
  See `m_timeOffGround`.
* `private VoidGeneric<ClientInteractable> m_interactTriggerCallback;`
  Used for animations.
* `private VoidGeneric<GameObject> m_throwTriggerCallback;`
  Used for animations.
* `private VoidGeneric<bool> m_fallingTriggerCallback;`
  Used for animations.
* `private PlayerControlsHelper.ControlAxisData m_controlAxisData;`
  * This is a frame-temporary axis data built by `Update_Movement` and then used in `Update_Rotation` and `UpdateMovementSuppression`.
* `private PlayerIDProvider m_playerIDProvider;`
  This should point to the same object as `m_controlsPlayer`.
* `private float m_LeftOverTime;`
  Remaining time to calculate "movement frame". This is incremented by Time.deltaTime every game frame,
  and then a movement frame happens as 1/60 second is subtracted from `m_LeftOverTime`.
* `private OrderedMessageReceivedCallback m_onChefEffectReceived;`
  A callback used to handle received ChefEffect messages, i.e. multiplayer impact and dash.
* `private uint m_entityID;`
  The entity ID of the chef.
* `private float m_lastPickupTimestamp;`
  * Timestamp of the last pickup action. This time is from `ClientTime.Time()`, which is some complicated synchronized time?
  * This is used to prevent another pickup within 0.5 seconds (`m_controls.m_pickupDelay`).
* `private LayerMask m_SlopedGroundMask = 0;`
  Something to do with gravity.

### Fields of player control classes that require persisting for TAS warping

* `ClientInteractable m_predictedInteracted` - persisted as an entity reference. Needed to calculate next frame interaction decisions.
* NOT `m_sessionInteraction` - we already record movement suppression, and in general it's not a big deal because the controller won't be trying to
  move the chef when it's trying to interact with something, and we'll also make sure that any sort of interactable will actually disable the control of the chef. Movement suppression is generally just not that reliable anyway because dashing is not prevented; it's only good for aiming, really.
* NOT `m_collisionRecorder` - colliding with another chef is unlikely to be helpful for TAS, we'll just avoid it in general so it's OK if this is not warped
  perfectly.
* `Vector3 m_lastVelocity`; unlike rigidbody's velocity, this is NOT impacted by pausing, because Update_Impl doesn't call any of the movement update functions if the game is paused.
* `float m_dashTimer`
* `bool m_aimingThrow`
* `bool m_movementInputSuppressed`
* `Vector3 m_lastMoveInputDirection`
* `float m_impactStartTime`
* `float m_impactTimer`
* `Vector3 m_impactVelocity`
* `float m_LeftOverTime`
* NOT `float m_lastPickupTimestamp`: We will limit this on the controller side. Otherwise this would require modding the field itself since it doesn't hold a pausable timestamp right now.