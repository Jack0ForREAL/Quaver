using System;
using System.IO;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Force.DeepCloner;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Quaver.API.Enums;
using Quaver.API.Helpers;
using Quaver.API.Maps;
using Quaver.API.Maps.Processors.Scoring;
using Quaver.API.Maps.Processors.Scoring.Data;
using Quaver.API.Replays;
using Quaver.Server.Client.Handlers;
using Quaver.Server.Client.Enums;
using Quaver.Server.Client.Objects;
using Quaver.Server.Client.Objects.Multiplayer;
using Quaver.Shared.Audio;
using Quaver.Shared.Config;
using Quaver.Shared.Database.Judgements;
using Quaver.Shared.Database.Maps;
using Quaver.Shared.Database.Scores;
using Quaver.Shared.Discord;
using Quaver.Shared.Graphics.Notifications;
using Quaver.Shared.Graphics.Overlays.Chatting;
using Quaver.Shared.Helpers;
using Quaver.Shared.Modifiers;
using Quaver.Shared.Online;
using Quaver.Shared.Scheduling;
using Quaver.Shared.Screens.Edit;
using Quaver.Shared.Screens.Editor;
using Quaver.Shared.Screens.Editor.Timing;
using Quaver.Shared.Screens.Gameplay.Replays;
using Quaver.Shared.Screens.Gameplay.Rulesets;
using Quaver.Shared.Screens.Gameplay.Rulesets.Input;
using Quaver.Shared.Screens.Gameplay.Rulesets.Keys;
using Quaver.Shared.Screens.Gameplay.Rulesets.Keys.HitObjects;
using Quaver.Shared.Screens.Gameplay.Rulesets.Keys.Playfield;
using Quaver.Shared.Screens.Gameplay.UI.Offset;
using Quaver.Shared.Screens.MultiplayerLobby;
using Quaver.Shared.Screens.Selection;
using Quaver.Shared.Screens.Selection.UI;
using Quaver.Shared.Screens.Tournament;
using Quaver.Shared.Screens.Tournament.Gameplay;
using Quaver.Shared.Skinning;
using Wobble;
using Wobble.Audio.Tracks;
using Wobble.Graphics.Animations;
using Wobble.Graphics.UI.Dialogs;
using Wobble.Input;
using Wobble.Logging;
using Wobble.Platform;
using Wobble.Screens;
using MathHelper = Microsoft.Xna.Framework.MathHelper;
using Microsoft.Xna.Framework.Graphics;
using Quaver.Shared.Audio;

namespace Quaver.Shared.Screens.Gameplay
{
    public class GameplayScreen : QuaverScreen
    {
        public override QuaverScreenType Type { get; } = QuaverScreenType.Gameplay;
        public sealed override ScreenView View { get; protected set; }
        public GameplayAudioTiming Timing { get; }
        public GameplayRuleset Ruleset { get; private set; }
        public Qua Map { get; }
        public Qua OriginalEditorMap { get; }
        public List<Score> LocalScores { get; }
        public string MapHash { get; }
        public Replay LoadedReplay { get; private set; }
        public bool InReplayMode { get; set; }
        public bool IsPlayTesting { get; }
        public bool IsTestPlayingInNewEditor { get; }
        public double PlayTestAudioTime { get; }
        public bool HasStarted { get; set; }
        public bool IsPaused { get; set; }
        private int TimesRequestedToPause { get; set; }
        public double TimePauseKeyHeld { get; private set; }
        public int TimeToHoldPause { get; } = 500;
        private long ResumeTime { get; set; }
        public bool IsResumeInProgress { get; private set; }

        public bool Failed => OnlineManager.CurrentGame == null 
                              && !OnlineManager.IsSpectatingSomeone 
                              && !IsPlayTesting 
                              && (!ModManager.IsActivated(ModIdentifier.NoFail) && Ruleset.ScoreProcessor.Health <= 0 && !ConfigManager.KeepPlayingUponFailing.Value) 
                              && !(this is TournamentGameplayScreen) 
                              || ForceFail 
                              || Ruleset.ScoreProcessor.ForceFail;

        public bool ForceFail { get; set; }
        private bool FailureHandled { get; set; }
        public bool IsPlayComplete => Ruleset.HitObjectManager.IsComplete;
        public bool HasQuit { get; set; }
        public bool FailedDuringGameplay { get; set; }
        public bool IsRestartingPlay { get; private set; }
        private double RestartKeyHoldTime { get; set; }
        public double TimeSincePlayEnded { get; set; }

        public bool EligibleToSkip
        {
            get
            {
                if (Map.HitObjects.Count == 0) 
                    return false;
                
                return Map.HitObjects.First().StartTime - Ruleset.Screen.Timing.Time >= GameplayAudioTiming.StartDelay + 5000;
            }
        }

        public int PauseCount { get; private set; }
        public int LastRecordedCombo { get; private set; }
        public ReplayCapturer ReplayCapturer { get; }
        public long TimePlayed { get; private set; }
        public long TimePlayEnd { get; set; }
        public bool IsCalibratingOffset { get; }
        public Metronome Metronome { get; }
        public int LastJudgementIndexSentToServer { get; private set; } = -1;
        private double TimeSinceLastJudgementsSentToServer { get; set; }
        public bool IsMultiplayerGame { get; }
        public bool IsMultiplayerGameStarted { get; private set; }
        public bool MultiplayerMatchEndedPrematurely { get; set; }
        public bool RequestedToSkipSong { get; private set; }
        private int NextSoundEffectIndex { get; set; }
        private double TimeSinceSpectatorFramesLastSent { get; set; }
        public int LastReplayFrameIndexSentToServer { get; set; } = -1;
        public SpectatorClient SpectatorClient { get; }
        public TournamentPlayerOptions TournamentOptions { get; }
        private bool DontPlayNextComboBreak { get; set; }
        public bool IsSongSelectPreview { get; }
        public bool UseExistingAudioTime { get; }
        private ReplayInputManagerKeys CachedReplayInputManager { get; set; }
        public bool IsSpectatingTournament => this is TournamentGameplayScreen tournamentGameplayScreen && tournamentGameplayScreen.Type == TournamentScreenType.Spectator;
        public bool IsDisposed { get; private set; }

