using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Quaver.API.Enums;
using Quaver.API.Helpers;
using Quaver.Server.Client.Enums;
using Quaver.Server.Client.Objects;
using Quaver.Shared.Audio;
using Quaver.Shared.Config;
using Quaver.Shared.Database.Maps;
using Quaver.Shared.Database.Playlists;
using Quaver.Shared.Database.Scores;
using Quaver.Shared.Database.Settings;
using Quaver.Shared.Discord;
using Quaver.Shared.Graphics.Notifications;
using Quaver.Shared.Graphics.Transitions;
using Quaver.Shared.Modifiers;
using Quaver.Shared.Modifiers.Mods;
using Quaver.Shared.Online;
using Quaver.Shared.Scheduling;
using Quaver.Shared.Screens.Edit;
using Quaver.Shared.Screens.Importing;
using Quaver.Shared.Screens.Loading;
using Quaver.Shared.Screens.Main;
using Quaver.Shared.Screens.Multi;
using Quaver.Shared.Screens.Selection.UI;
using Quaver.Shared.Screens.Selection.UI.Dialogs;
using Quaver.Shared.Screens.Selection.UI.FilterPanel.Search;
using Quaver.Shared.Screens.Selection.UI.Leaderboard;
using Quaver.Shared.Screens.Selection.UI.Maps;
using Quaver.Shared.Screens.Selection.UI.Mapsets;
using Quaver.Shared.Screens.Tournament;
using Quaver.Shared.Skinning;
using Wobble;
using Wobble.Audio.Tracks;
using Wobble.Bindables;
using Wobble.Graphics.UI.Dialogs;
using Wobble.Input;
using Wobble.Logging;

namespace Quaver.Shared.Screens.Selection
{
    public sealed class SelectionScreen : QuaverScreen, IHasLeftPanel
    {
        public override QuaverScreenType Type { get; } = QuaverScreenType.Select;
        public bool IsMultiplayer { get; }
        public Bindable<List<Mapset>> AvailableMapsets { get; private set; }
        public Bindable<string> CurrentSearchQuery { get; private set; }
        public Bindable<SelectContainerPanel> ActiveLeftPanel { get; set; }
        public Bindable<SelectScrollContainerType> ActiveScrollContainer { get; private set; }
        private Random Rng { get; } = new Random();
        public Stack<Map> RngHistory { get; set; } = new Stack<Map>();
        public static event EventHandler<RandomMapsetSelectedEventArgs> RandomMapsetSelected;
        private bool IsExportingMapset { get; set; }
        public Bindable<bool> IsPlayTestingInPreview { get; private set; }

        public SelectionScreen()
        {
            IsMultiplayer = OnlineManager.CurrentGame != null;
            if (MapsetImporter.Queue.Count > 0 || QuaverSettingsDatabaseCache.OutdatedMaps.Count != 0 || MapDatabaseCache.MapsToUpdate.Count != 0)
            {
                Exit(() => new ImportingScreen(null, true));
                return;
            }
            if (IsMultiplayer) OnlineManager.Client?.SetGameCurrentlySelectingMap(true);
            else SetRichPresence();
            InitializeSearchQueryBindable();
            InitializeAvailableMapsetsBindable();
            InitializeActiveLeftPanelBindable();
            InitializeActiveScrollContainerBindable();
            InitializeTestPlayingBindable();
            InitializeSelectedPlaylist();
            AvailableMapsets.Value = MapsetHelper.FilterMapsets(CurrentSearchQuery);
            MapManager.MapsetDeleted += OnMapsetDeleted;
            MapManager.MapDeleted += OnMapDeleted;
            MapManager.MapUpdated += OnMapUpdated;
            MapManager.SongRequestPlayed += OnSongRequestPlayed;
            ConfigManager.AutoLoadOsuBeatmaps.ValueChanged += OnAutoLoadOsuBeatmapsChanged;
            View = new SelectionScreenView(this);
        }

        public override void OnFirstUpdate()
        {
            GameBase.Game.GlobalUserInterface.Cursor.Alpha = 1;
            FadeAudioTrackIn();
            SkinManager.StartWatching();
            ScreenExiting += (_, _) => SkinManager.StopWatching();
            base.OnFirstUpdate();
        }

        public override void Update(GameTime gameTime)
        {
            if (!Exiting) GameBase.Game.GlobalUserInterface.Cursor.Alpha = 1;
            HandleInput(gameTime);
            base.Update(gameTime);
        }

