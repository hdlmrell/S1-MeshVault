#if DEBUG
using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

#if IL2CPP
using Il2CppTMPro;
#else
using TMPro;
#endif

namespace MeshVault.Tools
{
    /// <summary>
    /// Minimal TMP-based UI factory for debug tool panels.
    /// Provides Panel, Text, ScrollableVerticalList, and RoundedButtonWithLabel.
    /// </summary>
    internal static class UIHelper
    {
        /// <summary>
        /// Creates a panel GameObject with an Image background.
        /// </summary>
        internal static GameObject Panel(string name, Transform parent, Color bgColor, bool fullAnchor = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var rt = go.AddComponent<RectTransform>();
            if (fullAnchor)
            {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }

            var img = go.AddComponent<Image>();
            img.color = bgColor;
            return go;
        }

        /// <summary>
        /// Creates a TextMeshProUGUI element with the supplied content and styling.
        /// </summary>
        internal static TextMeshProUGUI Text(string name, string content, Transform parent,
            int fontSize = 14, TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = content;
            tmp.fontSize = fontSize;
            tmp.alignment = alignment;
            tmp.color = Color.white;
            tmp.richText = true;
            SetWrapping(tmp, true);

            return tmp;
        }

        /// <summary>
        /// Sets word wrapping on a TMP text element using the correct API per platform.
        /// </summary>
        internal static void SetWrapping(TextMeshProUGUI tmp, bool wrap)
        {
#if IL2CPP
            tmp.enableWordWrapping = wrap;
#else
            tmp.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
#endif
        }

        /// <summary>
        /// Builds a ScrollRect hierarchy for a vertical scrolling list.
        /// Returns the content Transform where child items should be added.
        /// </summary>
        internal static Transform ScrollableVerticalList(string name, Transform parent, out ScrollRect scrollRect)
        {
            // ScrollRect container
            var scrollGO = new GameObject(name);
            scrollGO.transform.SetParent(parent, false);

            var scrollRT = scrollGO.AddComponent<RectTransform>();
            scrollRT.anchorMin = Vector2.zero;
            scrollRT.anchorMax = Vector2.one;
            scrollRT.offsetMin = Vector2.zero;
            scrollRT.offsetMax = Vector2.zero;

            scrollRect = scrollGO.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;

            // Viewport
            var viewportGO = new GameObject("Viewport");
            viewportGO.transform.SetParent(scrollGO.transform, false);

            var viewportRT = viewportGO.AddComponent<RectTransform>();
            viewportRT.anchorMin = Vector2.zero;
            viewportRT.anchorMax = Vector2.one;
            viewportRT.offsetMin = Vector2.zero;
            viewportRT.offsetMax = Vector2.zero;

            // Transparent image gives the viewport a raycast target so ScrollRect
            // receives wheel events anywhere in the viewport, not just over child rows.
            var viewportImg = viewportGO.AddComponent<Image>();
            viewportImg.color = new Color(0, 0, 0, 0);
            viewportImg.raycastTarget = true;
            viewportGO.AddComponent<RectMask2D>();

            scrollRect.viewport = viewportRT;
            scrollRect.movementType = ScrollRect.MovementType.Elastic;
            scrollRect.elasticity = 0.08f;
            scrollRect.inertia = true;
            scrollRect.decelerationRate = 0.06f;
            scrollRect.scrollSensitivity = 12f;

            // Content
            var contentGO = new GameObject("Content");
            contentGO.transform.SetParent(viewportGO.transform, false);

            var contentRT = contentGO.AddComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0, 1);
            contentRT.anchorMax = new Vector2(1, 1);
            contentRT.pivot = new Vector2(0.5f, 1);
            contentRT.sizeDelta = Vector2.zero;

            var vlg = contentGO.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 2;
            vlg.padding = new RectOffset(2, 2, 2, 2);
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;

            var csf = contentGO.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollRect.content = contentRT;

            return contentGO.transform;
        }

        private static Sprite _roundedSprite;