        private const int FAILURE_FADE_TIME = 1700;
        private const int QUIT_FADE_TIME = 400;
        public int FailFadeTime => HasQuit ? QUIT_FADE_TIME : FAILURE_FADE_TIME;

        public GameplayScreen(Qua map, string md5, List<Score> scores, Replay replay = null, bool isPlayTesting = false, double playTestTime = 0,
            bool isCalibratingOffset = false, SpectatorClient spectatorClient = null, TournamentPlayerOptions options = null, bool isSongSelectPreview = false,
            bool isTestPlayingInNewEditor = false, bool useExistingAudioTime = false)
        {
            if (isPlayTesting && !isSongSelectPreview)
            {
                var testingQua = map.DeepClone();
                testingQua.HitObjects.RemoveAll(x => x.StartTime + 2 < playTestTime);
                Qua.RestoreDefaultValues(testingQua);
                Map = testingQua;
                OriginalEditorMap = map;
            }
            else
            {
                Map = map;
            }

            LocalScores = scores;
            MapHash = md5;
            LoadedReplay = replay;
            IsPlayTesting = isPlayTesting;
            IsTestPlayingInNewEditor = isTestPlayingInNewEditor;
            PlayTestAudioTime = playTestTime;
            IsCalibratingOffset = isCalibratingOffset;
            IsMultiplayerGame = OnlineManager.CurrentGame != null;
            SpectatorClient = spectatorClient;
            TournamentOptions = options;
            IsSongSelectPreview = isSongSelectPreview;
            UseExistingAudioTime = useExistingAudioTime;

            if (SpectatorClient != null) 
                LoadedReplay = SpectatorClient.Replay;
            
            if (IsMultiplayerGame)
            {
                OnlineManager.Client.OnUserJoinedGame += OnUserJoinedGame;
                OnlineManager.Client.OnUserLeftGame += OnUserLeftGame;
                OnlineManager.Client.OnAllPlayersLoaded += OnAllPlayersLoaded;
                OnlineManager.Client.OnAllPlayersSkipped += OnAllPlayersSkipped;
            }
            
            Timing = new GameplayAudioTiming(this);
            
            if (!IsCalibratingOffset) 
            {
                try 
                { 
                    CustomAudioSampleCache.LoadSamples(MapManager.Selected.Value, MapHash); 
                } 
                catch (Exception e) 
                { 
                    Logger.Error(e, LogType.Runtime); 
                }
            }

            NextSoundEffectIndex = 0;
            UpdateNextSoundEffectIndex();

            if (ModManager.IsActivated(ModIdentifier.Paused)) 
                ModManager.RemoveMod(ModIdentifier.Paused);
            
            if (ModManager.IsActivated(ModIdentifier.Autoplay)) 
                LoadedReplay = ReplayHelper.GeneratePerfectReplay(map, MapHash);
            
            if (LoadedReplay != null) 
                InReplayMode = true;

            ReplayCapturer = new ReplayCapturer(this);
            UpdateMapInDatabase();
            SetRuleset();
            SetRichPresence();
            AudioTrack.AllowPlayback = true;

            if (IsCalibratingOffset) 
                Metronome = new Metronome(map);
            
            if (InReplayMode && SpectatorClient == null && !IsSongSelectPreview) 
                GameBase.Game.GlobalUserInterface.Cursor.Alpha = 1;

            View = new GameplayScreenView(this);
            
            if (IsSongSelectPreview) 
            { 
                IsMultiplayerGameStarted = true; 
                HasStarted = true; 
            }
        }

        public override void OnFirstUpdate()
        {
            var game = (QuaverGame)GameBase.Game;
            game.InitializeFpsLimiting();
            
            if (IsMultiplayerGame && !IsSongSelectPreview) 
                OnlineManager.Client?.MultiplayerGameScreenLoaded();
            
            if (!InReplayMode) 
                OnlineManager.Client?.SendReplaySpectatorFrames(SpectatorClientStatus.NewSong, AudioEngine.Track.Time, new List<ReplayFrame>());
            
            TimePlayed = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            
            if (ReplayCapturer != null) 
                ReplayCapturer.Replay.TimePlayed = TimePlayed;
            
            if (!InReplayMode && ConfigManager.LockWinkeyDuringGameplay.Value) 
                Utils.NativeUtils.DisableWindowsKey();
            
            if (InReplayMode && !IsPlayTesting && !IsSongSelectPreview && !IsCalibratingOffset) 
            { 
                SkinManager.StartWatching(); 
                ScreenExiting += (_, _) => SkinManager.StopWatching(); 
            }
            
            base.OnFirstUpdate();
        }

        public override void Update(GameTime gameTime)
        {
            TimeSinceLastJudgementsSentToServer += gameTime.ElapsedGameTime.TotalMilliseconds;
            TimeSinceSpectatorFramesLastSent += gameTime.ElapsedGameTime.TotalMilliseconds;
            Timing.Update(gameTime);

            if (InReplayMode && !IsSongSelectPreview && OnlineManager.IsSpectatingSomeone) 
            { 
                HandleSpectatorSkipping(); 
                ((KeysInputManager)Ruleset.InputManager).ReplayInputManager?.HandleSpectating(); 
            }
            if (!Failed && !IsPlayComplete) 
            { 
                HandleResuming(); 
                PlayComboBreakSound(); 
            }

            HandleInput(gameTime);
            HandleSoundEffects();
            HandleFailure();
            ReplayCapturer.Capture(gameTime);
            SendJudgementsToServer();
            SendReplayFramesToServer();
            base.Update(gameTime);
        }

