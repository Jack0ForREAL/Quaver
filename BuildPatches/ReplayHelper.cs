using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using Quaver.API.Enums;
using Quaver.API.Maps;
using Quaver.API.Maps.Processors.Scoring;
using Quaver.API.Maps.Processors.Scoring.Data;
using Quaver.API.Replays;
using Quaver.Shared.Config;
using Wobble.Logging;

namespace Quaver.Shared.Helpers
{
    public static class ReplayHelper
    {
        internal static Replay GeneratePerfectReplay(Qua map, string md5)
        {
            var replay = new Replay(map.Mode, "Autoplay", Quaver.Shared.Modifiers.ModManager.Mods, md5);
            if (Quaver.API.Helpers.ModeHelper.IsKeyMode(map.Mode)) return Replay.GeneratePerfectReplayKeys(replay, map);
            throw new ArgumentOutOfRangeException();
        }

        public static void ExportDetailedAnalysis(List<HitStat> stats, Qua map, ScoreProcessor processor = null, string filename = "last_play_analysis.txt")
        {
            try
            {
                var sb = new StringBuilder();
                var inv = CultureInfo.InvariantCulture;

                // 1. HEADER: Identity & Metadata
                sb.AppendLine($"Map: {map.Artist} - {map.Title} [{map.DifficultyName}]");
                sb.AppendLine($"Mode: {map.Mode} | BPM: {map.GetCommonBpm().ToString("F1", inv)} | Notes: {map.HitObjects.Count}");

                int scrollSpeed = 0;
                string scrollDir = "Down";
                try
                {
                    if (map.Mode == GameMode.Keys4) { scrollSpeed = ConfigManager.ScrollSpeed4K?.Value ?? 0; scrollDir = ConfigManager.ScrollDirection4K?.Value.ToString() ?? "Down"; }
                    else if (map.Mode == GameMode.Keys7) { scrollSpeed = ConfigManager.ScrollSpeed7K?.Value ?? 0; scrollDir = ConfigManager.ScrollDirection7K?.Value.ToString() ?? "Down"; }
                } catch { }

                double visMs = scrollSpeed > 0 ? 610000.0 / scrollSpeed : 0;
                sb.AppendLine($"ScrollSpeed: {scrollSpeed} | ScrollDirection: {scrollDir} | NoteVisibilityWindowMs: {visMs.ToString("F1", inv)}");

                // 2. HEADER: Judgments & Windows
                if (processor != null)
                {
                    try {
                        var w = processor.JudgementWindow;
                        if (w != null) sb.AppendLine($"HitWindowsMs: Marv:{w[Judgement.Marv].ToString("F1", inv)} Perf:{w[Judgement.Perf].ToString("F1", inv)} Great:{w[Judgement.Great].ToString("F1", inv)} Good:{w[Judgement.Good].ToString("F1", inv)} Okay:{w[Judgement.Okay].ToString("F1", inv)} Miss:{w[Judgement.Miss].ToString("F1", inv)}");
                    } catch { }
                    try {
                        var j = processor.CurrentJudgements;
                        sb.AppendLine($"Judgments: Marv:{j[Judgement.Marv]} Perf:{j[Judgement.Perf]} Great:{j[Judgement.Great]} Good:{j[Judgement.Good]} Okay:{j[Judgement.Okay]} Miss:{j[Judgement.Miss]}");
                        sb.AppendLine($"Accuracy: {processor.Accuracy.ToString("F2", inv)}% | Score: {processor.Score} | MaxCombo: {processor.MaxCombo}");
                    } catch { }
                }

                var hitDiffs = stats.Where(s => s.Judgement != Judgement.Miss).Select(s => (double)s.HitDifference).ToList();
                if (hitDiffs.Count > 0)
                {
                    double mean = hitDiffs.Average();
                    double std = Math.Sqrt(hitDiffs.Select(x => (x - mean) * (x - mean)).Average());
                    sb.AppendLine($"MeanErrorMs: {mean.ToString("F2", inv)} | StdDevMs: {std.ToString("F2", inv)}");
                }

                sb.AppendLine($"PatternProfile: {ComputePatternProfile(map)}");
                sb.AppendLine();

                // 3. SEGMENTS: 10s LLM Context Blocks
                sb.AppendLine("=== SEGMENTS ===");
                AppendSegments(sb, stats, map, inv);
                sb.AppendLine();

                // 4. NOTES: Compressed note trace
                sb.AppendLine("=== NOTES ===");
                sb.AppendLine("# FORMAT: Time:LaneType+Diff (N=Normal H=LN-Head T=LN-Tail M=Miss)");

                var expected = new List<(float Time, int Lane, KeyPressType Type, bool IsLN)>();
                foreach (var note in map.HitObjects)
                {
                    expected.Add((note.StartTime, note.Lane, KeyPressType.Press, note.IsLongNote));
                    if (note.IsLongNote) expected.Add((note.EndTime, note.Lane, KeyPressType.Release, true));
                }
                
                expected = expected.OrderBy(x => x.Time).ThenBy(x => x.Lane).ThenBy(x => x.Type == KeyPressType.Press ? 0 : 1).ToList();

                var notesList = new List<string>();
                int count = Math.Min(stats.Count, expected.Count);
                for (int i = 0; i < count; i++)
                {
                    var exp = expected[i];
                    char typeChar = exp.Type == KeyPressType.Press ? (exp.IsLN ? 'H' : 'N') : 'T';
                    string diffStr = stats[i].Judgement == Judgement.Miss ? "M" : $"{stats[i].HitDifference:+#0;-#0;0}";
                    notesList.Add($"{exp.Time:F0}:{exp.Lane}{typeChar}{diffStr}");
                }

                for (int i = 0; i < notesList.Count; i += 15) sb.AppendLine(string.Join(" ", notesList.Skip(i).Take(15)));

                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, filename);
                File.WriteAllText(path, sb.ToString());
                Logger.Log($"[ReplayHelper] Saved LLM-optimized analysis to: {path}", LogLevel.Important, LogType.Runtime);
            }
            catch (Exception e) { Logger.Log($"[ReplayHelper] Error saving analysis: {e.Message}", LogLevel.Important, LogType.Runtime); }
        }

