using IvoryState.Client.Input;
using IvoryState.Client.Phone;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace IvoryState.Client.UI
{
    /// <summary>
    /// Bouton micro du HUD : un appui active la voix de proximité, un second la coupe.
    /// Affiche l'icône PNG personnalisée <c>microphone_icone</c> centrée en enfant du bouton rond
    /// avec une marge intérieure de 22 % et <c>Preserve Aspect = true</c>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProximityMicButton : MonoBehaviour, IPointerClickHandler
    {
        private static readonly Color On = new Color(0.45f, 1f, 0.65f, 1f);
        private static readonly Color Off = Color.white;

        private Image _micIcon;
        private Image _liveDot;

        private void Awake()
        {
            HudIconLibrary.EnsureInitialized();

            var iconGo = new GameObject("MicIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            iconGo.transform.SetParent(transform, false);
            var iconRect = (RectTransform)iconGo.transform;
            iconRect.anchorMin = new Vector2(0.22f, 0.22f);
            iconRect.anchorMax = new Vector2(0.78f, 0.78f);
            iconRect.offsetMin = Vector2.zero;
            iconRect.offsetMax = Vector2.zero;

            _micIcon = iconGo.GetComponent<Image>();
            _micIcon.sprite = HudIconLibrary.Microphone;
            _micIcon.preserveAspect = true;
            _micIcon.color = Off;
            _micIcon.raycastTarget = false;

            var dotGo = new GameObject("Direct", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            dotGo.transform.SetParent(transform, false);
            var dotRect = (RectTransform)dotGo.transform;
            dotRect.anchorMin = dotRect.anchorMax = dotRect.pivot = new Vector2(0.5f, 0.5f);
            dotRect.sizeDelta = new Vector2(14f, 14f);
            dotRect.anchoredPosition = new Vector2(36f, 36f);

            _liveDot = dotGo.GetComponent<Image>();
            _liveDot.sprite = HudIconLibrary.Circle;
            _liveDot.preserveAspect = true;
            _liveDot.color = new Color(0.30f, 0.95f, 0.45f, 1f);
            _liveDot.raycastTarget = false;

            Refresh();
        }

        private void OnEnable()
        {
            MobilePlayerControls.RegisterBlockingRect((RectTransform)transform);
        }

        private void OnDisable()
        {
            MobilePlayerControls.UnregisterBlockingRect((RectTransform)transform);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            ProximityVoice.Enabled = !ProximityVoice.Enabled;
            Refresh();
        }

        private void Update()
        {
            if (_liveDot != null && ProximityVoice.Enabled)
            {
                float level = Mathf.Clamp01(VoiceChat.InputLevel * 4f);
                _liveDot.rectTransform.localScale = Vector3.one * (0.85f + level * 0.55f);
            }
        }

        private void Refresh()
        {
            bool on = ProximityVoice.Enabled;
            if (_micIcon != null)
                _micIcon.color = on ? On : Off;
            if (_liveDot != null)
                _liveDot.gameObject.SetActive(on);
        }
    }
}