        public override void Exit(Func<QuaverScreen> screen, int delay = 0, QuaverScreenChangeType type = QuaverScreenChangeType.CompleteChange)
        {
            Utils.NativeUtils.EnableWindowsKey();
            base.Exit(screen, delay, type);
        }

        public override void Destroy()
        {
            if (IsCalibratingOffset) 
                AudioEngine.Track?.Dispose();
            
            if (IsMultiplayerGame)
            {
                OnlineManager.Client.OnUserLeftGame -= OnUserLeftGame;
                OnlineManager.Client.OnUserJoinedGame -= OnUserJoinedGame;
                OnlineManager.Client.OnAllPlayersLoaded -= OnAllPlayersLoaded;
                OnlineManager.Client.OnAllPlayersSkipped -= OnAllPlayersSkipped;
            }

            if (ReplayCapturer?.Replay != null && ReplayCapturer.Replay.Frames.Count > 0)
            {
                if (Ruleset?.ScoreProcessor?.Stats != null && Map != null) { ReplayHelper.ExportDetailedAnalysis(Ruleset.ScoreProcessor.Stats, Map, Ruleset.ScoreProcessor); }
            }

            Metronome?.Dispose();
            IsDisposed = true;
            base.Destroy();
        }

        protected virtual void HandleInput(GameTime gameTime)
        {
            if (Exiting) return;
            var dt = gameTime.ElapsedGameTime.TotalMilliseconds;
            
            if (!Failed && !IsPlayComplete && !IsSongSelectPreview) 
                HandlePauseInput(gameTime);
            
            if (!IsSongSelectPreview && KeyboardManager.IsUniqueKeyPress(ConfigManager.KeyScoreboardVisible.Value)) 
                ConfigManager.ScoreboardVisible.Value = !ConfigManager.ScoreboardVisible.Value;
            
            if (!IsSongSelectPreview && IsPlayTesting && KeyboardManager.IsCtrlDown()) 
            { 
                if (KeyboardManager.IsUniqueKeyPress(Keys.P)) 
                { 
                    if (!AudioEngine.Track.IsDisposed) 
                    { 
                        if (AudioEngine.Track.IsPlaying) { AudioEngine.Track.Pause(); IsPaused = true; } 
                        else { AudioEngine.Track.Play(); IsPaused = false; } 
                    } 
                } 
            }
            
            HandlePlayRestart(dt);
            
            if ((IsPaused && !InReplayMode) || Failed) 
                return;
            
            if (!IsPlayComplete && !IsCalibratingOffset || IsMultiplayerGame || IsSongSelectPreview) 
            { 
                if (KeyboardManager.IsUniqueKeyPress(ConfigManager.KeyQuickExit.Value)) 
                    HandleQuickExit(); 
            }
            
            if (!IsPlayComplete && !IsCalibratingOffset)
            {
                if (GenericKeyManager.IsUniquePress(ConfigManager.KeySkipIntro.Value)) 
                    SkipToNextObject();
                
                if (!IsSongSelectPreview && IsPlayTesting && KeyboardManager.IsUniqueKeyPress(Keys.F2)) 
                { 
                    if (AudioEngine.Track.IsPlaying) 
                        AudioEngine.Track.Pause(); 
                    
                    if (IsTestPlayingInNewEditor) 
                        ExitToNewEditor(true); 
                    else 
                        Exit(() => new EditorScreen(OriginalEditorMap)); 
                }
                
                if (!IsSongSelectPreview) 
                { 
                    HandleAutoplayTabInput(gameTime); 
                    HandleOverlayToggleInput(gameTime); 
                }
            }
            Ruleset.HandleInput(gameTime);
        }

        private void SetRuleset() 
        { 
            if (ModeHelper.IsKeyMode(Map.Mode)) 
                Ruleset = new GameplayRulesetKeys(this, Map); 
            else 
                throw new InvalidEnumArgumentException(); 
        }

        private void HandlePauseInput(GameTime gameTime)
        {
            if (OnlineManager.CurrentGame != null) return;
            
            if (GenericKeyManager.IsUp(ConfigManager.KeyPause.Value)) 
            { 
                if (Failed || IsPlayComplete || IsPaused) return; 
                
                var screenView = (GameplayScreenView)View; 
                if (!screenView.FadingOnRestartKeyPress) 
                    screenView.Transitioner.Alpha = MathHelper.Lerp(screenView.Transitioner.Alpha, 0, (float)Math.Min(gameTime.ElapsedGameTime.TotalMilliseconds / 120, 1)); 
                
                return; 
            }
            
            if (GenericKeyManager.IsUniquePress(ConfigManager.KeyPause.Value)) 
            { 
                if (IsPlayTesting) 
                { 
                    if (AudioEngine.Track.IsPlaying) 
                    {
                        AudioEngine.Track.Pause(); 
                        AudioEngine.Track.Seek(PlayTestAudioTime); 
                    }
                    
                    CustomAudioSampleCache.StopAll(); 
                    
                    if (IsTestPlayingInNewEditor) 
                        ExitToNewEditor(); 
                    else 
                        Exit(() => new EditorScreen(OriginalEditorMap)); 
                } 
                else if (IsCalibratingOffset) 
                { 
                    OffsetConfirmDialog.Exit(this); 
                } 
                else 
                { 
                    TimePauseKeyHeld = 0; 
                    Pause(gameTime); 
                } 
                return; 
            }
            
            if (!IsPaused || SpectatorClient != null) 
                Pause(gameTime);
        }