        private static void AppendSegments(StringBuilder sb, List<HitStat> stats, Qua map, CultureInfo inv)
        {
            if (map.HitObjects.Count == 0 || stats.Count == 0) return;
            const int segMs = 10000;
            double totalMs = map.HitObjects[map.HitObjects.Count - 1].StartTime;
            int numSegs = (int)(totalMs / segMs) + 1;

            var noteTimes = map.HitObjects.OrderBy(n => n.StartTime).Select(n => (float)n.StartTime).ToList();
            int n = Math.Min(stats.Count, noteTimes.Count);

            for (int s = 0; s < numSegs; s++)
            {
                double lo = s * segMs;
                double hi = lo + segMs;
                var bucketDiffs = new List<int>();
                int bucketNotes = 0, bucketMiss = 0;
                var bucketObjects = new List<Quaver.API.Maps.Structures.HitObjectInfo>();
                
                for (int i = 0; i < n; i++)
                {
                    if (noteTimes[i] < lo || noteTimes[i] >= hi) continue;
                    bucketNotes++;
                    bucketObjects.Add(map.HitObjects[i]);
                    if (stats[i].Judgement == Judgement.Miss) bucketMiss++;
                    else bucketDiffs.Add(stats[i].HitDifference);
                }
                
                if (bucketNotes == 0) continue;
                double acc = 100.0 * (bucketNotes - bucketMiss) / bucketNotes;
                double mean = bucketDiffs.Count > 0 ? bucketDiffs.Average() : 0;
                double std = bucketDiffs.Count > 0 ? Math.Sqrt(bucketDiffs.Select(x => (x - mean) * (x - mean)).Average()) : 0;
                
                sb.AppendLine($"S{s:D2} [{(int)(lo/1000)}-{(int)(hi/1000)}s] Acc:{acc.ToString("F1", inv)} Mean:{mean.ToString("+#0.0;-#0.0;0.0", inv)} Std:{std.ToString("F1", inv)} Notes:{bucketNotes} Pattern:{DominantPatternTag(bucketObjects)}");
            }
        }

        private static string DominantPatternTag(List<Quaver.API.Maps.Structures.HitObjectInfo> notes)
        {
            if (notes.Count == 0) return "empty";
            int singles=0, jumps=0, hands=0, quads=0, jacks=0, bursts=0;
            
            foreach (var g in notes.GroupBy(x => x.StartTime)) {
                var c = g.Count();
                if (c <= 1) singles++; else if (c == 2) jumps++; else if (c == 3) hands++; else quads++;
            }

            foreach (var col in notes.GroupBy(x => x.Lane)) {
                var arr = col.OrderBy(x => x.StartTime).ToList();
                int run = 1;
                for (int i = 1; i < arr.Count; i++) {
                    if (arr[i].StartTime - arr[i-1].StartTime <= 250) run++;
                    else { if (run >= 3) jacks++; run = 1; }
                }
                if (run >= 3) jacks++;
            }

            var ordered = notes.OrderBy(x => x.StartTime).ToList();
            int left = 0;
            for (int right = 0; right < ordered.Count; right++) {
                while (ordered[right].StartTime - ordered[left].StartTime > 500) left++;
                if (right - left + 1 >= 10) { bursts++; break; }
            }

            var parts = new List<string>();
            int total = singles + jumps + hands + quads;
            if (total > 0) {
                if (jumps + hands + quads > singles) parts.Add("jumpstream"); else parts.Add("singles");
                if (hands + quads > total * 0.15) parts.Add("chord-heavy");
            }
            if (jacks >= 2) parts.Add("jacks");
            if (bursts > 0) parts.Add("bursts");
            return parts.Count > 0 ? string.Join(",", parts) : "mixed";
        }

        private static string ComputePatternProfile(Qua map)
        {
            var notes = map.HitObjects.OrderBy(x => x.StartTime).ToList();
            if (notes.Count == 0) return "empty";
            int singles=0, jumps=0, hands=0, quads=0;
            
            foreach (var g in notes.GroupBy(x => x.StartTime)) {
                var c = g.Count();
                if (c <= 1) singles++; else if (c == 2) jumps++; else if (c == 3) hands++; else quads++;
            }
            int total = Math.Max(1, singles + jumps + hands + quads);
            
            var inv = CultureInfo.InvariantCulture;
            string r(int v) => ((double)v / total).ToString("F2", inv);
            int keys = map.Mode == GameMode.Keys7 ? 7 : 4;
            double mid = (keys - 1) / 2.0;
            int leftN = notes.Count(x => x.Lane - 1 < mid);
            int rightN = notes.Count(x => x.Lane - 1 > mid);
            double bias = (rightN - leftN) / (double)Math.Max(1, leftN + rightN);
            string hand = bias > 0.10 ? "RH-heavy" : bias < -0.10 ? "LH-heavy" : "Balanced";

            return $"ChordDensity{{S:{r(singles)},J:{r(jumps)},H:{r(hands)},Q:{r(quads)}}} Hand:{hand}"; 
        }
    }
}
