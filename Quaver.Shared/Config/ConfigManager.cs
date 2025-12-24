/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 * Copyright (c) Swan & The Quaver Team <support@quavergame.com>.
*/

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using IniFileParser;
using IniFileParser.Exceptions;
using IniFileParser.Model;
using Microsoft.Xna.Framework.Input;
using Quaver.API.Enums;
using Quaver.API.Helpers;
using Quaver.Server.Client.Helpers;
using Quaver.Shared.Database.Maps;
using Quaver.Shared.Graphics.Overlays.Hub.OnlineUsers;
using Quaver.Shared.Online;
using Quaver.Shared.Scheduling;
using Quaver.Shared.Screens.Edit.UI.Playfield;
using Quaver.Shared.Screens.Edit.UI.Playfield.Spectrogram;
using Quaver.Shared.Screens.Edit.UI.Playfield.Waveform;
using Quaver.Shared.Screens.MultiplayerLobby.UI.Filter;
using Quaver.Shared.Screens.Results.UI.Tabs.Overview.Graphs;
using Quaver.Shared.Screens.Selection.UI.Leaderboard;
using Quaver.Shared.Helpers;
using Wobble;
using Wobble.Bindables;
using Wobble.Graphics.Sprites;
using Wobble.Input;
using Wobble.Logging;

