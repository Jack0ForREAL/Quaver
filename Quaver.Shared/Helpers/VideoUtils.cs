using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using Quaver.Shared.Graphics.Notifications;

namespace Quaver.Shared.Helpers
{
    public static class VideoUtils
    {
        public static string FFmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, 
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ffmpeg.exe" : "ffmpeg");
        //  HARDWARE DETECTION 
        public static int GetCpuThreads() => Environment.ProcessorCount;

        public static int GetTotalRamMB()
        {
            try
            {
                var gcInfo = GC.GetGCMemoryInfo();
                long totalBytes = gcInfo.TotalAvailableMemoryBytes; 
                
                // If it returns 0 (older .NET), fallback to a safe 8GB default
                if (totalBytes == 0) return 8192;

                return (int)(totalBytes / 1024 / 1024);
            }
            catch
            {
                return 8192; // Fallback if detection fails
            }
        }
        // --------------------------

        public static async Task<bool> CheckOrDownloadFFmpeg()
        {
            if (File.Exists(FFmpegPath) || (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && CheckLinuxFFmpeg())) 
                return true;

            // Only auto-download on Windows for now
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return false;

            try
            {
                NotificationManager.Show(NotificationLevel.Info, "Downloading FFmpeg... (Please wait)", null, false);
                using (var client = new HttpClient())
                {
                    var url = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";
                    var zipBytes = await client.GetByteArrayAsync(url);
                    var zipPath = "ffmpeg_temp.zip";
                    await File.WriteAllBytesAsync(zipPath, zipBytes);
                    
                    using (var archive = ZipFile.OpenRead(zipPath))
                    {
                        foreach (var entry in archive.Entries)
                        {
                            if (entry.FullName.EndsWith("ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
                            {
                                entry.ExtractToFile(FFmpegPath, true);
                                break;
                            }
                        }
                    }
                    File.Delete(zipPath);
                    NotificationManager.Show(NotificationLevel.Success, "FFmpeg Ready!", null, true);
                    return true;
                }
            }
            catch { return false; }
        }

        private static bool CheckLinuxFFmpeg()
        {
            try 
            {
                Process.Start(new ProcessStartInfo { FileName = "ffmpeg", Arguments = "-version", CreateNoWindow = true });
                return true;
            } 
            catch { return false; }
        }

        public static (int width, int height, double fps) GetVideoInfo(string path)
        {
            try
            {
                var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = FFmpegPath,
                        Arguments = $"-i \"{path}\"",
                        UseShellExecute = false,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };
                proc.Start();
                string output = proc.StandardError.ReadToEnd();
                proc.WaitForExit();

                var resMatch = Regex.Match(output, @"(\d{3,5})x(\d{3,5})");
                var fpsMatch = Regex.Match(output, @"(\d+(?:\.\d+)?) fps");

                if (resMatch.Success && fpsMatch.Success)
                {
                    return (int.Parse(resMatch.Groups[1].Value), int.Parse(resMatch.Groups[2].Value), 1000.0 / double.Parse(fpsMatch.Groups[1].Value));
                }
            }
            catch {}
            return (0, 0, 0);
        }
    }
}
