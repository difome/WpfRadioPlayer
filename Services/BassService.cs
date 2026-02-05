using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ManagedBass;
using RadioPlayer.Services;

namespace RadioPlayer.Services;

public class BassService : IDisposable
{
    private int _streamHandle;
    private int _nextStreamHandle;
    private bool _isPlaying;
    private bool _isPaused;
    private int _metadataSyncHandle;
    private int _nextMetadataSyncHandle;
    private string? _currentUrl;
    private string? _nextUrl;
    private Timer? _metadataPollTimer;
    private string? _lastMetadata;
    private string? _lastMetadataUrl;
    private int _reconnectAttempts = 0;
    private const int MaxReconnectAttempts = 3;
    private Timer? _connectionMonitorTimer;
    private DateTime _lastStalledTime = DateTime.MinValue;
    private DateTime _lastSuccessfulDataTime = DateTime.MinValue;
    private const int StalledTimeoutSeconds = 10;
    private const int MinStalledTimeoutSeconds = 5;
    private const int MaxStalledTimeoutSeconds = 30;
    private readonly SemaphoreSlim _playSemaphore = new SemaphoreSlim(1, 1);
    private CancellationTokenSource? _currentPlayCancellation;
    private long _lastPlaybackPosition = 0;
    private DateTime _lastPositionCheckTime = DateTime.MinValue;

    public event Action<string, string?>? OnMetadataReceived;
    public event Action<bool>? OnPlaybackStateChanged;
    public event Action<string>? OnReconnectRequired;

    public BassService()
    {
        Bass.Configure((Configuration)10, 10000); // BASS_CONFIG_NET_TIMEOUT
        Bass.Configure((Configuration)15, 50);    // BASS_CONFIG_NET_PREBUF
        Bass.Configure((Configuration)21, 1);     // BASS_CONFIG_NET_PLAYLIST
        Bass.Configure((Configuration)11, 10000); // BASS_CONFIG_NET_AGENT (11) or Proxy? (Let's keep it but add ReadTimeout)
        Bass.Configure((Configuration)37, 10000); // BASS_CONFIG_NET_READTIMEOUT (37) - 10 seconds

        if (!Bass.Init(-1, 44100, DeviceInitFlags.Default, IntPtr.Zero))
        {
            throw new Exception($"Не удалось инициализировать Bass: {Bass.LastError}");
        }
    }

    public async Task<bool> Play(string url)
    {
        Logger.Log($"Play: Called with URL: {url}");

        try
        {
            _currentPlayCancellation?.Cancel();
        }
        catch { }

        _currentPlayCancellation?.Dispose();
        _currentPlayCancellation = new CancellationTokenSource();
        var cancellationToken = _currentPlayCancellation.Token;

        if (!string.IsNullOrEmpty(_currentUrl) && _currentUrl == url)
        {
            Logger.Log($"Play: Same URL already playing, resuming if paused");
            if (_isPaused)
            {
                Resume();
            }
            return true;
        }

        StopAllStreamsExceptCurrent();

        if (_streamHandle == 0)
        {
            Logger.Log($"Play: No current stream, starting immediately");
            _currentUrl = url;
            return await StartAndMonitor(_streamHandle, url, _metadataSyncHandle, true);
        }

        if (!string.IsNullOrEmpty(_nextUrl) && _nextUrl == url)
        {
            Logger.Log($"Play: Next stream already being prepared for URL: {url}");
            return true;
        }

        Logger.Log($"Play: Preparing next stream in background for URL: {url}");
        _nextUrl = url;

        _ = Task.Run(async () =>
        {
            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    Logger.Log($"Play (background): Operation cancelled");
                    return;
                }
                int next = 0;

                Logger.Log($"Play (background): Connecting to URL: {url} ...");
                var flags = BassFlags.Default | (BassFlags)0x800000;
                next = Bass.CreateStream(url, 0, flags, null, IntPtr.Zero);
                if (next == 0)
                {
                    Logger.Log($"Play (background): First attempt failed, retrying...");
                    await Task.Delay(200, cancellationToken).ConfigureAwait(false);
                    if (cancellationToken.IsCancellationRequested)
                    {
                        Logger.Log($"Play (background): Operation cancelled during retry delay");
                        return;
                    }
                    next = Bass.CreateStream(url, 0, flags, null, IntPtr.Zero);
                }
                if (next == 0)
                {
                    var error = Bass.LastError;
                    Logger.LogError($"Bass.CreateStream (next) failed: {error} for URL: {url}");
                    _nextUrl = null;
                    OnPlaybackStateChanged?.Invoke(false);
                    _isPlaying = false;
                    _isPaused = false;
                    return;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    Logger.Log($"Play (background): Operation cancelled after stream creation, freeing stream");
                    try
                    {
                        FadeOutHandle(next);
                        Bass.StreamFree(next);
                    }
                    catch { }
                    return;
                }

                Logger.Log($"Play (background): Stream created successfully, handle: {next}");

                _nextStreamHandle = next;

                Bass.ChannelSetAttribute(_nextStreamHandle, ChannelAttribute.Volume, 0f);
                if (!Bass.ChannelPlay(_nextStreamHandle, false))
                {
                    var error = Bass.LastError;
                    Logger.LogError($"Bass.ChannelPlay (next) failed: {error} for URL: {url}");
                    try { Bass.StreamFree(_nextStreamHandle); } catch { }
                    _nextStreamHandle = 0;
                    _nextUrl = null;
                    OnPlaybackStateChanged?.Invoke(false);
                    _isPlaying = false;
                    _isPaused = false;
                    return;
                }
                Logger.Log($"Play (background): ChannelPlay successful for handle: {next}");

                if (cancellationToken.IsCancellationRequested)
                {
                    Logger.Log($"Play (background): Operation cancelled after channel play, stopping stream");
                    try
                    {
                        FadeOutHandle(_nextStreamHandle);
                        Bass.ChannelStop(_nextStreamHandle);
                    }
                    catch { }
                    try { Bass.StreamFree(_nextStreamHandle); } catch { }
                    _nextStreamHandle = 0;
                    _nextUrl = null;
                    return;
                }

                _nextMetadataSyncHandle = Bass.ChannelSetSync(_nextStreamHandle, SyncFlags.MetadataReceived, 0, MetadataSyncProcInternal, IntPtr.Zero);
                Logger.Log($"Play (background): Metadata sync set for handle: {next}");

                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested)
                {
                    Logger.Log($"Play (background): Operation cancelled during initial delay");
                    StopNextStream();
                    return;
                }

                var initialState = Bass.ChannelIsActive(_nextStreamHandle);
                Logger.Log($"Play (background): Channel state after 500ms: {initialState} for handle: {next}");

                Logger.Log($"Play (background): Waiting for stream to become ready for URL: {url}");
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var lastLogTime = sw.ElapsedMilliseconds;
                long lastPos = 0;
                bool ready = false;

                while (sw.ElapsedMilliseconds < 10000)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        Logger.Log($"Play (background): Operation cancelled during readiness check");
                        StopNextStream();
                        return;
                    }

