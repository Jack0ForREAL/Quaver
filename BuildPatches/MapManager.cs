/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 * Copyright (c) Swan & The Quaver Team <support@quavergame.com>.
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Emik;
using Microsoft.Xna.Framework.Graphics;
using Quaver.API.Enums;
using Quaver.API.Maps;
using Quaver.API.Maps.Parsers;
using Quaver.Server.Client;
using Quaver.Server.Client.Objects.Twitch;
using Quaver.Shared.Assets;
using Quaver.Shared.Audio;
using Quaver.Shared.Config;
using Quaver.Shared.Database.Playlists;
using Quaver.Shared.Graphics;
using Quaver.Shared.Graphics.Backgrounds;
using Quaver.Shared.Graphics.Notifications;
using Quaver.Shared.Helpers;
using Quaver.Shared.Modifiers;
using Quaver.Shared.Online.API.Maps;
using Quaver.Shared.Screens.Selection.UI.Maps;
using RestSharp;
using RestSharp.Extensions;
using Wobble.Audio.Tracks;
using Wobble.Bindables;
using Wobble.Graphics.UI.Dialogs;
using Wobble.Logging;

namespace Quaver.Shared.Database.Maps
{
    public static class MapManager
    {
        public static Bindable<Map> Selected { get; set; } = new Bindable<Map>(null);
        public static List<Mapset> Mapsets { get; set; } = new List<Mapset>();
        public static List<Map> RecentlyPlayed { get; set; } = new List<Map>();
        public static string OsuSongsFolder { get; set; }
        public static string CurrentAudioPath => GetAudioPath(Selected?.Value);
        public static Texture2D CurrentBackground { get; set; }
        public static string CurrentBackgroundPath => GetBackgroundPath(Selected.Value);

        public static event EventHandler<MapsetDeletedEventArgs> MapsetDeleted;
        public static event EventHandler<MapDeletedEventArgs> MapDeleted;
        public static event EventHandler<MapUpdatedEventArgs> MapUpdated;
        public static event EventHandler<SongRequestPlayedEventArgs> SongRequestPlayed;

        public static void SelectMapFromMapset(Mapset mapset)
        {
            Map Select(List<Map> maps, ModIdentifier mods)
            {
                if (maps.Count == 0) return null;
                var minDelta = Double.PositiveInfinity;
                Map selection = null;
                foreach (var map in maps)
                {
                    var target = ConfigManager.PrioritizedMapDifficulty[map.Mode].Value / 10d;
                    double delta = Math.Abs(map.DifficultyFromMods(mods) - target);
                    if (delta < minDelta)
                    {
                        selection = map;
                        minDelta = delta;
                    }
                }
                return selection;
            }
            List<Map> prioritized = mapset.Maps.FindAll(x => x.Mode == ConfigManager.PrioritizedGameMode.Value);
            Selected.Value = Select(prioritized, ModManager.Mods) ?? Select(mapset.Maps, ModManager.Mods);
        }

        public static string GetBackgroundPath(Map map)
        {
            if (map == null) return "";
            switch (map.Game)
            {
                case MapGame.Osu:
                    var osu = new OsuBeatmap(OsuSongsFolder + map.Directory + "/" + map.Path);
                    return $@"{OsuSongsFolder}/{map.Directory}/{osu.Background}";
                case MapGame.Quaver:
                    return ConfigManager.SongDirectory + "/" + map.Directory + "/" + map.BackgroundPath;
                case MapGame.Etterna:
                    return map.BackgroundPath;
                default:
                    return "";
            }
        }

        public static string GetBannerPath(Map map)
        {
            if (map == null) return "";
            switch (map.Game)
            {
                case MapGame.Osu: return "";
                case MapGame.Quaver: return ConfigManager.SongDirectory + "/" + map.Directory + "/" + map.BannerPath;
                case MapGame.Etterna: return map.BannerPath;
                default: return "";
            }
        }

