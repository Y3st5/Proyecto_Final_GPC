using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Controlador del menú de inicio basado en UI Toolkit.
/// Se conecta al <see cref="UIDocument"/>, enlaza los botones por nombre,
/// gestiona los subpaneles modales (Opciones y Créditos), los ajustes de
/// audio/calidad y la transición entre el menú y el juego dentro de la misma escena.
///
/// No recarga la escena al pulsar Jugar: desactiva el menú, activa al jugador
/// y bloquea el cursor. Con Esc durante la partida se puede volver al menú.
///
/// Requiere que el mismo GameObject tenga un UIDocument con MainMenu.uxml
/// y un PanelSettings válido.
/// </summary>
[RequireComponent(typeof(UIDocument))]
[DisallowMultipleComponent]
public class MainMenuController : MonoBehaviour
{
    private const string VolumePrefKey = "mainmenu.volume";
    private const string QualityPrefKey = "mainmenu.quality";
    private const long ModalFadeMs = 220;

    [Header("Modo de juego (una sola escena)")]
    [Tooltip("GameObject del jugador. Se desactiva mientras el menú está abierto y se activa al pulsar Jugar.")]
    [SerializeField] private GameObject playerObject;

    [Tooltip("Cámara opcional para el menú. Solo si usas una cámara distinta a la del jugador. " +
             "Se activa con el menú y se desactiva al empezar a jugar.")]
    [SerializeField] private Camera menuCamera;

    [Tooltip("Scripts de control que deben desactivarse mientras el menú está abierto " +
             "(por ejemplo PlayerCameraFollow si está en otra cámara). Se restauran al jugar.")]
    [SerializeField] private List<MonoBehaviour> playerControls = new List<MonoBehaviour>();

    [Tooltip("Permite volver al menú pulsando Esc durante la partida.")]
    [SerializeField] private bool returnToMenuWithEscape = true;

    [Header("Navegación")]
    [Tooltip("Duración del fundido del menú al pulsar Jugar.")]
    [SerializeField, Min(0f)] private float fadeOutDuration = 0.35f;

    [Header("Ajustes predeterminados")]
    [Tooltip("Volumen inicial (0-100) si no hay un valor guardado.")]
    [SerializeField, Range(0, 100)] private int defaultVolume = 80;

    private UIDocument _uiDocument;
    private VisualElement _root;

    private VisualElement _modalLayer;
    private VisualElement _optionsPanel;
    private VisualElement _creditsPanel;

    private Button _playButton;
    private Button _optionsButton;
    private Button _creditsButton;
    private Button _quitButton;
    private Button _optionsBackButton;
    private Button _optionsResetButton;
    private Button _creditsBackButton;

    private Slider _volumeSlider;
    private Slider _qualitySlider;
    private Label _volumeValue;
    private Label _qualityValue;
    private Label _sceneLabel;

    private readonly List<string> _missingElements = new List<string>();
    private readonly List<MonoBehaviour> _cachedPlayerControls = new List<MonoBehaviour>();
    private readonly List<bool> _cachedControlEnabledStates = new List<bool>();

    private bool _initialized;
    private bool _isLeaving;
    private bool _menuOpen;
    private bool _controlsCached;
    private bool _modalOpen;
    private int _modalToken;
    private IVisualElementScheduledItem _modalCloseItem;
    private Button _lastModalTrigger;

    private void Awake()
    {
        _uiDocument = GetComponent<UIDocument>();

        // Se guardan los estados originales antes de que el menú desactive nada.
        CachePlayerControls();
    }

    private void OnEnable()
    {
        TryInitialize();
    }

