using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework.Graphics;
using Quaver.API.Enums;
using Quaver.API.Maps;
using Quaver.API.Maps.Parsers;
using Quaver.Server.Client;
using Quaver.Shared.Config;
using Quaver.Shared.Database.Playlists;
using Quaver.Shared.Graphics.Notifications;
using Quaver.Shared.Helpers;
using Quaver.Shared.Modifiers;
using Wobble.Bindables;
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
                    if (delta < minDelta) { selection = map; minDelta = delta; }
                }
                return selection;
            }
            List<Map> prioritized = mapset.Maps.FindAll(x => x.Mode == ConfigManager.PrioritizedGameMode.Value);
            Selected.Value = Select(prioritized, ModManager.Mods) ?? Select(mapset.Maps, ModManager.Mods);
        }

        public static string GetBackgroundPath(Map map)
        {
            if (map == null) return "";
            if (map.Game == MapGame.Osu) return $@"{OsuSongsFolder}/{map.Directory}/{map.BackgroundPath}";
            return ConfigManager.SongDirectory + "/" + map.Directory + "/" + map.BackgroundPath;
        }

        public static string GetAudioPath(Map map)
        {
            if (map == null) return "";
            if (map.Game == MapGame.Osu) return OsuSongsFolder + "/" + map.Directory + "/" + map.AudioPath;
            return ConfigManager.SongDirectory + "/" + map.Directory + "/" + map.AudioPath;
        }

        public static Map FindMapFromMd5(string md5)
        {
            foreach (var set in Mapsets) { var found = set.Maps.Find(x => x.Md5Checksum == md5); if (found != null) return found; }
            return null;
        }

        public static void Delete(Mapset mapset, int index)
        {
            try {
                var directory = Path.Combine(ConfigManager.SongDirectory.Value, mapset.Directory);
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
                mapset.Maps.ForEach(MapDatabaseCache.RemoveMap);
                Mapsets.Remove(mapset);
                MapsetDeleted?.Invoke(typeof(MapManager), new MapsetDeletedEventArgs(mapset, index));
            } catch (Exception e) { Logger.Error(e, LogType.Runtime); }
        }

        // --- THE NEW SMART REFRESH FUNCTION ---
        public static bool ReloadNewMapsets()
        {
            var loadedCount = 0;
            var songDir = ConfigManager.SongDirectory.Value;
            if (string.IsNullOrEmpty(songDir) || !Directory.Exists(songDir)) return false;

            // Check for Archive files first (.osz, .qp)
            var files = Directory.GetFiles(songDir);
            foreach (var file in files) {
                if (file.EndsWith(".qp") || file.EndsWith(".osz")) {
                    if (!MapsetImporter.Queue.Contains(file)) MapsetImporter.Queue.Add(file);
                }
            }

            // Check for Folders that aren't loaded
            var directories = Directory.GetDirectories(songDir);
            var loadedDirNames = new HashSet<string>(Mapsets.Select(x => x.Directory));

            foreach (var dir in directories) {
                var dirName = new DirectoryInfo(dir).Name;
                if (loadedDirNames.Contains(dirName)) continue;

                var quaFiles = Directory.GetFiles(dir, "*.qua", SearchOption.AllDirectories);
                if (quaFiles.Length > 0) {
                    try {
                        foreach (var quaPath in quaFiles) {
                            var map = Map.FromQua(Qua.Parse(quaPath), quaPath);
                            map.CalculateDifficulties();
                            MapDatabaseCache.InsertMap(map);
                        }
                        loadedCount++;
                    } catch (Exception e) { Logger.Error($"SmartLoad Failed: {e.Message}", LogType.Runtime); }
                }
            }

            if (loadedCount > 0) {
                MapDatabaseCache.OrderAndSetMapsets(true);
                return true;
            }
            return MapsetImporter.Queue.Count > 0;
        }
    }
}
