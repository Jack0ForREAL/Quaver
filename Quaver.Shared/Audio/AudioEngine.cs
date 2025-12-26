using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Quaver.API.Helpers;
using Quaver.API.Maps;
using Quaver.Shared.Config;
using Quaver.Shared.Database.Maps;
using Quaver.Shared.Modifiers;
using Quaver.Shared.Scheduling;
using Wobble;
using Wobble.Audio;
using Wobble.Audio.Tracks;
using Wobble.Graphics;

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
        ///     This decouples visual smoothness from audio buffer updates.
        /// </summary>
        public static double Time
        {
            get
            {
                if (Track == null || Track.IsDisposed) return 0;

                if (!Track.IsPlaying)
                {
                    _isHybridRunning = false;
                    HybridClock.Stop();
                    return Track.Time;
                }

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
                double drift = Math.Abs(projectedTime - audioTime);
                
                if (drift > 20) 
                {
                    _lastAudioTime = audioTime;
                    HybridClock.Restart();
                    return audioTime;
                }

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
            var seekTime = GetNearestSnapTimeFromTime(map, direction, snap, Track.Time);

            if (seekTime < 0 || seekTime > Track.Length)
                return;

            Track.Seek(seekTime);
            
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

        /// <summary>
        ///     Loads an audio track for a specific map.
        ///     Restored for V2 compatibility.
        /// </summary>
        public static IAudioTrack LoadMapAudioTrack(Map map)
        {
            IAudioTrack track;

            try
            {
                track = new AudioTrack(MapManager.GetAudioPath(map), false, false);
            }
            catch (Exception)
            {
                track = new AudioTrackVirtual(map.SongLength + 5000);
            }

            return track;
        }

        public static void MeasureAudioStartDelay()
        {
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
