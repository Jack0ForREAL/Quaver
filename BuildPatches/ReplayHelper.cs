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

namespace Quaver.Shared.Helpers
{
    public static class ReplayHelper
    {
        internal static Replay GeneratePerfectReplay(Qua map, string md5)
        {
            var replay = new Replay(map.Mode, "Autoplay", ModManager.Mods, md5);
            if (ModeHelper.IsKeyMode(map.Mode))
                return Replay.GeneratePerfectReplayKeys(replay, map);
            throw new ArgumentOutOfRangeException();
        }

        public static void ExportReplayToText(Replay replay, string filename = "last_replay_debug.txt")
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("=== QUAVER REPLAY DEBUG EXPORT ===");
                sb.AppendLine($"Player: {replay.PlayerName}");
                sb.AppendLine($"Map Hash: {replay.MapMd5}");
                sb.AppendLine($"Score: {replay.Score}");
                sb.AppendLine("TIME (ms) | KEYS");
                foreach (var frame in replay.Frames)
                {
                    sb.AppendLine($"{frame.Time.ToString().PadRight(9)} | {frame.Keys}");
                }
                var path = Path.Combine(ConfigManager.DataDirectory.Value, "Replays", filename);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, sb.ToString());
            }
            catch {}
        }
    }
}