        public override void Destroy()
        {
            CurrentSearchQuery?.Dispose();
            AvailableMapsets?.Dispose();
            ActiveLeftPanel?.Dispose();
            ActiveScrollContainer?.Dispose();
            IsPlayTestingInPreview?.Dispose();
            RandomMapsetSelected = null;
            MapManager.MapsetDeleted -= OnMapsetDeleted;
            MapManager.MapDeleted -= OnMapDeleted;
            MapManager.MapUpdated -= OnMapUpdated;
            MapManager.SongRequestPlayed -= OnSongRequestPlayed;
            SkinManager.StopWatching();
            ConfigManager.AutoLoadOsuBeatmaps.ValueChanged -= OnAutoLoadOsuBeatmapsChanged;
            base.Destroy();
        }

        private void InitializeSearchQueryBindable() => CurrentSearchQuery = new Bindable<string>(null) { Value = FilterPanelSearchBox.PreviousSearchTerm };
        private void InitializeAvailableMapsetsBindable() => AvailableMapsets = new Bindable<List<Mapset>>(null) { Value = new List<Mapset>() };
        private void InitializeActiveLeftPanelBindable() { ActiveLeftPanel = new Bindable<SelectContainerPanel>(SelectContainerPanel.Leaderboard) { Value = SelectContainerPanel.Leaderboard }; }
        private void InitializeActiveScrollContainerBindable()
        {
            ActiveScrollContainer = new Bindable<SelectScrollContainerType>(SelectScrollContainerType.Mapsets) { Value = SelectScrollContainerType.Mapsets };
            if (ConfigManager.SelectGroupMapsetsBy.Value == GroupMapsetsBy.Playlists) ActiveScrollContainer.Value = SelectScrollContainerType.Playlists;
            if (PlaylistManager.Selected.Value != null && ConfigManager.SelectGroupMapsetsBy.Value == GroupMapsetsBy.Playlists && PlaylistManager.Selected.Value.Maps.Count > 0) ActiveScrollContainer.Value = SelectScrollContainerType.Mapsets;
        }
        private void InitializeTestPlayingBindable() => IsPlayTestingInPreview = new Bindable<bool>(false) { Value = false };
        private void InitializeSelectedPlaylist() { if (PlaylistManager.Selected.Value == null && PlaylistManager.Playlists.Count != 0) PlaylistManager.Selected.Value = PlaylistManager.Playlists.First(); }

        private void HandleInput(GameTime gameTime)
        {
            if (Exiting) return;
            if (DialogManager.Dialogs.Count != 0) return;
            HandleKeyPressEscape();
            HandleKeyPressF1();
            HandleKeyPressF2();
            HandleKeyPressF3();
            HandleKeyPressF4();
            HandleKeyPressF5();  // Handles Smart/Full Refresh
            HandleKeyPressEnter();
            HandleKeyPressControlInput();
            HandleThumb1MouseButtonClick();
            if (ActiveLeftPanel.Value == SelectContainerPanel.Leaderboard) HandleKeyPressTab();
        }

        private void HandleKeyPressEscape() { if (!KeyboardManager.IsUniqueKeyPress(Keys.Escape)) return; HandleBackAction(); }
        private void HandleKeyPressF1() { if (!KeyboardManager.IsUniqueKeyPress(Keys.F1)) return; if (ActiveLeftPanel.Value == SelectContainerPanel.Modifiers) ActiveLeftPanel.Value = SelectContainerPanel.Leaderboard; else ActiveLeftPanel.Value = SelectContainerPanel.Modifiers; }
        private void HandleKeyPressF2() { if (!KeyboardManager.IsUniqueKeyPress(Keys.F2)) return; if (KeyboardManager.IsShiftDown()) SelectPrevRandomMap(); else SelectRandomMap(); }
        private void HandleKeyPressF3() { if (KeyboardManager.IsCtrlDown()) return; if (!KeyboardManager.IsUniqueKeyPress(Keys.F3)) return; if (ActiveLeftPanel.Value == SelectContainerPanel.MapPreview) ActiveLeftPanel.Value = SelectContainerPanel.Leaderboard; else ActiveLeftPanel.Value = SelectContainerPanel.MapPreview; }
        private void HandleKeyPressF4() { if (KeyboardManager.IsCtrlDown()) return; if (!KeyboardManager.IsUniqueKeyPress(Keys.F4)) return; if (ActiveLeftPanel.Value == SelectContainerPanel.UserProfile) ActiveLeftPanel.Value = SelectContainerPanel.Leaderboard; else ActiveLeftPanel.Value = SelectContainerPanel.UserProfile; }
        