namespace Quaver.Shared.Config
{
    public static class ConfigManager
    {
        private static string _gameDirectory;
        internal static Bindable<string> GameDirectory { get; private set; }
        private static string _skinDirectory;
        internal static Bindable<string> SkinDirectory { get; private set; }
        private static string _screenshotDirectory;
        internal static Bindable<string> ScreenshotDirectory { get; private set; }
        private static string _replayDirectory;
        internal static Bindable<string> ReplayDirectory { get; private set; }
        private static string _logsDirectory;
        internal static Bindable<string> LogsDirectory { get; private set; }
        private static string _dataDirectory;
        internal static Bindable<string> DataDirectory { get; private set; }
        internal static string BackupDirectory => Path.Join(DataDirectory.Value, "Backups");
        internal static string MapBackupDirectory => Path.Join(BackupDirectory, "Maps");
        internal static string TempDirectory => Path.Join(DataDirectory.Value, "Temp");
        private static string _songDirectory;
        internal static Bindable<string> SongDirectory { get; private set; }
        private static string _steamWorkshopDirectory;
        internal static Bindable<string> SteamWorkshopDirectory { get; private set; }
        internal static Bindable<string> Username { get; private set; }
        internal static Bindable<string> Skin { get; private set; }
        internal static Bindable<DefaultSkins> DefaultSkin { get; private set; }
        internal static Bindable<DefaultSkins?> DefaultEditorSkin { get; private set; }
        internal static BindableInt VolumeGlobal { get; private set; }
        internal static BindableInt VolumeEffect { get; private set; }
        internal static BindableInt VolumeMusic { get; private set; }
        internal static BindableInt DevicePeriod { get; private set; }
        internal static BindableInt DeviceBufferLengthMultiplier { get; private set; }
        internal static BindableInt BackgroundBrightness { get; private set; }
        internal static BindableInt WindowHeight { get; private set; }
        internal static BindableInt WindowWidth { get; private set; }
        internal static Bindable<bool> WindowFullScreen { get; private set; }
        internal static Bindable<bool> WindowBorderless { get; private set; }
        internal static BindableInt PlayfieldScale { get; private set; }
        internal static Bindable<bool> PreferWayland { get; private set; }
        internal static Bindable<bool> FpsCounter { get; private set; }
        internal static Bindable<FpsLimitType> FpsLimiterType { get; private set; }
        internal static BindableInt CustomFpsLimit { get; private set; }
        internal static Bindable<bool> SmoothAudioTimingGameplay { get; private set; }
        internal static Bindable<bool> SmoothAudioStart { get; private set; }
        internal static Bindable<bool> DisplaySongTimeProgress { get; private set; }
        [IgnoreWrite] internal static Dictionary<GameMode, BindableInt> ScrollSpeeds { get; private set; }
        [IgnoreWrite] internal static Dictionary<GameMode, Bindable<ScrollDirection>> ScrollDirections { get; private set; }
        internal static BindableInt NormaliseScrollVelocityByRatePercentage { get; private set; }
        internal static BindableInt GlobalAudioOffset { get; private set; }
        internal static Bindable<bool> Pitched { get; private set; }
        internal static Bindable<string> OsuDbPath { get; private set; }
        internal static Bindable<string> EtternaDbPath { get; private set; }
        internal static Bindable<bool> AutoLoadOsuBeatmaps { get; private set; }
        internal static Bindable<bool> DeleteOriginalFileAfterImport { get; private set; }
        internal static Bindable<bool> DiscordRichPresence { get; private set; }
        internal static Bindable<bool> ScoreboardVisible { get; private set; }
        internal static Bindable<bool> DisplayRankedAccuracy { get; private set; }
        internal static Bindable<bool> LeaderboardRankedAccuracy { get; private set; }
        internal static Bindable<bool> TintHitLightingBasedOnJudgementColor { get; private set; }
        internal static Bindable<OrderMapsetsBy> SelectOrderMapsetsBy { get; private set; }
        internal static Bindable<GroupMapsetsBy> SelectGroupMapsetsBy { get; private set; }
        internal static Bindable<GameMode> SelectFilterGameModeBy { get; private set; }
        internal static Bindable<GameMode> SelectedGameMode { get; private set; }
        internal static Bindable<LeaderboardType> LeaderboardSection { get; private set; }
        internal static Bindable<bool> AutoLoginToServer { get; private set; }
        internal static Bindable<bool> DisplayTimingLines { get; private set; }
        internal static Bindable<bool> DisplayHitBubbles { get; private set; }
        internal static Bindable<bool> DisplayMenuAudioVisualizer { get; private set; }
        internal static Bindable<bool> EnableHitsounds { get; private set; }
        internal static Bindable<bool> EnableLongNoteReleaseHitsounds { get; private set; }
        internal static Bindable<bool> EnableKeysounds { get; private set; }
        internal static Bindable<bool> TapToPause { get; private set; }
        internal static Bindable<bool> KeepPlayingUponFailing { get; private set; }
        internal static Bindable<bool> TapToRestart { get; private set; }
        internal static BindableInt LaneCoverTopHeight { get; private set; }
        internal static BindableInt LaneCoverBottomHeight { get; private set; }
        internal static Bindable<bool> LaneCoverTop { get; private set; }
        internal static Bindable<bool> LaneCoverBottom { get; private set; }
        internal static Bindable<bool> UIElementsOverLaneCover { get; private set; }
        internal static Bindable<bool> ReceptorsOverLaneCover { get; private set; }
        internal static Bindable<bool> DisplayFailedLocalScores { get; private set; }
        internal static Bindable<bool> SkipSplashScreen { get; private set; }
        internal static Bindable<bool> DisplayComboAlerts { get; private set; }
        internal static BindableInt EditorPlayfieldAlpha { get; private set; }
        internal static BindableInt EditorImGuiScalePercentage { get; private set; }
        internal static BindableInt EditorScrollSpeedKeys { get; private set; }
        internal static Bindable<bool> EditorLiveMapSnap { get; private set; }
        internal static BindableInt EditorLiveMapOffset { get; private set; }
        internal static Bindable<bool> EditorLiveMapLongNote { get; private set; }
        internal static BindableInt EditorLiveMapLongNoteThreshold { get; private set; }
        internal static Bindable<bool> EditorEnableHitsounds { get; private set; }
        internal static Bindable<bool> EditorEnableKeysounds { get; private set; }
        internal static Bindable<EditorBeatSnapColor> EditorBeatSnapColorType { get; private set; }
        internal static Bindable<bool> EditorOnlyShowMeasureLines { get; private set; }
        internal static Bindable<bool> EditorShowLaneDividerLines { get; private set; }
        internal static Bindable<bool> EditorHitObjectsMidpointAnchored { get; private set; }
        internal static Bindable<bool> EditorPlayMetronome { get; private set; }
        internal static Bindable<bool> EditorMetronomePlayHalfBeats { get; private set; }
        internal static Bindable<bool> DisplaySongTimeProgressNumbers { get; private set; }
        internal static Bindable<bool> DisplayJudgementCounter { get; private set; }
        internal static BindableInt HitErrorFadeTime { get; private set; }
        internal static Bindable<bool> SkipResultsScreenAfterQuit { get; private set; }
        internal static Bindable<bool> LockWinkeyDuringGameplay { get; private set; }
        internal static Bindable<HitObjectColoring> EditorObjectColoring { get; private set; }
        internal static Bindable<bool> EditorColorSvLineByTimingGroup { get; private set; }
        internal static Bindable<bool> LobbyFilterHasPassword { get; private set; }
        internal static Bindable<bool> LobbyFilterFullGame { get; private set; }
        internal static Bindable<bool> LobbyFilterOwnsMap { get; private set; }
        internal static Bindable<bool> LobbyFilterHasFriends { get; private set; }
        internal static Bindable<bool> EnableBattleRoyaleBackgroundFlashing { get; private set; }
        internal static Bindable<bool> EnableBattleRoyaleAlerts { get; private set; }
        internal static Bindable<bool> DisplayUnbeatableScoresDuringGameplay { get; private set; }
        internal static Bindable<bool> ShowSpectators { get; private set; }
        internal static Bindable<string> JudgementWindows { get; private set; }
        internal static Bindable<OrderMapsetsBy> MusicPlayerOrderMapsBy { get; private set; }
        internal static Bindable<OnlineUserListFilter> OnlineUserListFilterType { get; private set; }
        internal static Bindable<bool> DisplayFriendOnlineNotifications { get; private set; }
        internal static Bindable<bool> DisplaySongRequestNotifications { get; private set; }
        internal static Bindable<MultiplayerLobbyRuleset> MultiplayerLobbyRulesetType { get; private set; }
        internal static Bindable<MultiplayerLobbyGameMode> MultiplayerLobbyGameModeType { get; private set; }
        internal static Bindable<MultiplayerLobbyMapStatus> MultiplayerLobbyMapStatusType { get; private set; }
        internal static Bindable<MultiplayerLobbyRoomVisibility> MultiplayerLobbyVisibilityType { get; private set; }
        internal static Bindable<bool> UseSteamWorkshopSkin { get; private set; }
        internal static Bindable<bool> LowerFpsOnWindowInactive { get; private set; }
        internal static Bindable<bool> DownloadDisplayOwnedMapsets { get; private set; }
        internal static Bindable<bool> DownloadDisplayExplicitMapsets { get; private set; }
        internal static Bindable<bool> DownloadReverseSort { get; private set; }
        internal static Bindable<bool> DisplayNotificationsBottomToTop { get; private set; }
        internal static BindableInt SelectedProfileId { get; private set; }
        internal static BindableInt EditorBackgroundBrightness { get; private set; }
        internal static BindableInt EditorHitsoundVolume { get; private set; }
        internal static Bindable<bool> EditorScaleSpeedWithRate { get; private set; }
        internal static Bindable<EditorPlayfieldWaveformFilter> EditorAudioFilter { get; private set; }
        internal static Bindable<bool> EditorShowWaveform { get; private set; }
        internal static Bindable<bool> EditorShowSpectrogram { get; private set; }
        internal static Bindable<int> EditorSpectrogramMaximumFrequency { get; private set; }
        internal static Bindable<int> EditorSpectrogramMinimumFrequency { get; private set; }
        internal static Bindable<float> EditorSpectrogramCutoffFactor { get; private set; }
        internal static Bindable<float> EditorSpectrogramIntensityFactor { get; private set; }
        internal static Bindable<EditorPlayfieldSpectrogramFrequencyScale> EditorSpectrogramFrequencyScale { get; private set; }
        internal static BindableInt EditorSpectrogramFftSize { get; private set; }
        internal static BindableInt EditorSpectrogramInterleaveCount { get; private set; }
        internal static Bindable<EditorPlayfieldWaveformAudioDirection> EditorAudioDirection { get; private set; }
        internal static BindableInt EditorWaveformColorR { get; private set; }
        internal static BindableInt EditorWaveformColorG { get; private set; }
        internal static BindableInt EditorWaveformColorB { get; private set; }
        internal static BindableInt EditorWaveformBrightness { get; private set; }
        internal static BindableInt EditorSpectrogramBrightness { get; private set; }
        internal static Bindable<bool> EditorPlaceObjectsOnNearestTick { get; private set; }
        internal static Bindable<bool> EditorLiveMapping { get; private set; }
        internal static Bindable<bool> EditorInvertBeatSnapScroll { get; private set; }
        internal static BindableInt EditorLongNoteOpacity { get; private set; }
        internal static BindableInt GameplayNoteScale { get; private set; }
        internal static Bindable<bool> EditorDisplayGameplayPreview { get; private set; }
        internal static Bindable<string> EditorNoteSkin { get; private set; }
        internal static BindableInt VisualOffset { get; private set; }
        internal static Bindable<bool> Display1v1TournamentOverlay { get; private set; }
        internal static Bindable<bool> TournamentDisplay1v1PlayfieldScores { get; private set; }
        internal static Bindable<bool> ReloadSkinOnChange { get; private set; }
        [IgnoreWrite] internal static Dictionary<GameMode, Bindable<bool>> ScratchLanesLeft { get; private set; }
        internal static Bindable<bool> AcceptedTermsAndPrivacyPolicy { get; private set; }
        internal static Bindable<bool> DisplayGameplayOverlay { get; private set; }
        internal static Bindable<bool> EnableHighProcessPriority { get; private set; }
        internal static Bindable<bool> DisplayNotificationsInGameplay { get; private set; }
        internal static Bindable<bool> DisplayPauseWarning { get; private set; }
        internal static Bindable<bool> DisplayFailWarning { get; private set; }
        internal static Bindable<string> TournamentPlayer2Skin { get; private set; }
        internal static Bindable<Keys> KeyNavigateLeft { get; private set; }
        internal static Bindable<Keys> KeyNavigateRight { get; private set; }
        internal static Bindable<Keys> KeyNavigateUp { get; private set; }
        internal static Bindable<Keys> KeyNavigateDown { get; private set; }
        internal static Bindable<Keys> KeyNavigateBack { get; private set; }
        internal static Bindable<Keys> KeyNavigateSelect { get; private set; }

