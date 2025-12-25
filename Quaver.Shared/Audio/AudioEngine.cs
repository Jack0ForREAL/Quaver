using System;
using System.Diagnostics;
using System.Threading;
using Quaver.API.Helpers;
using Quaver.API.Maps;
using Quaver.Shared.Config;
using Quaver.Shared.Database.Maps;
using Quaver.Shared.Modifiers;
using Wobble;
using Wobble.Audio;
using Wobble.Audio.Tracks;

namespace Quaver.Shared.Audio
{
    public static class AudioEngine
    {
        public static IAudioTrack Track { get; internal set; }
        public static Map Map { get; set; }
        private static CancellationTokenSource Source { get; set; } = new CancellationTokenSource();
        public static double MeasuredAudioStartDelay { get; internal set; }

        // v2: Hybrid Clock Variables
        private static readonly Stopwatch HybridClock = new Stopwatch();
        private static double _lastAudioTime;
        private static bool _isHybridRunning;

        /// <summary>
        ///     v2: Gets the smoothed, interpolated time.
        ///     This decouples visual smoothness from audio buffer updates, fixing "jittery" notes on high refresh rates.
        /// </summary>
        public static double Time
        {
            get
            {
                if (Track == null || Track.IsDisposed) return 0;

                // If stopped, just return track time
                if (!Track.IsPlaying)
                {
                    _isHybridRunning = false;
                    HybridClock.Stop();
                    return Track.Time;
                }

                // If we just started playing, sync the clock
                if (!_isHybridRunning)
                {
                    _isHybridRunning = true;
                    _lastAudioTime = Track.Time;
                    HybridClock.Restart();
                    return _lastAudioTime;
                }

                double audioTime = Track.Time;
                double clockTime = HybridClock.Elapsed.TotalMilliseconds * Track.Rate;
                double projectedTime = _lastAudioTime + clockTime;

                // Sync logic: If the audio driver has moved significantly (buffer update), re-sync.
                // We allow a 20ms drift window before hard-snapping to prevent micro-stutters.
                double drift = Math.Abs(projectedTime - audioTime);
                
                if (drift > 20) 
                {
                     // Hard sync (Audio driver update or seek happened)
                    _lastAudioTime = audioTime;
                    HybridClock.Restart();
                    return audioTime;
                }

                // Return the smooth projected time for visuals
                return projectedTime;
            }
        }

        public static void LoadCurrentTrack(bool preview = false, int time = 300000)
        {
            Source.Cancel();
            Source.Dispose();
            Source = new CancellationTokenSource();

            Map = MapManager.Selected.Value;
            var token = Source.Token;

            try
            {
                if (Track != null && !Track.IsDisposed)
                    Track.Dispose();

                // v2: Optimization - Explicitly dispose old clock state
                HybridClock.Reset();
                _isHybridRunning = false;

                var newTrack = new AudioTrack(MapManager.CurrentAudioPath, false)
                {
                    Rate = ModHelper.GetRateFromMods(ModManager.Mods),
                };

                token.ThrowIfCancellationRequested();

                Track = newTrack;
                Track.ApplyRate(ConfigManager.Pitched.Value);
            }
            catch (Exception)
            {
                if (Track is { IsDisposed: false })
                    Track.Dispose();

                Track = new AudioTrackVirtual(time);
            }
        }

        public static void PlaySelectedTrackAtPreview()
        {
            try
            {
                if (MapManager.Selected?.Value == null) return;

                if (Track != null)
                {
                    lock (Track) LoadCurrentTrack(true);
                }
                else
                {
                    LoadCurrentTrack(true);
                }

                if (Track == null) return;

                lock (Track)
                {
                    Track?.Seek(MapManager.Selected.Value.AudioPreviewTime);
                    if (!Track.IsPlaying) Track?.Play();
                }
            }
            catch (Exception) { /* ignored */ }
        }

        public static void SeekTrackToNearestSnap(Qua map, Direction direction, int snap)
        {
            // Use the raw Track.Time for seeking logic, not the interpolated time
            var seekTime = GetNearestSnapTimeFromTime(map, direction, snap, Track.Time);

            if (seekTime < 0 || seekTime > Track.Length)
                return;

            Track.Seek(seekTime);
            
            // v2: Reset hybrid clock on seek
            HybridClock.Restart();
            _lastAudioTime = seekTime;
        }

        public static double GetNearestSnapTimeFromTime(Qua map, Direction direction, float snap, double time)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            var point = map.GetTimingPointAt(time);
            if (point == null) return 0;

            var snapTimePerBeat = 60000 / point.Bpm / snap;
            double pointToSnap;

            switch (direction)
            {
                case Direction.Forward:
                    pointToSnap = time + snapTimePerBeat;
                    break;
                case Direction.Backward:
                    pointToSnap = time - snapTimePerBeat;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(direction), direction, null);
            }

            var nearestTick = Math.Round((pointToSnap - point.StartTime) / snapTimePerBeat) * snapTimePerBeat + point.StartTime;

            if (Math.Abs(nearestTick - time) <= snapTimePerBeat * 1.01)
                return nearestTick;

            if (direction == Direction.Backward)
                return (Math.Round((pointToSnap - point.StartTime) / snapTimePerBeat) + 1) * snapTimePerBeat + point.StartTime;

            return (Math.Round((pointToSnap - point.StartTime) / snapTimePerBeat) - 1) * snapTimePerBeat + point.StartTime;
        }

        public static void MeasureAudioStartDelay()
        {
            // Kept primarily for legacy compatibility, but v2 relies less on this hack
            var prevTrack = Track;
            Track = new AudioTrack(GameBase.Game.Resources.Get($"Quaver.Resources/Maps/Offset/offset.mp3"));
            Track.Volume = 0;
            var stopwatch = Stopwatch.StartNew();
            Track.Play();
            while (Track.Time == 0) { }
            stopwatch.Stop();
            Track.Stop();
            Track.Dispose();
            MeasuredAudioStartDelay = stopwatch.ElapsedMilliseconds;
            Track = prevTrack;
        }
    }
}