        // --- SMART REFRESH IMPLEMENTATION ---
        private void HandleKeyPressF5() 
        {
            if (!KeyboardManager.IsUniqueKeyPress(Keys.F5)) return;

            // Ctrl + F5 = Full Refresh
            if (KeyboardManager.IsCtrlDown())
            {
                DialogManager.Show(new RefreshDialog());
                return;
            }

            // F5 = Smart Refresh
            NotificationManager.Show(NotificationLevel.Info, "Scanning for new maps...");
            
            ThreadScheduler.Run(() =>
            {
                // DETECTED FIX: This now returns bool, so we store it in 'found'
                var found = MapManager.DetectNewMapsets();
                
                if (found)
                {
                    // Case 1: Archives were found (.osz) in the Queue
                    if (MapsetImporter.Queue.Count > 0)
                    {
                        // Pass null for files, true for auto-import (standard Quaver logic)
                        Exit(() => new ImportingScreen(null, true)); 
                        return;
                    }

                    // Case 2: Only folders were found, just refresh UI
                    NotificationManager.Show(NotificationLevel.Success, "New maps loaded!");
                    lock (AvailableMapsets.Value) 
                        AvailableMapsets.Value = MapsetHelper.FilterMapsets(CurrentSearchQuery);
                }
                else
                {
                     NotificationManager.Show(NotificationLevel.Success, "No new maps found.");
                }
            });
        }

        private void HandleKeyPressEnter()
        {
            if (!KeyboardManager.IsUniqueKeyPress(Keys.Enter)) return;
            if (KeyboardManager.IsAltDown()) return;
            switch (ActiveScrollContainer.Value)
            {
                case SelectScrollContainerType.Mapsets: if (MapsetHelper.IsSingleDifficultySorted()) ExitToGameplay(); else ActiveScrollContainer.Value = SelectScrollContainerType.Maps; break;
                case SelectScrollContainerType.Maps: ExitToGameplay(); break;
                case SelectScrollContainerType.Playlists: ActiveScrollContainer.Value = SelectScrollContainerType.Mapsets; break;
                default: throw new ArgumentOutOfRangeException();
            }
        }

        public static void HandleKeyPressTab()
        {
            if (!KeyboardManager.IsUniqueKeyPress(Keys.Tab)) return;
            var index = (int)ConfigManager.LeaderboardSection.Value;
            var length = Enum.GetNames(typeof(LeaderboardType)).Length;
            int newIndex;
            if (KeyboardManager.IsShiftDown()) { if (index - 1 >= 0) newIndex = index - 1; else newIndex = length - 1; }
            else { if (index + 1 < length) newIndex = index + 1; else newIndex = 0; }
            ConfigManager.LeaderboardSection.Value = (LeaderboardType)newIndex;
        }

        private void HandleKeyPressControlInput()
        {
            if (!KeyboardManager.IsCtrlDown()) return;
            var shiftHeld = KeyboardManager.IsShiftDown();
            if (KeyboardManager.IsUniqueKeyPress(ConfigManager.KeyIncreaseGameplayAudioRate.Value)) ModManager.AddSpeedMods(GetNextRate(true, shiftHeld));
            if (KeyboardManager.IsUniqueKeyPress(ConfigManager.KeyDecreaseGameplayAudioRate.Value)) ModManager.AddSpeedMods(GetNextRate(false, shiftHeld));
            if (KeyboardManager.IsUniqueKeyPress(Keys.D0)) ConfigManager.Pitched.Value = !ConfigManager.Pitched.Value;
            if (KeyboardManager.IsUniqueKeyPress(ConfigManager.KeyToggleMirror.Value)) { if (ModManager.IsActivated(ModIdentifier.Mirror)) ModManager.RemoveMod(ModIdentifier.Mirror); else ModManager.AddMod(ModIdentifier.Mirror); }
            ChangeScrollSpeed();
        }

