using System;
using Microsoft.Xna.Framework;
using Quaver.API.Helpers;
using Quaver.Shared.Audio;
using Quaver.Shared.Config;
using Quaver.Shared.Modifiers;
using Quaver.Shared.Screens.Tournament.Gameplay;
using Wobble; // <--- This was missing!
using Wobble.Audio.Tracks;
using Wobble.Logging;
using MathHelper = Microsoft.Xna.Framework.MathHelper;

namespace Quaver.Shared.Screens.Gameplay
{
    public class GameplayAudioTiming
    {
        private GameplayScreen Screen { get; }
        public static int StartDelay { get; } = 3000;
        public double Time { get; set; }

        // v2: Simplified start threshold
        private double TimeToPlayAudio { get; } = ConfigManager.SmoothAudioStart.Value
            ? -Math.Clamp(AudioEngine.MeasuredAudioStartDelay, 0, 40) * AudioEngine.Track.Rate
            : 0;

        public GameplayAudioTiming(GameplayScreen screen)
        {
            Screen = screen;

            if (Screen.IsSongSelectPreview || Screen.UseExistingAudioTime)
                return;

            try
            {
                if (Screen.IsCalibratingOffset)
                    AudioEngine.Track = new AudioTrack(GameBase.Game.Resources.Get($"Quaver.Resources/Maps/Offset/offset.mp3"));
                else
                {
                    AudioEngine.LoadCurrentTrack();
                    AudioEngine.Track.Rate = ModHelper.GetRateFromMods(ModManager.Mods);
                }

                if (Screen.IsPlayTesting)
                {
                    const int delay = 500;
                    if (Screen.PlayTestAudioTime < StartDelay)
                    {
                        Time = Screen.PlayTestAudioTime <= 500 ? -1500 : -delay;
                        return;
                    }

                    AudioEngine.Track.Seek(MathHelper.Clamp((int)Screen.PlayTestAudioTime - delay, 0, (int)AudioEngine.Track.Length));
                    Time = AudioEngine.Track.Time;
                    return;
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, LogType.Runtime);
            }

            Time = -StartDelay * AudioEngine.Track.Rate;
        }

        public void Update(GameTime gameTime)
        {
            if (Screen.IsPaused) return;

            var isTournament = Screen is TournamentGameplayScreen;
            if (Screen.IsMultiplayerGame && !Screen.IsMultiplayerGameStarted && !isTournament)
                return;

            // Handle Pre-Song Delay
            if (Time < TimeToPlayAudio)
            {
                Time += gameTime.ElapsedGameTime.TotalMilliseconds * AudioEngine.Track.Rate;
                return;
            }

            // Start Audio
            if (!Screen.HasStarted)
            {
                try
                {
                    Screen.HasStarted = true;
                    AudioEngine.Track.Play();
                }
                catch { /* ignored */ }
            }

            // v2: Simplified Timing Logic (Hybrid Clock)
            if (AudioEngine.Track.IsPlaying)
            {
                Time = AudioEngine.Time;
            }
            else
            {
                Time += gameTime.ElapsedGameTime.TotalMilliseconds * AudioEngine.Track.Rate;
            }
        }
    }
}