        public static string GetMapsetBannerPath(Mapset mapset)
        {
            var map = mapset.Maps.First();
            switch (map.Game)
            {
                case MapGame.Osu: return "";
                case MapGame.Quaver: return (ConfigManager.SongDirectory + "/" + map.Directory + "/" + map.BannerPath).Replace("\\", "/");
                case MapGame.Etterna: return map.BannerPath;
                default: return "";
            }
        }

        public static string GetAudioPath(Map map)
        {
            if (map == null) return "";
            switch (map.Game)
            {
                case MapGame.Osu: return OsuSongsFolder + "/" + map.Directory + "/" + map.AudioPath;
                case MapGame.Quaver: return ConfigManager.SongDirectory + "/" + map.Directory + "/" + map.AudioPath;
                case MapGame.Etterna: return map.AudioPath;
                default: return "";
            }
        }

        public static Map FindMapFromMd5(string md5)
        {
            foreach (var set in Mapsets)
            {
                var found = set.Maps.Find(x => x.Md5Checksum == md5);
                if (found != null) return found;
            }
            return null;
        }

        public static Map FindMapFromOnlineId(int id)
        {
            foreach (var set in Mapsets)
            {
                var found = set.Maps.Find(x => x.MapId == id);
                if (found != null) return found;
            }
            return null;
        }

        public static string GetCustomAudioSamplePath(Map map, string samplePath)
        {
            switch (map.Game)
            {
                case MapGame.Osu: return OsuSongsFolder + "/" + map.Directory + "/" + samplePath;
                case MapGame.Quaver: return ConfigManager.SongDirectory + "/" + map.Directory + "/" + samplePath;
                default: return "";
            }
        }

        public static void ViewOnlineListing(Map map = null)
        {
            if (map == null) map = Selected.Value;
            if (map == null) return;
            if (map.MapId == -1) { NotificationManager.Show(NotificationLevel.Error, "This map is not submitted online!"); return; }
            BrowserHelper.OpenURL($"https://quavergame.com/mapsets/map/{map.MapId}");
        }

        public static void Delete(Map map, int index)
        {
            if (map.Game != MapGame.Quaver) { NotificationManager.Show(NotificationLevel.Error, "You cannot delete a map loaded from another game"); return; }
            try
            {
                var mapsetPath = Path.Combine(ConfigManager.SongDirectory.Value, map.Mapset.Directory);
                var path = Path.Combine(mapsetPath, map.Path);
                if (Rubbish.Move(path)) MapDatabaseCache.RemoveMap(map);
                else
                {
                    ShowFallbackMapDeletionDialog("map", () =>
                    {
                        try { File.Delete(path); } catch (Exception e) { Logger.Error(e, LogType.Runtime); }
                        if (File.Exists(path)) { NotificationManager.Show(NotificationLevel.Error, "Unable to delete the map. Is the file protected?"); return; }
                        MapDatabaseCache.RemoveMap(map);
                        map.Mapset.Maps.Remove(map);
                        if (map.Mapset.Maps.Count == 0) Mapsets.Remove(map.Mapset);
                        PlaylistManager.RemoveMapFromAllPlaylists(map);
                        MapDeleted?.Invoke(typeof(MapManager), new MapDeletedEventArgs(map, index));
                    });
                    return;
                }
            }
            catch (Exception e) { Logger.Error(e, LogType.Runtime); }
            map.Mapset.Maps.Remove(map);
            if (map.Mapset.Maps.Count == 0) Mapsets.Remove(map.Mapset);
            PlaylistManager.RemoveMapFromAllPlaylists(map);
            MapDeleted?.Invoke(typeof(MapManager), new MapDeletedEventArgs(map, index));
        }