    private void Start()
    {
        // El rootVisualElement puede no estar listo en OnEnable si el UIDocument
        // acaba de habilitarse. Start es el segundo intento seguro.
        TryInitialize();

        if (!_initialized)
        {
            Debug.LogError(
                "[MainMenuController] No se encontró el rootVisualElement. " +
                "Asigna MainMenu.uxml al UIDocument y asegúrate de que tenga un PanelSettings.",
                this);
            return;
        }

        // Arranca con el menú visible y el jugador desactivado.
        ShowMenu();

        if (playerObject == null && (playerControls == null || playerControls.Count == 0))
        {
            Debug.LogWarning(
                "[MainMenuController] Asigna 'Player Object' (o al menos un script en 'Player Controls') " +
                "para desactivar los controles del jugador mientras el menú esté abierto.",
                this);
        }
    }

    private void Update()
    {
        if (!_initialized || _menuOpen || _isLeaving || !returnToMenuWithEscape)
            return;

        // Durante la partida no hay foco en la UI, así que se consulta el input
        // global. Se usa el New Input System si está habilitado en el proyecto.
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            ShowMenu();
#else
        if (Input.GetKeyDown(KeyCode.Escape))
            ShowMenu();
#endif
    }

    private void OnDisable()
    {
        _modalToken++;
        _modalCloseItem?.Pause();

        if (!_initialized)
            return;

        UnregisterCallbacks();
        _initialized = false;
    }

    private void OnApplicationQuit()
    {
        PlayerPrefs.Save();
    }

    // ------------------------------------------------------------------
    // Inicialización y enlace de elementos
    // ------------------------------------------------------------------

    private void TryInitialize()
    {
        if (_initialized)
            return;

        if (_uiDocument == null)
            _uiDocument = GetComponent<UIDocument>();

        if (_uiDocument == null)
            return;

        _root = _uiDocument.rootVisualElement;
        if (_root == null)
            return;

        CacheElements();

        if (_missingElements.Count > 0)
        {
            Debug.LogError(
                "[MainMenuController] Faltan elementos en MainMenu.uxml: " +
                string.Join(", ", _missingElements),
                this);
            return;
        }

        RegisterCallbacks();
        LoadSettings();
        UpdateSceneLabel();
        CloseModal(immediate: true, restoreFocus: false);

        _initialized = true;
    }

    private void CacheElements()
    {
        _missingElements.Clear();

        _modalLayer = Query<VisualElement>("modal-layer");
        _optionsPanel = Query<VisualElement>("options-panel");
        _creditsPanel = Query<VisualElement>("credits-panel");

        _playButton = Query<Button>("btn-play");
        _optionsButton = Query<Button>("btn-options");
        _creditsButton = Query<Button>("btn-credits");
        _quitButton = Query<Button>("btn-quit");
        _optionsBackButton = Query<Button>("btn-options-back");
        _optionsResetButton = Query<Button>("btn-options-reset");
        _creditsBackButton = Query<Button>("btn-credits-back");

        _volumeSlider = Query<Slider>("slider-volume");
        _qualitySlider = Query<Slider>("slider-quality");
        _volumeValue = Query<Label>("volume-value");
        _qualityValue = Query<Label>("quality-value");
        _sceneLabel = Query<Label>("scene-label");
    }

    private T Query<T>(string elementName) where T : VisualElement
    {
        T element = _root.Q<T>(elementName);
        if (element == null)
            _missingElements.Add(elementName);

        return element;
    }

    private void RegisterCallbacks()
    {
        _playButton.clicked += OnPlayClicked;
        _optionsButton.clicked += OnOptionsClicked;
        _creditsButton.clicked += OnCreditsClicked;
        _quitButton.clicked += OnQuitClicked;

        _optionsBackButton.clicked += OnOptionsBackClicked;
        _optionsResetButton.clicked += OnResetClicked;
        _creditsBackButton.clicked += OnCreditsBackClicked;

        _volumeSlider.RegisterValueChangedCallback(OnVolumeChanged);
        _qualitySlider.RegisterValueChangedCallback(OnQualityChanged);

        _root.RegisterCallback<KeyDownEvent>(OnKeyDown);
    }