        public void Pause(GameTime gameTime = null, bool removeMods = true)
        {
            if (IsPlayComplete) return;
            var screenView = (GameplayScreenView)View;
            
            if (!IsPaused || SpectatorClient != null)
            {
                if (gameTime == null) 
                    throw new InvalidOperationException("Cannot pause if GameTime is null");
                
                TimePauseKeyHeld += gameTime.ElapsedGameTime.TotalMilliseconds;
                screenView.Transitioner.Alpha = MathHelper.Lerp(screenView.Transitioner.Alpha, 1, (float)Math.Min(gameTime.ElapsedGameTime.TotalMilliseconds / TimeToHoldPause, 1));
                
                if (!ConfigManager.TapToPause.Value && TimePauseKeyHeld < TimeToHoldPause) 
                    return;
                
                IsPaused = true; 
                IsResumeInProgress = false;
                
                if (Ruleset.ScoreProcessor.TotalJudgementCount > 0) 
                    PauseCount++;
                
                GameBase.Game.GlobalUserInterface.Cursor.Alpha = 1;
                
                if (InReplayMode || this is TournamentGameplayScreen tournScreen && tournScreen.Type != TournamentScreenType.Spectator) 
                { 
                    CustomAudioSampleCache.StopAll(); 
                    if (removeMods) ModManager.RemoveAllMods(); 
                    if (SpectatorClient != null) OnlineManager.Client?.StopSpectating(); 
                    Exit(() => new SelectionScreen()); 
                    return; 
                }
                
                if (!ModManager.IsActivated(ModIdentifier.Paused) && Ruleset.ScoreProcessor.TotalJudgementCount > 0) 
                { 
                    if (ConfigManager.DisplayPauseWarning.Value) 
                        NotificationManager.Show(NotificationLevel.Warning, "WARNING! Your score will not be submitted due to pausing during gameplay!", null, true); 
                    
                    ModManager.AddMod(ModIdentifier.Paused); 
                    ReplayCapturer.Replay.Mods |= ModIdentifier.Paused; 
                    Ruleset.ScoreProcessor.Mods |= ModIdentifier.Paused; 
                }
                
                try { AudioEngine.Track.Pause(); } catch (Exception) { }
                CustomAudioSampleCache.PauseAll();
                
                DiscordHelper.Presence.State = $"Paused for the {StringHelper.AddOrdinal(PauseCount)} time"; 
                DiscordHelper.Presence.EndTimestamp = 0; 
                DiscordHelper.UpdatePresence();
                OnlineManager.Client?.UpdateClientStatus(GetClientStatus());
                
                screenView.Transitioner.Animations.Clear(); 
                screenView.Transitioner.Animations.Add(new Animation(AnimationProperty.Alpha, Easing.Linear, screenView.Transitioner.Alpha, 0.75f, 400));
                screenView.PauseScreen?.Activate(); 
                Utils.NativeUtils.EnableWindowsKey(); 
                return;
            }
            
            if (IsResumeInProgress) return;
            
            IsResumeInProgress = true; 
            ResumeTime = GameBase.Game.TimeRunning;
            screenView.Transitioner.Animations.Clear(); 
            var alphaTransformation = new Animation(AnimationProperty.Alpha, Easing.Linear, 0.75f, 0, 400); 
            screenView.Transitioner.Animations.Add(alphaTransformation);
            
            screenView.PauseScreen?.Deactivate(); 
            SetRichPresence(); 
            OnlineManager.Client?.UpdateClientStatus(GetClientStatus()); 
            GameBase.Game.GlobalUserInterface.Cursor.Alpha = 0;
            
            if (!InReplayMode) 
                Utils.NativeUtils.DisableWindowsKey();
        }

        private void HandleQuickExit()
        {
            if (IsSongSelectPreview || InReplayMode && !Failed && !IsPlayComplete || Exiting) 
                return;
            
            if (IsPlayTesting) 
            { 
                if (AudioEngine.Track.IsPlaying) 
                { 
                    AudioEngine.Track.Pause(); 
                    AudioEngine.Track.Seek(PlayTestAudioTime); 
                } 
                CustomAudioSampleCache.StopAll(); 
                if (IsTestPlayingInNewEditor) ExitToNewEditor(); 
                else Exit(() => new EditorScreen(OriginalEditorMap)); 
            }
            
            TimesRequestedToPause++;
            switch (TimesRequestedToPause)
            {
                case 1: 
                    NotificationManager.Show(NotificationLevel.Warning, "Press the exit button once more to quit.", null, true); 
                    break;
                default: 
                    var game = GameBase.Game as QuaverGame; 
                    var cursor = game?.GlobalUserInterface.Cursor; 
                    cursor.Alpha = 1; 
                    
                    if (IsMultiplayerGame) 
                    { 
                        Exit(() => { OnlineManager.LeaveGame(); return new MultiplayerLobbyScreen(); }); 
                        return; 
                    } 
                    
                    ForceFail = true; 
                    HasQuit = true; 
                    var view = (GameplayScreenView)View; 
                    view.Transitioner.Animations.Clear(); 
                    break;
            }
        }

        private void PlayComboBreakSound()
        {
            if (IsSongSelectPreview) return;
            if (DontPlayNextComboBreak) { DontPlayNextComboBreak = false; return; }
            if (LastRecordedCombo >= 20 && Ruleset.ScoreProcessor.Combo == 0) SkinManager.Skin.SoundComboBreak.CreateChannel().Play();
            LastRecordedCombo = Ruleset.ScoreProcessor.Combo;
        }

        private void HandleResuming()
        {
            if (!IsPaused || !IsResumeInProgress) return;
            if (GameBase.Game.TimeRunning - ResumeTime > 800)
            {
                IsPaused = false; IsResumeInProgress = false;
                try { if (HasStarted) AudioEngine.Track.Play(); } catch (Exception) { }
                CustomAudioSampleCache.ResumeAll();
            }
        }

