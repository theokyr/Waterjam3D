using Godot;

namespace Waterjam.UI;

public partial class LoadingScreen : Control
{
    private ProgressBar _progressBar;
    private Label _statusLabel;
    private AnimationPlayer _animationPlayer;
    private Control _container;
    private ColorRect _background;

    private bool _isShowing = false;

    public override void _Ready()
    {
        _progressBar = GetNode<ProgressBar>("Container/VBoxContainer/ProgressBar");
        _statusLabel = GetNode<Label>("Container/VBoxContainer/StatusLabel");
        _animationPlayer = GetNode<AnimationPlayer>("AnimationPlayer");
        _container = GetNode<Control>("Container");
        _background = GetNode<ColorRect>("Background");

        // Initialize hidden
        _container.Modulate = new Color(_container.Modulate, 0);
        _background.Modulate = new Color(_background.Modulate, 0);
        _isShowing = false;

        Hide();
    }

    /// <summary>
    /// Set the current progress of the loading operation
    /// </summary>
    /// <param name="progress">Progress value between 0 and 1</param>
    /// <param name="statusText">Status message to display</param>
    public void SetProgress(float progress, string statusText = "")
    {
        if (!_isShowing)
        {
            Show();
            _isShowing = true;

            // Show the loading screen with animation
            var tween = CreateTween();
            tween.SetParallel(true);
            tween.TweenProperty(_container, "modulate:a", 1.0, 0.3)
                .SetTrans(Tween.TransitionType.Cubic)
                .SetEase(Tween.EaseType.Out);
            tween.TweenProperty(_background, "modulate:a", 1.0, 0.3)
                .SetTrans(Tween.TransitionType.Cubic)
                .SetEase(Tween.EaseType.Out);
        }

        // Update progress bar and status text
        _progressBar.Value = progress;

        if (!string.IsNullOrEmpty(statusText)) _statusLabel.Text = statusText;
    }

    /// <summary>
    /// Hide the loading screen with a fade animation
    /// </summary>
    public void HideScreen()
    {
        if (!_isShowing) return;

        var tween = CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(_container, "modulate:a", 0.0, 0.3)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.In);
        tween.TweenProperty(_background, "modulate:a", 0.0, 0.3)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.In);

        tween.TweenCallback(Callable.From(() =>
        {
            _isShowing = false;
            Hide();
        }));
    }
}