        /// <summary>
        /// Creates a rounded button with mask, inner Image, Button, and TMP label.
        /// </summary>
        internal static (GameObject, Button, TextMeshProUGUI) RoundedButtonWithLabel(
            string name, string label, Transform parent,
            Color bgColor, float width, float height, int fontSize, Color textColor)
        {
            var maskGO = new GameObject(name + "_RoundedMask");
            maskGO.transform.SetParent(parent, false);

            var maskRT = maskGO.AddComponent<RectTransform>();
            maskRT.sizeDelta = new Vector2(width, height);

            var layoutElement = maskGO.AddComponent<LayoutElement>();
            layoutElement.preferredWidth = width;
            layoutElement.preferredHeight = height;

            var maskImage = maskGO.AddComponent<Image>();
            maskImage.sprite = GetRoundedSprite();
            maskImage.type = Image.Type.Sliced;
            maskImage.color = Color.white;

            var maskComp = maskGO.AddComponent<Mask>();
            maskComp.showMaskGraphic = false;

            // Inner button fills the mask
            var buttonGO = new GameObject(name);
            buttonGO.transform.SetParent(maskGO.transform, false);

            var rt = buttonGO.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var img = buttonGO.AddComponent<Image>();
            img.color = bgColor;
            img.sprite = GetRoundedSprite();
            img.type = Image.Type.Sliced;

            var btn = buttonGO.AddComponent<Button>();
            btn.targetGraphic = img;

            // TMP label
            var tmp = Text("Label", label, buttonGO.transform, fontSize, TextAlignmentOptions.Center);
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = textColor;

            return (maskGO, btn, tmp);
        }

        private static Sprite GetRoundedSprite()
        {
            if (_roundedSprite != null)
                return _roundedSprite;

            int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
            float radius = 6f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool isCorner =
                        (x < radius && y < radius && Vector2.Distance(new Vector2(x, y), new Vector2(radius, radius)) > radius) ||
                        (x > size - radius - 1 && y < radius && Vector2.Distance(new Vector2(x, y), new Vector2(size - radius - 1, radius)) > radius) ||
                        (x < radius && y > size - radius - 1 && Vector2.Distance(new Vector2(x, y), new Vector2(radius, size - radius - 1)) > radius) ||
                        (x > size - radius - 1 && y > size - radius - 1 && Vector2.Distance(new Vector2(x, y), new Vector2(size - radius - 1, size - radius - 1)) > radius);

                    tex.SetPixel(x, y, isCorner ? new Color(0, 0, 0, 0) : Color.white);
                }
            }

            tex.Apply();
            var border = new Vector4(8, 8, 8, 8);
            _roundedSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
            return _roundedSprite;
        }

        /// <summary>
        /// Makes a panel draggable by attaching EventTrigger drag callbacks.
        /// The panel must have a RectTransform. Uses EventTrigger for IL2CPP compatibility.
        /// </summary>
        internal static void MakeDraggable(GameObject panel)
        {
            var rt = panel.GetComponent<RectTransform>();
            if (rt == null) return;

            var trigger = panel.AddComponent<EventTrigger>();
            Vector2 dragOffset = Vector2.zero;

            var beginEntry = new EventTrigger.Entry { eventID = EventTriggerType.BeginDrag };
            beginEntry.callback.AddListener(new Action<BaseEventData>(data =>
            {
                var pointer = (PointerEventData)data;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rt.parent as RectTransform, pointer.position, pointer.pressEventCamera, out var localPoint);
                dragOffset = rt.anchoredPosition - localPoint;
            }));
            trigger.triggers.Add(beginEntry);

            var dragEntry = new EventTrigger.Entry { eventID = EventTriggerType.Drag };
            dragEntry.callback.AddListener(new Action<BaseEventData>(data =>
            {
                var pointer = (PointerEventData)data;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rt.parent as RectTransform, pointer.position, pointer.pressEventCamera, out var localPoint);
                rt.anchoredPosition = localPoint + dragOffset;
            }));
            trigger.triggers.Add(dragEntry);
        }
    }

#if !IL2CPP
    /// <summary>
    /// Forwards scroll events to the nearest parent ScrollRect so that
    /// buttons and other interactive elements don't swallow mouse wheel input.
    /// IL2CPP cannot implement Unity interfaces on injected types.
    /// </summary>
    internal class ScrollForwarder : MonoBehaviour, IScrollHandler
    {
        private ScrollRect _parentScroll;

        private void Awake()
        {
            _parentScroll = GetComponentInParent<ScrollRect>();
        }

        public void OnScroll(PointerEventData eventData)
        {
            if (_parentScroll != null)
                _parentScroll.OnScroll(eventData);
        }
    }

    /// <summary>
    /// Bridges System.Action delegates to UnityAction for Mono builds.
    /// IL2CPP handles this conversion automatically via Il2CppInterop.
    /// </summary>
    internal static class UnityEventExtensions
    {
        internal static void AddListener(this UnityEvent ev, Action action)
            => ev.AddListener(new UnityAction(action));

        internal static void AddListener<T0>(this UnityEvent<T0> ev, Action<T0> action)
            => ev.AddListener(new UnityAction<T0>(action));
    }
#endif
}
#endif
