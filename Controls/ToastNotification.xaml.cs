using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;

namespace RadioPlayer.Controls;

public partial class ToastNotification : UserControl
{
    private CancellationTokenSource? _toastCancellation;
    private Border? _toastBorder;

    public ToastNotification()
    {
        InitializeComponent();
        Loaded += ToastNotification_Loaded;
    }

    private void ToastNotification_Loaded(object sender, RoutedEventArgs e)
    {
        _toastBorder = ToastPopup.Child as Border;
    }

    public async Task ShowAsync(string message, Point position, FrameworkElement? relativeTo = null)
    {
        _toastCancellation?.Cancel();
        _toastCancellation = new CancellationTokenSource();
        var token = _toastCancellation.Token;

        ToastTextBlock.Text = message;

        if (_toastBorder == null)
            _toastBorder = ToastPopup.Child as Border;

        if (relativeTo != null)
        {
            var window = Window.GetWindow(relativeTo);
            if (window != null)
            {
                var screenPoint = relativeTo.PointToScreen(position);
                
                ToastPopup.PlacementTarget = null;
                ToastPopup.Placement = PlacementMode.Absolute;
                ToastPopup.HorizontalOffset = screenPoint.X - 50;
                ToastPopup.VerticalOffset = screenPoint.Y - 30;
            }
        }
        else
        {
            ToastPopup.PlacementTarget = null;
            ToastPopup.Placement = PlacementMode.Absolute;
            ToastPopup.HorizontalOffset = position.X - 50;
            ToastPopup.VerticalOffset = position.Y - 30;
        }

        ToastPopup.IsOpen = true;

        if (_toastBorder != null)
        {
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));
            _toastBorder.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        }

        await Task.Delay(1500, token);

        if (!token.IsCancellationRequested && _toastBorder != null)
        {
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150));
            fadeOut.Completed += (s, e) =>
            {
                if (!token.IsCancellationRequested)
                {
                    ToastPopup.IsOpen = false;
                }
            };
            _toastBorder.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }
    }

    public void Hide()
    {
        _toastCancellation?.Cancel();
        ToastPopup.IsOpen = false;
    }
}