        // --- VIDEO MOD VARIABLES ---
        internal static Bindable<bool> VideoModEnabled { get; private set; }
        internal static Bindable<bool> VideoModAutoConfiguration { get; private set; }
        internal static BindableInt VideoModRamBudget { get; private set; }
        internal static BindableInt VideoModDecoderThreads { get; private set; }
        internal static BindableInt VideoModPreloadSeconds { get; private set; }
        internal static BindableInt VideoModTargetHeight { get; private set; }
        internal static Bindable<bool> VideoModHighQuality { get; private set; }
        internal static Bindable<bool> VideoModUse32Bit { get; private set; }
        internal static Bindable<bool> VideoModDebug { get; private set; }

        [IgnoreWrite] internal static Dictionary<GameMode, List<Bindable<GenericKey>>> KeyLayouts { get; private set; }
        [IgnoreWrite] internal static Dictionary<GameMode, List<Bindable<GenericKey>>> CoopKeyLayouts { get; private set; }
        [IgnoreWrite] internal static Dictionary<GameMode, List<Bindable<GenericKey>>> ScratchKeyLayouts { get; private set; }

        internal static GenericKey DefaultKeyLayout(GameMode mode, int index)
        {
            var keyCount = ModeHelper.ToKeyCount(mode);
            var half = keyCount / 2;
            var keys = new[] { Keys.A, Keys.S, Keys.D, Keys.F, Keys.V, Keys.B, Keys.H, Keys.J, Keys.K, Keys.L };
            var middleKey = Keys.Space;
            if (keyCount % 2 != 0 && index == half)
                return new GenericKey() { KeyboardKey = middleKey };
            else if (index <= half - 1)
                return new GenericKey() { KeyboardKey = keys[index] };
            else
                return new GenericKey() { KeyboardKey = keys[^(keyCount - index)] };
        }

        internal static Bindable<GenericKey> KeyPause { get; private set; }
        internal static Bindable<GenericKey> KeySkipIntro { get; private set; }
        internal static Bindable<Keys> KeyToggleOverlay { get; private set; }
        internal static Bindable<Keys> KeyToggleMirror { get; private set; }
        internal static Bindable<Keys> KeyDecreaseGameplayAudioRate { get; private set; }
        internal static Bindable<Keys> KeyIncreaseGameplayAudioRate { get; private set; }
        internal static Bindable<Keys> KeyRestartMap { get; private set; }
        internal static Bindable<Keys> KeyIncreaseScrollSpeed { get; private set; }
        internal static Bindable<Keys> KeyDecreaseScrollSpeed { get; private set; }
        internal static Bindable<Keys> KeyIncreaseMapOffset { get; private set; }
        internal static Bindable<Keys> KeyDecreaseMapOffset { get; private set; }
        internal static Bindable<Keys> KeyResetMapOffset { get; private set; }
        internal static Bindable<Keys> KeyTogglePlaytestAutoplay { get; private set; }
        internal static Bindable<Keys> KeyScoreboardVisible { get; private set; }
        internal static Bindable<Keys> KeyQuickExit { get; private set; }
        internal static Bindable<Keys> KeyEditorPausePlay { get; private set; }
        internal static Bindable<Keys> KeyEditorDecreaseAudioRate { get; private set; }
        internal static Bindable<Keys> KeyEditorIncreaseAudioRate { get; private set; }
        internal static Bindable<bool> InvertScrolling { get; private set; }
        internal static Bindable<bool> InvertEditorScrolling { get; private set; }
        internal static Bindable<Keys> KeyScreenshot { get; private set; }
        internal static Bindable<ResultGraphs> ResultGraph { get; private set; }
        internal static Bindable<string> AudioOutputDevice { get; private set; }
        [IgnoreWrite] internal static Dictionary<GameMode, BindableInt> PrioritizedMapDifficulty { get; private set; }
        internal static Bindable<GameMode> PrioritizedGameMode { get; private set; }
        [IgnoreWrite] private static bool FirstWrite { get; set; }
        [IgnoreWrite] private static long LastWrite { get; set; }