        private void HandleFailure()
        {
            if (Ruleset.ScoreProcessor.Mods.HasFlag(ModIdentifier.NoMiss)) return;
            
            if (!FailedDuringGameplay && OnlineManager.CurrentGame == null && !OnlineManager.IsSpectatingSomeone 
                && !IsPlayTesting && !IsCalibratingOffset 
                && (!ModManager.IsActivated(ModIdentifier.NoFail) && Ruleset.ScoreProcessor.Health <= 0 && ConfigManager.KeepPlayingUponFailing.Value) 
                && !(this is TournamentGameplayScreen) && !ForceFail && !Ruleset.ScoreProcessor.ForceFail)
            {
                if (ConfigManager.DisplayFailWarning.Value) 
                    NotificationManager.Show(NotificationLevel.Warning, "WARNING! Your score will not be submitted due to failing during gameplay!", null, true);
                FailedDuringGameplay = true;
            }
            
            if (!Failed || FailureHandled) return;
            
            try 
            { 
                if (!IsPaused && HasStarted) 
                { 
                    if (HasQuit && AudioEngine.Track.IsPlaying) 
                        AudioEngine.Track.Fade(0, FailFadeTime); 
                    else 
                    { 
                        AudioEngine.Track.ApplyRate(true); 
                        AudioEngine.Track.FadeSpeed(0f, FailFadeTime); 
                        AudioEngine.Track.Fade(0, FailFadeTime); 
                    } 
                } 
            } catch (Exception) { }
            
            CustomAudioSampleCache.StopAll(); 
            FailureHandled = true;
        }

        private void HandlePlayRestart(double dt)
        {
            if (!IsPlayTesting && (IsPaused || Failed) || IsCalibratingOffset || SpectatorClient != null || IsSongSelectPreview) 
                return;
            
            if (OnlineManager.CurrentGame != null) return;
            
            if (KeyboardManager.IsUniqueKeyPress(ConfigManager.KeyRestartMap.Value)) 
                IsRestartingPlay = true;
            
            var screenView = (GameplayScreenView)View;
            if (KeyboardManager.CurrentState.IsKeyDown(ConfigManager.KeyRestartMap.Value) && IsRestartingPlay)
            {
                RestartKeyHoldTime += dt;
                if (!screenView.FadingOnRestartKeyPress) 
                { 
                    screenView.FadingOnRestartKeyPress = true; 
                    screenView.FadingOnRestartKeyRelease = false; 
                    screenView.Transitioner.Animations.Clear(); 
                    screenView.Transitioner.Animations.Add(new Animation(AnimationProperty.Alpha, Easing.Linear, screenView.Transitioner.Alpha, 1, 100)); 
                }
                
                if (RestartKeyHoldTime >= 200 || ConfigManager.TapToRestart.Value) 
                { 
                    SkinManager.Skin.SoundRetry.CreateChannel().Play(); 
                    Retry(); 
                }
                return;
            }
            
            RestartKeyHoldTime = 0; 
            IsRestartingPlay = false;
            
            if (!screenView.FadingOnRestartKeyRelease && screenView.FadingOnRestartKeyPress) 
            { 
                screenView.FadingOnRestartKeyPress = false; 
                screenView.FadingOnRestartKeyRelease = true; 
                screenView.Transitioner.Animations.Clear(); 
                screenView.Transitioner.Animations.Add(new Animation(AnimationProperty.Alpha, Easing.Linear, 1, 0, 200)); 
            }
        }

        public void Retry()
        {
            GameBase.Game.GlobalUserInterface.Cursor.Alpha = 0; 
            SkinManager.Skin.SoundRetry.CreateChannel().Play(); 
            CustomAudioSampleCache.StopAll();
            
            if (IsPlayTesting) 
                QuaverScreenManager.ScheduleScreenChange(() => new GameplayScreen(OriginalEditorMap, MapHash, LocalScores, null, true, PlayTestAudioTime, false, null, null, false, IsTestPlayingInNewEditor), true);
            else if (InReplayMode) 
                QuaverScreenManager.ScheduleScreenChange(() => new GameplayScreen(Map, MapHash, LocalScores, LoadedReplay), true);
            else 
                QuaverScreenManager.ScheduleScreenChange(() => new GameplayScreen(Map, MapHash, LocalScores), true);
        }

        public void SkipToNextObject(bool force = false)
        {
            if (!EligibleToSkip || IsPaused || IsResumeInProgress || IsSongSelectPreview) return;
            
            if (IsMultiplayerGame && !force && !OnlineManager.IsSpectatingSomeone) 
            { 
                if (RequestedToSkipSong) return; 
                OnlineManager.Client?.RequestToSkipSong(); 
                RequestedToSkipSong = true; 
                NotificationManager.Show(NotificationLevel.Info, "Requested to skip song. Waiting for all other players to skip!", null, true); 
                return; 
            }
            
            var nextObject = Ruleset.HitObjectManager.NextHitObject.StartTime;
            var skipTime = nextObject - GameplayAudioTiming.StartDelay * ModHelper.GetRateFromMods(ModManager.Mods);
            
            try 
            { 
                AudioEngine.Track?.Seek(skipTime); 
                Timing.Time = AudioEngine.Track.Time; 
            } 
            catch (Exception e) 
            { 
                Logger.Error(e, LogType.Runtime); 
                Logger.Warning("Trying to skip with no audio file loaded. Still continuing..", LogType.Runtime); 
                Timing.Time = skipTime; 
            }
            finally 
            { 
                if (InReplayMode) ((KeysInputManager)Ruleset.InputManager).ReplayInputManager.HandleSkip(); 
                CustomAudioSampleCache.StopAll(); 
                UpdateNextSoundEffectIndex(); 
            }
        }

        private void UpdateNextSoundEffectIndex() 
        { 
            while (NextSoundEffectIndex < Map.SoundEffects.Count && Map.SoundEffects[NextSoundEffectIndex].StartTime <= Timing.Time) 
                NextSoundEffectIndex++; 
        }

