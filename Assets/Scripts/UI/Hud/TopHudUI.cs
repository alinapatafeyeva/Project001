using Project001.Gameplay;
using Project001.Gameplay.Levels;
using UnityEngine;
using UnityEngine.UI;

namespace Project001.UI.Hud
{
    /// <summary>
    /// Top gameplay HUD: Exit (left), "LEVEL {number}" (center), and the
    /// 1x/2x speed toggle + Pause button (right). Owns only this row's own
    /// presentation — it never decides what any button actually does beyond
    /// forwarding to the gameplay-layer controller that owns that behaviour
    /// (GameplaySpeedController, PauseFlowController, LevelExitFlowController),
    /// and never touches Time.timeScale or GameplayFlowController directly,
    /// mirroring VictoryUI/FailureUI's own convention.
    ///
    /// The level number is read once, in Start (after every Awake in the
    /// scene has already run, including LevelBootstrapper's own Awake —
    /// which is what actually resolves LevelProgressionController's current
    /// level for this session) rather than in Awake, so this label can never
    /// read a not-yet-initialized LevelId regardless of these two
    /// components' relative Awake order. A later level launch always
    /// happens through a full scene reload (LoadNextLevel, Retry — see
    /// LevelProgressionController), which destroys and recreates this
    /// component along with everything else, so this one-time read is
    /// naturally correct on every launch without an OnLevelChanged event.
    ///
    /// Progress UI (a bar, percentage, or remaining-pixel counter) is
    /// deliberately never part of this class — the remaining PixelGrid
    /// itself already communicates progress, and future mechanics may
    /// intentionally hide information from the player (see the top gameplay
    /// HUD task's own scope).
    /// </summary>
    public class TopHudUI : MonoBehaviour
    {
        [SerializeField, Tooltip("Zero-size anchor point (top-center) that the LevelLabel icon + one digit icon per digit of the current level number are built under, read once from levelProgressionController in Start.")]
        private RectTransform levelDisplayContainer;

        [SerializeField, Tooltip("The \"LEVEL\" wordmark icon, always the first element of the level display group.")]
        private Sprite levelLabelSprite;

        [SerializeField, Tooltip("Digit icons indexed by digit value 0-9 (levelDigitSprites[3] is the '3' icon), used to spell out the current level number one icon per digit.")]
        private Sprite[] levelDigitSprites;

        [SerializeField, Tooltip("Left button — opens the Exit confirmation dialog directly (gameplay is running beforehand).")]
        private Button backButton;

        [SerializeField, Tooltip("Right button — toggles the selected gameplay speed via gameplaySpeedController.")]
        private Button speedButton;

        [SerializeField, Tooltip("Icon Image of speedButton, swapped between speedNormalSprite/speedFastSprite whenever the selection changes — this button never carries a text label.")]
        private Image speedButtonIcon;

        [SerializeField, Tooltip("Shown on speedButtonIcon while gameplaySpeedController.IsFastSpeedSelected is false (1x, including a freshly started level).")]
        private Sprite speedNormalSprite;

        [SerializeField, Tooltip("Shown on speedButtonIcon while gameplaySpeedController.IsFastSpeedSelected is true (2x).")]
        private Sprite speedFastSprite;

        [SerializeField, Tooltip("Right button — opens the Pause modal.")]
        private Button pauseButton;

        [SerializeField, Tooltip("Single source of truth for the current level's display number.")]
        private LevelProgressionController levelProgressionController;

        [SerializeField, Tooltip("Sole owner of the selected 1x/2x gameplay speed.")]
        private GameplaySpeedController gameplaySpeedController;

        [SerializeField, Tooltip("Sole owner of what the Exit confirmation dialog actually does — reached only through its own API.")]
        private LevelExitFlowController levelExitFlowController;

        [SerializeField, Tooltip("Sole owner of what the Pause button actually does — reached only through its own API.")]
        private PauseFlowController pauseFlowController;

        [SerializeField, Tooltip("Shared Exit confirmation dialog, opened directly by backButton.")]
        private ExitConfirmationUI exitConfirmationUI;

        [SerializeField, Tooltip("Pause modal panel, shown by pauseButton after pauseFlowController.OpenPause.")]
        private PauseUI pauseUI;

        private void Awake()
        {
            if (backButton != null)
                backButton.onClick.AddListener(OnBackPressed);

            if (speedButton != null)
                speedButton.onClick.AddListener(OnSpeedPressed);

            if (pauseButton != null)
                pauseButton.onClick.AddListener(OnPausePressed);
        }

        private void Start()
        {
            BuildLevelDisplay();
            RefreshSpeedIcon();
        }

        private void OnDestroy()
        {
            if (backButton != null)
                backButton.onClick.RemoveListener(OnBackPressed);

            if (speedButton != null)
                speedButton.onClick.RemoveListener(OnSpeedPressed);

            if (pauseButton != null)
                pauseButton.onClick.RemoveListener(OnPausePressed);
        }