    private void UnregisterCallbacks()
    {
        if (_playButton != null) _playButton.clicked -= OnPlayClicked;
        if (_optionsButton != null) _optionsButton.clicked -= OnOptionsClicked;
        if (_creditsButton != null) _creditsButton.clicked -= OnCreditsClicked;
        if (_quitButton != null) _quitButton.clicked -= OnQuitClicked;

        if (_optionsBackButton != null) _optionsBackButton.clicked -= OnOptionsBackClicked;
        if (_optionsResetButton != null) _optionsResetButton.clicked -= OnResetClicked;
        if (_creditsBackButton != null) _creditsBackButton.clicked -= OnCreditsBackClicked;

        if (_volumeSlider != null)
            _volumeSlider.UnregisterValueChangedCallback(OnVolumeChanged);

        if (_qualitySlider != null)
            _qualitySlider.UnregisterValueChangedCallback(OnQualityChanged);

        if (_root != null)
            _root.UnregisterCallback<KeyDownEvent>(OnKeyDown);
    }

    // ------------------------------------------------------------------
    // Botones principales
    // ------------------------------------------------------------------

    private void OnPlayClicked()
    {
        if (_isLeaving || !_menuOpen)
            return;

        _isLeaving = true;
        _playButton.SetEnabled(false);
        _root.AddToClassList("is-leaving");
        StartCoroutine(EnterGameRoutine());
    }

    private IEnumerator EnterGameRoutine()
    {
        PlayerPrefs.Save();

        if (fadeOutDuration > 0f)
            yield return new WaitForSecondsRealtime(fadeOutDuration);

        HideMenuAndStartGame();
    }

    /// <summary>
    /// Oculta la UI, reactiva al jugador y bloquea el cursor.
    /// La escena actual no se recarga.
    /// </summary>
    private void HideMenuAndStartGame()
    {
        CloseModal(immediate: true, restoreFocus: false);

        _root.style.display = DisplayStyle.None;
        _root.RemoveFromClassList("is-leaving");
        _menuOpen = false;

        SetMenuCameraActive(false);
        SetPlayerActive(true);
        SetCursorForGame();

        _playButton.SetEnabled(true);
        _isLeaving = false;
    }

    /// <summary>
    /// Muestra el menú, desactiva al jugador y libera el cursor.
    /// Se usa al arrancar la escena y al volver desde la partida con Esc.
    /// </summary>
    public void ShowMenu()
    {
        if (!_initialized || _menuOpen)
            return;

        _isLeaving = false;
        _menuOpen = true;
        _playButton.SetEnabled(true);

        _root.style.display = DisplayStyle.Flex;
        _root.RemoveFromClassList("is-leaving");

        CloseModal(immediate: true, restoreFocus: false);

        SetMenuCameraActive(true);
        SetPlayerActive(false);
        SetCursorForMenu();

        _root.schedule.Execute(() =>
        {
            if (_menuOpen && !_modalOpen && _playButton != null)
                _playButton.Focus();
        }).ExecuteLater(80);
    }

    // ------------------------------------------------------------------
    // Jugador, cámara y cursor
    // ------------------------------------------------------------------

    private void SetPlayerActive(bool active)
    {
        if (playerObject != null && playerObject != gameObject)
            playerObject.SetActive(active);

        ApplyPlayerControls(active);
    }

    /// <summary>
    /// Desactiva los scripts de control indicados mientras el menú está abierto
    /// y restaura su estado original al volver a jugar.
    /// </summary>
    private void ApplyPlayerControls(bool active)
    {
        CachePlayerControls();

        for (int i = 0; i < _cachedPlayerControls.Count; i++)
        {
            MonoBehaviour control = _cachedPlayerControls[i];
            if (control == null)
                continue;

            control.enabled = active && _cachedControlEnabledStates[i];
        }
    }

