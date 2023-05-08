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
			return m_cannonCosmeticDecisions.m_cannonAnimator.GetAnimatorTransitionInfo(0).normalizedTime;
		}

		public void SetLoadAnimationProgress(float time)
        {
			m_cannonCosmeticDecisions.m_cannonAnimator.Play("DLC08_Cannon_Load");
			m_cannonCosmeticDecisions.m_cannonAnimator.CrossFade("DLC08_Cannon_Ready", 0.25f, 0, 0, time);
		}

		// Token: 0x04000D67 RID: 3431
		private CannonCosmeticDecisions m_cannonCosmeticDecisions;

		// Token: 0x04000D69 RID: 3433
		private const string m_readyStateName = "DLC08_Cannon_Ready";
	}
}
