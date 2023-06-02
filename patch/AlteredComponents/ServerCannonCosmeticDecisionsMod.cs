using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace SuperchargedPatch.AlteredComponents
{
    public class ServerCannonCosmeticDecisionsMod : ServerSynchroniserBase
	{
		// Token: 0x06001142 RID: 4418 RVA: 0x0006306D File Offset: 0x0006146D
		public override void StartSynchronising(Component synchronisedObject)
		{
			base.StartSynchronising(synchronisedObject);
			this.m_cannonCosmeticDecisions = (CannonCosmeticDecisions)synchronisedObject;
		}

		// Token: 0x06001143 RID: 4419 RVA: 0x000630AC File Offset: 0x000614AC
		public bool IsReadyToLaunch()
		{
			return this.m_cannonCosmeticDecisions.m_cannonAnimator.GetCurrentAnimatorStateInfo(0).IsName(m_readyStateName);
		}

		public float GetLoadAnimationProgress()
        {
            // Note: I tried to use GetAnimationTransitionInfo. But, that apparently just never returns any transitions.
            // Instead, it looks like when we transition from Load to Ready, we just stay in Load state for a while and
            // then suddenly become the Ready state; and also, when we call Play("DLC08_Cannon_Load", 0, time), we will
            // actually restore the exact state as if we had been transitioning from Load to Ready for "time". Note also
            // that the normalized time is between 0 and 1, with 1 being the end of the transition. It's weird, but this
            // seems to be the only way this can work.
			return m_cannonCosmeticDecisions.m_cannonAnimator.GetCurrentAnimatorStateInfo(0).normalizedTime;
		}

		public void SetLoadAnimationProgress(float time)
        {
			m_cannonCosmeticDecisions.m_cannonAnimator.Play("DLC08_Cannon_Load", 0, time);
		}
		
        // Debugging code... leaving it here for now.
		// public void Update()
		// {
		// 	if (Input.GetKeyDown(KeyCode.F1))
        //     {
        //         m_cannonCosmeticDecisions.m_cannonAnimator.Play("DLC08_Cannon_Load", 0, 0.99f);
        //     } else if (Input.GetKeyDown(KeyCode.F2))
        //     {
        //         m_cannonCosmeticDecisions.m_cannonAnimator.SetBool("IsOccupied", false);
        //         m_cannonCosmeticDecisions.m_cannonAnimator.SetBool("IsOccupied", true);
        //         m_cannonCosmeticDecisions.m_cannonAnimator.Play("DLC08_Cannon_Load", 0, 0f);
        //     }
        //     else if (Input.GetKeyDown(KeyCode.F3))
        //     {
        //         m_cannonCosmeticDecisions.m_cannonAnimator.Play("DLC08_Cannon_Load", 0, 0.99f);
        //     }
        //     else if (Input.GetKeyDown(KeyCode.F4))
        //     {
        //         m_cannonCosmeticDecisions.m_cannonAnimator.Play("DLC08_Cannon_Load", 0, 0f);
        //         m_cannonCosmeticDecisions.m_cannonAnimator.Play("DLC08_Cannon_Load", 0, 0.99f);
        //     }
        //     else if (Input.GetKeyDown(KeyCode.F5))
        //     {
        //         m_cannonCosmeticDecisions.m_cannonAnimator.Play("DLC08_Cannon_Ready", 0, 0f);
        //     }
        //     if (Input.GetKeyDown(KeyCode.F8))
        //     {
		// 		m_cannonCosmeticDecisions.m_cannonAnimator.speed = 0f;
        //     } else if (Input.GetKeyDown(KeyCode.F9))
        //     {
        //         m_cannonCosmeticDecisions.m_cannonAnimator.speed = 0.25f;
        //     } else if (Input.GetKeyDown(KeyCode.F10))
        //     {
        //         m_cannonCosmeticDecisions.m_cannonAnimator.speed = 1f;
        //     }
        // }

		// Token: 0x04000D67 RID: 3431
		private CannonCosmeticDecisions m_cannonCosmeticDecisions;

		// Token: 0x04000D69 RID: 3433
		private const string m_readyStateName = "DLC08_Cannon_Ready";
	}
}