        public static void Delete(Mapset mapset, int index)
        {
            if (mapset.Maps.Count == 0) return;
            if (mapset.Maps.First().Game != MapGame.Quaver) { NotificationManager.Show(NotificationLevel.Error, "You cannot delete a mapset loaded from another game"); return; }
            if (mapset.Maps.Contains(Selected.Value))
            {
                var oldTrack = AudioEngine.Track;
                AudioEngine.Track = new AudioTrackVirtual(300000);
                if (oldTrack != null && !oldTrack.IsDisposed) oldTrack.Dispose();
            }
            try
            {
                var directory = Path.Combine(ConfigManager.SongDirectory.Value, mapset.Directory);
                if (!Rubbish.Move(directory) && Directory.Exists(directory))
                {
                    ShowFallbackMapDeletionDialog("mapset", () =>
                    {
                        try { Directory.Delete(directory, true); } catch (Exception e) { Logger.Error(e, LogType.Runtime); }
                        if (Directory.Exists(directory)) { NotificationManager.Show(NotificationLevel.Error, "Unable to delete the mapset. Is the directory protected?"); return; }
                        try { mapset.Maps.ForEach(MapDatabaseCache.RemoveMap); } catch (Exception e) { Logger.Error(e, LogType.Runtime); }
                        Mapsets.Remove(mapset);
                        MapsetDeleted?.Invoke(typeof(MapManager), new MapsetDeletedEventArgs(mapset, index));
                        lock (BackgroundHelper.MapsetBanners)
                        {
                            if (!BackgroundHelper.MapsetBanners.ContainsKey(mapset.Directory)) return;
                            var banner = BackgroundHelper.MapsetBanners[mapset.Directory];
                            if (banner != UserInterface.DefaultBanner) banner.Dispose();
                            BackgroundHelper.MapsetBanners.Remove(mapset.Directory);
                        }
                    });
                    return;
                }
            }
            catch (Exception e) { Logger.Error(e, LogType.Runtime); }
            try { mapset.Maps.ForEach(MapDatabaseCache.RemoveMap); } catch (Exception e) { Logger.Error(e, LogType.Runtime); }
            Mapsets.Remove(mapset);
            // FIX IS HERE: Removing the double typeof
            MapsetDeleted?.Invoke(typeof(MapManager), new MapsetDeletedEventArgs(mapset, index));
            lock (BackgroundHelper.MapsetBanners)
            {
                if (!BackgroundHelper.MapsetBanners.ContainsKey(mapset.Directory)) return;
                var banner = BackgroundHelper.MapsetBanners[mapset.Directory];
                if (banner != UserInterface.DefaultBanner) banner.Dispose();
                BackgroundHelper.MapsetBanners.Remove(mapset.Directory);
            }
        }