        private void HandleThumb1MouseButtonClick()
        {
            if (!MouseManager.IsUniqueClick(MouseButton.Thumb1)) return;
            var view = (SelectionScreenView)View;
            switch (ActiveScrollContainer.Value)
            {
                case SelectScrollContainerType.Mapsets:
                    if (ConfigManager.SelectGroupMapsetsBy.Value != GroupMapsetsBy.Playlists) return;
                    if (!view.MapsetContainer.IsHovered()) return;
                    ActiveScrollContainer.Value = SelectScrollContainerType.Playlists;
                    break;
                case SelectScrollContainerType.Maps:
                    if (!view.MapContainer.IsHovered()) return;
                    ActiveScrollContainer.Value = SelectScrollContainerType.Mapsets;
                    break;
                case SelectScrollContainerType.Playlists: return;
            }
        }

        public void HandleBackAction()
        {
            switch (ActiveLeftPanel.Value)
            {
                case SelectContainerPanel.Leaderboard:
                case SelectContainerPanel.MapPreview:
                    if (ActiveScrollContainer.Value == SelectScrollContainerType.Maps) { ActiveScrollContainer.Value = SelectScrollContainerType.Mapsets; return; }
                    if (ActiveScrollContainer.Value == SelectScrollContainerType.Mapsets && ConfigManager.SelectGroupMapsetsBy.Value == GroupMapsetsBy.Playlists) { ActiveScrollContainer.Value = SelectScrollContainerType.Playlists; return; }
                    if (ActiveLeftPanel.Value == SelectContainerPanel.Leaderboard) ExitToMenu();
                    if (ActiveLeftPanel.Value == SelectContainerPanel.MapPreview) ActiveLeftPanel.Value = SelectContainerPanel.Leaderboard;
                    break;
                default: ActiveLeftPanel.Value = SelectContainerPanel.Leaderboard; break;
            }
        }

        public static float GetNextRate(bool faster, bool forceHalfRate = false)
        {
            var current = ModHelper.GetRateFromMods(ModManager.Mods);
            var adjustment = 0.1f;
            if (current < 1.0f || forceHalfRate || current == 1.0f && !faster) adjustment = 0.05f;
            var next = current + adjustment * (faster ? 1f : -1f);
            next = Math.Clamp(next, ModSpeed.MinSpeed, ModSpeed.MaxSpeed);
            return (float)Math.Round(next, 2);
        }

        private void FadeAudioTrackIn()
        {
            if (ConfigManager.VolumeMusic == null) return;
            if (AudioEngine.Track != null && AudioEngine.Track.IsPlaying) AudioEngine.Track?.Fade(100, 300);
        }

        private void ChangeScrollSpeed()
        {
            if (MapManager.Selected.Value == null) return;
            var scrollSpeed = ConfigManager.ScrollSpeeds[MapManager.Selected.Value.Mode];
            var changed = false;
            var speedIncrease = KeyboardManager.IsShiftDown() ? 1 : 10;
            if (KeyboardManager.IsUniqueKeyPress(Keys.F3)) { scrollSpeed.Value -= speedIncrease; changed = true; }
            else if (KeyboardManager.IsUniqueKeyPress(Keys.F4)) { scrollSpeed.Value += speedIncrease; changed = true; }
            if (changed) NotificationManager.Show(NotificationLevel.Info, $"Your {ModeHelper.ToShortHand(MapManager.Selected.Value.Mode)} scroll speed has been changed to: {scrollSpeed.Value / 10f:0.0}");
        }

        public void SelectPrevRandomMap()
        {
            if (AvailableMapsets.Value.Count == 0) return;
            if (RngHistory.Count == 0) return;
            var map = RngHistory.Pop();
            var index = AvailableMapsets.Value.FindIndex(0, AvailableMapsets.Value.Count, x => x.Maps.Contains(map));
            while (index == -1)
            {
                if (RngHistory.Count == 0) return;
                map = RngHistory.Pop();
                index = AvailableMapsets.Value.FindIndex(0, AvailableMapsets.Value.Count, x => x.Maps.Contains(map));
            }
            MapManager.Selected.Value = map;
            RandomMapsetSelected?.Invoke(this, new RandomMapsetSelectedEventArgs(map.Mapset, index));
        }

        public void SelectRandomMap()
        {
            if (AvailableMapsets.Value.Count == 0) return;
            ActiveScrollContainer.Value = SelectScrollContainerType.Mapsets;
            var index = Rng.Next(AvailableMapsets.Value.Count);
            var mapIndex = Rng.Next(AvailableMapsets.Value[index].Maps.Count);
            RngHistory.Push(MapManager.Selected.Value);
            MapManager.Selected.Value = AvailableMapsets.Value[index].Maps[mapIndex];
            RandomMapsetSelected?.Invoke(this, new RandomMapsetSelectedEventArgs(AvailableMapsets.Value[index], index));
        }