        public static void Initialize()
        {
            _gameDirectory = Directory.GetCurrentDirectory();
            _skinDirectory = _gameDirectory + "/Skins"; Directory.CreateDirectory(_skinDirectory);
            _screenshotDirectory = _gameDirectory + "/Screenshots"; Directory.CreateDirectory(_screenshotDirectory);
            _logsDirectory = _gameDirectory + "/Logs"; Directory.CreateDirectory(_logsDirectory);
            _replayDirectory = _gameDirectory + "/Replays"; Directory.CreateDirectory(_replayDirectory);
            _dataDirectory = _gameDirectory + "/Data"; Directory.CreateDirectory(_dataDirectory); Directory.CreateDirectory(_dataDirectory + "/r/");
            _songDirectory = _gameDirectory + "/Songs"; Directory.CreateDirectory(_songDirectory);
            Directory.CreateDirectory($"{WobbleGame.WorkingDirectory}/Plugins");
            Directory.CreateDirectory($"{WobbleGame.WorkingDirectory}/Tournament");
            ReadConfigFile();
            Logger.Important("Config file has been successfully read.", LogType.Runtime);
        }

        private static void ReadConfigFile()
        {
            var configFilePath = _gameDirectory + "/quaver.cfg";
            if (File.Exists(configFilePath))
            {
                try { var _ = new IniFileParser.IniFileParser(new ConcatenateDuplicatedKeysIniDataParser()).ReadFile(configFilePath)["Config"]; }
                catch (ParsingException)
                {
                    Logger.Important("Config file couldn't be read.", LogType.Runtime);
                    File.Copy(configFilePath, _gameDirectory + "/quaver.corrupted." + TimeHelper.GetUnixTimestampMilliseconds() + ".cfg");
                    File.Delete(configFilePath);
                }
            }
            if (!File.Exists(configFilePath))
            {
                File.WriteAllText(configFilePath, "; Quaver Configuration File");
                Logger.Important("Creating a new config file...", LogType.Runtime);
            }

            var data = new IniFileParser.IniFileParser(new ConcatenateDuplicatedKeysIniDataParser()).ReadFile(configFilePath, Encoding.UTF8)["Config"];

            GameDirectory = ReadSpecialConfigType(SpecialConfigType.Directory, @"GameDirectory", _gameDirectory, data);
            SkinDirectory = ReadSpecialConfigType(SpecialConfigType.Directory, @"SkinDirectory", _skinDirectory, data);
            ScreenshotDirectory = ReadSpecialConfigType(SpecialConfigType.Directory, @"ScreenshotDirectory", _screenshotDirectory, data);
            ReplayDirectory = ReadSpecialConfigType(SpecialConfigType.Directory, @"ReplayDirectory", _replayDirectory, data);
            LogsDirectory = ReadSpecialConfigType(SpecialConfigType.Directory, @"LogsDirectory", _logsDirectory, data);
            DataDirectory = ReadSpecialConfigType(SpecialConfigType.Directory, @"DataDirectory", _dataDirectory, data);
            SongDirectory = ReadSpecialConfigType(SpecialConfigType.Directory, @"SongDirectory", _songDirectory, data);
            _steamWorkshopDirectory = $"{GameDirectory.Value}/../../workshop/content/{SteamManager.ApplicationId}";
            SteamWorkshopDirectory = ReadSpecialConfigType(SpecialConfigType.Directory, @"SteamWorkshopDirectory", _steamWorkshopDirectory, data);
            SelectedGameMode = ReadValue(@"SelectedGameMode", GameMode.Keys4, data);
            Username = ReadValue(@"Username", "Player", data);
            VolumeGlobal = ReadInt(@"VolumeGlobal", 20, 0, 100, data);
            VolumeEffect = ReadInt(@"VolumeEffect", 20, 0, 100, data);
            VolumeMusic = ReadInt(@"VolumeMusic", 50, 0, 100, data);
            DevicePeriod = ReadInt(@"DevicePeriod", 2, 1, 100, data);
            DeviceBufferLengthMultiplier = ReadInt(@"DeviceBufferLengthMultiplier", 4, 2, 10, data);
            BackgroundBrightness = ReadInt(@"BackgroundBrightness", 50, 0, 100, data);
            WindowHeight = ReadInt(@"WindowHeight", 768, 360, short.MaxValue, data);
            WindowWidth = ReadInt(@"WindowWidth", 1366, 640, short.MaxValue, data);
            WindowBorderless = ReadValue(@"WindowBorderless", false, data);
            PlayfieldScale = ReadInt(@"PlayfieldScale", 100, 25, 100, data);
            PreferWayland = ReadValue(@"PreferWayland", false, data);
            DisplaySongTimeProgress = ReadValue(@"DisplaySongTimeProgress", true, data);
            WindowFullScreen = ReadValue(@"WindowFullScreen", false, data);
            FpsCounter = ReadValue(@"FpsCounter", false, data);
            FpsLimiterType = ReadValue(@"FpsLimiterType", FpsLimitType.Unlimited, data);
            CustomFpsLimit = ReadInt(@"CustomFpsLimit", 240, 60, 5000, data);
            SmoothAudioTimingGameplay = ReadValue(@"SmoothAudioTimingGameplay", false, data);
            SmoothAudioStart = ReadValue(@"SmoothAudioStart", false, data);
            NormaliseScrollVelocityByRatePercentage = ReadInt(@"NormaliseScrollVelocityByRatePercentage", 0, 0, 100, data);
            GlobalAudioOffset = ReadInt(@"GlobalAudioOffset", 0, -500, 500, data);
            Skin = ReadValue(@"Skin", "", data);
            DefaultSkin = ReadValue(@"DefaultSkin", DefaultSkins.Bar, data);
            DefaultEditorSkin = ReadValue<DefaultSkins?>(@"DefaultEditorSkin", null, data);
            Pitched = ReadValue(@"Pitched", true, data);
            ScoreboardVisible = ReadValue(@"ScoreboardVisible", true, data);
            DisplayRankedAccuracy = ReadValue(@"DisplayRankedAccuracy", false, data);
            LeaderboardRankedAccuracy = ReadValue(@"LeaderboardRankedAccuracy", false, data);
            SelectOrderMapsetsBy = ReadValue(@"SelectOrderMapsetsBy", OrderMapsetsBy.Artist, data);
            LeaderboardSection = ReadValue(@"LeaderboardSection", LeaderboardType.Local, data);
            OsuDbPath = ReadSpecialConfigType(SpecialConfigType.Path, @"OsuDbPath", "", data);
            EtternaDbPath = ReadSpecialConfigType(SpecialConfigType.Path, @"EtternaDbPath", "", data);
            AutoLoadOsuBeatmaps = ReadValue(@"AutoLoadOsuBeatmaps", false, data);
            DeleteOriginalFileAfterImport = ReadValue(@"DeleteOriginalFileAfterImport", true, data);
            DiscordRichPresence = ReadValue(@"DiscordRichPresence", true, data);
            AutoLoginToServer = ReadValue(@"AutoLoginToServer", true, data);
            DisplayTimingLines = ReadValue(@"DisplayTimingLines", true, data);
            DisplayHitBubbles = ReadValue(@"DisplayHitBubbles", true, data);
            DisplayMenuAudioVisualizer = ReadValue(@"DisplayMenuAudioVisualizer", true, data);
            EnableHitsounds = ReadValue(@"EnableHitsounds", true, data);
            EnableLongNoteReleaseHitsounds = ReadValue(@"EnableLongNoteReleaseHitsounds", false, data);
            EnableKeysounds = ReadValue(@"EnableKeysounds", true, data);
            KeyNavigateLeft = ReadValue(@"KeyNavigateLeft", Keys.Left, data);
            KeyNavigateRight = ReadValue(@"KeyNavigateRight", Keys.Right, data);
            KeyNavigateUp = ReadValue(@"KeyNavigateUp", Keys.Up, data);
            KeyNavigateDown = ReadValue(@"KeyNavigateDown", Keys.Down, data);
            KeyNavigateBack = ReadValue(@"KeyNavigateBack", Keys.Escape, data);
            KeyNavigateSelect = ReadValue(@"KeyNavigateSelect", Keys.Enter, data);
            KeySkipIntro = ReadGenericKey(@"KeySkipIntro", new GenericKey { KeyboardKey = Keys.Space }, data);
            KeyPause = ReadGenericKey(@"KeyPause", new GenericKey { KeyboardKey = Keys.Escape }, data);
            KeyToggleOverlay = ReadValue(@"KeyToggleOverlay", Keys.F8, data);
            KeyToggleMirror = ReadValue(@"KeyToggleMirror", Keys.H, data);
            KeyDecreaseGameplayAudioRate = ReadValue(@"KeyDecreaseGameplayAudioRate", Keys.OemMinus, data);
            KeyIncreaseGameplayAudioRate = ReadValue(@"KeyIncreaseGameplayAudioRate", Keys.OemPlus, data);
            KeyRestartMap = ReadValue(@"KeyRestartMap", Keys.OemTilde, data);
            KeyDecreaseScrollSpeed = ReadValue(@"KeyDecreaseScrollSpeed", Keys.F3, data);
            KeyIncreaseScrollSpeed = ReadValue(@"KeyIncreaseScrollSpeed", Keys.F4, data);
            KeyDecreaseMapOffset = ReadValue(@"KeyDecreaseMapOffset", Keys.OemMinus, data);
            KeyIncreaseMapOffset = ReadValue(@"KeyIncreaseMapOffset", Keys.OemPlus, data);
            KeyResetMapOffset = ReadValue(@"KeyResetMapOffset", Keys.D0, data);
            KeyTogglePlaytestAutoplay = ReadValue(@"KeyTogglePlaytestAutoplay", Keys.Tab, data);
            KeyScoreboardVisible = ReadValue(@"KeyScoreboardVisible", Keys.Tab, data);
            KeyQuickExit = ReadValue(@"KeyQuickExit", Keys.F1, data);
            KeyScreenshot = ReadValue(@"KeyScreenshot", Keys.F12, data);
            TapToPause = ReadValue(@"TapToPause", false, data);
            KeepPlayingUponFailing = ReadValue(@"KeepPlayingUponFailing", false, data);
            TapToRestart = ReadValue(@"TapToRestart", false, data);
            DisplayFailedLocalScores = ReadValue(@"DisplayFailedLocalScores", true, data);
            EditorScrollSpeedKeys = ReadInt(@"EditorScrollSpeedKeys", 16, 5, 100, data);
            EditorImGuiScalePercentage = ReadInt(@"EditorImGuiScalePercentage", 100, 25, 300, data);
            EditorPlayfieldAlpha = ReadInt(@"EditorPlayfieldAlpha", 100, 0, 100, data);
            KeyEditorPausePlay = ReadValue(@"KeyEditorPausePlay", Keys.Space, data);
            KeyEditorDecreaseAudioRate = ReadValue(@"KeyEditorDecreaseAudioRate", Keys.OemMinus, data);
            KeyEditorIncreaseAudioRate = ReadValue(@"KeyEditorIncreaseAudioRate", Keys.OemPlus, data);
            InvertScrolling = ReadValue(@"InvertScrolling", false, data);
            InvertEditorScrolling = ReadValue(@"InvertEditorScrolling", true, data);
            EditorLiveMapSnap = ReadValue(@"EditorLiveMapSnap", false, data);
            EditorLiveMapOffset = ReadInt(@"EditorLiveMapOffset", 0, -200, 200, data);
            EditorLiveMapLongNote = ReadValue(@"EditorLiveMapLongNote", true, data);
            EditorLiveMapLongNoteThreshold = ReadInt(@"EditorLiveMapLongNoteThreshold", 100, 0, 1000, data);
            EditorEnableHitsounds = ReadValue(@"EditorEnableHitsounds", true, data);
            EditorEnableKeysounds = ReadValue(@"EditorEnableKeysounds", true, data);
            EditorBeatSnapColorType = ReadValue(@"EditorBeatSnapColorType", EditorBeatSnapColor.Default, data);
            EditorOnlyShowMeasureLines = ReadValue(@"EditorOnlyShowMeasureLines", false, data);
            EditorShowLaneDividerLines = ReadValue(@"EditorShowLaneDividerLines", true, data);
            EditorHitObjectsMidpointAnchored = ReadValue(@"EditorHitObjectsMidpointAnchored", false, data);
            EditorPlayMetronome = ReadValue(@"EditorPlayMetronome", true, data);
            EditorMetronomePlayHalfBeats = ReadValue(@"EditorMetronomePlayHalfBeats", false, data);
            DisplaySongTimeProgressNumbers = ReadValue(@"DisplaySongTimeProgressNumbers", true, data);
            DisplayJudgementCounter = ReadValue(@"DisplayJudgementCounter", true, data);
            HitErrorFadeTime = ReadInt(@"HitErrorFadeTime", 1000, 100, 5000, data);
            SkipResultsScreenAfterQuit = ReadValue(@"SkipResultsScreenAfterQuit", false, data);
            LockWinkeyDuringGameplay = ReadValue(@"LockWinkeyDuringGameplay", true, data);
            DisplayComboAlerts = ReadValue(@"DisplayComboAlerts", true, data);
            LaneCoverTopHeight = ReadInt(@"LaneCoverTopHeight", 25, 0, 75, data);
            LaneCoverBottomHeight = ReadInt(@"LaneCoverBottomHeight", 25, 0, 75, data);
            LaneCoverTop = ReadValue(@"LaneCoverTop", false, data);
            LaneCoverBottom = ReadValue(@"LaneCoverBottom", false, data);
            UIElementsOverLaneCover = ReadValue(@"UIElementsOverLaneCover", true, data);
            ReceptorsOverLaneCover = ReadValue(@"ReceptorsOverLaneCover", false, data);
            EditorObjectColoring = ReadValue(@"EditorObjectColoring", HitObjectColoring.None, data);
            EditorColorSvLineByTimingGroup = ReadValue(@"EditorColorSVLineByTimingGroup", true, data);
            LobbyFilterHasPassword = ReadValue(@"LobbyFilterHasPassword", true, data);
            LobbyFilterFullGame = ReadValue(@"LobbyFilterFullGame", false, data);
            LobbyFilterOwnsMap = ReadValue(@"LobbyFilterOwnsMap", false, data);
            LobbyFilterHasFriends = ReadValue(@"LobbyFilterHasFriends", false, data);
            EnableBattleRoyaleBackgroundFlashing = ReadValue(@"EnableBattleRoyaleBackgroundFlashing", true, data);
            EnableBattleRoyaleAlerts = ReadValue(@"EnableBattleRoyaleAlerts", true, data);
            SelectFilterGameModeBy = ReadValue(@"SelectFilterGameModeBy", (GameMode)0, data);
            DisplayUnbeatableScoresDuringGameplay = ReadValue(@"DisplayUnbeatableScoresDuringGameplay", true, data);
            ShowSpectators = ReadValue(@"ShowSpectators", true, data);
            JudgementWindows = ReadValue("JudgementWindows", "", data);
            SelectGroupMapsetsBy = ReadValue(@"SelectGroupMapsetsBy", GroupMapsetsBy.None, data);
            MusicPlayerOrderMapsBy = ReadValue(@"MusicPlayerOrderMapsBy", OrderMapsetsBy.Artist, data);
            OnlineUserListFilterType = ReadValue(@"OnlineUserListFilterType", OnlineUserListFilter.All, data);
            DisplayFriendOnlineNotifications = ReadValue(@"DisplayFriendOnlineNotifications", true, data);
            DisplaySongRequestNotifications = ReadValue(@"DisplaySongRequestNotifications", true, data);
            MultiplayerLobbyRulesetType = ReadValue(@"MultiplayerLobbyRulesetType", MultiplayerLobbyRuleset.All, data);
            MultiplayerLobbyGameModeType = ReadValue(@"MultiplayerLobbyGameModeType", MultiplayerLobbyGameMode.All, data);
            MultiplayerLobbyMapStatusType = ReadValue(@"MultiplayerLobbyMapStatusType", MultiplayerLobbyMapStatus.All, data);
            MultiplayerLobbyVisibilityType = ReadValue(@"MultiplayerLobbyVisibilityType", MultiplayerLobbyRoomVisibility.All, data);
            UseSteamWorkshopSkin = ReadValue(@"UseSteamWorkshopSkin", false, data);
            LowerFpsOnWindowInactive = ReadValue(@"LowerFpsOnWindowInactive", true, data);
            DownloadDisplayOwnedMapsets = ReadValue(@"DownloadDisplayOwnedMapsets", true, data);
            DownloadDisplayExplicitMapsets = ReadValue(@"DownloadDisplayExplicitMapsets", false, data);
            DownloadReverseSort = ReadValue(@"DownloadReverseSort", false, data);
            DisplayNotificationsBottomToTop = ReadValue(@"DisplayNotificationsBottomToTop", false, data);
            SelectedProfileId = ReadInt(@"SelectedProfileId", -1, -1, int.MaxValue, data);
            EditorBackgroundBrightness = ReadInt(@"EditorBackgroundBrightness", 40, 0, 100, data);
            EditorHitsoundVolume = ReadInt(@"EditorHitsoundVolume", -1, -1, 100, data);
            EditorScaleSpeedWithRate = ReadValue(@"EditorScaleSpeedWithRate", true, data);
            EditorLongNoteOpacity = ReadInt(@"EditorLongNoteOpacity", 100, 30, 100, data);
            GameplayNoteScale = ReadInt(@"GameplayNoteScale", 100, 25, 100, data);
            EditorDisplayGameplayPreview = ReadValue(@"EditorDisplayGameplayPreview", false, data);
            EditorNoteSkin = ReadValue<string>(@"EditorNoteSkin", null, data);
            EditorPlaceObjectsOnNearestTick = ReadValue(@"EditorPlaceObjectsOnNearestTick", true, data);
            EditorInvertBeatSnapScroll = ReadValue(@"EditorInvertBeatSnapScroll", false, data);
            EditorLiveMapping = ReadValue(@"EditorLiveMapping", true, data);
            EditorAudioFilter = ReadValue(@"EditorAudioFilter", EditorPlayfieldWaveformFilter.None, data);
            EditorShowWaveform = ReadValue(@"EditorShowWaveform", true, data);
            EditorShowSpectrogram = ReadValue(@"EditorShowSpectrogram", false, data);
            EditorSpectrogramMaximumFrequency = ReadInt(@"EditorSpectrogramMaximumFrequency", 7000, 5000, 10000, data);
            EditorSpectrogramMinimumFrequency = ReadInt("EditorSpectrogramMinimumFrequency", 125, 0, 1500, data);
            EditorSpectrogramCutoffFactor = ReadValue("EditorSpectrogramCutoffFactor", 0.34f, data);
            EditorSpectrogramIntensityFactor = ReadValue("EditorSpectrogramIntensityFactor", 9.5f, data);
            EditorSpectrogramFrequencyScale = ReadValue("EditorSpectrogramFrequencyScale", EditorPlayfieldSpectrogramFrequencyScale.Linear, data);
            EditorSpectrogramFftSize = ReadInt(@"EditorSpectrumFftSize", 512, 256, 16384, data);
            EditorSpectrogramInterleaveCount = ReadInt(@"EditorSpectrogramInterleaveCount", 4, 1, 16, data);
            EditorAudioDirection = ReadValue(@"EditorAudioDirection", EditorPlayfieldWaveformAudioDirection.Both, data);
            EditorWaveformColorR = ReadInt(@"EditorWaveformColorR", 0, 0, 255, data);
            EditorWaveformColorG = ReadInt(@"EditorWaveformColorG", 200, 0, 255, data);
            EditorWaveformColorB = ReadInt(@"EditorWaveformColorB", 255, 0, 255, data);
            EditorWaveformBrightness = ReadInt(@"EditorWaveformBrightness", 50, 0, 100, data);
            EditorSpectrogramBrightness = ReadInt(@"EditorSpectrogramBrightness", 50, 0, 100, data);
            VisualOffset = ReadInt(@"VisualOffset", 0, -500, 500, data);
            TintHitLightingBasedOnJudgementColor = ReadValue(@"TintHitLightingBasedOnJudgementColor", false, data);
            Display1v1TournamentOverlay = ReadValue(@"Display1v1TournamentOverlay", true, data);
            TournamentDisplay1v1PlayfieldScores = ReadValue(@"TournamentDisplay1v1PlayfieldScores", true, data);
            ReloadSkinOnChange = ReadValue(@"ReloadSkinOnChange", false, data);
            AcceptedTermsAndPrivacyPolicy = ReadValue(@"AcceptedTermsAndPrivacyPolicy", false, data);
            SkipSplashScreen = ReadValue(@"SkipSplashScreen", false, data);
            DisplayGameplayOverlay = ReadValue(@"DisplayGameplayOverlay", true, data);
            EnableHighProcessPriority = ReadValue(@"EnableHighProcessPriority", false, data);
            DisplayNotificationsInGameplay = ReadValue(@"DisplayNotificationsInGameplay", false, data);
            DisplayPauseWarning = ReadValue(@"DisplayPauseWarning", true, data);
            DisplayFailWarning = ReadValue(@"DisplayFailWarning", true, data);
            TournamentPlayer2Skin = ReadValue(@"TournamentPlayer2Skin", "", data);
            ResultGraph = ReadValue(@"ResultGraph", ResultGraphs.Deviance, data);
            AudioOutputDevice = ReadValue(@"AudioOutputDevice", "Default", data);
            PrioritizedGameMode = ReadValue(@"PrioritizedGameMode", (GameMode)0, data);
            
            // --- VIDEO MOD SETTINGS ---
            VideoModEnabled = ReadValue(@"VideoModEnabled", true, data);
            VideoModHighQuality = ReadValue(@"VideoModHighQuality", VideoUtils.GetTotalRamMB() > 4096, data);
            VideoModAutoConfiguration = ReadValue(@"VideoModAutoConfiguration", true, data);
            VideoModRamBudget = ReadInt(@"VideoModRamBudget", 1024, 256, VideoUtils.GetTotalRamMB(), data);
            VideoModDecoderThreads = ReadInt(@"VideoModDecoderThreads", 2, 1, VideoUtils.GetCpuThreads(), data);
            VideoModPreloadSeconds = ReadInt(@"VideoModPreloadSeconds", 3, 1, 30, data);
            VideoModTargetHeight = ReadInt(@"VideoModTargetHeight", 720, 0, 2160, data);
            VideoModUse32Bit = ReadValue(@"VideoModUse32Bit", false, data);
            VideoModDebug = ReadValue(@"VideoModDebug", false, data);
            
            KeyLayouts = new();
            CoopKeyLayouts = new();
            ScratchKeyLayouts = new();
            PrioritizedMapDifficulty = new();
            ScrollSpeeds = new();
            ScrollDirections = new();
            ScratchLanesLeft = new();
            for (var keyCount = 1; keyCount <= ModeHelper.MaxKeyCount; keyCount++)
            {
                var mode = ModeHelper.FromKeyCount(keyCount);
                KeyLayouts.Add(mode, new List<Bindable<GenericKey>>());
                CoopKeyLayouts.Add(mode, new List<Bindable<GenericKey>>());
                for (var key = 1; key <= keyCount; key++)
                {
                    KeyLayouts[mode].Add(ReadGenericKey($"KeyMania{keyCount}K{key}", DefaultKeyLayout(mode, key - 1), data));
                    CoopKeyLayouts[mode].Add(ReadGenericKey($"KeyCoop2P{keyCount}K{key}", new GenericKey() { KeyboardKey = Keys.None }, data));
                }
                ScratchKeyLayouts.Add(mode, new List<Bindable<GenericKey>>
                {
                    ReadGenericKey($"KeyScratch{keyCount}K1", new GenericKey() { KeyboardKey = Keys.None }, data),
                    ReadGenericKey($"KeyScratch{keyCount}K2", new GenericKey() { KeyboardKey = Keys.None }, data)
                });
                PrioritizedMapDifficulty.Add(mode, ReadInt($"PrioritizedMapDifficulty{keyCount}K", 0, 0, 1000, data));
                ScrollSpeeds.Add(mode, ReadInt($"ScrollSpeed{keyCount}K", 150, 50, 1000, data));
                ScrollDirections.Add(mode, ReadValue($"ScrollDirection{keyCount}K", ScrollDirection.Down, data));
                ScratchLanesLeft.Add(mode, ReadValue($"ScratchLaneLeft{keyCount}K", true, data));
            }
            ScrollContainer.GlobalInvertedScrolling = InvertScrolling;
            if (string.IsNullOrEmpty(Username.Value))
                Username.Value = "Player";
            WriteConfigFileAsync().Wait();
        }

