/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 * Copyright (c) Swan & The Quaver Team <support@quavergame.com>.
*/

using System;
using System.IO;
using System.Text;
using System.Linq;
using Quaver.API.Enums;
using Quaver.API.Helpers;
using Quaver.API.Maps;
using Quaver.API.Replays;
using Quaver.Shared.Config;
using Quaver.Shared.Modifiers;
using Wobble.Logging;

namespace Quaver.Shared.Helpers
{
    public static class ReplayHelper
    {
        /// <summary>
        ///     Generates a perfect replay given the game mode.
        /// </summary>
        internal static Replay GeneratePerfectReplay(Qua map, string md5)
        {
            var replay = new Replay(map.Mode, "Autoplay", ModManager.Mods, md5);

            if (ModeHelper.IsKeyMode(map.Mode))
                replay = Replay.GeneratePerfectReplayKeys(replay, map);
            else
                throw new ArgumentOutOfRangeException();

            return replay;
        }

        /// <summary>
        ///     CUSTOM: Saves the replay as a readable TXT file for debugging/analysis.
        /// </summary>
        public static void ExportReplayToText(Replay replay, string filename = "last_replay_debug.txt")
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("=== QUAVER REPLAY DEBUG EXPORT ===");
                sb.AppendLine($"Player: {replay.PlayerName}");
                sb.AppendLine($"Date: {replay.Date}");
                sb.AppendLine($"Map Hash: {replay.MapMd5}");
                sb.AppendLine($"Score: {replay.Score}");
                sb.AppendLine($"Max Combo: {replay.MaxCombo}");
                sb.AppendLine($"Mods: {replay.Mods}");
                sb.AppendLine($"Mode: {replay.Mode}");
                sb.AppendLine("----------------------------------");
                sb.AppendLine("TIME (ms) | KEYS PRESSED");
                sb.AppendLine("----------------------------------");

                foreach (var frame in replay.Frames)
                {
                    // Convert the key state enum to a readable string (e.g., "K1 | K2")
                    var keys = frame.Keys.ToString(); 
                    sb.AppendLine($"{frame.Time.ToString().PadRight(9)} | {keys}");
                }

                // Path to save
                var path = Path.Combine(ConfigManager.DataDirectory.Value, "Replays", filename);
                
                // Ensure directory exists
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                
                File.WriteAllText(path, sb.ToString());
                
                // FIX: Using Logger.Important which takes (string, LogType)
                Logger.Important($"[ReplayHelper] Saved text replay to: {path}", LogType.Runtime);
            }
            catch (Exception ex)
            {
                Logger.Error($"[ReplayHelper] Failed to export text replay: {ex.Message}", LogType.Runtime);
            }
        }
    }
}