        public void ExitToGameplay()
        {
            if (MapManager.Selected.Value == null) return;
            if (OnlineManager.CurrentGame != null) { SelectMultiplayerMap(); return; }
            if (SkinManager.Skin.SoundSelect != null) { AudioEngine.Track.Pause(); SkinManager.Skin.SoundSelect.CreateChannel().Play(); }
            if (OnlineManager.IsSpectatingSomeone) OnlineManager.Client?.StopSpectating();
            if (ModManager.IsActivated(ModIdentifier.Coop)) { Exit(() => new TournamentScreen(2)); return; }
            Exit(() => new MapLoadingScreen(new List<Score>()));
        }

        public void ExitToMenu()
        {
            Exit(() => { if (IsMultiplayer) { OnlineManager.Client?.SetGameCurrentlySelectingMap(false); return new MultiplayerGameScreen(); } return new MainMenuScreen(); });
        }

        public void ExitToEditor()
        {
            if (MapManager.Selected.Value == null) return;
            if (AudioEngine.Track != null && !AudioEngine.Track.IsStopped) AudioEngine.Track.Stop();
            if (OnlineManager.CurrentGame != null) { NotificationManager.Show(NotificationLevel.Error, "You cannot use the editor while playing multiplayer."); return; }
            if (OnlineManager.IsSpectatingSomeone) OnlineManager.Client?.StopSpectating();
            try { IAudioTrack track; try { track = new AudioTrack(MapManager.GetAudioPath(MapManager.Selected.Value), false, false); } catch (Exception) { track = new AudioTrackVirtual(MapManager.Selected.Value.SongLength + 5000); } ModManager.RemoveAllMods(); Exit(() => new EditScreen(MapManager.Selected.Value, track)); }
            catch (Exception e) { Logger.Error(e, LogType.Runtime); NotificationManager.Show(NotificationLevel.Error, "Failed to load the editor with this map!"); }
        }

        private void SelectMultiplayerMap()
        {
            var map = MapManager.Selected.Value;
            if (!CheckMultiplayerDifficultyRange()) return;
            if (!CheckMultiplayerSongLength()) return;
            if (!CheckMultiplayerGameMode()) return;
            if (!CheckMultiplayerLongNotePercentage()) return;
            Transitioner.FadeIn();
            ThreadScheduler.Run(() => { OnlineManager.Client.ChangeMultiplayerGameMap(map.Md5Checksum, map.MapId, map.MapSetId, map.ToString(), (byte)map.Mode, map.DifficultyFromMods(ModManager.Mods), map.GetDifficultyRatings(), map.GetJudgementCount(), MapManager.Selected.Value.GetAlternativeMd5()); OnlineManager.Client.SetGameCurrentlySelectingMap(false); Exit(() => new MultiplayerGameScreen()); });
        }

        private bool CheckMultiplayerDifficultyRange()
        {
            var diff = MapManager.Selected.Value.DifficultyFromMods(ModManager.Mods);
            if (diff < OnlineManager.CurrentGame.MinimumDifficultyRating || diff > OnlineManager.CurrentGame.MaximumDifficultyRating) { NotificationManager.Show(NotificationLevel.Error, $"Difficulty rating must be between {OnlineManager.CurrentGame.MinimumDifficultyRating} and {OnlineManager.CurrentGame.MaximumDifficultyRating} for this multiplayer match!"); return false; }
            return true;
        }

        private bool CheckMultiplayerSongLength()
        {
            var length = MapManager.Selected.Value.SongLength * ModHelper.GetRateFromMods(ModManager.Mods) / 1000;
            if (length > OnlineManager.CurrentGame.MaximumSongLength) { NotificationManager.Show(NotificationLevel.Error, $"The maximum length allowed for this multiplayer match is: {OnlineManager.CurrentGame.MaximumSongLength} seconds"); return false; }
            return true;
        }

        private bool CheckMultiplayerGameMode()
        {
            if (!OnlineManager.CurrentGame.AllowedGameModes.Contains((byte)MapManager.Selected.Value.Mode)) { NotificationManager.Show(NotificationLevel.Error, "You cannot pick maps of this game mode in this multiplayer match!"); return false; }
            return true;
        }