        private static Bindable<T> ReadValue<T>(string name, T defaultVal, KeyDataCollection ini)
        {
            var binded = new Bindable<T>(name, defaultVal);
            var converter = TypeDescriptor.GetConverter(typeof(T));
            try { binded.Value = (T)converter.ConvertFromString(null, CultureInfo.InvariantCulture, ini[name]); }
            catch (Exception) { binded.Value = defaultVal; }
            binded.ValueChanged += AutoSaveConfiguration;
            return binded;
        }

        private static BindableInt ReadInt(string name, int defaultVal, int min, int max, KeyDataCollection ini)
        {
            var binded = new BindableInt(name, defaultVal, min, max);
            binded.Value = int.TryParse(ini[name], out var value) ? value : defaultVal;
            binded.ValueChanged += AutoSaveConfiguration;
            return binded;
        }

        private static Bindable<string> ReadSpecialConfigType(SpecialConfigType type, string name, string defaultVal, KeyDataCollection ini)
        {
            var binded = new Bindable<string>(name, defaultVal);
            try
            {
                var parsedVal = ini[name];
                switch (type)
                {
                    case SpecialConfigType.Directory:
                        if (Directory.Exists(parsedVal)) binded.Value = parsedVal;
                        else { Directory.CreateDirectory(defaultVal); binded.Value = defaultVal; }
                        break;
                    case SpecialConfigType.Path:
                        binded.Value = File.Exists(parsedVal) ? parsedVal : defaultVal;
                        break;
                    default: binded.Value = defaultVal; break;
                }
            }
            catch (Exception) { binded.Value = defaultVal; }
            binded.ValueChanged += AutoSaveConfiguration;
            return binded;
        }

