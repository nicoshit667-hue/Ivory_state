using System.Text;
using FishNet.Object;
using IvoryState.Client.Input;
using IvoryState.Core.Networking;
using IvoryState.Gameplay.Player;
using IvoryState.Gameplay.Social;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace IvoryState.Client.UI
{
    /// <summary>
    /// Interface du chat de proximité (Images 1 &amp; 2) :
    /// <list type="bullet">
    /// <item><b>Haut-centre</b> : champ souligné « Envoyer un message » + fil des derniers messages
    /// directement dessous (« Moi : Coucou », « Joueur : T'es ou ? »).</item>
    /// <item><b>Panneau de saisie</b> : phrases rapides d'Abidjan en un tap + saisie clavier.</item>
    /// <item><b>Bulles</b> au-dessus des têtes des joueurs proches.</item>
    /// </list>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ChatHud : MonoBehaviour
    {
        private const int LogLines = 3;
        private const float BubbleDuration = 5f;
        private const float BubbleMaxDistance = 35f;
        private const float BubbleHeight = 2.3f;

        /// <summary>Phrases rapides : expressions courantes à Abidjan, prêtes à envoyer en un tap.</summary>
        public static readonly string[] QuickPhrases =
        {
            "Bonjour !", "On dit quoi ?", "C'est comment ?", "Ça va un peu !",
            "Bonne arrivée !", "Merci, on est ensemble !", "On va au maquis ?", "Ça va aller !",
        };

        [SerializeField] private Button _chatButton;
        [SerializeField] private RectTransform _panel;
        [SerializeField] private Button[] _phraseButtons;
        [SerializeField] private InputField _input;
        [SerializeField] private Button _sendButton;
        [SerializeField] private Text _logText;
        [SerializeField] private GameObject _logBar;
        [SerializeField] private Text[] _bubbles;

        private readonly string[] _log = new string[LogLines];
        private readonly StringBuilder _logBuilder = new StringBuilder(512);
        private int _logCount;

        private Transform[] _bubbleTargets;
        private float[] _bubbleExpiry;
        private int _nextBubble;
        private UnityEngine.Camera _camera;

        private void Awake()
        {
            _bubbleTargets = new Transform[_bubbles.Length];
            _bubbleExpiry = new float[_bubbles.Length];

            for (int i = 0; i < _phraseButtons.Length && i < QuickPhrases.Length; i++)
            {
                string phrase = QuickPhrases[i];
                _phraseButtons[i].GetComponentInChildren<Text>().text = phrase;
                _phraseButtons[i].onClick.AddListener(() => SendChat(phrase));
            }

            _chatButton.onClick.AddListener(TogglePanel);
            _sendButton.onClick.AddListener(SendInput);
            _input.characterLimit = PlayerChat.MaxLength;
            _input.onEndEdit.AddListener(OnEndEdit);

            RestyleTopCenterChat();
        }

        /// <summary>
        /// Positionne le champ « Envoyer un message » souligné en haut au centre et la liste des
        /// messages juste en dessous, exactement comme sur les maquettes.
        /// </summary>
        private void RestyleTopCenterChat()
        {
            // Masque l'ancienne boussole en haut au centre pour laisser la place au chat.
            Transform compass = transform.parent != null ? transform.parent.Find("Compass") : null;
            if (compass != null)
                compass.gameObject.SetActive(false);

            if (_chatButton != null)
            {
                var btnRect = (RectTransform)_chatButton.transform;
                btnRect.anchorMin = btnRect.anchorMax = btnRect.pivot = new Vector2(0.5f, 1f);
                btnRect.sizeDelta = new Vector2(560f, 44f);
                btnRect.anchoredPosition = new Vector2(0f, -20f);

                if (_chatButton.TryGetComponent(out Image btnImg))
                    btnImg.color = new Color(0f, 0f, 0f, 0.01f); // transparent mais cliquable

                Text label = _chatButton.GetComponentInChildren<Text>();
                if (label != null)
                {
                    label.text = "Envoyer un message";
                    label.fontSize = 25;
                    label.alignment = TextAnchor.MiddleLeft;
                    label.color = new Color(1f, 1f, 1f, 0.72f);
                    var lRect = (RectTransform)label.transform;
                    lRect.offsetMin = new Vector2(4f, 4f);
                    lRect.offsetMax = new Vector2(-4f, -2f);
                    if (label.GetComponent<Shadow>() == null)
                        label.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.75f);
                }

                // Ligne blanche fine de soulignement
                if (btnRect.Find("Underline") == null)
                {
                    var lineGo = new GameObject("Underline", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                    var lineRect = (RectTransform)lineGo.transform;
                    lineRect.SetParent(btnRect, false);
                    lineRect.anchorMin = new Vector2(0f, 0f);
                    lineRect.anchorMax = new Vector2(1f, 0f);
                    lineRect.pivot = new Vector2(0.5f, 0f);
                    lineRect.sizeDelta = new Vector2(0f, 2f);
                    lineRect.anchoredPosition = Vector2.zero;
                    var lineImg = lineGo.GetComponent<Image>();
                    lineImg.color = new Color(1f, 1f, 1f, 0.55f);
                    lineImg.raycastTarget = false;
                }
            }

            if (_logBar != null)
            {
                var barRect = (RectTransform)_logBar.transform;
                barRect.anchorMin = barRect.anchorMax = barRect.pivot = new Vector2(0.5f, 1f);
                barRect.sizeDelta = new Vector2(560f, 96f);
                barRect.anchoredPosition = new Vector2(0f, -70f);
                if (_logBar.TryGetComponent(out Image barImg))
                {
                    barImg.color = new Color(0f, 0f, 0f, 0f);
                    barImg.raycastTarget = false;
                }
            }

            if (_logText != null)
            {
                _logText.fontSize = 25;
                _logText.alignment = TextAnchor.UpperLeft;
                _logText.lineSpacing = 1.12f;
                var logRect = (RectTransform)_logText.transform;
                logRect.offsetMin = new Vector2(4f, 0f);
                logRect.offsetMax = new Vector2(-4f, 0f);
            }

            if (_panel != null)
            {
                _panel.anchorMin = _panel.anchorMax = _panel.pivot = new Vector2(0.5f, 1f);
                _panel.anchoredPosition = new Vector2(0f, -170f);
            }
        }

        private void OnEnable()
        {
            PlayerChat.LocalMessageReceived += OnMessage;
            MobilePlayerControls.RegisterBlockingRect((RectTransform)_chatButton.transform);
            MobilePlayerControls.RegisterBlockingRect(_panel);
        }

        private void OnDisable()
        {
            PlayerChat.LocalMessageReceived -= OnMessage;
            MobilePlayerControls.UnregisterBlockingRect((RectTransform)_chatButton.transform);
            MobilePlayerControls.UnregisterBlockingRect(_panel);
            MobilePlayerControls.KeyboardSuspended = false;
        }

        private void Start()
        {
            _panel.gameObject.SetActive(false);
            ShowPlaceholder();
            for (int i = 0; i < _bubbles.Length; i++)
                _bubbles[i].gameObject.SetActive(false);
        }

        private void Update()
        {
            bool connected = PlayerChat.Local != null;
            if (_chatButton.gameObject.activeSelf != connected)
                _chatButton.gameObject.SetActive(connected);
            if (_logBar != null && _logBar.activeSelf != connected)
                _logBar.SetActive(connected);
            if (!connected && _panel.gameObject.activeSelf)
                _panel.gameObject.SetActive(false);

            MobilePlayerControls.KeyboardSuspended = _input.isFocused;

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && connected)
            {
                if (!_input.isFocused && keyboard.tKey.wasPressedThisFrame)
                {
                    _panel.gameObject.SetActive(true);
                    _input.ActivateInputField();
                }
                else if (keyboard.escapeKey.wasPressedThisFrame && _panel.gameObject.activeSelf)
                {
                    _panel.gameObject.SetActive(false);
                }
            }
        }

        private void LateUpdate()
        {
            if (_camera == null)
                _camera = UnityEngine.Camera.main;
            if (_camera == null)
                return;

            Vector3 cameraPosition = _camera.transform.position;
            float now = Time.unscaledTime;

            for (int i = 0; i < _bubbles.Length; i++)
            {
                Text bubble = _bubbles[i];
                if (!bubble.gameObject.activeSelf)
                    continue;

                Transform target = _bubbleTargets[i];
                if (target == null || now > _bubbleExpiry[i])
                {
                    bubble.gameObject.SetActive(false);
                    continue;
                }

                Vector3 head = target.position + Vector3.up * BubbleHeight;
                Vector3 screen = _camera.WorldToScreenPoint(head);
                bool visible = screen.z > 0f && (head - cameraPosition).sqrMagnitude < BubbleMaxDistance * BubbleMaxDistance;

                bubble.enabled = visible;
                if (visible)
                    bubble.rectTransform.position = new Vector3(screen.x, screen.y, 0f);
            }
        }

        private void ShowPlaceholder()
        {
            _logText.text = string.Empty;
            _logText.color = Color.white;
        }

        private void TogglePanel()
        {
            bool open = !_panel.gameObject.activeSelf;
            _panel.gameObject.SetActive(open);
            if (open)
                _input.ActivateInputField();
        }

        private void OnEndEdit(string text)
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame))
                SendInput();
        }

        private void SendInput()
        {
            SendChat(_input.text);
            _input.text = string.Empty;
        }

        private void SendChat(string text)
        {
            if (PlayerChat.Local == null || string.IsNullOrWhiteSpace(text))
                return;

            PlayerChat.Local.Send(text);
            _panel.gameObject.SetActive(false);
        }

        private void OnMessage(int speakerObjectId, string speakerName, string text)
        {
            bool isLocal = PlayerNetworkIdentity.Local != null && PlayerNetworkIdentity.Local.ObjectId == speakerObjectId;
            string prefix = isLocal ? "Moi : " : (speakerName + " : ");
            AppendLog(prefix + text);
            ShowBubble(speakerObjectId, text);
        }

        private void AppendLog(string line)
        {
            if (_logCount == LogLines)
            {
                for (int i = 1; i < LogLines; i++)
                    _log[i - 1] = _log[i];
                _logCount--;
            }

            _log[_logCount++] = line;

            _logBuilder.Clear();
            for (int i = 0; i < _logCount; i++)
            {
                if (i > 0)
                    _logBuilder.Append('\n');
                _logBuilder.Append(_log[i]);
            }

            _logText.text = _logBuilder.ToString();
            _logText.color = Color.white;
        }

        private void ShowBubble(int speakerObjectId, string text)
        {
            Transform target = FindSpeaker(speakerObjectId);
            if (target == null)
                return;

            int slot = -1;
            for (int i = 0; i < _bubbles.Length; i++)
            {
                if (_bubbleTargets[i] == target && _bubbles[i].gameObject.activeSelf)
                {
                    slot = i;
                    break;
                }
            }

            if (slot < 0)
            {
                slot = _nextBubble;
                _nextBubble = (_nextBubble + 1) % _bubbles.Length;
            }

            _bubbles[slot].text = text;
            _bubbleTargets[slot] = target;
            _bubbleExpiry[slot] = Time.unscaledTime + BubbleDuration;
            _bubbles[slot].gameObject.SetActive(true);
        }

        private static Transform FindSpeaker(int objectId)
        {
            NetworkBootstrap bootstrap = NetworkBootstrap.Instance;
            if (bootstrap == null ||
                !bootstrap.NetworkManager.ClientManager.Objects.Spawned.TryGetValue(objectId, out NetworkObject speaker) ||
                speaker == null)
                return null;

            return speaker.TryGetComponent(out PlayerMotor motor) ? motor.GraphicalTransform : speaker.transform;
        }
    }
}