    private void CachePlayerControls()
    {
        if (_controlsCached)
            return;

        _controlsCached = true;
        _cachedPlayerControls.Clear();
        _cachedControlEnabledStates.Clear();

        if (playerControls == null)
            return;

        foreach (MonoBehaviour control in playerControls)
        {
            if (control == null || control == this)
                continue;

            _cachedPlayerControls.Add(control);
            _cachedControlEnabledStates.Add(control.enabled);
        }
    }

    private void SetMenuCameraActive(bool active)
    {
        if (menuCamera == null)
            return;

        menuCamera.enabled = active;
    }

    private static void SetCursorForMenu()
    {
        UnityEngine.Cursor.lockState = CursorLockMode.None;
        UnityEngine.Cursor.visible = true;
    }

    private static void SetCursorForGame()
    {
        UnityEngine.Cursor.lockState = CursorLockMode.Locked;
        UnityEngine.Cursor.visible = false;
    }

    private void OnOptionsClicked()
    {
        OpenModal(_optionsPanel, _optionsButton, _volumeSlider);
    }

    private void OnCreditsClicked()
    {
        OpenModal(_creditsPanel, _creditsButton, _creditsBackButton);
    }

    private void OnOptionsBackClicked()
    {
        CloseModal();
    }

    private void OnCreditsBackClicked()
    {
        CloseModal();
    }

