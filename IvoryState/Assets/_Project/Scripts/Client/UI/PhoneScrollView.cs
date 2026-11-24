using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace IvoryState.Client.UI
{
    /// <summary>
    /// Conteneur de défilement fluide style smartphone pour les pages et listes du téléphone :
    /// <list type="bullet">
    /// <item>Inertie et rebond élastique aux extrémités (<see cref="ScrollRect.MovementType.Elastic"/>).</item>
    /// <item>Lissage animé de la molette souris et des sauts programmatiques (ease-out).</item>
    /// <item>Indicateur de défilement latéral qui apparaît en mouvement et s'efface au repos.</item>
    /// </list>
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class PhoneScrollView : MonoBehaviour, IScrollHandler
    {
        private ScrollRect _scrollRect;
        private RectTransform _viewport;
        private RectTransform _content;
        private RectTransform _indicatorTrack;
        private RectTransform _indicatorPill;
        private Image _indicatorImg;

        private float _targetNormY = 1f;
        private bool _hasWheelTarget;
        private float _indicatorAlpha;
        private float _lastActivityTime = -10f;
        private float _lastNormY = 1f;
        private float _entranceTime = -10f;
        private float _requestedHeight = 200f;
        private const float EntranceDuration = 0.22f;
        private const float WheelStepPixels = 130f;

        public RectTransform Content => _content;
        public ScrollRect ScrollRect => _scrollRect;

        /// <summary>Crée un <see cref="PhoneScrollView"/> étiré dans <paramref name="parent"/> avec marges optionnelles.</summary>
        public static PhoneScrollView Create(Transform parent, string name, float topMargin = 0f, float bottomMargin = 0f)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(RectMask2D), typeof(ScrollRect), typeof(PhoneScrollView));
            var viewport = (RectTransform)root.transform;
            viewport.SetParent(parent, false);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(0f, bottomMargin);
            viewport.offsetMax = new Vector2(0f, -topMargin);

            // Fond quasi transparent nécessaire pour capter le glisser partout dans la page.
            var bg = root.GetComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.002f);
            bg.raycastTarget = true;

            var contentGo = new GameObject("Content", typeof(RectTransform));
            var content = (RectTransform)contentGo.transform;
            content.SetParent(viewport, false);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, 400f);

            var scroll = root.GetComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.elasticity = 0.085f;
            scroll.inertia = true;
            scroll.decelerationRate = 0.08f;
            scroll.scrollSensitivity = 0f; // géré par lissage animé dans OnScroll/Update

            // Barre indicatrice flottante à droite.
            var trackGo = new GameObject("ScrollTrack", typeof(RectTransform));
            var track = (RectTransform)trackGo.transform;
            track.SetParent(viewport, false);
            track.anchorMin = new Vector2(1f, 0f);
            track.anchorMax = new Vector2(1f, 1f);
            track.pivot = new Vector2(1f, 0.5f);
            track.sizeDelta = new Vector2(8f, -16f);
            track.anchoredPosition = new Vector2(-4f, 0f);

            var pillGo = new GameObject("ScrollPill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var pill = (RectTransform)pillGo.transform;
            pill.SetParent(track, false);
            pill.anchorMin = new Vector2(0.5f, 1f);
            pill.anchorMax = new Vector2(0.5f, 1f);
            pill.pivot = new Vector2(0.5f, 1f);
            pill.sizeDelta = new Vector2(4.5f, 64f);
            pill.anchoredPosition = Vector2.zero;

            var pillImg = pillGo.GetComponent<Image>();
            pillImg.sprite = HudIconLibrary.Pill;
            pillImg.type = Image.Type.Sliced;
            pillImg.color = new Color(1f, 1f, 1f, 0f);
            pillImg.raycastTarget = false;

            var view = root.GetComponent<PhoneScrollView>();
            view._scrollRect = scroll;
            view._viewport = viewport;
            view._content = content;
            view._indicatorTrack = track;
            view._indicatorPill = pill;
            view._indicatorImg = pillImg;
            return view;
        }

        public void SetContentHeight(float height)
        {
            _requestedHeight = Mathf.Max(200f, height);
            SyncContentHeight();
        }

        private void SyncContentHeight()
        {
            if (_content == null)
                return;
            float viewHeight = _viewport != null ? _viewport.rect.height : 0f;
            float clamped = Mathf.Max(_requestedHeight, Mathf.Max(200f, viewHeight));
            if (Mathf.Abs(_content.sizeDelta.y - clamped) > 0.5f)
                _content.sizeDelta = new Vector2(0f, clamped);
        }

        /// <summary>Replace en haut avec une brève animation de glissement à l'ouverture d'une page.</summary>
        public void ResetToTopAnimated(bool playEntrance = true)
        {
            _hasWheelTarget = false;
            _targetNormY = 1f;
            if (_scrollRect != null)
            {
                _scrollRect.StopMovement();
                _scrollRect.verticalNormalizedPosition = 1f;
            }
            if (_content != null)
            {
                if (playEntrance)
                {
                    _entranceTime = Time.unscaledTime;
                    _content.anchoredPosition = new Vector2(0f, -24f);
                }
                else
                {
                    _entranceTime = -10f;
                    _content.anchoredPosition = Vector2.zero;
                }
            }
            _lastActivityTime = Time.unscaledTime;
            _indicatorAlpha = 0.5f;
        }

        public void SmoothScrollTo(float normalizedY)
        {
            _targetNormY = Mathf.Clamp01(normalizedY);
            _hasWheelTarget = true;
            _lastActivityTime = Time.unscaledTime;
        }

        public void OnScroll(PointerEventData eventData)
        {
            if (_scrollRect == null || _content == null || _viewport == null)
                return;

            float overflow = _content.rect.height - _viewport.rect.height;
            if (overflow <= 2f)
                return;

            if (!_hasWheelTarget)
                _targetNormY = _scrollRect.verticalNormalizedPosition;

            float deltaNorm = (eventData.scrollDelta.y * WheelStepPixels) / overflow;
            _targetNormY = Mathf.Clamp(_targetNormY + deltaNorm, -0.06f, 1.06f);
            _hasWheelTarget = true;
            _lastActivityTime = Time.unscaledTime;
        }

        private void Update()
        {
            if (_scrollRect == null || _content == null || _viewport == null)
                return;

            SyncContentHeight();
            float dt = Time.unscaledDeltaTime;

            // Animation d'entrée douce quand la page vient de s'afficher.
            float entranceAge = Time.unscaledTime - _entranceTime;
            if (entranceAge >= 0f && entranceAge < EntranceDuration && !_hasWheelTarget)
            {
                float t = entranceAge / EntranceDuration;
                float ease = 1f - Mathf.Pow(1f - t, 3f);
                _content.anchoredPosition = new Vector2(0f, Mathf.Lerp(-24f, 0f, ease));
            }

            // Lissage animé de la cible molette / programmatic scroll.
            if (_hasWheelTarget)
            {
                float current = _scrollRect.verticalNormalizedPosition;
                float clampedTarget = Mathf.Clamp01(_targetNormY);
                float next = Mathf.Lerp(current, _targetNormY, 1f - Mathf.Exp(-16f * dt));
                _scrollRect.verticalNormalizedPosition = next;
                _targetNormY = Mathf.Lerp(_targetNormY, clampedTarget, 1f - Mathf.Exp(-10f * dt));

                if (Mathf.Abs(next - clampedTarget) < 0.001f && Mathf.Abs(_targetNormY - clampedTarget) < 0.001f)
                {
                    _scrollRect.verticalNormalizedPosition = clampedTarget;
                    _hasWheelTarget = false;
                }
            }

            UpdateIndicator(dt);
        }

        private void UpdateIndicator(float dt)
        {
            if (_indicatorPill == null || _indicatorImg == null || _indicatorTrack == null)
                return;

            float viewH = _viewport.rect.height;
            float contentH = _content.rect.height;
            if (viewH <= 1f || contentH <= viewH + 4f)
            {
                _indicatorImg.color = new Color(1f, 1f, 1f, 0f);
                return;
            }

            float norm = _scrollRect.verticalNormalizedPosition;
            if (Mathf.Abs(norm - _lastNormY) > 0.0008f || _scrollRect.velocity.sqrMagnitude > 4f || _hasWheelTarget)
                _lastActivityTime = Time.unscaledTime;
            _lastNormY = norm;

            bool active = Time.unscaledTime - _lastActivityTime < 0.85f;
            float targetAlpha = active ? 0.58f : 0f;
            _indicatorAlpha = Mathf.MoveTowards(_indicatorAlpha, targetAlpha, dt * (active ? 6f : 2.4f));

            float trackH = _indicatorTrack.rect.height;
            float ratio = Mathf.Clamp(viewH / contentH, 0.12f, 0.85f);
            float pillH = Mathf.Max(34f, trackH * ratio);

            // Compression élastique si dépassement haut/bas.
            float overscroll = norm > 1f ? norm - 1f : (norm < 0f ? -norm : 0f);
            pillH = Mathf.Max(22f, pillH * (1f - Mathf.Clamp01(overscroll * 2.5f) * 0.35f));

            float clampedNorm = Mathf.Clamp01(norm);
            float travel = Mathf.Max(0f, trackH - pillH);
            _indicatorPill.sizeDelta = new Vector2(4.5f, pillH);
            _indicatorPill.anchoredPosition = new Vector2(0f, -(1f - clampedNorm) * travel);

            Color c = _indicatorImg.color;
            c.a = _indicatorAlpha;
            _indicatorImg.color = c;
        }
    }
}