        /// <summary>
        /// Square icon-box side lengths for the LevelLabel icon vs. each
        /// digit icon — deliberately unequal, unlike the boxes elsewhere in
        /// this HUD. LevelLabel.png's own visible wordmark only fills ~30%
        /// of its square canvas height (a short, wide glyph), while a digit
        /// sprite's visible numeral fills ~99% of its canvas height (tall
        /// and narrow) — using the same box size for both, as this class
        /// used to, therefore rendered the digit's *visible ink* roughly 3x
        /// taller than the label's, however equal their boxes were. Since
        /// preserveAspect renders a square (both sprites' sprite rects are
        /// square canvases), each box's actual visible content height is
        /// simply boxSize * thatSprite's own vertical fill fraction — so a
        /// label box ~2.5x the digit box's size is what actually equalizes
        /// their *visible* heights to the desired ~1.3x digit:label ratio.
        /// Both sprites' content also happens to already sit vertically
        /// centered within their own square canvas, so giving every icon
        /// the same box-center Y (see BuildLevelDisplay/CreateLevelIcon)
        /// automatically aligns their visible ink centers too, despite the
        /// boxes themselves being different heights.
        ///
        /// Both raised 10% (160-&gt;176, 64-&gt;70.4) from their original values
        /// once the Coin Balance group's own text grew large enough (see
        /// BootstrapSceneCreator's own HudCoinBalanceFontSizeMax) that Level
        /// started reading as visually weaker than it and "1x"/Pause — a
        /// uniform scale preserves the already-tuned label:digit size ratio
        /// exactly rather than changing the relationship between them.
        /// </summary>
        private const float LevelLabelBoxSize = 176f;
        private const float LevelDigitBoxSize = 70.4f;

        /// <summary>Deliberate horizontal gap between the label and the first digit, and reused between consecutive digits of a multi-digit level number. Scaled by the same 10% as LevelLabelBoxSize/LevelDigitBoxSize so the row's internal proportions stay identical, just larger.</summary>
        private const float LevelGroupGap = 13.2f;

        /// <summary>
        /// Builds the LevelLabel icon followed by one digit icon per digit of
        /// levelProgressionController.GetCurrentLevelNumber() — the same
        /// single source of truth the old text label read — under
        /// levelDisplayContainer, centering the whole row (label box +
        /// gap + all digit boxes + gaps) on that container's own top-center
        /// anchor point regardless of how many digits the level number has,
        /// mirroring WaitingLine.GenerateSlots' own offsetX/stepX centering
        /// trick for a row of elements. Called once in Start, exactly like
        /// the text label it replaces — a level change always reloads the
        /// scene (see class remarks), so this never needs to run again.
        /// </summary>
        private void BuildLevelDisplay()
        {
            if (levelDisplayContainer == null || levelProgressionController == null)
                return;

            string levelNumberDigits = levelProgressionController.GetCurrentLevelNumber().ToString();

            float totalWidth = LevelLabelBoxSize + LevelGroupGap
                + levelNumberDigits.Length * LevelDigitBoxSize
                + (levelNumberDigits.Length - 1) * LevelGroupGap;

            float labelX = -totalWidth * 0.5f + LevelLabelBoxSize * 0.5f;
            CreateLevelIcon(levelDisplayContainer, "LevelLabelIcon", levelLabelSprite, labelX, LevelLabelBoxSize);

            float nextDigitCenterX = labelX + LevelLabelBoxSize * 0.5f + LevelGroupGap + LevelDigitBoxSize * 0.5f;
            for (int i = 0; i < levelNumberDigits.Length; i++)
            {
                int digitValue = levelNumberDigits[i] - '0';
                Sprite digitSprite = levelDigitSprites != null && digitValue >= 0 && digitValue < levelDigitSprites.Length
                    ? levelDigitSprites[digitValue]
                    : null;
                CreateLevelIcon(levelDisplayContainer, $"LevelDigitIcon_{i}", digitSprite, nextDigitCenterX, LevelDigitBoxSize);
                nextDigitCenterX += LevelDigitBoxSize + LevelGroupGap;
            }
        }

        private static void CreateLevelIcon(Transform parent, string name, Sprite sprite, float anchoredX, float boxSize)
        {
            var iconObject = new GameObject(name, typeof(Image));
            iconObject.transform.SetParent(parent, false);

            var rectTransform = iconObject.GetComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.sizeDelta = new Vector2(boxSize, boxSize);
            rectTransform.anchoredPosition = new Vector2(anchoredX, 0f);

            var image = iconObject.GetComponent<Image>();
            image.sprite = sprite;
            image.color = Color.white;
            image.preserveAspect = true;
        }

        /// <summary>
        /// Derives the displayed icon from gameplaySpeedController.IsFastSpeedSelected
        /// — the sole source of truth for the selected speed — rather than
        /// tracking any independent UI-side speed state of its own. Called
        /// once in Start (a freshly started level is always 1x, per
        /// GameplaySpeedController's own reset-on-Awake behaviour) and again
        /// after every OnSpeedPressed toggle, so the icon can never drift
        /// from the real gameplay speed, including across pause/resume —
        /// which never changes IsFastSpeedSelected — since nothing else in
        /// this class swaps this sprite.
        /// </summary>
        private void RefreshSpeedIcon()
        {
            if (speedButtonIcon == null || gameplaySpeedController == null)
                return;

            speedButtonIcon.sprite = gameplaySpeedController.IsFastSpeedSelected ? speedFastSprite : speedNormalSprite;
        }

        private void OnBackPressed()
        {
            if (levelExitFlowController != null)
                levelExitFlowController.OpenExitConfirmation();

            if (exitConfirmationUI != null)
            {
                LevelExitFlowController controller = levelExitFlowController;
                exitConfirmationUI.Open(controller != null ? controller.CancelExit : (System.Action)null);
            }
        }

        private void OnSpeedPressed()
        {
            if (gameplaySpeedController != null)
                gameplaySpeedController.ToggleSpeed();

            RefreshSpeedIcon();
        }

        private void OnPausePressed()
        {
            if (pauseFlowController != null)
                pauseFlowController.OpenPause();

            if (pauseUI != null)
                pauseUI.Show();
        }
    }
}