    private void OnQuitClicked()
    {
#if UNITY_EDITOR
        // Detiene el Play Mode desde el editor.
        EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ------------------------------------------------------------------
    // Modales
    // ------------------------------------------------------------------

    private void OpenModal(VisualElement panel, Button trigger, VisualElement focusTarget)
    {
        if (panel == null)
            return;

        _modalOpen = true;
        _lastModalTrigger = trigger;
        int token = ++_modalToken;
        _modalCloseItem?.Pause();

        _optionsPanel.AddToClassList("is-hidden");
        _creditsPanel.AddToClassList("is-hidden");
        _optionsPanel.SetEnabled(false);
        _creditsPanel.SetEnabled(false);

        panel.RemoveFromClassList("is-hidden");
        panel.SetEnabled(true);

        _modalLayer.pickingMode = PickingMode.Position;

        // Se espera un frame para que la transición de entrada se reproduzca.
        _modalLayer.schedule.Execute(() =>
        {
            if (token == _modalToken)
                _modalLayer.AddToClassList("is-open");
        }).ExecuteLater(16);

        _root.schedule.Execute(() =>
        {
            if (_modalOpen && token == _modalToken && focusTarget != null)
                focusTarget.Focus();
        }).ExecuteLater(90);
    }

    private void CloseModal(bool immediate = false, bool restoreFocus = true)
    {
        _modalOpen = false;
        int token = ++_modalToken;
        _modalCloseItem?.Pause();

        if (_modalLayer != null)
        {
            _modalLayer.RemoveFromClassList("is-open");
            _modalLayer.pickingMode = PickingMode.Ignore;
        }

        if (_optionsPanel != null)
        {
            _optionsPanel.SetEnabled(false);
            if (immediate) _optionsPanel.AddToClassList("is-hidden");
        }

        if (_creditsPanel != null)
        {
            _creditsPanel.SetEnabled(false);
            if (immediate) _creditsPanel.AddToClassList("is-hidden");
        }

        if (!immediate && _modalLayer != null)
        {
            _modalCloseItem = _modalLayer.schedule.Execute(() =>
            {
                if (token != _modalToken)
                    return;

                _optionsPanel.AddToClassList("is-hidden");
                _creditsPanel.AddToClassList("is-hidden");
            });
            _modalCloseItem.ExecuteLater(ModalFadeMs);
        }

        if (restoreFocus && _root != null)
        {
            Button target = _lastModalTrigger != null ? _lastModalTrigger : _optionsButton;
            _root.schedule.Execute(() =>
            {
                if (!_modalOpen && target != null)
                    target.Focus();
            }).ExecuteLater(40);
        }
    }

    private void OnKeyDown(KeyDownEvent evt)
    {
        if (!_modalOpen || evt.keyCode != KeyCode.Escape)
            return;

        CloseModal();
        evt.StopPropagation();
    }

    // ------------------------------------------------------------------
    // Ajustes: volumen y calidad
    // ------------------------------------------------------------------

    private void LoadSettings()
    {
        int qualityCount = Mathf.Max(1, QualitySettings.names.Length);
        _qualitySlider.lowValue = 0f;
        _qualitySlider.highValue = qualityCount - 1;

        int savedVolume = Mathf.Clamp(PlayerPrefs.GetInt(VolumePrefKey, defaultVolume), 0, 100);
        int savedQuality = Mathf.Clamp(
            PlayerPrefs.GetInt(QualityPrefKey, QualitySettings.GetQualityLevel()),
            0,
            qualityCount - 1);

        _volumeSlider.SetValueWithoutNotify(savedVolume);
        _qualitySlider.SetValueWithoutNotify(savedQuality);

        ApplyVolume(savedVolume, save: false);
        ApplyQuality(savedQuality, save: false);
    }

    private void OnVolumeChanged(ChangeEvent<float> evt)
    {
        int percent = Mathf.Clamp(Mathf.RoundToInt(evt.newValue), 0, 100);
        ApplyVolume(percent, save: true);
    }

    private void OnQualityChanged(ChangeEvent<float> evt)
    {
        int qualityCount = Mathf.Max(1, QualitySettings.names.Length);
        int index = Mathf.Clamp(Mathf.RoundToInt(evt.newValue), 0, qualityCount - 1);

        ApplyQuality(index, save: true);

        // El slider de calidad es discreto: se ajusta al nivel entero más cercano.
        if (!Mathf.Approximately(evt.newValue, index))
            _qualitySlider.SetValueWithoutNotify(index);
    }

    private void OnResetClicked()
    {
        int maxQuality = Mathf.Max(0, QualitySettings.names.Length - 1);

        _volumeSlider.SetValueWithoutNotify(defaultVolume);
        _qualitySlider.SetValueWithoutNotify(maxQuality);

        ApplyVolume(defaultVolume, save: true);
        ApplyQuality(maxQuality, save: true);
    }

    private void ApplyVolume(int percent, bool save)
    {
        percent = Mathf.Clamp(percent, 0, 100);
        AudioListener.volume = percent / 100f;

        if (_volumeValue != null)
            _volumeValue.text = $"{percent}%";

        if (save)
            PlayerPrefs.SetInt(VolumePrefKey, percent);
    }

    private void ApplyQuality(int index, bool save)
    {
        if (QualitySettings.names.Length == 0)
            return;

        index = Mathf.Clamp(index, 0, QualitySettings.names.Length - 1);
        QualitySettings.SetQualityLevel(index, applyExpensiveChanges: true);

        if (_qualityValue != null)
            _qualityValue.text = GetQualityDisplayName(index);

        if (save)
            PlayerPrefs.SetInt(QualityPrefKey, index);
    }

    /// <summary>
    /// Traduce los nombres de calidad habituales de Unity al español.
    /// Si el proyecto usa nombres personalizados, se muestran tal cual.
    /// </summary>
    private static string GetQualityDisplayName(int index)
    {
        string qualityName = QualitySettings.names[index];

        switch (qualityName)
        {
            case "Very Low": return "Muy bajo";
            case "Low": return "Bajo";
            case "Medium": return "Medio";
            case "High": return "Alto";
            case "Very High": return "Muy alto";
            case "Ultra": return "Ultra";
            default: return qualityName;
        }
    }

    private void UpdateSceneLabel()
    {
        if (_sceneLabel == null)
            return;

        string sceneName = SceneManager.GetActiveScene().name;

        _sceneLabel.text = string.IsNullOrWhiteSpace(sceneName)
            ? "ESCENA · SIN ASIGNAR"
            : $"ESCENA · {sceneName.ToUpperInvariant()}";
    }
}
