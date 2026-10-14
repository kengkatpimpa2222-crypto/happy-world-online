using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HappyWorld.Unity.UI
{
    /// <summary>
    /// Builds uGUI from code so the project needs no hand-made scenes or prefabs.
    /// Font: Resources/HWOFont (bundled Thai font), falling back to Unity's built-in LegacyRuntime.ttf.
    /// </summary>
    public static class UiKit
    {
        private static Font _font;

        public static Font Font
        {
            get
            {
                if (_font == null) _font = Resources.Load<Font>("HWOFont");
                if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return _font;
            }
        }

        public static readonly Color Panel = new Color(1f, 0.99f, 0.96f, 0.97f);
        public static readonly Color Primary = new Color(0.27f, 0.6f, 0.95f);
        public static readonly Color Accent = new Color(1f, 0.62f, 0.25f);
        public static readonly Color Good = new Color(0.3f, 0.75f, 0.4f);
        public static readonly Color Danger = new Color(0.9f, 0.35f, 0.35f);
        public static readonly Color Muted = new Color(0.6f, 0.6f, 0.65f);
        public static readonly Color Ink = new Color(0.15f, 0.15f, 0.2f);

        private static Sprite _rounded;

        /// <summary>
        /// A white rounded-corner sprite drawn in code (no image asset), 9-sliced so any box can use it.
        /// Buttons, rows, panels and toasts use it so the whole UI reads soft and friendly instead of boxy.
        /// </summary>
        public static Sprite Rounded
        {
            get
            {
                if (_rounded != null) return _rounded;
                const int size = 64, r = 22;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "UiRounded" };
                var px = new Color32[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float cx = Mathf.Clamp(x + 0.5f, r, size - r), cy = Mathf.Clamp(y + 0.5f, r, size - r);
                        float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                        px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(r - d + 0.5f) * 255));
                    }
                tex.SetPixels32(px);
                tex.Apply(false, true);
                _rounded = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
                return _rounded;
            }
        }

        /// <summary>Gives a box the rounded look (and an optional soft drop shadow).</summary>
        public static Image Round(this Image img, bool shadow = false, float radiusScale = 1f)
        {
            img.sprite = Rounded;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 1f / Mathf.Max(0.1f, radiusScale);
            if (shadow)
            {
                var s = img.gameObject.AddComponent<Shadow>();
                s.effectColor = new Color(0f, 0f, 0f, 0.22f);
                s.effectDistance = new Vector2(0f, -5f);
            }
            return img;
        }

        public static Canvas CreateCanvas()
        {
            var go = new GameObject("UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1f;
            if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            return canvas;
        }

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>Anchors a rect: min/max are 0..1 of the parent; offsets in reference pixels.</summary>
        public static RectTransform Place(this RectTransform r, Vector2 min, Vector2 max, Vector2 offMin = default(Vector2), Vector2 offMax = default(Vector2))
        {
            r.anchorMin = min;
            r.anchorMax = max;
            r.offsetMin = offMin;
            r.offsetMax = offMax;
            return r;
        }

        public static Image Box(string name, Transform parent, Color color)
        {
            var r = Rect(name, parent);
            var img = r.gameObject.AddComponent<Image>();
            img.color = color;
            return img;
        }

        public static Text Label(Transform parent, string text, int size = 34, TextAnchor align = TextAnchor.MiddleLeft, Color? color = null)
        {
            var r = Rect("Text", parent);
            var t = r.gameObject.AddComponent<Text>();
            t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.alignment = align;
            t.color = color ?? Ink;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        public static Button Button(Transform parent, string text, Color color, Action onClick, int size = 34)
        {
            var img = Box("Button", parent, color).Round(true);
            var b = img.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            img.gameObject.AddComponent<PressBounce>();
            var label = Label(img.transform, text, size, TextAnchor.MiddleCenter, Color.white);
            label.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(8, 4), new Vector2(-8, -4));
            b.onClick.AddListener(() =>
            {
                Audio.GameAudio.Sfx("sfx_click", 0.7f);
                onClick();
            });
            return b;
        }

        public static InputField Input(Transform parent, string placeholder, int maxChars)
        {
            var img = Box("Input", parent, Color.white);
            var outline = img.gameObject.AddComponent<Outline>();
            outline.effectColor = Muted;
            var field = img.gameObject.AddComponent<InputField>();
            var text = Label(img.transform, "", 36);
            text.supportRichText = false;
            text.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(16, 4), new Vector2(-16, -4));
            var ph = Label(img.transform, placeholder, 36, TextAnchor.MiddleLeft, Muted);
            ph.fontStyle = FontStyle.Italic;
            ph.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(16, 4), new Vector2(-16, -4));
            field.textComponent = text;
            field.placeholder = ph;
            field.characterLimit = maxChars;
            return field;
        }

        /// <summary>Vertical list inside a ScrollRect; returns the content transform to add rows to.</summary>
        public static RectTransform ScrollList(Transform parent)
        {
            var view = Box("Scroll", parent, new Color(0, 0, 0, 0.03f));
            view.rectTransform.Place(Vector2.zero, Vector2.one);
            view.gameObject.AddComponent<RectMask2D>();
            var scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            var content = Rect("Content", view.transform);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 10;
            layout.padding = new RectOffset(12, 12, 12, 12);
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = content;
            scroll.viewport = (RectTransform)view.transform;
            return content;
        }

        /// <summary>A list row: text on the left, buttons on the right.</summary>
        public static RectTransform Row(Transform list, string text, float height = 96)
        {
            var row = Box("Row", list, new Color(0.95f, 0.96f, 1f)).Round(false, 0.6f).rectTransform;
            var le = row.gameObject.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(16, 12, 8, 8);
            h.spacing = 10;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            var t = Label(row, text, 30);
            var tle = t.gameObject.AddComponent<LayoutElement>();
            tle.flexibleWidth = 1;
            return row;
        }

        public static Button RowButton(RectTransform row, string text, Color color, Action onClick, float width = 190)
        {
            var b = Button(row, text, color, onClick, 28);
            var le = b.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.minWidth = width;
            return b;
        }

        public static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
        }
    }

    /// <summary>Buttons squish a little under the finger and spring back: they feel pressable.</summary>
    public sealed class PressBounce : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        private float _target = 1f;

        public void OnPointerDown(PointerEventData e) => _target = 0.93f;
        public void OnPointerUp(PointerEventData e) => _target = 1f;
        public void OnPointerExit(PointerEventData e) => _target = 1f;

        private void Update()
        {
            var s = Mathf.Lerp(transform.localScale.x, _target, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 25f));
            transform.localScale = new Vector3(s, s, 1f);
        }
    }

    /// <summary>On-screen joystick for touch. Value is -1..1 on each axis.</summary>
    public sealed class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public Vector2 Value { get; private set; }
        private RectTransform _base, _knob;
        private float _radius = 110f;

        public static VirtualJoystick Create(Transform parent)
        {
            var area = UiKit.Box("JoystickArea", parent, new Color(0, 0, 0, 0));
            area.rectTransform.Place(new Vector2(0, 0), new Vector2(0.4f, 0.55f));
            var j = area.gameObject.AddComponent<VirtualJoystick>();
            var b = UiKit.Box("Base", area.transform, new Color(1, 1, 1, 0.25f));
            b.rectTransform.anchorMin = b.rectTransform.anchorMax = new Vector2(0, 0);
            b.rectTransform.sizeDelta = new Vector2(260, 260);
            b.rectTransform.anchoredPosition = new Vector2(220, 220);
            b.raycastTarget = false;
            var k = UiKit.Box("Knob", b.transform, new Color(1, 1, 1, 0.7f));
            k.rectTransform.sizeDelta = new Vector2(120, 120);
            k.raycastTarget = false;
            j._base = b.rectTransform;
            j._knob = k.rectTransform;
            return j;
        }

        public void OnPointerDown(PointerEventData e) => OnDrag(e);

        public void OnDrag(PointerEventData e)
        {
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_base, e.position, e.pressEventCamera, out local)) return;
            var v = Vector2.ClampMagnitude(local, _radius);
            _knob.anchoredPosition = v;
            Value = v / _radius;
        }

        public void OnPointerUp(PointerEventData e)
        {
            _knob.anchoredPosition = Vector2.zero;
            Value = Vector2.zero;
        }
    }
}
