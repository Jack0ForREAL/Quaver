using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
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

        public static int GetCpuThreads() => Environment.ProcessorCount;

        public static int GetTotalRamMB()
        {
            try
            {
                var gcInfo = GC.GetGCMemoryInfo();
                long totalBytes = gcInfo.TotalAvailableMemoryBytes; 
                if (totalBytes == 0) return 8192;
                return (int)(totalBytes / 1024 / 1024);
            }
            catch { return 8192; }
        }

        public static async Task<bool> CheckOrDownloadFFmpeg()
        {
            // Check if file exists AND is valid (larger than 10MB)
            if (File.Exists(FFmpegPath))
            {
                var info = new FileInfo(FFmpegPath);
                if (info.Length > 10 * 1024 * 1024) return true;
                try { File.Delete(FFmpegPath); } catch {}
            }
            
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && CheckLinuxFFmpeg()) 
                return true;

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return false;

            try
            {
                NotificationManager.Show(NotificationLevel.Info, "Downloading Video Component... (0%)", null, false);
                
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;

                using (var client = new HttpClient())
                {
                    // Fake Browser Headers
                    client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

                    var url = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";
                    var zipPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg_temp.zip");

                    using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();
                        var totalBytes = response.Content.Headers.ContentLength ?? 100_000_000;

                        using (var stream = await response.Content.ReadAsStreamAsync())
                        using (var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                        {
                            var buffer = new byte[8192];
                            long totalRead = 0;
                            int bytesRead;
                            bool notifiedMidway = false;

                            while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                await fileStream.WriteAsync(buffer, 0, bytesRead);
                                totalRead += bytesRead;

                                var progress = (double)totalRead / totalBytes * 100;
                                if (progress > 50 && !notifiedMidway)
                                {
                                    NotificationManager.Show(NotificationLevel.Info, "Downloading Video Component... (50%)", null, false);
                                    notifiedMidway = true;
                                }
                            }
                        }
                    }
                    
                    NotificationManager.Show(NotificationLevel.Info, "Extracting...", null, false);

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
                    NotificationManager.Show(NotificationLevel.Success, "Video Ready! Please restart map.", null, true);
                    return true;
                }
            }
            catch (Exception)
            {
                NotificationManager.Show(NotificationLevel.Error, "Download failed. Please install FFmpeg manually.", null, true);
                return false;
            }
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

        public static (int width, int height, double frameTimeMs) GetVideoInfo(string path)
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
                    int w = int.Parse(resMatch.Groups[1].Value);
                    int h = int.Parse(resMatch.Groups[2].Value);
                    double fps = double.Parse(fpsMatch.Groups[1].Value);
                    return (w, h, 1000.0 / fps);
                }
            }
            catch {}
            return (0, 0, 0);
        }
    }
}
