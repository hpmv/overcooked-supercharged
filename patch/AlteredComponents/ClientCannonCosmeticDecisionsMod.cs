using Team17.Online;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace SuperchargedPatch.AlteredComponents
{
    public class ClientCannonCosmeticDecisionsMod : ClientSynchroniserBase
	{
		// Token: 0x06001145 RID: 4421 RVA: 0x000630EC File Offset: 0x000614EC
		public override void StartSynchronising(Component synchronisedObject)
		{
			this.m_cannonCosmeticDecisions = (CannonCosmeticDecisions)synchronisedObject;
			this.m_cannon = base.gameObject.RequireComponent<Cannon>();
			this.m_animator = this.m_cannonCosmeticDecisions.m_cannonAnimator;
			AvatarDirectoryData avatarDirectoryData = GameUtils.GetAvatarDirectoryData();
			this.m_colours = new Color[avatarDirectoryData.Colours.Length];
			for (int i = 0; i < this.m_colours.Length; i++)
			{
				this.m_colours[i] = avatarDirectoryData.Colours[i].MaskColour;
			}
			this.m_targetMeshRenderers = this.m_cannonCosmeticDecisions.m_targetVisuals.RequestComponentsRecursive<MeshRenderer>();
			this.m_targetAnimator = this.m_cannonCosmeticDecisions.m_targetVisuals.RequireComponentRecursive<Animator>();
			this.m_targetGlow = this.m_cannonCosmeticDecisions.m_targetVisuals.RequestComponentRecursive<ParticleSystem>();
			this.m_defaultGlowAlpha = this.m_targetGlow.main.startColor.color.a;
			this.SetTargetColour(this.m_cannonCosmeticDecisions.m_defaultTargetColour);
			this.SetGlowColour(this.m_cannonCosmeticDecisions.m_defaultTargetColour);
			this.m_cannonCosmeticDecisions.m_fireFX.SetActive(false);
			this.m_cannonCosmeticDecisions.m_fuseFX.SetActive(false);
			this.ShowTarget(false);
		}

		// Token: 0x06001146 RID: 4422 RVA: 0x00063284 File Offset: 0x00061684
		public void Load(GameObject _objToLoad)
		{
			this.m_loadedObject = _objToLoad;
			this.m_playerAnimations = this.m_loadedObject.RequestComponent<PlayerAnimationDecisions>();
			if (this.m_playerAnimations != null)
			{
				this.m_playerAnimations.SetInCannon(true);
				GameUtils.TriggerAudio(GameOneShotAudioTag.DLC_08_Cannon_Enter, base.gameObject.layer);
			}
			this.m_playerControls = this.m_loadedObject.RequestComponent<PlayerControls>();
			if (this.m_playerControls != null)
			{
				uint uEntityID = EntitySerialisationRegistry.GetEntry(this.m_playerControls.gameObject).m_Header.m_uEntityID;
				if (ClientUserSystem.m_Users.Count == 1)
				{
					Color color = this.m_colours[0];
					this.SetTargetColour(color);
					this.SetGlowColour(color);
				}
				for (int i = 0; i < ClientUserSystem.m_Users.Count; i++)
				{
					if (ClientUserSystem.m_Users._items[i].EntityID == uEntityID)
					{
						Color color2 = this.m_colours[i];
						this.SetTargetColour(color2);
						this.SetGlowColour(color2);
						break;
					}
				}
			}
			this.m_animator.SetBool("IsOccupied", true);
			this.m_cannonCosmeticDecisions.m_fireFX.SetActive(false);
			this.m_cannonCosmeticDecisions.m_fuseFX.SetActive(true);
			GameUtils.TriggerAudio(GameOneShotAudioTag.DLC_08_Fuse_Ignite, base.gameObject.layer);
			GameUtils.StartAudio(GameLoopingAudioTag.DLC_08_Cannon_Fuse, this.m_fuseAudioToken, base.gameObject.layer);
			this.ShowTarget(true);
		}

		// Token: 0x06001147 RID: 4423 RVA: 0x0006340C File Offset: 0x0006180C
		public void Unload(GameObject _obj)
		{
			if (this.m_playerAnimations != null)
			{
				this.m_playerAnimations.SetInCannon(false);
			}
			GameUtils.TriggerAudio(GameOneShotAudioTag.DLC_08_Cannon_Enter, base.gameObject.layer);
			GameUtils.StopAudio(GameLoopingAudioTag.DLC_08_Cannon_Fuse, this.m_fuseAudioToken);
			this.SetTargetColour(this.m_cannonCosmeticDecisions.m_defaultTargetColour);
			this.SetGlowColour(this.m_cannonCosmeticDecisions.m_defaultTargetColour);
			this.m_loadedObject = null;
			this.m_animator.SetBool("IsOccupied", false);
			this.m_cannonCosmeticDecisions.m_fuseFX.SetActive(false);
			this.ShowTarget(false);
		}

		// Token: 0x06001148 RID: 4424 RVA: 0x000634AC File Offset: 0x000618AC
		public void Launch(GameObject _obj)
		{
			if (this.m_playerAnimations != null)
			{
				this.m_playerAnimations.SetCannonSpeed(this.m_cannon.m_animation.m_CurveTime * 2f);
				this.m_playerAnimations.FireCannon();
				this.m_playerAnimations.SetInCannon(false);
			}
			this.m_cannonCosmeticDecisions.m_fireFX.SetActive(true);
			this.m_cannonCosmeticDecisions.m_fuseFX.SetActive(false);
			this.m_loadedObject = null;
			this.m_animator.SetTrigger("CannonFire");
			GameUtils.TriggerAudio(GameOneShotAudioTag.DLC_08_Cannon_Fire, base.gameObject.layer);
			GameUtils.StopAudio(GameLoopingAudioTag.DLC_08_Cannon_Fuse, this.m_fuseAudioToken);
			GameUtils.TriggerAudio(GameOneShotAudioTag.DLC_08_Fuse_Death, base.gameObject.layer);
			GameUtils.TriggerAudio(GameOneShotAudioTag.DLC_08_Cannon_Crowd, base.gameObject.layer);
			this.SetTargetColour(this.m_cannonCosmeticDecisions.m_defaultTargetColour);
			this.SetGlowColour(this.m_cannonCosmeticDecisions.m_defaultTargetColour);
			this.ShowTarget(false);
		}

		// Token: 0x06001149 RID: 4425 RVA: 0x000635B4 File Offset: 0x000619B4
		public void OnTrigger(string _triggerMessage)
		{
			if (!string.IsNullOrEmpty(this.m_cannonCosmeticDecisions.m_aimStartTrigger) && this.m_cannonCosmeticDecisions.m_aimStartTrigger == _triggerMessage)
			{
				this.ShowTarget(true);
			}
			if (!string.IsNullOrEmpty(this.m_cannonCosmeticDecisions.m_aimEndTrigger) && this.m_cannonCosmeticDecisions.m_aimEndTrigger == _triggerMessage)
			{
				this.ShowTarget(false);
			}
		}

		// Token: 0x0600114A RID: 4426 RVA: 0x00063628 File Offset: 0x00061A28
		private void ShowTarget(bool show)
		{
			if (show)
			{
				this.m_targetVisualsActiveCount++;
			}
			if (!show && this.m_targetVisualsActiveCount > 0)
			{
				this.m_targetVisualsActiveCount--;
			}
			if (this.m_targetVisualsActiveCount > 0)
			{
				this.m_targetAnimator.SetBool("isInUse", true);
				this.SetTargetAlpha(1f);
				this.SetGlowAlpha(this.m_defaultGlowAlpha);
			}
			else
			{
				this.m_targetAnimator.SetBool("isInUse", false);
				this.SetTargetAlpha(this.m_cannonCosmeticDecisions.m_disabledTargetAlpha);
				this.SetGlowAlpha(this.m_cannonCosmeticDecisions.m_disabledTargetAlpha);
			}
		}

		// Token: 0x0600114B RID: 4427 RVA: 0x000636D8 File Offset: 0x00061AD8
		private void SetTargetColour(Color _colour)
		{
			for (int i = 0; i < this.m_targetMeshRenderers.Length; i++)
			{
				this.m_targetMeshRenderers[i].material.SetColor("_Color", _colour);
				if (this.m_targetMeshRenderers[i].material.HasProperty("_EmissionColor"))
				{
					this.m_targetMeshRenderers[i].material.SetColor("_EmissionColor", _colour);
				}
			}
		}

		// Token: 0x0600114C RID: 4428 RVA: 0x0006374C File Offset: 0x00061B4C
		private void SetTargetAlpha(float alpha)
		{
			for (int i = 0; i < this.m_targetMeshRenderers.Length; i++)
			{
				Color color = this.m_targetMeshRenderers[i].material.color;
				color.a = alpha;
				this.m_targetMeshRenderers[i].material.SetColor("_Color", color);
				if (this.m_targetMeshRenderers[i].material.HasProperty("_EmissionColor"))
				{
					this.m_targetMeshRenderers[i].material.SetColor("_EmissionColor", color);
				}
			}
		}

		// Token: 0x0600114D RID: 4429 RVA: 0x000637DC File Offset: 0x00061BDC
		private void SetGlowColour(Color _colour)
		{
			ParticleSystem.MainModule main = this.m_targetGlow.main;
			float a = main.startColor.color.a;
			main.startColor = new Color(_colour.r, _colour.g, _colour.b, a);
			this.m_targetGlow.RestartPFX();
		}

		// Token: 0x0600114E RID: 4430 RVA: 0x00063840 File Offset: 0x00061C40
		private void SetGlowAlpha(float alpha)
		{
			ParticleSystem.MainModule main = this.m_targetGlow.main;
			Color color = main.startColor.color;
			color.a = alpha;
			main.startColor = color;
			this.m_targetGlow.RestartPFX();
		}

		// Token: 0x04000D6A RID: 3434
		private CannonCosmeticDecisions m_cannonCosmeticDecisions;

		// Token: 0x04000D6C RID: 3436
		private Cannon m_cannon;

		// Token: 0x04000D6D RID: 3437
		private Animator m_animator;

		// Token: 0x04000D6E RID: 3438
		private GameObject m_loadedObject;

		// Token: 0x04000D6F RID: 3439
		private PlayerAnimationDecisions m_playerAnimations;

		// Token: 0x04000D70 RID: 3440
		private PlayerControls m_playerControls;

		// Token: 0x04000D71 RID: 3441
		private const string m_occupiedString = "IsOccupied";

		// Token: 0x04000D72 RID: 3442
		private const string m_fireString = "CannonFire";

		// Token: 0x04000D73 RID: 3443
		private const string m_targetInUse = "isInUse";

		// Token: 0x04000D74 RID: 3444
		private int m_targetVisualsActiveCount;

		// Token: 0x04000D75 RID: 3445
		private Color[] m_colours;

		// Token: 0x04000D76 RID: 3446
		private MeshRenderer[] m_targetMeshRenderers;

		// Token: 0x04000D77 RID: 3447
		private Animator m_targetAnimator;

		// Token: 0x04000D78 RID: 3448
		private ParticleSystem m_targetGlow;

		// Token: 0x04000D79 RID: 3449
		private float m_defaultGlowAlpha;

		// Token: 0x04000D7A RID: 3450
		private object m_fuseAudioToken = new object();
	}

}