        public void SetRichPresence()
        {
            if (IsSongSelectPreview || (this is TournamentGameplayScreen && InReplayMode)) return;
            
            DiscordHelper.Presence.Details = Map.ToString();
            
            if (OnlineManager.CurrentGame != null) 
            { 
                if (OnlineManager.CurrentGame.Ruleset == MultiplayerGameRuleset.Battle_Royale) 
                { 
                    var view = View as GameplayScreenView; 
                    var alivePlayers = OnlineManager.CurrentGame.Players.Count; 
                    
                    if (view?.ScoreboardLeft != null) 
                        alivePlayers = view.ScoreboardLeft.Users.FindAll(x => !x.Processor.MultiplayerProcessor.IsBattleRoyaleEliminated).Count; 
                    
                    DiscordHelper.Presence.State = $"Battle Royale - {alivePlayers} Left"; 
                } 
                else 
                { 
                    DiscordHelper.Presence.State = $"{OnlineManager.CurrentGame.Name} ({OnlineManager.CurrentGame.PlayerIds.Count} of {OnlineManager.CurrentGame.MaxPlayers})"; 
                } 
            }
            else if (IsPlayTesting) 
                DiscordHelper.Presence.State = "Play Testing";
            else if (InReplayMode) 
                DiscordHelper.Presence.State = OnlineManager.IsSpectatingSomeone ? $"Spectating {LoadedReplay.PlayerName}" : $"Watching {LoadedReplay.PlayerName}";
            else 
                DiscordHelper.Presence.State = $"Playing {(ModManager.Mods > 0 ? "+ " + ModHelper.GetModsString(ModManager.Mods) : "")}";
            
            if (!OnlineManager.IsSpectatingSomeone) 
            { 
                var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc); 
                var time = Convert.ToInt64((DateTime.UtcNow.AddMilliseconds((Map.Length - Timing.Time) / AudioEngine.Track.Rate) - epoch).TotalSeconds); 
                DiscordHelper.Presence.EndTimestamp = time; 
            }
            
            DiscordHelper.Presence.LargeImageText = OnlineManager.GetRichPresenceLargeKeyText(Ruleset.Mode); 
            DiscordHelper.Presence.SmallImageKey = ModeHelper.ToShortHand(Ruleset.Mode).ToLower(); 
            DiscordHelper.Presence.SmallImageText = ModeHelper.ToLongHand(Ruleset.Mode); 
            DiscordHelper.UpdatePresence(); 
            
            SteamManager.SetRichPresence("State", DiscordHelper.Presence.State); 
            SteamManager.SetRichPresence("Details", Map.ToString());
        }

        public override UserClientStatus GetClientStatus()
        {
            ClientStatus status; 
            string content;
            
            if (IsPlayTesting) { status = ClientStatus.Playing; content = Map.ToString(); }
            else if (InReplayMode) { status = ClientStatus.Watching; content = LoadedReplay.PlayerName; }
            else if (IsResumeInProgress) { status = ClientStatus.Playing; content = Map.ToString(); }
            else if (IsPaused) { status = ClientStatus.Paused; content = ""; }
            else { status = ClientStatus.Playing; content = Map.ToString(); }
            
            return new UserClientStatus(status, Map.MapId, MapHash, (byte)Ruleset.Mode, content, (long)ModManager.Mods);
        }

        public void HandleSuggestedOffsetCalculations()
        {
            var stats = Ruleset.ScoreProcessor.Stats.FindAll(x => x.KeyPressType == KeyPressType.Press);
            if (stats.Count == 0) { ExitOffsetCalibrationOnFailure(); return; }
            
            var samples = new List<double>(stats.Select(x => (double)x.HitDifference).ToList()); 
            var stdDev = samples.StandardDeviation();
            
            if (stdDev < 25 && !Exiting) 
                DialogManager.Show(new OffsetConfirmDialog(this, (int)samples.Average() + ConfigManager.GlobalAudioOffset.Value)); 
            else 
                ExitOffsetCalibrationOnFailure();
        }

        private void ExitOffsetCalibrationOnFailure() 
        { 
            if (Exiting) return; 
            NotificationManager.Show(NotificationLevel.Error, "A global audio offset could not be suggested. Please try again!", null, true); 
            OffsetConfirmDialog.Exit(this); 
        }

        public void SendJudgementsToServer(bool force = false)
        {
            if ((TimeSinceLastJudgementsSentToServer < 400 && !force) || OnlineManager.CurrentGame == null) return;
            if (OnlineManager.IsSpectatingSomeone) return; 
            if (IsSongSelectPreview) return;
            
            TimeSinceLastJudgementsSentToServer = 0; 
            if (Ruleset.StandardizedReplayPlayer.ScoreProcessor.Stats.Count == 0) return;
            if (Ruleset.StandardizedReplayPlayer.ScoreProcessor.Stats.Count == LastJudgementIndexSentToServer + 1) return;
            
            var judgementsToGive = new List<Judgement>();
            for (var i = LastJudgementIndexSentToServer + 1; i < Ruleset.StandardizedReplayPlayer.ScoreProcessor.Stats.Count; i++) 
                judgementsToGive.Add(Ruleset.StandardizedReplayPlayer.ScoreProcessor.Stats[i].Judgement);
            
            LastJudgementIndexSentToServer = Ruleset.StandardizedReplayPlayer.ScoreProcessor.Stats.Count - 1;
            
            if (OnlineManager.CurrentGame.InProgress) 
                OnlineManager.Client.SendGameJudgements(judgementsToGive);
        }

        public float SpectatorTargetSyncTime => (this is TournamentGameplayScreen && ((QuaverGame)GameBase.Game).CurrentScreen is TournamentScreen tournamentScreen) 
            ? tournamentScreen.GameplayScreens.Min(s => { var replayFrames = s.SpectatorClient.Replay.Frames; return (replayFrames?.Count ?? 0) == 0 ? int.MaxValue : replayFrames.Last()?.Time ?? int.MaxValue; }) 
            : SpectatorClient.Replay.Frames.Last().Time;

        private void HandleSpectatorSkipping()
        {
            if (SpectatorClient.Replay.Frames.Count == 0 || this is TournamentGameplayScreen) return;
            var targetSyncTime = SpectatorClient.Replay.Frames.Last().Time;
            if (Math.Abs(AudioEngine.Track.Time - targetSyncTime) < 3000) return;
            SkipTo(targetSyncTime);
        }

