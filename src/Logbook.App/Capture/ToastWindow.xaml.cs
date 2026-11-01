using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Logbook.App.Capture;

/// <summary>
/// The "Logged — click to edit" toast (DESIGN.md sections 5 and 16): a non-activating topmost window at the bottom
/// right of the work area that fades out after five seconds. The same window, in the danger color, reports an unsaved capture.
/// </summary>
public sealed partial class ToastWindow : Window
{
    private readonly DispatcherTimer _timer = new();
    private bool _fading;

    public ToastWindow(Rect workArea, string headline, string detail, bool danger, TimeSpan lifetime)
    {
        InitializeComponent();
        Headline.Text = headline;
        Detail.Text = detail;
        Detail.Visibility = detail.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (danger)
        {
            Body.Background = (Brush)FindResource("DangerBrush");
        }

        Left = workArea.Right - Width - 8;
        Loaded += (_, _) => Top = workArea.Bottom - ActualHeight - 8;

        _timer.Interval = lifetime;
        _timer.Tick += (_, _) => FadeOut();
        _timer.Start();
    }

    /// <summary>Raised when the user clicks the toast, before it closes.</summary>
    public event Action? Clicked;

    private void OnClick(object sender, MouseButtonEventArgs e)
    {
        Clicked?.Invoke();
        Close();
    }

    private void OnMouseEnter(object sender, MouseEventArgs e)
    {
        _timer.Stop();
        if (_fading)
        {
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
            _fading = false;
        }
    }

    private void OnMouseLeave(object sender, MouseEventArgs e) => _timer.Start();

    private void FadeOut()
    {
        _timer.Stop();
        _fading = true;
        DoubleAnimation fade = new(1, 0, TimeSpan.FromMilliseconds(600)) { FillBehavior = FillBehavior.Stop };
        fade.Completed += (_, _) =>
        {
            if (_fading)
            {
                Close();
            }
        };
        BeginAnimation(OpacityProperty, fade);
    }
}