        public static void UpdateMapToLatestVersion(Map outdated)
        {
            try
            {
                var lookup = new APIRequestMapInformation(outdated.MapId).ExecuteRequest();
                if (lookup.Status != 200) throw new Exception($"Map updated failed. APIRequestMapInformation failed with status: {lookup.Status}");
                foreach (var mapset in Mapsets)
                {
                    var foundMap = mapset.Maps.Find(x => x.Md5Checksum == lookup.Map.Md5);
                    if (foundMap == null) continue;
                    outdated.Mapset.Maps.Remove(outdated);
                    MapDatabaseCache.RemoveMap(outdated);
                    if (outdated.Mapset.Maps.Count == 0) Mapsets.Remove(outdated.Mapset);
                    if (Selected.Value == outdated) Selected.Value = foundMap;
                    PlaylistManager.UpdateMapInPlaylists(outdated, foundMap);
                    MapUpdated?.Invoke(typeof(MapManager), new MapUpdatedEventArgs(outdated, foundMap));
                    return;
                }
                var path = $"{ConfigManager.SongDirectory.Value}/{outdated.Directory}/{outdated.Path}";
                Logger.Important($"Downloading latest version of map: {outdated.Id}", LogType.Runtime);
                var client = new RestClient($"{OnlineClient.API_ENDPOINT}");
                client.DownloadData(new RestRequest($"{OnlineClient.API_ENDPOINT}/d/web/map/{outdated.MapId}", Method.GET)).SaveAs(path);
                Logger.Important($"Successfully downloaded latest version of map: {outdated.Id}", LogType.Runtime);
                var newMap = Map.FromQua(Qua.Parse(path), path);
                newMap.CalculateDifficulties();
                newMap.Id = outdated.Id;
                newMap.Mapset = outdated.Mapset;
                newMap.Directory = outdated.Directory;
                newMap.Path = outdated.Path;
                newMap.DateAdded = outdated.DateAdded;
                newMap.TimesPlayed = outdated.TimesPlayed;
                newMap.LocalOffset = outdated.LocalOffset;
                MapDatabaseCache.UpdateMap(outdated);
                outdated.Mapset.Maps.Remove(outdated);
                outdated.Mapset.Maps.Add(newMap);
                outdated.Mapset.Maps = newMap.Mapset.Maps.OrderBy(x => x.DifficultyFromMods(ModManager.Mods)).ToList();
                if (Selected.Value == outdated) Selected.Value = newMap;
                PlaylistManager.UpdateMapInPlaylists(outdated, newMap);
                MapUpdated?.Invoke(typeof(MapManager), new MapUpdatedEventArgs(outdated, newMap));
            }
            catch (Exception e)
            {
                Logger.Error(e, LogType.Runtime);
                MapUpdated?.Invoke(typeof(MapManager), new MapUpdatedEventArgs(outdated, outdated));
            }
        }

        public static void PlaySongRequest(SongRequest request, Map map) => SongRequestPlayed?.Invoke(typeof(MapManager), new SongRequestPlayedEventArgs(request, map));

        private static void ShowFallbackMapDeletionDialog(string label, Action onYes) => DialogManager.Show(new YesNoDialog("Map Deletion", $"Failed to move the {label} in the recycle bin.\nWould you like to delete it instead?", onYes));

        // --- NEW FEATURE: SMART RELOAD ---
        public static bool ReloadNewMapsets()
        {
            var loadedCount = 0;
            var songDir = ConfigManager.SongDirectory.Value;
            
            if (string.IsNullOrEmpty(songDir) || !Directory.Exists(songDir)) 
                return false;

            // 1. Check for Archives (files to import via ImportingScreen)
            var files = Directory.GetFiles(songDir);
            foreach (var file in files)
            {
                if (file.EndsWith(".qp") || file.EndsWith(".osz") || file.EndsWith(".sm"))
                {
                    if (!MapsetImporter.Queue.Contains(file))
                        MapsetImporter.Queue.Add(file);
                }
            }

            // 2. Check for New Folders (Instant Load)
            var directories = Directory.GetDirectories(songDir);
            var loadedDirNames = new HashSet<string>(Mapsets.Select(x => x.Directory));

            foreach (var dir in directories)
            {
                var dirName = new DirectoryInfo(dir).Name;
                if (loadedDirNames.Contains(dirName)) 
                    continue;

                // Found a new folder! Load all .qua files inside.
                var quaFiles = Directory.GetFiles(dir, "*.qua", SearchOption.AllDirectories);
                if (quaFiles.Length > 0)
                {
                    try 
                    {
                        foreach (var quaPath in quaFiles)
                        {
                            var map = Map.FromQua(Qua.Parse(quaPath), quaPath);
                            map.CalculateDifficulties();
                            MapDatabaseCache.InsertMap(map); // Direct insert
                        }
                        loadedCount++;
                    }
                    catch (Exception e)
                    {
                        Logger.Error($"SmartLoad Failed for {dirName}: {e.Message}", LogType.Runtime);
                    }
                }
            }

            if (loadedCount > 0)
            {
                // Re-sort the list so the new map shows up
                MapDatabaseCache.OrderAndSetMapsets(true);
                return true;
            }

            return MapsetImporter.Queue.Count > 0;
        }
    }
}