        private bool CheckMultiplayerLongNotePercentage()
        {
            var map = MapManager.Selected.Value;
            if (map.LNPercentage < OnlineManager.CurrentGame.MinimumLongNotePercentage || map.LNPercentage > OnlineManager.CurrentGame.MaximumLongNotePercentage) { NotificationManager.Show(NotificationLevel.Error, $"You cannot select this map. The long note percentage must be between {OnlineManager.CurrentGame.MinimumLongNotePercentage}%-{OnlineManager.CurrentGame.MaximumLongNotePercentage}% for this multiplayer match."); return false; }
            return true;
        }

        public void ExportSelectedMapset()
        {
            if (MapManager.Selected.Value == null) return;
            if (IsExportingMapset) { NotificationManager.Show(NotificationLevel.Warning, "Slow down! You must wait for your previous mapset to export"); return; }
            IsExportingMapset = true;
            ThreadScheduler.Run(() => 
            { 
                NotificationManager.Show(NotificationLevel.Info, "Exporting mapset to zip archive. Please wait!"); 
                MapManager.Selected.Value.Mapset.ExportToZip(); 
                IsExportingMapset = false; 
                NotificationManager.Show(NotificationLevel.Success, $"Successfully exported {MapManager.Selected.Value.Mapset.Artist} - {MapManager.Selected.Value.Mapset.Title}!");
            });
        }

        private void SetRichPresence()
        {
            DiscordHelper.Presence.PartySize = 0; DiscordHelper.Presence.PartyMax = 0; DiscordHelper.Presence.StartTimestamp = 0; DiscordHelper.Presence.EndTimestamp = 0;
            DiscordHelper.Presence.LargeImageText = OnlineManager.GetRichPresenceLargeKeyText(ConfigManager.SelectedGameMode.Value);
            DiscordHelper.Presence.SmallImageKey = ModeHelper.ToShortHand(ConfigManager.SelectedGameMode.Value).ToLower();
            DiscordHelper.Presence.SmallImageText = ModeHelper.ToLongHand(ConfigManager.SelectedGameMode.Value);
            Helpers.RichPresenceHelper.UpdateRichPresence("In the menus", "Selecting a song");
        }

        private void OnMapsetDeleted(object sender, MapsetDeletedEventArgs e)
        {
            ThreadScheduler.Run(() => { var index = 0; if (e.Index == -1) index = 0; if (e.Index - 1 >= 0) index = e.Index - 1; lock (AvailableMapsets.Value) AvailableMapsets.Value = MapsetHelper.FilterMapsets(CurrentSearchQuery); if (index >= 0 && index < AvailableMapsets.Value.Count) { MapManager.SelectMapFromMapset(AvailableMapsets.Value[index]); return; } lock (AudioEngine.Track) { if (AudioEngine.Track.IsDisposed && !AudioEngine.Track.IsStopped) AudioEngine.Track.Dispose(); } });
        }

        private void OnMapDeleted(object sender, MapDeletedEventArgs e)
        {
            ThreadScheduler.Run(() => { lock (AvailableMapsets.Value) AvailableMapsets.Value = MapsetHelper.FilterMapsets(CurrentSearchQuery); var mapsetIndex = AvailableMapsets.Value.FindIndex(x => x.Maps.Contains(e.Map)); if (mapsetIndex == -1 && AvailableMapsets.Value.Count != 0) { MapManager.SelectMapFromMapset(AvailableMapsets.Value.First()); return; } lock (AudioEngine.Track) { if (AudioEngine.Track.IsDisposed && !AudioEngine.Track.IsStopped) AudioEngine.Track.Dispose(); } });
        }

        private void OnMapUpdated(object sender, MapUpdatedEventArgs e) => AvailableMapsets.Value = MapsetHelper.FilterMapsets(CurrentSearchQuery);

        private void OnSongRequestPlayed(object sender, SongRequestPlayedEventArgs e)
        {
            ThreadScheduler.Run(() => { MapManager.Selected.Value = e.Map; lock (AvailableMapsets.Value) AvailableMapsets.Value = MapsetHelper.FilterMapsets(CurrentSearchQuery); });
        }

        private void OnAutoLoadOsuBeatmapsChanged(object sender, BindableValueChangedEventArgs<bool> e) => Exit(() => new ImportingScreen(null, true));

        public override UserClientStatus GetClientStatus() => new UserClientStatus(ClientStatus.Selecting, -1, "", 0, "", 0);
    }
}