        public void SkipTo(float targetSyncTime)
        {
            try 
            { 
                AudioTrack.AllowPlayback = true; 
                AudioEngine.Track?.Seek(targetSyncTime); 
                Timing.Time = AudioEngine.Track.Time; 
            } 
            catch (Exception e) 
            { 
                Timing.Time = targetSyncTime; 
            }
            finally 
            { 
                ((KeysInputManager)Ruleset.InputManager).ReplayInputManager.HandleSkip(); 
                ((HitObjectManagerKeys)Ruleset.HitObjectManager).HandleSkip(); 
                CustomAudioSampleCache.StopAll(); 
                UpdateNextSoundEffectIndex(); 
            }
        }

        public void SendReplayFramesToServer(bool force = false, bool appendFinishSong = false)
        {
            if (!OnlineManager.IsBeingSpectated || InReplayMode || IsSongSelectPreview) return;
            if (TimeSinceSpectatorFramesLastSent < 750 && !force) return;
            TimeSinceSpectatorFramesLastSent = 0; 
            if (ReplayCapturer.Replay.Frames.Count == 0 && !force) return;
            if (ReplayCapturer.Replay.Frames.Count == LastReplayFrameIndexSentToServer + 1 && !force) return;
            
            OnlineManager.Client?.PerformActionOnThread(() => 
            { 
                var frames = new List<ReplayFrame>(); 
                for (var i = LastReplayFrameIndexSentToServer + 1; i < ReplayCapturer.Replay.Frames.Count; i++) 
                    frames.Add(ReplayCapturer.Replay.Frames[i]); 
                
                LastReplayFrameIndexSentToServer = ReplayCapturer.Replay.Frames.Count - 1; 
                SpectatorClientStatus status; 
                
                if (LastReplayFrameIndexSentToServer == -1) status = SpectatorClientStatus.NewSong; 
                else if (IsPaused) status = SpectatorClientStatus.Paused; 
                else status = SpectatorClientStatus.Playing; 
                
                if (status == SpectatorClientStatus.Playing && frames.Count == 0) 
                { 
                    if (appendFinishSong) OnlineManager.Client?.SendReplaySpectatorFrames(SpectatorClientStatus.FinishedSong, int.MaxValue, new List<ReplayFrame>()); 
                    return; 
                } 
                
                OnlineManager.Client?.SendReplaySpectatorFrames(status, AudioEngine.Track.Time, frames); 
                if (appendFinishSong) OnlineManager.Client?.SendReplaySpectatorFrames(SpectatorClientStatus.FinishedSong, int.MaxValue, new List<ReplayFrame>()); 
            });
        }

        private void OnUserLeftGame(object sender, UserLeftGameEventArgs e) 
        { 
            var view = (GameplayScreenView)View; 
            view.ScoreboardLeft?.Users.Find(x => x.LocalScore?.PlayerId == e.UserId)?.QuitGame(); 
            SetRichPresence(); 
        }
        
        private void OnUserJoinedGame(object sender, UserJoinedGameEventArgs e) => SetRichPresence();
        private void OnAllPlayersLoaded(object sender, AllPlayersLoadedEventArgs e) => IsMultiplayerGameStarted = true;
        private void OnAllPlayersSkipped(object sender, AllPlayersSkippedEventArgs e) => SkipToNextObject(true);

        private void HandleSoundEffects()
        {
            var game = GameBase.Game as QuaverGame;
            if (game?.CurrentScreen is IHasLeftPanel screen && IsSongSelectPreview) 
            { 
                if (screen.ActiveLeftPanel.Value != SelectContainerPanel.MapPreview) return; 
            }
            
            if (NextSoundEffectIndex == Map.SoundEffects.Count) return;
            var info = Map.SoundEffects[NextSoundEffectIndex];
            
            while (info.StartTime <= Timing.Time) 
            { 
                CustomAudioSampleCache.Play(info.Sample - 1, info.Volume); 
                if (++NextSoundEffectIndex == Map.SoundEffects.Count) break; 
                info = Map.SoundEffects[NextSoundEffectIndex]; 
            }
        }

        private void UpdateMapInDatabase()
        {
            if (IsSongSelectPreview) return;
            var map = MapManager.Selected.Value; 
            map.TimesPlayed++; 
            map.LastTimePlayed = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            MapDatabaseCache.UpdateMap(map);
        }

        public void HandleReplaySeeking(double time = -1)
        {
            if (!InReplayMode) return;
            var hitobjectManager = (HitObjectManagerKeys)Ruleset.HitObjectManager;
            
            if (time != -1) 
            { 
                time = MathHelper.Clamp((float)time, 0, (float)AudioEngine.Track.Length); 
                AudioEngine.Track.Seek(time); 
            }
            
            HasStarted = true; 
            hitobjectManager.HandleSkip(); 
            var inputManager = (KeysInputManager)Ruleset.InputManager; 
            inputManager.ReplayInputManager.HandleSkip(); 
            CustomAudioSampleCache.StopAll(); 
            UpdateNextSoundEffectIndex(); 
            DontPlayNextComboBreak = true;
        }

        public void ExitToNewEditor(bool seekToTime = false)
        {
            IAudioTrack track; 
            try 
            { 
                track = new AudioTrack(MapManager.GetAudioPath(MapManager.Selected.Value), false, false); 
            } 
            catch (Exception) 
            { 
                track = new AudioTrackVirtual(MapManager.Selected.Value.SongLength + 5000); 
            }
            
            try 
            { 
                track.Seek(seekToTime ? AudioEngine.Track.Time : PlayTestAudioTime); 
            } 
            catch (Exception e) 
            { 
                Logger.Error(e, LogType.Runtime); 
            }
            
            Exit(() => new EditScreen(MapManager.Selected.Value, track));
        }

