using SuperchargedPatch.AlteredComponents;
using SuperchargedPatch.Extensions;
using System.Collections.Generic;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace SuperchargedPatch
{
    public class DebugItemOverlay
    {
        private static List<DebugItemOverlayProvider> providers = new List<DebugItemOverlayProvider>();
        private static GUIStyle style;

        public static void Awake()
        {
            // AddProviderFor<PlayerControls>("grav", c => c.m_bApplyGravity ? "ON" : "OFF");
            // AddProviderFor<PlayerControls>("pos", c =>
            // {
            //     // format position as 2 decimal places max
            //     return string.Format("{0:0.##}", c.transform.position);
            // });
            // AddProviderFor<Rigidbody>("kin", c => c.GetComponent<Rigidbody>().isKinematic ? "ON" : "OFF");
            // AddProviderFor<GroundCast>("gnorm", c =>
            // {
            //     return string.Format("{0:0.###}", c.GetGroundNormal());
            // });
            // AddProviderFor<GroundCast>("gmask", c =>
            // {
            //     return "" + (int)c.GetGroundLayer();
            // });
            // AddProviderFor<PlayerControls>("gstr", c => "" + c.Movement.GravityStrength);
            // AddProviderFor<PlayerControls>("scale", c =>
            // {
            //     return string.Format("{0:0.###}", c.transform.localScale);
            // });
            // AddProviderFor<PlayerControls>("parent", c =>
            // {
            //     return c.transform.parent?.name ?? "null";
            // });
            // AddProviderFor<CannonCosmeticDecisions>("transDur", c =>
            // {
            //     var info = c.m_cannonAnimator.GetAnimatorTransitionInfo(0);
            //     return "" + info.duration;
            // });
            // AddProviderFor<CannonCosmeticDecisions>("transTime", c =>
            // {
            //     var info = c.m_cannonAnimator.GetAnimatorTransitionInfo(0);
            //     return "" + info.normalizedTime;
            // });
            // AddProviderFor<CannonCosmeticDecisions>("transName", c =>
            // {
            //     var info = c.m_cannonAnimator.GetAnimatorTransitionInfo(0);
            //     return "" + info.nameHash;
            // });
            // AddProviderFor<CannonCosmeticDecisions>("stateTime", c =>
            // {
            //     var info = c.m_cannonAnimator.GetCurrentAnimatorStateInfo(0);
            //     return "" + info.normalizedTime;
            // });
            // AddProviderFor<CannonCosmeticDecisions>("stateName", c =>
            // {
            //     var info = c.m_cannonAnimator.GetCurrentAnimatorStateInfo(0);
            //     return "" + info.nameHash;
            // });
            // AddProviderFor<CannonCosmeticDecisions>("nextStateTime", c =>
            // {
            //     var info = c.m_cannonAnimator.GetNextAnimatorStateInfo(0);
            //     return "" + info.normalizedTime;
            // });
            // AddProviderFor<CannonCosmeticDecisions>("nextStateName", c =>
            // {
            //     var info = c.m_cannonAnimator.GetNextAnimatorStateInfo(0);
            //     return "" + info.nameHash;
            // });
            // AddProvider("name", go =>
            // {
            //     var entityId = EntitySerialisationRegistry.GetEntry(go);
            //     if (entityId == null)
            //     {
            //         return "null";
            //     }
            //     if (entityId.m_Header.m_uEntityID == 81)
            //     {
            //         return go.name;
            //     }
            //     return null;
            // });
            // AddProviderFor<ServerPilotRotation>("spr.startAngle", c =>
            // {
            //     return "" + c.m_startAngle();
            // });
            // AddProviderFor<ServerPilotRotation>("spr.angle", c =>
            // {
            //     return "" + c.m_angle();
            // });
            // AddProviderFor<PilotRotation>("pr.euler.y", c =>
            // {
            //     return "" + c.transform.eulerAngles.y;
            // });
            AddProviderFor<PlayerControls>("buttons", c =>
            {
                var controlScheme = c.ControlScheme;
                if (controlScheme == null)
                {
                    return "?";
                }
                var str = "";
                if (controlScheme.m_pickupButton is TASLogicalButton)
                {
                    if (controlScheme.m_pickupButton.IsDown())
                    {
                        str += "+P ";
                    } else
                    {
                        str += "-P ";
                    }
                } else
                {
                    str += "?P ";
                }
                if (controlScheme.m_worksurfaceUseButton is TASLogicalButton)
                {
                    if (controlScheme.m_worksurfaceUseButton.IsDown())
                    {
                        str += "+U ";
                    }
                    else
                    {
                        str += "-U ";
                    }
                }
                else
                {
                    str += "?U ";
                }
                if (controlScheme.m_dashButton is TASLogicalButton)
                {
                    if (controlScheme.m_dashButton.IsDown())
                    {
                        str += "+D";
                    }
                    else
                    {
                        str += "-D";
                    }
                }
                else
                {
                    str += "?D";
                }
                return str;
            });
            AddProviderFor<PlayerControls>("axes", c =>
            {
                var controlScheme = c.ControlScheme;
                if (controlScheme == null)
                {
                    return "?";
                }
                var str = "";
                if (controlScheme.m_moveX is TASLogicalValue)
                {
                    str += "X:" + string.Format("{0:0.###}", controlScheme.m_moveX.GetValue()) + " ";
                }
                else
                {
                    str += "X:? ";
                }
                if (controlScheme.m_moveY is TASLogicalValue)
                {
                    str += "Y:" + string.Format("{0:0.###}", controlScheme.m_moveY.GetValue());
                }
                else
                {
                    str += "Y:?";
                }
                return str;
            });
            AddProviderFor<PlayerControls>("canpress", c =>
            {
                return c.CanButtonBePressed() ? "YES" : "NO";
            });
        }

        public static void OnGUI()
        {
            if (style == null)
            {
                style = new GUIStyle();
                style.normal.textColor = Color.black;
                style.normal.background = Texture2D.whiteTexture;
                style.fontSize = 10;
                style.font = Font.CreateDynamicFontFromOSFont("Courier New", 10);
                style.fontStyle = FontStyle.Bold;
                style.alignment = TextAnchor.UpperLeft;
                style.padding = new RectOffset(4, 4, 1, 1);
            }

            if (EntitySerialisationRegistry.m_EntitiesList?._items == null)
            {
                return;
            }
            EntitySerialisationRegistry.m_EntitiesList.ForEach(entry =>
            {
                var screenPos = Camera.main.WorldToScreenPoint(entry.m_GameObject.transform.position).XY();
                var curPos = screenPos + new Vector2(-50, 50);
                foreach (var provider in providers)
                {
                    if (!provider.enabled)
                    {
                        continue;
                    }
                    var data = provider.provider(entry.m_GameObject);
                    if (data == null)
                    {
                        continue;
                    }
                    var guiContent = new GUIContent(provider.name + ": " + data);

                    var labelSize = style.CalcSize(guiContent);
                    GUI.Label(new Rect(curPos.x, Screen.height - curPos.y, labelSize.x, labelSize.y), guiContent, style);
                    curPos.y += labelSize.y;
                }
            });
        }

        public static void Destroy()
        {
            providers.Clear();
            style = null;
        }

        public static void AddProvider(string name, DebugItemOverlayDataProvider provider)
        {
            providers.Add(new DebugItemOverlayProvider()
            {
                name = name,
                provider = provider,
                enabled = true
            });
        }

        public static void AddProviderFor<T>(string name, DebugItemOverlayDataProviderFor<T> provider)
            where T : Component
        {
            var wrapped = new DebugItemOverlayDataProvider((o) =>
            {
                var component = o.GetComponent<T>();
                if (component == null)
                {
                    return null;
                }
                return provider(component);
            });
            AddProvider(name, wrapped);
        }
    }

    public delegate string DebugItemOverlayDataProvider(GameObject item);
    public delegate string DebugItemOverlayDataProviderFor<T>(T component) where T : Component;

    class DebugItemOverlayProvider
    {
        public DebugItemOverlayDataProvider provider;
        public string name;
        public bool enabled;
    }
}