        private static Bindable<GenericKey> ReadGenericKey(string name, GenericKey defaultVal, KeyDataCollection ini)
        {
            var binded = new Bindable<GenericKey>(name, defaultVal);
            if (GenericKey.TryParse(ini[name], out var key)) binded.Value = key;
            binded.ValueChanged += AutoSaveConfiguration;
            return binded;
        }

        private static void AutoSaveConfiguration<T>(object sender, BindableValueChangedEventArgs<T> d)
        {
            CommonTaskScheduler.Add(CommonTask.WriteConfig);
        }

        internal static void WriteKeySpecific(StringBuilder sb)
        {
            for (var keyCount = 1; keyCount <= ModeHelper.MaxKeyCount; keyCount++)
            {
                var mode = ModeHelper.FromKeyCount(keyCount);
                for (var key = 1; key <= keyCount; key++)
                {
                    sb.AppendLine($"KeyMania{keyCount}K{key} = {KeyLayouts[mode][key - 1].Value}");
                    sb.AppendLine($"KeyCoop2P{keyCount}K{key} = {CoopKeyLayouts[mode][key - 1].Value}");
                }
                sb.AppendLine($"KeyScratch{keyCount}K1 = {ScratchKeyLayouts[mode][0].Value}");
                sb.AppendLine($"KeyScratch{keyCount}K2 = {ScratchKeyLayouts[mode][1].Value}");
                sb.AppendLine($"PrioritizedMapDifficulty{keyCount}K = {PrioritizedMapDifficulty[mode].Value}");
                sb.AppendLine($"ScrollSpeed{keyCount}K = {ScrollSpeeds[mode].Value}");
                sb.AppendLine($"ScrollDirection{keyCount}K = {ScrollDirections[mode].Value}");
                sb.AppendLine($"ScratchLaneLeft{keyCount}K = {ScratchLanesLeft[mode].Value}");
            }
        }

