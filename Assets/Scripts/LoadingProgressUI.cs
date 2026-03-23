using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// 加载进度显示 UI
/// 显示模型导入的进度条和状态信息
/// </summary>
public class LoadingProgressUI : MonoBehaviour
{
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private Image progressBar;
    [SerializeField] private TextMeshProUGUI progressText;
    [SerializeField] private TextMeshProUGUI stageText;
    [SerializeField] private Button cancelButton;

    private ImportOptimizer _currentOptimizer;
    private float _targetProgress;
    private float _currentProgress;

    private void Start()
    {
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        if (progressBar == null) progressBar = GetComponentInChildren<Image>();
        if (progressText == null) progressText = GetComponentInChildren<TextMeshProUGUI>();

        Hide();

        if (cancelButton != null)
            cancelButton.onClick.AddListener(OnCancelClicked);
    }

    private void Update()
    {
        // 平滑过渡进度条
        if (Mathf.Abs(_currentProgress - _targetProgress) > 0.01f)
        {
            _currentProgress = Mathf.Lerp(_currentProgress, _targetProgress, Time.deltaTime * 2f);
            UpdateProgressDisplay();
        }
    }

    public void Show(ImportOptimizer optimizer)
    {
        _currentOptimizer = optimizer;
        _currentProgress = 0f;
        _targetProgress = 0f;

        if (canvasGroup != null)
            canvasGroup.alpha = 1f;

        if (optimizer != null)
            optimizer.ProgressChanged += OnProgressChanged;

        UpdateProgressDisplay();
    }

    public void Hide()
    {
        if (_currentOptimizer != null)
            _currentOptimizer.ProgressChanged -= OnProgressChanged;

        _currentOptimizer = null;

        if (canvasGroup != null)
            canvasGroup.alpha = 0f;
    }

    private void OnProgressChanged(float progress, string stage)
    {
        _targetProgress = progress;
        if (stageText != null)
            stageText.text = stage;
    }

    private void UpdateProgressDisplay()
    {
        if (progressBar != null)
            progressBar.fillAmount = _currentProgress / 100f;

        if (progressText != null)
            progressText.text = $"{_currentProgress:F0}%";
    }

    private void OnCancelClicked()
    {
        if (_currentOptimizer != null)
        {
            _currentOptimizer.CancelLoad();
            Hide();
        }
    }

    private void OnDestroy()
    {
        if (cancelButton != null)
            cancelButton.onClick.RemoveListener(OnCancelClicked);
    }
}