                    var state = Bass.ChannelIsActive(_nextStreamHandle);

                    long currentPos = Bass.StreamGetFilePosition(_nextStreamHandle, FileStreamPosition.Download);
                    var now = sw.ElapsedMilliseconds;
                    if (now - lastLogTime >= 1000)
                    {
                        double speed = (currentPos - lastPos) / 1024.0 / ((now - lastLogTime) / 1000.0);
                        if (speed < 0) speed = 0;
                        Logger.Log($"Play (background): Buffering... {currentPos / 1024} KB downloaded, Speed: {speed:F1} KB/s, Time: {sw.ElapsedMilliseconds}ms");
                        lastLogTime = now;
                        lastPos = currentPos;
                    }

                    if (state == PlaybackState.Playing || state == PlaybackState.Stalled)
                    {
                        if (sw.ElapsedMilliseconds > 300)
                        {
                            Logger.Log($"Play (background): Stream became ready (state: {state}) after {sw.ElapsedMilliseconds}ms. Total downloaded: {currentPos / 1024} KB.");
                            ready = true;
                            break;
                        }
                    }

                    var metaTags = Bass.ChannelGetTags(_nextStreamHandle, TagType.META);
                    var icyTags = Bass.ChannelGetTags(_nextStreamHandle, TagType.ICY);
                    if (metaTags != IntPtr.Zero || icyTags != IntPtr.Zero)
                    {
                        Logger.Log($"Play (background): Stream has metadata tags, considering ready after {sw.ElapsedMilliseconds}ms");
                        ready = true;
                        break;
                    }

                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    Logger.Log($"Play (background): Operation cancelled after readiness check");
                    StopNextStream();
                    return;
                }

                if (!ready)
                {
                    Logger.LogError($"Next stream did not become ready in time for URL: {url} (timeout after {sw.ElapsedMilliseconds}ms)");
                    StopNextStream();
                    OnPlaybackStateChanged?.Invoke(false);
                    _isPlaying = false;
                    _isPaused = false;
                    return;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    Logger.Log($"Play (background): Operation cancelled before crossfade");
                    StopNextStream();
                    return;
                }

                Logger.Log($"Play (background): Starting crossfade for URL: {url}");
                CrossfadeHandles(_streamHandle, _nextStreamHandle, durationMs: 1500, cancellationToken);

                if (cancellationToken.IsCancellationRequested)
                {
                    Logger.Log($"Play (background): Operation cancelled during crossfade");
                    StopNextStream();
                    return;
                }

                try
                {
                    if (_metadataSyncHandle != 0 && _streamHandle != 0)
                    {
                        Bass.ChannelRemoveSync(_streamHandle, _metadataSyncHandle);
                        _metadataSyncHandle = 0;
                    }
                }
                catch { }

                try
                {
                    Bass.StreamFree(_streamHandle);
                }
                catch { }

                _streamHandle = _nextStreamHandle;
                _nextStreamHandle = 0;

                _metadataSyncHandle = _nextMetadataSyncHandle;
                _nextMetadataSyncHandle = 0;

                _currentUrl = _nextUrl;
                _nextUrl = null;

                _isPlaying = true;
                _isPaused = false;
                _lastMetadata = null;
                _reconnectAttempts = 0;
                _lastSuccessfulDataTime = DateTime.Now;
                _lastPositionCheckTime = DateTime.Now;
                try
                {
                    var playbackPos = Bass.ChannelGetPosition(_streamHandle);
                    if (playbackPos > 0)
                    {
                        _lastPlaybackPosition = playbackPos;
                    }
                }
                catch { }
                Logger.Log($"Play (background): Crossfade complete, new stream is active for URL: {_currentUrl}");
                OnPlaybackStateChanged?.Invoke(true);

                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested)
                {
                    Logger.Log($"Play (background): Operation cancelled after crossfade");
                    return;
                }

                Logger.Log($"Play (background): Checking metadata after crossfade for URL: {_currentUrl}");
                CheckMetadataOnce(_streamHandle);
                StartMetadataPolling();
            }
            catch (OperationCanceledException)
            {
                Logger.Log($"Play (background): Operation was cancelled for URL: {url}");
                StopNextStream();
            }
            catch (Exception ex)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    Logger.LogError($"Error during stream switch for URL: {url}", ex);
                }
                StopNextStream();
                OnPlaybackStateChanged?.Invoke(false);
                _isPlaying = false;
                _isPaused = false;
            }
        }, cancellationToken);

        return true;
    }

    private void StopNextStream()
    {
        try
        {
            if (_nextMetadataSyncHandle != 0 && _nextStreamHandle != 0)
            {
                try { Bass.ChannelRemoveSync(_nextStreamHandle, _nextMetadataSyncHandle); } catch { }
                _nextMetadataSyncHandle = 0;
            }
            if (_nextStreamHandle != 0)
            {
                FadeOutHandle(_nextStreamHandle);
                try { Bass.ChannelStop(_nextStreamHandle); } catch { }
                try { Bass.StreamFree(_nextStreamHandle); } catch { }
                _nextStreamHandle = 0;
            }
            _nextUrl = null;
        }
        catch (Exception ex)
        {
            Logger.LogError("Error stopping next stream", ex);
        }
    }

    private void FadeOutHandle(int handle)
    {
        if (handle == 0) return;

        try
        {
            float currentVolume = Volume;
            var steps = 10;
            var stepVolume = currentVolume / steps;

            for (int i = steps; i > 0; i--)
            {
                var vol = stepVolume * i;
                try
                {
                    Bass.ChannelSetAttribute(handle, ChannelAttribute.Volume, vol);
                }
                catch { }
                Task.Delay(30).Wait();
            }

            try
            {
                Bass.ChannelSetAttribute(handle, ChannelAttribute.Volume, 0);
            }
            catch { }
        }
        catch { }
    }

    private void StopAllStreamsExceptCurrent()
    {
        try
        {
            StopNextStream();
        }
        catch (Exception ex)
        {
            Logger.LogError("Error stopping all streams except current", ex);
        }
    }

    // Helper that starts a stream into provided handle ref (used when no current exists)
    private async Task<bool> StartAndMonitor(int currentHandle, string url, int currentMetadataHandle, bool becomeCurrent)
    {
        try
        {
            Logger.Log($"StartAndMonitor: Connecting to URL: {url} ...");
            var flags = BassFlags.Default | (BassFlags)0x800000;
            int handle = Bass.CreateStream(url, 0, flags, null, IntPtr.Zero);
            if (handle == 0)
            {
                var error = Bass.LastError;
                Logger.LogError($"Bass.CreateStream failed: {error} for URL: {url}");
                _isPlaying = false;
                _isPaused = false;
                OnPlaybackStateChanged?.Invoke(false);
                return false;
            }
            Logger.Log($"StartAndMonitor: Stream created successfully, handle: {handle}");
            int handleRef = handle;

            Logger.Log($"StartAndMonitor: Waiting for stream to become ready for URL: {url}");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool ready = false;
            while (sw.ElapsedMilliseconds < 10000)
            {
                var state = Bass.ChannelIsActive(handleRef);
                long currentPos = Bass.StreamGetFilePosition(handleRef, FileStreamPosition.Download);

                if (state == PlaybackState.Playing || state == PlaybackState.Stalled)
                {
                    if (sw.ElapsedMilliseconds > 300)
                    {
                        Logger.Log($"StartAndMonitor: Stream ready (state: {state}) after {sw.ElapsedMilliseconds}ms. Downloaded: {currentPos / 1024} KB.");
                        ready = true;
                        break;
                    }
                }

                var metaTags = Bass.ChannelGetTags(handleRef, TagType.META);
                if (metaTags != IntPtr.Zero)
                {
                    Logger.Log($"StartAndMonitor: Stream has metadata, considering ready after {sw.ElapsedMilliseconds}ms");
                    ready = true;
                    break;
                }

                await Task.Delay(100).ConfigureAwait(false);
            }

            if (!ready)
            {
                Logger.LogError($"StartAndMonitor: Stream did not become ready in time for URL: {url}");
                try { Bass.StreamFree(handleRef); } catch { }
                if (becomeCurrent)
                {
                    _streamHandle = 0;
                    _currentUrl = null;
                    _isPlaying = false;
                    _isPaused = false;
                    OnPlaybackStateChanged?.Invoke(false);
                }
                return false;
            }

            if (becomeCurrent)
            {
                _streamHandle = handleRef;
                _metadataSyncHandle = Bass.ChannelSetSync(_streamHandle, SyncFlags.MetadataReceived, 0, MetadataSyncProcInternal, IntPtr.Zero);
                _currentUrl = url;
                _isPlaying = true;
                _isPaused = false;
                _lastMetadata = null;
                _reconnectAttempts = 0;
                _lastSuccessfulDataTime = DateTime.Now;
                _lastPositionCheckTime = DateTime.Now;

                try
                {
                    var playbackPos = Bass.ChannelGetPosition(_streamHandle);
                    if (playbackPos > 0)
                    {
                        _lastPlaybackPosition = playbackPos;
                    }
                }
                catch { }
            }

            if (!Bass.ChannelPlay(handleRef, false))
            {
                var error = Bass.LastError;
                Logger.LogError($"StartAndMonitor: Bass.ChannelPlay failed: {error} for URL: {url}");
                try { Bass.StreamFree(handleRef); } catch { }
                if (becomeCurrent)
                {
                    _streamHandle = 0;
                    _currentUrl = null;
                    _isPlaying = false;
                    _isPaused = false;
                    OnPlaybackStateChanged?.Invoke(false);
                }
                return false;
            }

            if (becomeCurrent)
            {
                OnPlaybackStateChanged?.Invoke(true);
                CheckMetadataOnce(_streamHandle);
                StartMetadataPolling();
            }

            Logger.Log($"StartAndMonitor: Stream started successfully for URL: {url}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Exception in StartAndMonitor: {ex.Message}", ex);
            _isPlaying = false;
            _isPaused = false;
            OnPlaybackStateChanged?.Invoke(false);
            return false;
        }
    }

    private void CrossfadeHandles(int oldHandle, int newHandle, int durationMs, CancellationToken cancellationToken = default)
    {
        try
        {
            var steps = Math.Max(20, durationMs / 30);
            var stepDelay = durationMs / steps;

            for (int i = 0; i <= steps; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                float progress = i / (float)steps;

                // ФИКС: Используем Sin/Cos для "Equal Power" кроссфейда, чтобы не было провала громкости
                float newVol = (float)Math.Sin(progress * Math.PI / 2) * Volume;
                float oldVol = (float)Math.Cos(progress * Math.PI / 2) * Volume;

                try
                {
                    if (newHandle != 0)
                        Bass.ChannelSetAttribute(newHandle, ChannelAttribute.Volume, newVol);
                    if (oldHandle != 0)
                        Bass.ChannelSetAttribute(oldHandle, ChannelAttribute.Volume, oldVol);
                }
                catch { }

                if (i < steps)
                {
                    try
                    {
                        cancellationToken.WaitHandle.WaitOne(Math.Max(1, stepDelay));
                    }
                    catch { }
                }
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (newHandle != 0)
                        Bass.ChannelSetAttribute(newHandle, ChannelAttribute.Volume, Volume);
                    if (oldHandle != 0)
                        Bass.ChannelSetAttribute(oldHandle, ChannelAttribute.Volume, 0f);
                }
                catch { }
            }
        }
        catch { }
    }

    private void MetadataSyncProcInternal(int handle, int channel, int data, IntPtr user)
    {
        try
        {
            Logger.Log($"MetadataSyncProcInternal called for handle {handle}");

            string? url = null;
            if (handle == _streamHandle)
            {
                url = _currentUrl;
            }
            else if (handle == _nextStreamHandle)
            {
                url = _nextUrl;
            }

            var metadata = ReadMetadataFromHandle(handle);
            if (!string.IsNullOrEmpty(metadata))
            {
                if (metadata != _lastMetadata || url != _lastMetadataUrl)
                {
                    Logger.Log($"Metadata received via callback: {metadata} for URL: {url}");
                    _lastMetadata = metadata;
                    _lastMetadataUrl = url;
                    OnMetadataReceived?.Invoke(metadata, url);
                }
            }
            else
            {
                Logger.Log($"Metadata callback called but no metadata found for handle {handle}");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError("Error in MetadataSyncProcInternal", ex);
        }
    }

    private string? ReadMetadataFromHandle(int handle)
    {
        try
        {
            var metaTags = Bass.ChannelGetTags(handle, TagType.META);
            if (metaTags != IntPtr.Zero)
            {
                var metadata = ReadICYMetadataWithEncoding(metaTags);
                if (!string.IsNullOrEmpty(metadata))
                    return metadata;
            }

            var icyTags = Bass.ChannelGetTags(handle, TagType.ICY);
            if (icyTags != IntPtr.Zero)
            {
                var metadata = ReadICYMetadataWithEncoding(icyTags);
                if (!string.IsNullOrEmpty(metadata))
                    return metadata;
            }

            var httpTags = Bass.ChannelGetTags(handle, TagType.HTTP);
            if (httpTags != IntPtr.Zero)
            {
                var metadata = ReadIcecastMetadataWithEncoding(httpTags);
                if (!string.IsNullOrEmpty(metadata))
                    return metadata;
            }

            var oggTags = Bass.ChannelGetTags(handle, TagType.OGG);
            if (oggTags != IntPtr.Zero)
            {
                var metadata = ReadIcecastMetadataWithEncoding(oggTags);
                if (!string.IsNullOrEmpty(metadata))
                    return metadata;
            }

            return string.Empty;
        }
        catch (Exception ex)
        {
            Logger.LogError("Error reading metadata from handle", ex);
            return null;
        }
    }

    private void CheckMetadataOnce(int handle)
    {
        try
        {
            string? url = null;
            if (handle == _streamHandle)
            {
                url = _currentUrl;
            }
            else if (handle == _nextStreamHandle)
            {
                url = _nextUrl;
            }

            var metadata = ReadMetadataFromHandle(handle);
            if (!string.IsNullOrEmpty(metadata))
            {
                if (metadata != _lastMetadata || url != _lastMetadataUrl)
                {
                    Logger.Log($"Initial metadata check found: {metadata} for URL: {url}");
                    _lastMetadata = metadata;
                    _lastMetadataUrl = url;
                    OnMetadataReceived?.Invoke(metadata, url);
                }
            }
            else
            {
                Logger.Log($"CheckMetadataOnce: No metadata found for handle {handle}, notifying ViewModel with empty string for URL: {url}");
                OnMetadataReceived?.Invoke(string.Empty, url);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError("Error in CheckMetadataOnce", ex);
        }
    }

    private void StartMetadataPolling()
    {
        StopMetadataPolling();
        _metadataPollTimer = new Timer(_ =>
        {
            if (_streamHandle == 0 || !_isPlaying || _isPaused)
                return;

            try
            {
                var playbackState = Bass.ChannelIsActive(_streamHandle);

                if (playbackState == PlaybackState.Stopped)
                {
                    Logger.LogError($"Stream stopped unexpectedly for URL: {_currentUrl}, attempting reconnect");
                    HandleStreamDisconnection();
                    return;
                }

                if (playbackState != PlaybackState.Playing && playbackState != PlaybackState.Stalled)
                    return;

                var metadata = ReadMetadataFromHandle(_streamHandle);
                if (!string.IsNullOrEmpty(metadata) && (metadata != _lastMetadata || _currentUrl != _lastMetadataUrl))
                {
                    _lastMetadata = metadata;
                    _lastMetadataUrl = _currentUrl;
                    OnMetadataReceived?.Invoke(metadata, _currentUrl);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("Error in metadata polling", ex);
            }
        }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

        StartConnectionMonitoring();
    }

    private void StartConnectionMonitoring()
    {
        StopConnectionMonitoring();
        _lastStalledTime = DateTime.MinValue;
        _lastSuccessfulDataTime = DateTime.Now;
        _lastPositionCheckTime = DateTime.Now;
        _lastPlaybackPosition = 0;

        _connectionMonitorTimer = new Timer(_ =>
        {
            if (_streamHandle == 0 || !_isPlaying || _isPaused)
            {
                _lastStalledTime = DateTime.MinValue;
                return;
            }

            try
            {
                var playbackState = Bass.ChannelIsActive(_streamHandle);

                if (playbackState == PlaybackState.Stopped)
                {
                    Logger.LogError($"Connection monitor detected stream stopped for URL: {_currentUrl}");
                    HandleStreamDisconnection();
                }
                else if (playbackState == PlaybackState.Stalled)
                {
                    if (_lastStalledTime == DateTime.MinValue)
                    {
                        _lastStalledTime = DateTime.Now;
                        Logger.Log($"Stream stalled for URL: {_currentUrl}, monitoring buffer level");
                    }

                        var stalledDuration = (DateTime.Now - _lastStalledTime).TotalSeconds;

                    bool shouldReconnect = false;

                        if (stalledDuration >= StalledTimeoutSeconds)
                        {
                        long currentPosition = 0;
                        try
                        {
                            currentPosition = Bass.ChannelGetPosition(_streamHandle);
                        }
                        catch { }

                        if (_lastPositionCheckTime != DateTime.MinValue && _lastPlaybackPosition > 0)
                        {
                            var timeSinceLastCheck = (DateTime.Now - _lastPositionCheckTime).TotalSeconds;
                            if (timeSinceLastCheck > 0)
                            {
                                var positionDifference = currentPosition - _lastPlaybackPosition;

                                if (positionDifference <= 0 && timeSinceLastCheck > 5)
                                {
                                    Logger.LogError($"Stream stalled for {stalledDuration:F1} seconds, playback position not advancing, treating as disconnection for URL: {_currentUrl}");
                                    shouldReconnect = true;
                                }
                                else if (positionDifference > 0)
                                {
                                    Logger.Log($"Stream buffering (state: Stalled), but playback advancing ({positionDifference} bytes in {timeSinceLastCheck:F1}s), waiting...");
                                    _lastStalledTime = DateTime.Now - TimeSpan.FromSeconds(StalledTimeoutSeconds * 0.5);
                                }
                                else
                                {
                                    Logger.Log($"Stream stalled for {stalledDuration:F1} seconds, monitoring position...");
                                }
                            }
                        }
                        else
                        {
                            shouldReconnect = true;
                        }

                        _lastPlaybackPosition = currentPosition;
                        _lastPositionCheckTime = DateTime.Now;

                        if (shouldReconnect)
                        {
                            Logger.LogError($"Stream stalled for {stalledDuration:F1} seconds, treating as disconnection for URL: {_currentUrl}");
                            HandleStreamDisconnection();
                        }
                    }
                }
                else if (playbackState == PlaybackState.Playing)
                {
                    if (_lastStalledTime != DateTime.MinValue)
                    {
                        var stalledDuration = (DateTime.Now - _lastStalledTime).TotalSeconds;
                        if (stalledDuration > 0)
                        {
                            Logger.Log($"Stream resumed after {stalledDuration:F1} seconds of buffering for URL: {_currentUrl}");
                        }
                    }

                    _lastStalledTime = DateTime.MinValue;
                    _lastSuccessfulDataTime = DateTime.Now;
                    _reconnectAttempts = 0;

                    try
                    {
                        var playbackPos = Bass.ChannelGetPosition(_streamHandle);
                        if (playbackPos > 0)
                        {
                            _lastPlaybackPosition = playbackPos;
                            _lastPositionCheckTime = DateTime.Now;
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("Error in connection monitoring", ex);
            }
        }, null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));
    }

    private void StopConnectionMonitoring()
    {
        _connectionMonitorTimer?.Dispose();
        _connectionMonitorTimer = null;
    }

    private void HandleStreamDisconnection()
    {
        if (string.IsNullOrEmpty(_currentUrl))
            return;

        if (_reconnectAttempts >= MaxReconnectAttempts)
        {
            Logger.LogError($"Max reconnect attempts ({MaxReconnectAttempts}) reached for URL: {_currentUrl}");
            _isPlaying = false;
            _isPaused = false;
            OnPlaybackStateChanged?.Invoke(false);
            _reconnectAttempts = 0;
            return;
        }

        _reconnectAttempts++;
        Logger.Log($"Attempting reconnect {_reconnectAttempts}/{MaxReconnectAttempts} for URL: {_currentUrl}");

        var urlToReconnect = _currentUrl;
        var currentHandle = _streamHandle;

        try
        {
            if (_metadataSyncHandle != 0 && currentHandle != 0)
            {
                Bass.ChannelRemoveSync(currentHandle, _metadataSyncHandle);
                _metadataSyncHandle = 0;
            }

            if (currentHandle != 0)
            {
                Bass.StreamFree(currentHandle);
                _streamHandle = 0;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError("Error cleaning up disconnected stream", ex);
        }

        _isPlaying = false;
        _isPaused = false;
        OnPlaybackStateChanged?.Invoke(false);

        Task.Run(async () =>
        {
            await Task.Delay(1000 * _reconnectAttempts).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(urlToReconnect))
            {
                Logger.Log($"Reconnecting to URL: {urlToReconnect}");
                OnReconnectRequired?.Invoke(urlToReconnect);
            }
        });
    }

    private void StopMetadataPolling()
    {
        _metadataPollTimer?.Dispose();
        _metadataPollTimer = null;
        StopConnectionMonitoring();
    }

    private void FadeIn()
    {
    }

    public void Pause()
    {
        if (_streamHandle != 0 && _isPlaying && !_isPaused)
        {
            Bass.ChannelPause(_streamHandle);
            _isPaused = true;
            _isPlaying = false;
            StopMetadataPolling();
            OnPlaybackStateChanged?.Invoke(false);
        }
    }

    public void Resume()
    {
        if (_streamHandle != 0 && _isPaused)
        {
            Bass.ChannelPlay(_streamHandle, false);
            _isPaused = false;
            _isPlaying = true;
            StartMetadataPolling();
            OnPlaybackStateChanged?.Invoke(true);
        }
    }

    public void Stop()
    {
        try
        {
            if (_currentPlayCancellation != null && !_currentPlayCancellation.Token.IsCancellationRequested)
            {
                try
                {
                    _currentPlayCancellation.Cancel();
        }
                catch (ObjectDisposedException) { }
            }
        }
        catch (ObjectDisposedException) { }
        catch { }

        StopMetadataPolling();
        _reconnectAttempts = 0;
        _lastMetadata = null;

        try
        {
            if (_metadataSyncHandle != 0 && _streamHandle != 0)
            {
                Bass.ChannelRemoveSync(_streamHandle, _metadataSyncHandle);
                _metadataSyncHandle = 0;
            }
        }
        catch { }

        StopNextStream();

        if (_streamHandle != 0)
        {
            FadeOutSync();
            try { Bass.ChannelStop(_streamHandle); } catch { }
            try { Bass.StreamFree(_streamHandle); } catch { }
            _streamHandle = 0;
            _currentUrl = null;
        }

        _isPlaying = false;
        _isPaused = false;
        OnPlaybackStateChanged?.Invoke(false);
    }

    private void MetadataSyncProc(int handle, int channel, int data, IntPtr user)
    {
    }

    private string? ExtractStreamTitle(string str)
    {
        if (string.IsNullOrEmpty(str))
            return null;

        var matchQuoted = Regex.Match(str, @"StreamTitle=['""](?<title>[^'""]+)['""]", RegexOptions.IgnoreCase);
        if (matchQuoted.Success)
        {
            return matchQuoted.Groups["title"].Value.Trim();
        }

        var matchSimple = Regex.Match(str, @"StreamTitle=(?<title>[^;]+)", RegexOptions.IgnoreCase);
        if (matchSimple.Success)
        {
            return matchSimple.Groups["title"].Value.Trim(' ', '\'', '"', '\0');
        }

        var streamTitleIndex = str.IndexOf("StreamTitle=", StringComparison.OrdinalIgnoreCase);
        if (streamTitleIndex >= 0)
        {
            var startIndex = streamTitleIndex + 12;
            var endIndex = str.IndexOf(';', startIndex);
            if (endIndex < 0) endIndex = str.Length;
            return str.Substring(startIndex, endIndex - startIndex).Trim().Trim('\'', '"', '\0');
        }

        return null;
    }

    private string? ReadICYMetadataWithEncoding(IntPtr tags)
{
    if (tags == IntPtr.Zero)
        return null;

    try
    {
        int length = 0;
        while (length < 8192)
        {
            byte b = Marshal.ReadByte(tags, length);
            if (b == 0) break;
            length++;
        }

        if (length == 0) return null;

        byte[] bytes = new byte[length];
        Marshal.Copy(tags, bytes, 0, length);

        bool isPureASCII = true;
        for (int i = 0; i < length; i++)
        {
            if (bytes[i] > 0x7F)
            {
                isPureASCII = false;
                break;
            }
        }

        if (isPureASCII)
        {
            var asciiStr = Encoding.ASCII.GetString(bytes);
            var match = ExtractStreamTitle(asciiStr);
            if (!string.IsNullOrEmpty(match))
            {
                return match;
            }
        }

        var encodings = new List<Encoding>();
        encodings.Add(Encoding.UTF8);
        try { encodings.Add(Encoding.GetEncoding(1251)); } catch { }
        try { encodings.Add(Encoding.GetEncoding("windows-1251")); } catch { }
        try { encodings.Add(Encoding.GetEncoding("koi8-r")); } catch { }
        try { encodings.Add(Encoding.GetEncoding(866)); } catch { }
        try { encodings.Add(Encoding.GetEncoding("iso-8859-1")); } catch { }

        string? bestResult = null;
        int bestScore = -1;

        foreach (var encoding in encodings)
        {
            try
            {
                var str = encoding.GetString(bytes);
                if (string.IsNullOrEmpty(str)) continue;

                var match = ExtractStreamTitle(str);
                if (!string.IsNullOrEmpty(match))
                {
                    var score = ImprovedCountValidCyrillic(match);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestResult = match;
                        if (isPureASCII || encoding == Encoding.UTF8)
                        {
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"ReadICYMetadataWithEncoding: Error with {encoding.WebName}: {ex.Message}");
            }
        }

        if (bestResult != null && bestScore < 20 && ContainsSuspiciousChars(bestResult))
        {
            var fixedResult = FixEncoding(bestResult);
            var fixedScore = ImprovedCountValidCyrillic(fixedResult);
            if (fixedScore > bestScore)
            {
                bestResult = fixedResult;
                bestScore = fixedScore;
            }
        }

        return bestResult;
    }
    catch (Exception ex)
    {
        Logger.LogError("Error parsing ICY metadata", ex);
        return null;
    }
}

private int ImprovedCountValidCyrillic(string text)
{
    if (string.IsNullOrEmpty(text)) return -10;

    int score = 0;
    int cyrillicCount = 0;
    int validCharCount = 0;
    int invalidCharCount = 0;
    int totalLength = text.Length;

    foreach (char c in text)
    {
        if ((c >= 0x0400 && c <= 0x04FF) || (c >= 0x0500 && c <= 0x052F))  // Cyrillic
        {
            cyrillicCount++;
            score += 5;
            validCharCount++;
            string lowerC = char.ToLowerInvariant(c).ToString();
            if ("аеиоуыэюябвгджийклмнопрстуфхцчшщъьё".Contains(lowerC)) score += 2;
        }
        else if (char.IsLetterOrDigit(c) && c < 0x0100)
        {
            validCharCount++;
            score += 1;
        }
        else if (char.IsWhiteSpace(c))
        {
            validCharCount++;
        }
        else if (c == 0xFFFD || c == '?' || c == '\0')
        {
            invalidCharCount += 3;
            score -= 3;
        }
        else
        {
            invalidCharCount++;
            score -= 2;
        }
    }

    if (cyrillicCount > totalLength * 0.3) score += 20;
    if (invalidCharCount > totalLength * 0.1) score -= 15;
    if (text.Contains("-") || text.Contains("feat.") || text.Contains("&")) score += 5;

    return score;
}

private bool ContainsSuspiciousChars(string text)
{
    return Regex.IsMatch(text, "[ÐÒÍÃàáâãäåæçèéêëìíîïðñòóôõö÷øùúûüýþÞÊÀËÓŽÿÑî]");
}

private string FixEncoding(string text)
{
    if (string.IsNullOrEmpty(text))
        return text;

    if (HasValidCyrillicInBass(text) && !ContainsWrongLatinCharsInBass(text))
        return text;

    bool hasSuspiciousChars = Regex.IsMatch(text, "[ÐÒÍÃàáâãäåæçèéêëìíîïðñòóôõö÷øùúûüýþÞÊÀËÓ]");
    bool hasWrongLatin = ContainsWrongLatinCharsInBass(text);
    bool needsFix = hasSuspiciousChars || hasWrongLatin;

    if (!needsFix && !HasValidCyrillicInBass(text))
    {
        foreach (char c in text)
        {
            if ((c >= 0x00C0 && c <= 0x00FF && c != 0x00D7 && c != 0x00F7) || c == 0x00DF)
            {
                needsFix = true;
                break;
            }
        }
    }

    if (needsFix)
    {
        Logger.Log($"FixEncoding: Detected wrong encoding in text: '{text}'");

        string? bestResult = null;
        int bestScore = -1;

        try
        {
            var bytes = Encoding.GetEncoding("windows-1252").GetBytes(text);
            var fixedText = Encoding.GetEncoding("windows-1251").GetString(bytes);

            if (HasValidCyrillicInBass(fixedText) && !ContainsWrongLatinCharsInBass(fixedText))
            {
                Logger.Log($"FixEncoding: Fixed Windows-1252->Windows-1251: '{text}' -> '{fixedText}'");
                return fixedText;
            }

            if (HasValidCyrillicInBass(fixedText))
            {
                var score = ImprovedCountValidCyrillic(fixedText);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestResult = fixedText;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"FixEncoding: Error with Windows-1252->Windows-1251: {ex.Message}");
        }

        try
        {
            var bytes = Encoding.GetEncoding("ISO-8859-1").GetBytes(text);
            var fixedText = Encoding.GetEncoding("windows-1251").GetString(bytes);

            if (HasValidCyrillicInBass(fixedText) && !ContainsWrongLatinCharsInBass(fixedText))
            {
                Logger.Log($"FixEncoding: Fixed ISO-8859-1->Windows-1251: '{text}' -> '{fixedText}'");
                return fixedText;
            }

            if (HasValidCyrillicInBass(fixedText))
            {
                var score = ImprovedCountValidCyrillic(fixedText);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestResult = fixedText;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"FixEncoding: Error with ISO-8859-1->Windows-1251: {ex.Message}");
        }

        if (bestResult != null && bestScore > 0)
        {
            Logger.Log($"FixEncoding: Using best result (score: {bestScore}): '{text}' -> '{bestResult}'");
            return bestResult;
        }

        if (needsFix)
        {
            try
            {
                var bytes = Encoding.GetEncoding("windows-1252").GetBytes(text);
                var fixedText = Encoding.GetEncoding("windows-1251").GetString(bytes);

                if (HasValidCyrillicInBass(fixedText))
                {
                    Logger.Log($"FixEncoding: Force fixed Windows-1252->Windows-1251: '{text}' -> '{fixedText}'");
                    return fixedText;
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"FixEncoding: Error with force fix: {ex.Message}");
            }
        }
    }

    return text;
}

private bool HasValidCyrillicInBass(string text)
{
    if (string.IsNullOrEmpty(text))
        return false;

    foreach (char c in text)
    {
        if ((c >= 0x0400 && c <= 0x04FF) || (c >= 0x0500 && c <= 0x052F))
        {
            return true;
        }
    }

    return false;
}

private bool ContainsWrongLatinCharsInBass(string text)
{
    if (string.IsNullOrEmpty(text))
        return false;

    int suspiciousCount = 0;
    int totalLetters = 0;

    foreach (char c in text)
    {
        if (char.IsLetter(c))
        {
            totalLetters++;
            if ((c >= 0x00C0 && c <= 0x00FF && c != 0x00D7 && c != 0x00F7) ||
                c == 0x00DF ||
                (c >= 0x00C0 && c <= 0x01FF && c != 0x00D7 && c != 0x00F7))
            {
                suspiciousCount++;
            }
        }
    }

    if (totalLetters == 0) return false;
    return (double)suspiciousCount / totalLetters > 0.1;
}

    private int CountValidCyrillic(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        int cyrillicCount = 0;
        int validCharCount = 0;
        int invalidCharCount = 0;

        foreach (char c in text)
        {
            if ((c >= 0x0400 && c <= 0x04FF) || (c >= 0x0500 && c <= 0x052F))
            {
                cyrillicCount += 3;
                validCharCount++;
            }
            else if (c >= 0x20 && c < 0x7F)
            {
                validCharCount++;
            }
            else if (c == 0x20 || c == 0x09 || c == 0x0A || c == 0x0D)
            {
                validCharCount++;
            }
            else if (c == 0xFFFD || c == '?')
            {
                invalidCharCount += 2;
            }
            else if (c < 0x20 && c != 0x09 && c != 0x0A && c != 0x0D)
            {
                invalidCharCount++;
            }
        }

        int score = cyrillicCount + validCharCount - invalidCharCount;
        Logger.Log($"CountValidCyrillic: text='{text.Substring(0, Math.Min(50, text.Length))}', cyrillic={cyrillicCount}, valid={validCharCount}, invalid={invalidCharCount}, score={score}");
        return score;
    }

    private string? ReadICYMetadata(IntPtr tags)
    {
        if (tags == IntPtr.Zero) return null;
        try
        {
            var str = Marshal.PtrToStringAnsi(tags);
            if (string.IsNullOrEmpty(str)) return null;
            var nullIndex = str.IndexOf('\0');
            if (nullIndex > 0) str = str.Substring(0, nullIndex);

            var streamTitleIndex = str.IndexOf("StreamTitle=", StringComparison.OrdinalIgnoreCase);
            if (streamTitleIndex >= 0)
            {
                var startIndex = streamTitleIndex + 12;
                var endIndex = str.IndexOf(';', startIndex);
                if (endIndex < 0) endIndex = str.Length;
                var value = str.Substring(startIndex, endIndex - startIndex).Trim().Trim('\'', '"', '\0');
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }

            var lines = str.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("StreamTitle=", StringComparison.OrdinalIgnoreCase))
                {
                    var value = trimmed.Substring(12).Trim(' ', '\'', '"', '\0');
                    if (!string.IsNullOrWhiteSpace(value)) return value;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError("Error parsing ICY metadata", ex);
        }
        return null;
    }

    private string? ReadIcecastMetadataWithEncoding(IntPtr tags)
    {
        if (tags == IntPtr.Zero) return null;

        try
        {
            int length = 0;
            while (length < 8192)
            {
                byte b = Marshal.ReadByte(tags, length);
                if (b == 0) break;
                length++;
            }

            if (length == 0) return null;

            byte[] bytes = new byte[length];
            Marshal.Copy(tags, bytes, 0, length);

            var rawHex = BitConverter.ToString(bytes, 0, Math.Min(100, length)).Replace("-", " ");
            Logger.Log($"ReadIcecastMetadataWithEncoding: Raw bytes (first 100): {rawHex}");

            var encodings = new List<Encoding>();
            try { encodings.Add(Encoding.GetEncoding(1251)); } catch { }
            try { encodings.Add(Encoding.GetEncoding("windows-1251")); } catch { }
            try { encodings.Add(Encoding.GetEncoding("koi8-r")); } catch { }
            try { encodings.Add(Encoding.GetEncoding(866)); } catch { }
            try { encodings.Add(Encoding.GetEncoding("cp866")); } catch { }
            try { encodings.Add(Encoding.GetEncoding("iso-8859-1")); } catch { }
            try { encodings.Add(Encoding.GetEncoding(28591)); } catch { }
            encodings.Add(Encoding.UTF8);
            try { encodings.Add(Encoding.GetEncoding(1252)); } catch { }
            try { encodings.Add(Encoding.GetEncoding("windows-1252")); } catch { }
            encodings.Add(Encoding.Default);

            string? bestResult = null;
            int bestScore = -1;

            foreach (var encoding in encodings)
            {
                try
                {
                    var str = encoding.GetString(bytes);
                    if (string.IsNullOrEmpty(str)) continue;

                    Logger.Log($"ReadIcecastMetadataWithEncoding: Trying {encoding.WebName}, str preview: '{str.Substring(0, Math.Min(100, str.Length))}', full len: {str.Length}");

                    var matchIcyTitle = Regex.Match(str, @"icy-title:\s*(?<title>[^\r\n]+)", RegexOptions.IgnoreCase);
                    if (matchIcyTitle.Success)
                    {
                        var value = matchIcyTitle.Groups["title"].Value.Trim().Trim('\'', '"', '\0');
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            var score = ImprovedCountValidCyrillic(value);
                            Logger.Log($"ReadIcecastMetadataWithEncoding: Found icy-title:: '{value}', score: {score}");
                            if (score > bestScore)
                            {
                                bestScore = score;
                                bestResult = value;
                            }
                        }
                    }

                    var matchIcyName = Regex.Match(str, @"icy-name[:=]\s*(?<name>[^\r\n]+)", RegexOptions.IgnoreCase);
                    if (matchIcyName.Success)
                    {
                        var value = matchIcyName.Groups["name"].Value.Trim().Trim('\'', '"', '\0');
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            var score = ImprovedCountValidCyrillic(value);
                            Logger.Log($"ReadIcecastMetadataWithEncoding: Found icy-name: '{value}', score: {score}");
                            if (score > bestScore)
                            {
                                bestScore = score;
                                bestResult = value;
                            }
                        }
                    }

                    var matchStreamTitle = Regex.Match(str, @"StreamTitle=['""](?<title>[^'""]+)['""]", RegexOptions.IgnoreCase);
                    if (matchStreamTitle.Success)
                    {
                        var value = matchStreamTitle.Groups["title"].Value.Trim();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            var score = ImprovedCountValidCyrillic(value);
                            Logger.Log($"ReadIcecastMetadataWithEncoding: Found StreamTitle (quoted): '{value}', score: {score}");
                            if (score > bestScore)
                            {
                                bestScore = score;
                                bestResult = value;
                            }
                        }
                    }

                    var matchStreamTitleSimple = Regex.Match(str, @"StreamTitle=(?<title>[^;\r\n]+)", RegexOptions.IgnoreCase);
                    if (matchStreamTitleSimple.Success)
                    {
                        var value = matchStreamTitleSimple.Groups["title"].Value.Trim(' ', '\'', '"', '\0');
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            var score = ImprovedCountValidCyrillic(value);
                            Logger.Log($"ReadIcecastMetadataWithEncoding: Found StreamTitle (simple): '{value}', score: {score}");
                            if (score > bestScore)
                            {
                                bestScore = score;
                                bestResult = value;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError($"ReadIcecastMetadataWithEncoding: Error with {encoding.WebName}: {ex.Message}");
                }
            }

            if (bestResult != null && bestScore < 20 && ContainsSuspiciousChars(bestResult))
            {
                var fixedResult = FixEncoding(bestResult);
                var fixedScore = ImprovedCountValidCyrillic(fixedResult);
                if (fixedScore > bestScore)
                {
                    Logger.Log($"ReadIcecastMetadataWithEncoding: Applied FixEncoding: '{bestResult}' -> '{fixedResult}', score {bestScore} -> {fixedScore}");
                    bestResult = fixedResult;
                    bestScore = fixedScore;
                }
            }

            if (bestResult != null && bestScore >= 0)
            {
                Logger.Log($"ReadIcecastMetadataWithEncoding: Selected best: '{bestResult}' (score: {bestScore})");
                return bestResult;
            }

            Logger.Log("ReadIcecastMetadataWithEncoding: No valid metadata found");
            return null;
        }
        catch (Exception ex)
        {
            Logger.LogError("Error parsing Icecast metadata with encoding", ex);
        }

        return null;
    }

    public Dictionary<string, string> GetAllHTTPHeaders(int handle)
    {
        var headers = new Dictionary<string, string>();

        var httpTags = Bass.ChannelGetTags(handle, TagType.HTTP);
        if (httpTags == IntPtr.Zero) return headers;

        try
        {
            int length = 0;
            var maxLength = 8192;
            var bytes = new List<byte>();

            while (length < maxLength)
            {
                byte b = Marshal.ReadByte(httpTags, length);
                if (b == 0) break;
                bytes.Add(b);
                length++;
            }

            if (bytes.Count == 0) return headers;

            var encodings = new[]
            {
                Encoding.GetEncoding("windows-1251"),
                Encoding.UTF8,
                Encoding.GetEncoding("iso-8859-1"),
                Encoding.ASCII
            };

            string? bestStr = null;
            foreach (var encoding in encodings)
            {
                try
                {
                    var str = encoding.GetString(bytes.ToArray());
                    if (string.IsNullOrEmpty(str)) continue;

                    var lines = str.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    var hasValidHeaders = false;
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        if (trimmed.StartsWith("icy-", StringComparison.OrdinalIgnoreCase) ||
                            trimmed.StartsWith("content-", StringComparison.OrdinalIgnoreCase) ||
                            trimmed.StartsWith("server:", StringComparison.OrdinalIgnoreCase) ||
                            trimmed.StartsWith("date:", StringComparison.OrdinalIgnoreCase) ||
                            trimmed.StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase))
                        {
                            hasValidHeaders = true;
                            break;
                        }
                    }
                    if (hasValidHeaders)
                    {
                        bestStr = str;
                        break;
                    }
                }
                catch { }
            }

            if (bestStr != null)
            {
                var lines = bestStr.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrWhiteSpace(trimmed)) continue;

                    var colonIndex = trimmed.IndexOf(':');
                    if (colonIndex > 0)
                    {
                        var key = trimmed.Substring(0, colonIndex).Trim();
                        var value = trimmed.Substring(colonIndex + 1).Trim();

                        if (!headers.ContainsKey(key))
                        {
                            headers[key] = value;
                        }
                    }
                    else if (trimmed.StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!headers.ContainsKey("Status"))
                        {
                            headers["Status"] = trimmed;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError("Error getting HTTP headers", ex);
        }

        return headers;
    }

    private void LogAllHTTPHeaders(IntPtr tags)
    {
        if (tags == IntPtr.Zero) return;

        try
        {
            int length = 0;
            var maxLength = 8192;
            var bytes = new List<byte>();

            while (length < maxLength)
            {
                byte b = Marshal.ReadByte(tags, length);
                if (b == 0) break;
                bytes.Add(b);
                length++;
            }

            if (bytes.Count == 0) return;

            var encodings = new[]
            {
                Encoding.GetEncoding("windows-1251"),
                Encoding.UTF8,
                Encoding.GetEncoding("iso-8859-1"),
                Encoding.ASCII
            };

            string? bestStr = null;
            foreach (var encoding in encodings)
            {
                try
                {
                    var str = encoding.GetString(bytes.ToArray());
                    if (string.IsNullOrEmpty(str)) continue;

                    var lines = str.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    var hasValidHeaders = false;
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        if (trimmed.StartsWith("icy-", StringComparison.OrdinalIgnoreCase) ||
                            trimmed.StartsWith("content-", StringComparison.OrdinalIgnoreCase) ||
                            trimmed.StartsWith("server:", StringComparison.OrdinalIgnoreCase) ||
                            trimmed.StartsWith("date:", StringComparison.OrdinalIgnoreCase))
                        {
                            hasValidHeaders = true;
                            break;
                        }
                    }
                    if (hasValidHeaders)
                    {
                        bestStr = str;
                        break;
                    }
                }
                catch { }
            }

            if (bestStr != null)
            {
                var lines = bestStr.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("icy-", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.StartsWith("content-type:", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.StartsWith("server:", StringComparison.OrdinalIgnoreCase))
                    {
                        Logger.Log($"HTTP Header: {trimmed}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError("Error logging HTTP headers", ex);
        }
    }

    private string? ReadTagStringOld(IntPtr tags, string tagName)
    {
        if (tags == IntPtr.Zero) return null;
        try
        {
            var ptr = tags;
            var maxLength = 8192;
            var buffer = new byte[maxLength];
            Marshal.Copy(ptr, buffer, 0, Math.Min(maxLength, 8192));

            var text = Encoding.UTF8.GetString(buffer);
            var nullIndex = text.IndexOf('\0');
            if (nullIndex > 0) text = text.Substring(0, nullIndex);

            var lines = text.Split(new[] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (trimmedLine.StartsWith(tagName + "=", StringComparison.OrdinalIgnoreCase))
                {
                    var value = trimmedLine.Substring(tagName.Length + 1).Trim(' ', '\'', '"', '\0');
                    if (!string.IsNullOrWhiteSpace(value)) return value;
                }
            }

            var directRead = Marshal.PtrToStringAnsi(tags);
            if (!string.IsNullOrEmpty(directRead))
            {
                var lines2 = directRead.Split(new[] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines2)
                {
                    var trimmedLine = line.Trim();
                    if (trimmedLine.StartsWith(tagName + "=", StringComparison.OrdinalIgnoreCase))
                    {
                        var value = trimmedLine.Substring(tagName.Length + 1).Trim(' ', '\'', '"', '\0');
                        if (!string.IsNullOrWhiteSpace(value)) return value;
                    }
                }
            }
        }
        catch { }
        return null;
    }

    private void FadeOutSync()
    {
        if (_streamHandle == 0) return;

        try
        {
            var currentVolume = Volume;
            var steps = 10;
            var stepVolume = currentVolume / steps;

            for (int i = steps; i > 0; i--)
            {
                var vol = stepVolume * i;
                Bass.ChannelSetAttribute(_streamHandle, ChannelAttribute.Volume, vol);
                System.Threading.Thread.Sleep(30);
            }

            Bass.ChannelSetAttribute(_streamHandle, ChannelAttribute.Volume, 0);
        }
        catch
        {
        }
    }

    public bool IsPlaying => _isPlaying && !_isPaused;
    public bool IsPaused => _isPaused;

    private float _volume = 0.5f;
    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Max(0f, Math.Min(1f, value));
            if (_streamHandle != 0)
            {
                Bass.ChannelSetAttribute(_streamHandle, ChannelAttribute.Volume, _volume);
            }
        }
    }


    public int GetCurrentStreamHandle()
    {
        return _streamHandle != 0 ? _streamHandle : _nextStreamHandle;
    }

    public string? GetStreamFormat()
    {
        var handle = GetCurrentStreamHandle();
        if (handle == 0)
            return null;

        try
        {
            if (Bass.ChannelGetInfo(handle, out var info))
            {
                if (info.ChannelType.HasFlag(ChannelType.AAC))
                    return "AAC";
                if (info.ChannelType.HasFlag(ChannelType.MP3))
                    return "MP3";
                if (info.ChannelType.HasFlag(ChannelType.OGG))
                    return "OGG";
                return info.ChannelType.ToString();
            }
        }
        catch (Exception ex)
        {
            Logger.LogError("Error getting stream format", ex);
        }

        return null;
    }

    public void Dispose()
    {
        try
        {
            if (_currentPlayCancellation != null && !_currentPlayCancellation.Token.IsCancellationRequested)
            {
                try
                {
                    _currentPlayCancellation.Cancel();
        }
                catch (ObjectDisposedException) { }
            }
        }
        catch (ObjectDisposedException) { }
        catch { }

        try
        {
            _currentPlayCancellation?.Dispose();
        }
        catch { }

        Stop();

        try
        {
            _playSemaphore?.Dispose();
        }
        catch { }

        try
        {
            Bass.Free();
        }
        catch { }
    }
}