        public void HandleAutoplayTabInput(GameTime gameTime)
        {
            if (IsPlayTesting && KeyboardManager.IsUniqueKeyPress(ConfigManager.KeyTogglePlaytestAutoplay.Value) && 
                !KeyboardManager.IsShiftDown() && !OnlineChat.Instance.IsOpen && DialogManager.Dialogs.Count == 0)
            {
                var inputManager = (KeysInputManager)Ruleset.InputManager;
                
                if (inputManager.ReplayInputManager != null) 
                    CachedReplayInputManager = inputManager.ReplayInputManager;
                
                if (LoadedReplay == null || inputManager.ReplayInputManager == null)
                {
                    if (LoadedReplay == null) 
                    { 
                        LoadedReplay = ReplayHelper.GeneratePerfectReplay(Map, MapHash); 
                        CachedReplayInputManager = new ReplayInputManagerKeys(this); 
                        inputManager.ReplayInputManager = CachedReplayInputManager; 
                    }
                    if (inputManager.ReplayInputManager == null) 
                        inputManager.ReplayInputManager = CachedReplayInputManager;
                    
                    inputManager.ReplayInputManager.HandleSkip(); 
                    inputManager.ReplayInputManager.CurrentFrame++;
                }
                
                InReplayMode = !InReplayMode;
                
                if (inputManager.ReplayInputManager != null) 
                { 
                    inputManager.ReplayInputManager.HandleSkip(); 
                    inputManager.ReplayInputManager.CurrentFrame++; 
                }
                
                if (!InReplayMode) 
                { 
                    inputManager.ReplayInputManager = null; 
                    Ruleset.ScoreProcessor = new ScoreProcessorKeys(Map, ModManager.Mods, JudgementWindowsDatabaseCache.Selected.Value); 
                    for (var i = 0; i < Map.GetKeyCount(); i++) 
                    { 
                        inputManager.BindingStore[i].Pressed = false; 
                        inputManager.HandleInput(0); 
                        var playfield = (GameplayPlayfieldKeys)Ruleset.Playfield; 
                        playfield.Stage.HitLightingObjects[i].StopHolding(); 
                        playfield.Stage.SetReceptorAndLightingActivity(i, inputManager.BindingStore[i].Pressed); 
                    } 
                    inputManager.HandleInput(gameTime.ElapsedGameTime.TotalMilliseconds); 
                }
                
                NotificationManager.Show(NotificationLevel.Info, $"Autoplay has been turned {(InReplayMode ? "on" : "off")}.", null, true);
            }
            
            if (!IsSongSelectPreview && Ruleset.Screen.Timing.Time <= 5000 || Ruleset.Screen.EligibleToSkip)
            {
                var change = 5; 
                if (KeyboardManager.IsCtrlDown()) change = 1;
                
                if (KeyboardManager.IsUniqueKeyPress(ConfigManager.KeyIncreaseMapOffset.Value)) 
                { 
                    if (KeyboardManager.IsAltDown()) 
                    { 
                        ConfigManager.VisualOffset.Value += change; 
                        NotificationManager.Show(NotificationLevel.Success, $"Visual offset has been changed to: {ConfigManager.VisualOffset.Value} ms", null, true); 
                    } 
                    else 
                    { 
                        MapManager.Selected.Value.LocalOffset += change; 
                        NotificationManager.Show(NotificationLevel.Success, $"Local map audio offset is now: {MapManager.Selected.Value.LocalOffset} ms", null, true); 
                        ThreadScheduler.Run(() => MapDatabaseCache.UpdateMap(MapManager.Selected.Value)); 
                    } 
                }
                
                if (KeyboardManager.IsUniqueKeyPress(ConfigManager.KeyDecreaseMapOffset.Value)) 
                { 
                    if (KeyboardManager.IsAltDown()) 
                    { 
                        ConfigManager.VisualOffset.Value -= change; 
                        NotificationManager.Show(NotificationLevel.Success, $"Visual offset has been changed to: {ConfigManager.VisualOffset.Value} ms", null, true); 
                    } 
                    else 
                    { 
                        MapManager.Selected.Value.LocalOffset -= change; 
                        NotificationManager.Show(NotificationLevel.Success, $"Local map audio offset is now: {MapManager.Selected.Value.LocalOffset} ms", null, true); 
                        ThreadScheduler.Run(() => MapDatabaseCache.UpdateMap(MapManager.Selected.Value)); 
                    } 
                }
                
                if (KeyboardManager.IsUniqueKeyPress(ConfigManager.KeyResetMapOffset.Value)) 
                { 
                    if (KeyboardManager.IsAltDown()) 
                    { 
                        ConfigManager.VisualOffset.Value = 0; 
                        NotificationManager.Show(NotificationLevel.Success, $"Visual offset has been reset to: {ConfigManager.VisualOffset.Value} ms", null, true); 
                    } 
                    else 
                    { 
                        MapManager.Selected.Value.LocalOffset = 0; 
                        NotificationManager.Show(NotificationLevel.Success, $"Local map audio offset has been reset to: {MapManager.Selected.Value.LocalOffset} ms", null, true); 
                        ThreadScheduler.Run(() => MapDatabaseCache.UpdateMap(MapManager.Selected.Value)); 
                    } 
                }
            }
        }

        private void HandleOverlayToggleInput(GameTime gameTime)
        {
            if (!KeyboardManager.IsShiftDown()) return;
            if (!KeyboardManager.IsUniqueKeyPress(Keys.F6)) return;
            
            ConfigManager.DisplayGameplayOverlay.Value = !ConfigManager.DisplayGameplayOverlay.Value; 
            var on = ConfigManager.DisplayGameplayOverlay.Value ? "on" : "off";
            
            NotificationManager.Show(NotificationLevel.Info, $"Gameplay overlay is now {on}. Press Shift+F6 to toggle the display.", null, true);
        }
    }
}
