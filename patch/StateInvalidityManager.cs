using UnityEngine;

namespace SuperchargedPatch
{
    public class StateInvalidityManager
    {
        public static string InvalidReason { get; set; } = null;
        public static bool PreventInvalidState { get; set; } = false;
        public static bool IsInvalid
        {
            get
            {
                return InvalidReason != null;
            }
        }

        private static GUIStyle style;

        public static void OnGUI()
        {
            // display invalid reason as a banner on screen
            if (IsInvalid)
            {
                if (style == null)
                {
                    style = new GUIStyle
                    {
                        fontSize = 50,
                        font = Font.CreateDynamicFontFromOSFont("Arial", 50),
                        fontStyle = FontStyle.Bold,
                        alignment = TextAnchor.UpperLeft,
                        padding = new RectOffset(4, 4, 1, 1),
                        normal = new GUIStyleState
                        {
                            textColor = Color.red,
                            background = Texture2D.whiteTexture,
                        }
                    };
                }
                var content = new GUIContent("Invalid Game State: " + InvalidReason);
                GUI.Label(new Rect(Vector2.zero, style.CalcSize(content)), InvalidReason, style);
            }
        }

        public static void Destroy()
        {
            style = null;
            InvalidReason = null;
            PreventInvalidState = false;
        }
    }
}