        internal static async Task WriteConfigFileAsync()
        {
            var attempts = 0;
            var sb = new StringBuilder();
            sb.AppendLine("; Last Updated On: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();
            sb.AppendLine("[Config]");
            sb.AppendLine("; Quaver Configuration Values");

            foreach (var prop in typeof(ConfigManager).GetProperties(BindingFlags.Static | BindingFlags.NonPublic))
            {
                if (prop.GetCustomAttribute<IgnoreWriteAttribute>() != null) continue;
                try { sb.AppendLine(prop.Name + " = " + prop.GetValue(null)); }
                catch (Exception) { sb.AppendLine(prop.Name + " = "); }
            }
            WriteKeySpecific(sb);

            try
            {
                using (var sw = new StreamWriter(_gameDirectory + "/quaver.cfg")) { await sw.WriteLineAsync(sb.ToString()); }
                FirstWrite = false;
            }
            catch (Exception)
            {
                while (attempts != 2)
                {
                    attempts++;
                    using (var sw = new StreamWriter(_gameDirectory + "/quaver.cfg")) { await sw.WriteLineAsync(sb.ToString()); }
                }
                if (attempts == 2) Logger.Error("Too many write attempts to the config file have been made.", LogType.Runtime);
            }
            LastWrite = GameBase.Game?.TimeRunning ?? -1;
        }

        public static bool IsFileReady(string sFilename)
        {
            try { using (var inputStream = File.Open(sFilename, FileMode.Open, FileAccess.Read, FileShare.None)) return (inputStream.Length > 0); }
            catch (Exception) { return false; }
        }
    }

    internal enum SpecialConfigType { Directory, Path, Skin }
    public enum DefaultSkins { Arrow, Bar, Circle }
    internal sealed class IgnoreWriteAttribute : Attribute { }
}
