using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace RadioPlayer.Controls;

public partial class PlayerControl : UserControl
{
    public static readonly DependencyProperty CurrentTrackProperty =
        DependencyProperty.Register(nameof(CurrentTrack), typeof(string),
            typeof(PlayerControl), new PropertyMetadata(string.Empty, OnTrackChanged));

    public string CurrentTrack
    {
        get => (string)GetValue(CurrentTrackProperty);
        set => SetValue(CurrentTrackProperty, value);
    }

    public static readonly DependencyProperty CurrentArtistProperty =
        DependencyProperty.Register(nameof(CurrentArtist), typeof(string),
            typeof(PlayerControl), new PropertyMetadata(string.Empty, OnArtistChanged));

    public string CurrentArtist
    {
        get => (string)GetValue(CurrentArtistProperty);
        set => SetValue(CurrentArtistProperty, value);
    }

    public static readonly DependencyProperty IsPlayingProperty =
        DependencyProperty.Register(nameof(IsPlaying), typeof(bool),
            typeof(PlayerControl), new PropertyMetadata(false, OnPlayingChanged));

    public bool IsPlaying
    {
        get => (bool)GetValue(IsPlayingProperty);
        set => SetValue(IsPlayingProperty, value);
    }

    public static readonly DependencyProperty IsPausedProperty =
        DependencyProperty.Register(nameof(IsPaused), typeof(bool),
            typeof(PlayerControl), new PropertyMetadata(false, OnPausedChanged));

    public bool IsPaused
    {
        get => (bool)GetValue(IsPausedProperty);
        set => SetValue(IsPausedProperty, value);
    }

    public static readonly DependencyProperty PlayCommandProperty =
        DependencyProperty.Register(nameof(PlayCommand), typeof(ICommand),
            typeof(PlayerControl), new PropertyMetadata(null));

    public ICommand PlayCommand
    {
        get => (ICommand)GetValue(PlayCommandProperty);
        set => SetValue(PlayCommandProperty, value);
    }

    public static readonly DependencyProperty PauseCommandProperty =
        DependencyProperty.Register(nameof(PauseCommand), typeof(ICommand),
            typeof(PlayerControl), new PropertyMetadata(null));

    public ICommand PauseCommand
    {
        get => (ICommand)GetValue(PauseCommandProperty);
        set => SetValue(PauseCommandProperty, value);
    }

    public static readonly DependencyProperty StopCommandProperty =
        DependencyProperty.Register(nameof(StopCommand), typeof(ICommand),
            typeof(PlayerControl), new PropertyMetadata(null));

    public ICommand StopCommand
    {
        get => (ICommand)GetValue(StopCommandProperty);
        set => SetValue(StopCommandProperty, value);
    }

    public static readonly DependencyProperty PreviousCommandProperty =
        DependencyProperty.Register(nameof(PreviousCommand), typeof(ICommand),
            typeof(PlayerControl), new PropertyMetadata(null));

    public ICommand PreviousCommand
    {
        get => (ICommand)GetValue(PreviousCommandProperty);
        set => SetValue(PreviousCommandProperty, value);
    }

    public static readonly DependencyProperty NextCommandProperty =
        DependencyProperty.Register(nameof(NextCommand), typeof(ICommand),
            typeof(PlayerControl), new PropertyMetadata(null));

    public ICommand NextCommand
    {
        get => (ICommand)GetValue(NextCommandProperty);
        set => SetValue(NextCommandProperty, value);
    }

    public static readonly DependencyProperty ProgressProperty =
        DependencyProperty.Register(nameof(Progress), typeof(double),
            typeof(PlayerControl), new PropertyMetadata(0.0, OnProgressChanged));

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public static readonly DependencyProperty VolumeProperty =
        DependencyProperty.Register(nameof(Volume), typeof(double),
            typeof(PlayerControl), new PropertyMetadata(0.5, OnVolumeChanged));

    public double Volume
    {
        get => (double)GetValue(VolumeProperty);
        set => SetValue(VolumeProperty, value);
    }

    public static readonly DependencyProperty OnVolumeChangedCommandProperty =
        DependencyProperty.Register(nameof(OnVolumeChangedCommand), typeof(ICommand),
            typeof(PlayerControl), new PropertyMetadata(null));

    public ICommand OnVolumeChangedCommand
    {
        get => (ICommand)GetValue(OnVolumeChangedCommandProperty);
        set => SetValue(OnVolumeChangedCommandProperty, value);
    }

    public PlayerControl()
    {
        InitializeComponent();
        Loaded += PlayerControl_Loaded;
    }

    private void PlayerControl_Loaded(object sender, RoutedEventArgs e)
    {
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (VolumeSlider != null && VolumeText != null)
        {
            var volume = VolumeSlider.Value;
            Volume = volume;
            VolumeText.Text = $"{(int)(volume * 100)}%";
            OnVolumeChangedCommand?.Execute(Volume);
        }
    }

    private static void OnTrackChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PlayerControl control)
        {
            control.TrackText.Text = e.NewValue?.ToString() ?? string.Empty;
        }
    }

    private static void OnArtistChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PlayerControl control)
        {
            control.ArtistText.Text = e.NewValue?.ToString() ?? string.Empty;
        }
    }

    private static void OnPlayingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
    }

    private static void OnPausedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
    }

    private static void OnProgressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
    }

    private static void OnVolumeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PlayerControl control && control.VolumeSlider != null && control.VolumeText != null)
        {
            var volume = (double)e.NewValue;
            control.VolumeSlider.Value = volume;
            control.VolumeText.Text = $"{(int)(volume * 100)}%";
        }
    }

    private async void StackPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is StackPanel stackPanel)
        {
            var textToCopy = string.Empty;

            if (!string.IsNullOrWhiteSpace(TrackText.Text))
            {
                textToCopy = TrackText.Text;
                if (!string.IsNullOrWhiteSpace(ArtistText.Text))
                {
                    textToCopy += " - " + ArtistText.Text;
                }
            }
            else if (!string.IsNullOrWhiteSpace(ArtistText.Text))
            {
                textToCopy = ArtistText.Text;
            }

            if (!string.IsNullOrWhiteSpace(textToCopy))
            {
                try
                {
                    Clipboard.SetText(textToCopy);
                    var position = e.GetPosition(this);
                    await ToastNotificationControl.ShowAsync("Скопировано", position, this);
                }
                catch (Exception)
                {
                }
            }
        }
    }

    private void VolumeSlider_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (VolumeSlider != null)
        {
            var delta = e.Delta > 0 ? 0.05 : -0.05;
            var newValue = Math.Max(0, Math.Min(1, VolumeSlider.Value + delta));
            VolumeSlider.Value = newValue;
            e.Handled = true;
        }
    }
}
