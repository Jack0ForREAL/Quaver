using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quaver.API.Enums;
using Quaver.API.Helpers;
using Quaver.API.Maps.Processors.Rating;
using Quaver.API.Maps.Processors.Scoring;
using Quaver.Server.Client.Handlers;
using Quaver.Server.Client.Objects.Multiplayer;
using Quaver.Shared.Assets;
using Quaver.Shared.Audio;
using Quaver.Shared.Config;
using Quaver.Shared.Database.Maps;
using Quaver.Shared.Graphics;
using Quaver.Shared.Graphics.Backgrounds;
using Quaver.Shared.Graphics.Notifications;
using Quaver.Shared.Helpers;
using Quaver.Shared.Modifiers;
using Quaver.Shared.Online;
using Quaver.Shared.Screens.Editor;
using Quaver.Shared.Screens.Gameplay.Rulesets.Keys.HitObjects;
using Quaver.Shared.Screens.Gameplay.UI;
using Quaver.Shared.Screens.Gameplay.UI.Counter;
using Quaver.Shared.Screens.Gameplay.UI.Multiplayer;
using Quaver.Shared.Screens.Gameplay.UI.Offset;
using Quaver.Shared.Screens.Gameplay.UI.Replays;
using Quaver.Shared.Screens.Gameplay.UI.Scoreboard;
using Quaver.Shared.Screens.Multi;
using Quaver.Shared.Screens.Results;
using Quaver.Shared.Screens.Selection;
using Quaver.Shared.Screens.Tournament.Gameplay;
using Quaver.Shared.Skinning;
using Steamworks;
using Wobble;
using Wobble.Graphics;
using Wobble.Graphics.Animations;
using Wobble.Graphics.Sprites;
using Wobble.Graphics.UI;
using Wobble.Screens;
using Wobble.Window;
using MathHelper = Microsoft.Xna.Framework.MathHelper;

namespace Quaver.Shared.Screens.Gameplay
{
    public class GameplayScreenView : ScreenView
    {
        public new GameplayScreen Screen { get; }
        internal RatingProcessorKeys RatingProcessor { get; }
        public BackgroundImage Background { get; private set; }
        public SongTimeProgressBar ProgressBar { get; private set; }
        public GameplayNumberDisplay ScoreDisplay { get; private set; }
        public GameplayNumberDisplay RatingDisplay { get; private set; }
        public GameplayNumberDisplay AccuracyDisplay { get; private set; }
        public KeysPerSecond KpsDisplay { get; private set; }
        public JudgementCounter JudgementCounter { get; private set; }
        public GradeDisplay GradeDisplay { get; private set; }
        public Scoreboard ScoreboardLeft { get; set; }
        public Scoreboard ScoreboardRight { get; set; }
        public SkipDisplay SkipDisplay { get; set; }
        public Sprite Transitioner { get; set; }
        public PauseScreen PauseScreen { get; set; }
        private ComboAlert ComboAlert { get; set; }
        public bool FadingOnRestartKeyPress { get; set; }
        public bool FadingOnRestartKeyRelease { get; set; }
        public bool FadingOnPlayCompletion { get; set; }
        public bool ScreenChangedToRedOnFailure { get; set; }
        private bool ResultsScreenLoadInitiated { get; set; }
        private bool ClearToExitScreen { get; set; }
        private OffsetCalibratorTip Tip { get; set; }
        private MultiplayerEndGameWaitTime MultiplayerEndTime { get; set; }
        private bool StopCheckingForScoreboardUsers { get; set; }
        private BattleRoyaleBackgroundAlerter BattleRoyaleBackgroundAlerter { get; }
        public ScoreboardUser SelfScoreboard { get; private set; }
        private SpectatorDialog SpectatorDialog { get; set; }
        private SpectatorCount SpectatorCount { get; }
        private ReplayController ReplayController { get; }

        // --- MP4 VIDEO MOD VARIABLES ---
        private struct DecodedFrame { public int Index; public byte[] PixelData; }

        private Texture2D[] RingTextures;
        private int CurrentRingIndex = 0;
        private int DrawRingIndex = -1;

        private long MaxRamUsageBytes;
        private int DecoderThreadCount;
        private ConcurrentDictionary<int, DecodedFrame> VideoBuffer = new ConcurrentDictionary<int, DecodedFrame>();
        private CancellationTokenSource VideoLoaderToken;
        private Process FfmpegProcess;
        private SpriteBatch VideoBatch;
        private int VideoWidth, VideoHeight;
        private double FrameTimeMs;
        private int LastUploadedIndex = -1;
        
        // SYNC VARIABLES
        private double LastSyncCheckTime = 0;
        private double InternalVideoClockStart = 0;
        private bool RequiresResync = false;
        private object ProcessLock = new object();

        private ConcurrentStack<byte[]> FreeBufferPool = new ConcurrentStack<byte[]>();
        private bool IsPoolInitialized = false;
        private bool IsVideoCrashed = false;

        public GameplayScreenView(Screen screen) : base(screen)
        {
            Screen = (GameplayScreen)screen;
            RatingProcessor = new RatingProcessorKeys(Screen.Map.SolveDifficulty(
                screen is TournamentGameplayScreen ? Screen.Ruleset.ScoreProcessor.Mods : ModManager.Mods,
                true).OverallDifficulty);

            CreateBackground();

            if (!Screen.IsSongSelectPreview)
            {
                try {
                    VideoBatch = new SpriteBatch(GameBase.Game.GraphicsDevice);
                    ConfigureVideoMod();

                    if (ConfigManager.VideoModEnabled.Value)
                    {
                        StartVideoLoader(0);
                    }
                } catch { 
                    IsVideoCrashed = true; 
                }
            }

            if (OnlineManager.CurrentGame != null && OnlineManager.CurrentGame.Ruleset == MultiplayerGameRuleset.Battle_Royale
                && ConfigManager.EnableBattleRoyaleBackgroundFlashing.Value)
            {
                BattleRoyaleBackgroundAlerter = new BattleRoyaleBackgroundAlerter(this);
            }

            if (!Screen.IsPlayTesting && !Screen.IsCalibratingOffset)
                CreateScoreboards();

            CreateProgressBar();

            if (SkinManager.Skin.Keys[Screen.Map.Mode].ShowMiniSongBar)
                CreateMiniProgressBar();

            CreateScoreDisplay();
            CreateRatingDisplay();
            CreateAccuracyDisplay();

            if (ConfigManager.DisplayComboAlerts.Value && !Screen.IsSongSelectPreview)
                ComboAlert = new ComboAlert(Screen.Ruleset.ScoreProcessor) { Parent = Container };

            if (ConfigManager.DisplayJudgementCounter.Value)
            {
                if (OnlineManager.CurrentGame == null || OnlineManager.CurrentGame.Ruleset != MultiplayerGameRuleset.Team)
                    JudgementCounter = new JudgementCounter(Screen) { Parent = Container };
            }

            CreateKeysPerSecondDisplay();
            CreateGradeDisplay();

            SkipDisplay = new SkipDisplay(Screen, SkinManager.Skin.Skip) { Parent = Container };

            if (Screen.IsMultiplayerGame)
            {
                MultiplayerEndTime = new MultiplayerEndGameWaitTime
                {
                    Parent = Container,
                    Alignment = Alignment.MidCenter
                };
            }

            if (Screen.SpectatorClient != null)
            {
                SpectatorDialog = new SpectatorDialog(Screen.SpectatorClient)
                {
                    Parent = Container,
                    Alignment = Alignment.MidCenter,
                    Alpha = 0
                };
            }

            SpectatorCount = new SpectatorCount
            {
                Parent = Container,
                Y = 120,
                Alignment = Alignment.TopRight,
                X = -10
            };

            if (Screen.InReplayMode && Screen.SpectatorClient == null && !Screen.IsSongSelectPreview)
            {
                ReplayController = new ReplayController(Screen)
                {
                    Parent = Container,
                    Alignment = Alignment.BotRight,
                    Position = new ScalableVector2(-12, -110)
                };
            }

            Transitioner = new Sprite()
            {
                Parent = Container,
                Size = new ScalableVector2(WindowManager.Width, WindowManager.Height),
                Tint = Color.Black,
                Alpha = 1,
                Animations =
                {
                    new Animation(AnimationProperty.Alpha, Easing.Linear, 1, 0, 1500)
                }
            };

            if (Screen.SpectatorClient == null)
                PauseScreen = new PauseScreen(Screen) { Parent = Container };

            if (!Screen.IsSongSelectPreview && MapManager.Selected.Value.LocalOffset != 0)
            {
                NotificationManager.Show(NotificationLevel.Info, $"The local audio offset for this map is: {MapManager.Selected.Value.LocalOffset} ms",
                    null, true);
            }

            if (Screen.IsCalibratingOffset)
            {
                Tip = new OffsetCalibratorTip
                {
                    Parent = Container,
                    Alignment = Alignment.MidCenter
                };
            }

            if (OnlineManager.Client != null && !Screen.IsSongSelectPreview)
                OnlineManager.Client.OnGameEnded += OnGameEnded;
        }

        private void ConfigureVideoMod()
        {
            try {
                if (ConfigManager.VideoModAutoConfiguration.Value)
                {
                    int ram = VideoUtils.GetTotalRamMB();
                    int threads = VideoUtils.GetCpuThreads();
                    MaxRamUsageBytes = (long)(Math.Min(ram / 2, 4096)) * 1024 * 1024;
                    DecoderThreadCount = Math.Clamp(threads / 2, 1, 4);
                }
                else
                {
                    MaxRamUsageBytes = (long)ConfigManager.VideoModRamBudget.Value * 1024 * 1024;
                    DecoderThreadCount = ConfigManager.VideoModDecoderThreads.Value;
                }
            } catch {
                MaxRamUsageBytes = 1024 * 1024 * 1024;
                DecoderThreadCount = 2;
            }
        }

        private void StartVideoLoader(double startTimeMs)
        {
            if (VideoLoaderToken != null)
            {
                VideoLoaderToken.Cancel();
                VideoLoaderToken.Dispose();
            }

            VideoLoaderToken = new CancellationTokenSource();
            
            VideoBuffer.Clear();
            
            Task.Run(() => RunVideoLoaderMp4(VideoLoaderToken.Token, startTimeMs));
        }

        private async void RunVideoLoaderMp4(CancellationToken token, double startTimeMs)
        {
            if (Screen.IsSongSelectPreview) return;
            try 
            {
                if (!await VideoUtils.CheckOrDownloadFFmpeg()) return;

                var currentMap = MapManager.Selected.Value;
                if (currentMap == null) return;

                var fileName = ConfigManager.VideoModHighQuality.Value ? "video.mp4" : "video_low.mp4";
                var videoPath = Path.Combine(ConfigManager.SongDirectory.Value, currentMap.Directory, fileName);

                if (!File.Exists(videoPath))
                {
                    var otherName = fileName == "video.mp4" ? "video_low.mp4" : "video.mp4";
                    var otherPath = Path.Combine(ConfigManager.SongDirectory.Value, currentMap.Directory, otherName);
                    if (File.Exists(otherPath)) videoPath = otherPath;
                    else return;
                }

                var (width, height, frameTime) = VideoUtils.GetVideoInfo(videoPath);
                if (width == 0) return;

                int targetH = ConfigManager.VideoModTargetHeight.Value;
                if (targetH <= 0) targetH = height;
                targetH = Math.Min(targetH, height);
                float aspect = (float)width / height;
                int targetW = (int)(targetH * aspect);
                if (targetW % 2 != 0) targetW++;
                if (targetH % 2 != 0) targetH++;

                VideoWidth = targetW;
                VideoHeight = targetH;
                FrameTimeMs = frameTime;

                InternalVideoClockStart = startTimeMs;
                int startFrameIndex = (int)(startTimeMs / FrameTimeMs);

                bool use32Bit = ConfigManager.VideoModUse32Bit.Value;
                int bytesPerPixel = use32Bit ? 4 : 2;
                string pixelFormat = use32Bit ? "rgba" : "rgb565le";
                
                int frameSize = VideoWidth * VideoHeight * bytesPerPixel;
                int bufferCount = (int)(ConfigManager.VideoModPreloadSeconds.Value * (1000.0 / FrameTimeMs));
                int cap = use32Bit ? 200 : 400;
                bufferCount = Math.Clamp(bufferCount, 10, cap);

                if (!IsPoolInitialized)
                {
                    for (int i = 0; i < bufferCount; i++)
                        FreeBufferPool.Push(new byte[frameSize]);
                    IsPoolInitialized = true;
                }

                string seekCmd = startTimeMs > 0 ? $"-ss {startTimeMs / 1000.0:0.000}" : "";

                var startInfo = new ProcessStartInfo
                {
                    FileName = VideoUtils.FFmpegPath,
                    Arguments = $"-threads {DecoderThreadCount} {seekCmd} -i \"{videoPath}\" -vf scale={VideoWidth}:{VideoHeight} -vsync 0 -f rawvideo -pix_fmt {pixelFormat} -v quiet -",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                lock (ProcessLock)
                {
                    FfmpegProcess = Process.Start(startInfo);
                }

                var stream = FfmpegProcess.StandardOutput.BaseStream;
                int frameOffset = 0;

                while (!token.IsCancellationRequested && !FfmpegProcess.HasExited)
                {
                    if (FreeBufferPool.IsEmpty)
                    {
                        Thread.Sleep(2); 
                        continue;
                    }

                    if (!FreeBufferPool.TryPop(out byte[] data)) continue;

                    try
                    {
                        int totalRead = 0;
                        while (totalRead < frameSize)
                        {
                            int read = await stream.ReadAsync(data, totalRead, frameSize - totalRead, token);
                            if (read == 0) break;
                            totalRead += read;
                        }

                        if (totalRead < frameSize)
                        {
                            FreeBufferPool.Push(data);
                            break;
                        }

                        var frame = new DecodedFrame { Index = startFrameIndex + frameOffset, PixelData = data };
                        frameOffset++;

                        if (!VideoBuffer.TryAdd(frame.Index, frame))
                        {
                            FreeBufferPool.Push(data);
                        }
                    }
                    catch
                    {
                        FreeBufferPool.Push(data);
                        break;
                    }
                }
            }
            catch (Exception e) 
            { 
                IsVideoCrashed = true; 
            }
            finally
            {
                lock (ProcessLock)
                {
                    if (FfmpegProcess != null && !FfmpegProcess.HasExited)
                    {
                        try { FfmpegProcess.Kill(); } catch { }
                    }
                }
            }
        }

        private void CheckSyncStatus(double currentTime)
        {
            if (Math.Abs(currentTime - LastSyncCheckTime) < 250) return;
            LastSyncCheckTime = currentTime;

            if (VideoBuffer.IsEmpty || FrameTimeMs == 0) return;

            int targetFrame = (int)(currentTime / FrameTimeMs);
            int maxBufferFrame = VideoBuffer.Keys.Max();
            int minBufferFrame = VideoBuffer.Keys.Min();

            int thresholdFrames = (int)(1000.0 / FrameTimeMs);

            if (targetFrame > maxBufferFrame + thresholdFrames || targetFrame < minBufferFrame - thresholdFrames)
            {
                RequiresResync = true;
            }
        }

        private void UpdateAndUploadVideoTexture()
        {
            if (IsVideoCrashed || AudioEngine.Track == null || VideoWidth == 0 || FrameTimeMs == 0) return;

            var time = AudioEngine.Track.Time;

            CheckSyncStatus(time);

            if (RequiresResync)
            {
                RequiresResync = false;
                StartVideoLoader(time);
                return;
            }

            int currentFrameIndex = (int)(Math.Max(0, time) / FrameTimeMs);

            DecodedFrame frameToUpload;
            bool found = VideoBuffer.TryRemove(currentFrameIndex, out frameToUpload);
            
            if (!found) found = VideoBuffer.TryRemove(currentFrameIndex - 1, out frameToUpload);
            if (!found) found = VideoBuffer.TryRemove(currentFrameIndex + 1, out frameToUpload);

            foreach (var key in VideoBuffer.Keys)
            {
                if (key < currentFrameIndex - 10)
                {
                    if (VideoBuffer.TryRemove(key, out var oldFrame))
                        FreeBufferPool.Push(oldFrame.PixelData);
                }
            }

            if (!found) return;

            try
            {
                bool use32Bit = ConfigManager.VideoModUse32Bit.Value;
                int bytesPerPixel = use32Bit ? 4 : 2;
                SurfaceFormat format = use32Bit ? SurfaceFormat.Color : SurfaceFormat.Bgr565;

                if (RingTextures == null)
                {
                    RingTextures = new Texture2D[3];
                    for (int i = 0; i < 3; i++)
                        RingTextures[i] = new Texture2D(GameBase.Game.GraphicsDevice, VideoWidth, VideoHeight, false, format);
                }

                RingTextures[CurrentRingIndex].SetData(frameToUpload.PixelData, 0, VideoWidth * VideoHeight * bytesPerPixel);
                DrawRingIndex = CurrentRingIndex;
                CurrentRingIndex = (CurrentRingIndex + 1) % 3;
                LastUploadedIndex = currentFrameIndex;
            }
            catch 
            {
            }
            finally
            {
                if (IsPoolInitialized)
                    FreeBufferPool.Push(frameToUpload.PixelData);
            }
        }

        public override void Update(GameTime gameTime)
        {
            HandleWaitingForPlayersDialog();
            CheckIfNewScoreboardUsers();
            HandlePlayCompletion(gameTime);
            BattleRoyaleBackgroundAlerter?.Update(gameTime);
            Screen.Ruleset?.Update(gameTime);
            Container?.Update(gameTime);

            if (ConfigManager.VideoModEnabled.Value && !IsVideoCrashed)
            {
                UpdateAndUploadVideoTexture();
            }

            UpdateGradeDisplay();

            if (SpectatorDialog != null)
            {
                SpectatorDialog.Alpha = MathHelper.Lerp(SpectatorDialog.Alpha, Screen.IsPaused ? 1 : 0,
                    (float)Math.Min(gameTime.ElapsedGameTime.TotalMilliseconds / 100, 1));
            }
        }

        public override void Draw(GameTime gameTime)
        {
            GameBase.Game.GraphicsDevice.Clear(Color.Black);

            bool drawnVideo = false;

            if (ConfigManager.VideoModEnabled.Value && !IsVideoCrashed && DrawRingIndex != -1 && RingTextures != null && RingTextures[DrawRingIndex] != null)
            {
                try
                {
                    if (Background != null) Background.Alpha = 0;

                    var currentTexture = RingTextures[DrawRingIndex];

                    VideoBatch.Begin();
                    int w = GameBase.Game.GraphicsDevice.Viewport.Width;
                    int h = GameBase.Game.GraphicsDevice.Viewport.Height;
                    VideoBatch.Draw(currentTexture, new Rectangle(0, 0, w, h), Color.White);
                    VideoBatch.End();
                    drawnVideo = true;
                }
                catch
                {
                    drawnVideo = false;
                    IsVideoCrashed = true; 
                }
            }
            
            if (!drawnVideo)
            {
                if (Background != null) Background.Alpha = 1;
                Background.Draw(gameTime);
            }

            BattleRoyaleBackgroundAlerter?.Draw(gameTime);
            Screen.Ruleset?.Draw(gameTime);
            Container?.Draw(gameTime);
        }

        public override void Destroy()
        {
            if (VideoLoaderToken != null) VideoLoaderToken.Cancel();

            lock (ProcessLock)
            {
                if (FfmpegProcess != null && !FfmpegProcess.HasExited)
                {
                    try { FfmpegProcess.Kill(); } catch { }
                }
            }

            VideoBuffer.Clear();
            FreeBufferPool.Clear(); 

            if (RingTextures != null)
            {
                foreach (var tex in RingTextures)
                    try { tex?.Dispose(); } catch {}
            }
            try { VideoBatch?.Dispose(); } catch {}

            GC.Collect();

            if (OnlineManager.Client != null)
                OnlineManager.Client.OnGameEnded -= OnGameEnded;

            Screen.Ruleset?.Destroy();
            Container?.Destroy();
        }

        private void CreateBackground()
        {
            var background = BackgroundHelper.RawTexture;
            if (background == null) background = UserInterface.MenuBackgroundClear;
            Background = new BackgroundImage(background, 100 - ConfigManager.BackgroundBrightness.Value, false);
        }

        private void CreateProgressBar()
        {
            if (!ConfigManager.DisplaySongTimeProgress.Value) return;
            var skin = SkinManager.Skin.Keys[Screen.Map.Mode];
            ProgressBar = new SongTimeProgressBar(Screen, new Vector2(WindowManager.Width, 4), 0, Screen.Map.Length / ModHelper.GetRateFromMods(ModManager.Mods), 0,
                skin.SongTimeProgressInactiveColor, skin.SongTimeProgressActiveColor)
            {
                Parent = Container,
                Alignment = skin.SongTimeProgressPositionAtTop ? Alignment.TopLeft : Alignment.BotLeft,
                DestroyIfParentIsNull = false
            };
        }

        private void CreateMiniProgressBar()
        {
            if (!ConfigManager.DisplaySongTimeProgress.Value) return;
            var skin = SkinManager.Skin.Keys[Screen.Map.Mode];
            ProgressBar = new SongTimeProgressBar(Screen, new Vector2(WindowManager.Width / SkinManager.Skin.Keys[Screen.Map.Mode].MiniSongBarDisplayWidthFactor, SkinManager.Skin.Keys[Screen.Map.Mode].MiniSongBarDisplayHeight), 0, Screen.Map.Length / ModHelper.GetRateFromMods(ModManager.Mods), 0,
                skin.SongTimeProgressInactiveColor, skin.SongTimeProgressActiveColor, true)
            {
                Parent = Container,
                Alignment = Alignment.MidCenter,
                X = SkinManager.Skin.Keys[Screen.Map.Mode].MiniSongBarDisplayPosX,
                Y = SkinManager.Skin.Keys[Screen.Map.Mode].MiniSongBarDisplayPosY,
                DestroyIfParentIsNull = false
            };
        }

        private void CreateScoreDisplay()
        {
            var skin = SkinManager.Skin.Keys[Screen.Map.Mode];
            ScoreDisplay = new GameplayNumberDisplay(NumberDisplayType.Score, StringHelper.ScoreToString(0),
                new Vector2(skin.ScoreDisplayScale / 100f, skin.ScoreDisplayScale / 100f))
            {
                Parent = Container,
                Alignment = Alignment.TopLeft,
                X = SkinManager.Skin.Keys[Screen.Map.Mode].ScoreDisplayPosX,
                Y = SkinManager.Skin.Keys[Screen.Map.Mode].ScoreDisplayPosY
            };
        }

        private void CreateRatingDisplay()
        {
            var skin = SkinManager.Skin.Keys[Screen.Map.Mode];
            RatingDisplay = new GameplayNumberDisplay(NumberDisplayType.Rating, StringHelper.RatingToString(0),
                new Vector2(skin.RatingDisplayScale / 100f, skin.RatingDisplayScale / 100f))
            {
                Parent = Container,
                Alignment = Alignment.TopLeft,
                X = SkinManager.Skin.Keys[Screen.Map.Mode].RatingDisplayPosX,
                Y = 40 + SkinManager.Skin.Keys[Screen.Map.Mode].RatingDisplayPosY
            };
        }

        private void CreateAccuracyDisplay()
        {
            var skin = SkinManager.Skin.Keys[Screen.Map.Mode];
            AccuracyDisplay = new GameplayNumberDisplay(NumberDisplayType.Accuracy, StringHelper.AccuracyToString(0),
                new Vector2(skin.AccuracyDisplayScale / 100f, skin.AccuracyDisplayScale / 100f))
            {
                Parent = Container,
                Alignment = Alignment.TopRight,
                X = SkinManager.Skin.Keys[Screen.Map.Mode].AccuracyDisplayPosX,
                Y = SkinManager.Skin.Keys[Screen.Map.Mode].AccuracyDisplayPosY
            };
        }

        public void UpdateScoreAndAccuracyDisplays()
        {
            ScoreDisplay.UpdateValue(Screen.Ruleset.ScoreProcessor.Score);
            RatingDisplay.UpdateValue(RatingProcessor.CalculateRating(Screen.Ruleset.StandardizedReplayPlayer.ScoreProcessor.Accuracy));
            if (ConfigManager.DisplayRankedAccuracy.Value || Screen.IsSpectatingTournament)
                AccuracyDisplay.UpdateValue(Screen.Ruleset.StandardizedReplayPlayer.ScoreProcessor.Accuracy);
            else
                AccuracyDisplay.UpdateValue(Screen.Ruleset.ScoreProcessor.Accuracy);
        }

        private void CreateKeysPerSecondDisplay()
        {
            var skin = SkinManager.Skin.Keys[Screen.Map.Mode];
            KpsDisplay = new KeysPerSecond(NumberDisplayType.Score, "0", new Vector2(skin.KpsDisplayScale / 100f, skin.KpsDisplayScale / 100f))
            {
                Parent = Container,
                Alignment = Alignment.TopRight,
                X = SkinManager.Skin.Keys[Screen.Map.Mode].KpsDisplayPosX,
                Y = 40 + SkinManager.Skin.Keys[Screen.Map.Mode].KpsDisplayPosY
            };
        }

        private void CreateGradeDisplay() => GradeDisplay = new GradeDisplay(Screen)
        {
            Parent = Container,
            Alignment = Alignment.TopRight,
            X = AccuracyDisplay.X - AccuracyDisplay.Width - 8,
            Y = AccuracyDisplay.Y
        };

        private void CreateScoreboards()
        {
            var scoreboardName = Screen.InReplayMode ? Screen.LoadedReplay.PlayerName : ConfigManager.Username.Value;
            var selfAvatar = ConfigManager.Username.Value == scoreboardName ? SteamManager.GetAvatarOrUnknown(SteamUser.GetSteamID().m_SteamID)
                : UserInterface.UnknownAvatar;

            SelfScoreboard = new ScoreboardUser(Screen, ScoreboardUserType.Self, scoreboardName, null, selfAvatar,
                ModManager.Mods, null, RatingProcessor)
            {
                Parent = Container,
                Alignment = Alignment.MidLeft
            };

            var users = new List<ScoreboardUser> { SelfScoreboard };

            if (OnlineManager.CurrentGame != null && OnlineManager.CurrentGame.Ruleset == MultiplayerGameRuleset.Team)
            {
                ScoreboardRight = new Scoreboard(ScoreboardType.Teams,
                    OnlineManager.GetTeam(OnlineManager.Self.OnlineUser.Id) == MultiplayerTeam.Blue ? users : new List<ScoreboardUser>(), MultiplayerTeam.Blue)
                {
                    Parent = Container,
                    Alignment = Alignment.TopLeft,
                };
            }

            var scoreboardType = OnlineManager.CurrentGame != null &&
                                 OnlineManager.CurrentGame.Ruleset == MultiplayerGameRuleset.Team
                                ? ScoreboardType.Teams
                                : ScoreboardType.FreeForAll;

            ScoreboardLeft = new Scoreboard(scoreboardType,
                OnlineManager.CurrentGame == null || OnlineManager.GetTeam(OnlineManager.Self.OnlineUser.Id) == MultiplayerTeam.Red ?
                    users : new List<ScoreboardUser>())
            { Parent = Container };

            ScoreboardLeft?.Users.ForEach(x => x.SetImage());
            ScoreboardRight?.Users.ForEach(x => x.SetImage());
        }

        public void UpdateScoreboardUsers()
        {
            ScoreboardLeft?.CalculateScores();
            ScoreboardRight?.CalculateScores();
        }

        private void CheckIfNewScoreboardUsers()
        {
            if (Screen.IsPlayTesting || StopCheckingForScoreboardUsers)
                return;

            var mapScores = Screen.IsMultiplayerGame ? Screen.LocalScores : MapManager.Selected.Value.Scores.Value;

            if (mapScores == null || mapScores.Count <= 0 || (ScoreboardLeft.Users?.Count < 1 && ScoreboardRight != null && ScoreboardRight.Users.Count < 1))
                return;

            var maxRating = new RatingProcessorKeys(MapManager.Selected.Value.DifficultyFromMods(ModManager.Mods)).CalculateRating(100);

            for (var i = 0; i < mapScores.Count; i++)
            {
                if (OnlineManager.CurrentGame == null && ScoreboardLeft?.Users?.Count == 5)
                    break;

                ScoreboardUser user;

                if (OnlineManager.CurrentGame == null &&
                    !ConfigManager.DisplayUnbeatableScoresDuringGameplay.Value && mapScores[i].PerformanceRating > maxRating)
                    continue;

                if (OnlineManager.CurrentGame == null && mapScores[i].Grade == Grade.None)
                    continue;

                if (mapScores[i].IsOnline)
                {
                    var judgements = mapScores[i].OnlineJudgements;

                    if (judgements == null || !OnlineManager.IsDonator)
                        judgements = new List<Judgement>();

                    user = new ScoreboardUser(Screen, ScoreboardUserType.Other, $"{mapScores[i].Name}",
                        judgements, UserInterface.UnknownAvatar, (ModIdentifier)mapScores[i].Mods, mapScores[i])
                    {
                        Parent = Container,
                        Alignment = Alignment.MidLeft
                    };

                    if (OnlineManager.CurrentGame != null &&
                        OnlineManager.CurrentGame.Ruleset == MultiplayerGameRuleset.Team && OnlineManager.GetTeam(user.LocalScore.PlayerId) == MultiplayerTeam.Blue)
                    {
                        user.Scoreboard = ScoreboardRight;
                        user.X = WindowManager.Width;
                    }
                    else
                    {
                        user.Scoreboard = ScoreboardLeft;
                    }

                    user.SetImage();

                    var processor = user.Processor as ScoreProcessorKeys;
                    processor.Accuracy = (float)mapScores[i].Accuracy;
                    processor.MaxCombo = mapScores[i].MaxCombo;
                    processor.Score = mapScores[i].TotalScore;
                    processor.PlayerName = mapScores[i].Name;
                    processor.UserId = mapScores[i].PlayerId;
                    processor.SteamId = (ulong)mapScores[i].SteamId;

                    if (judgements.Count == 0)
                    {
                        user.Score.Text = $"{user.RatingProcessor.CalculateRating(processor.Accuracy):0.00} / {StringHelper.AccuracyToString(processor.Accuracy)}";
                        user.Combo.Text = $"{processor.MaxCombo}x";
                    }
                }
                else
                {
                    var breakdownHits = GzipHelper.Decompress(mapScores[i].JudgementBreakdown);

                    var judgements = new List<Judgement>();

                    foreach (var hit in breakdownHits)
                        judgements.Add((Judgement)int.Parse(hit.ToString()));

                    user = new ScoreboardUser(Screen, ScoreboardUserType.Other, $"{mapScores[i].Name}",
                        judgements, UserInterface.UnknownAvatar, (ModIdentifier)mapScores[i].Mods, mapScores[i])
                    {
                        Parent = Container,
                        Alignment = Alignment.MidLeft
                    };

                    if (OnlineManager.CurrentGame != null &&
                        OnlineManager.CurrentGame.Ruleset == MultiplayerGameRuleset.Team && OnlineManager.GetTeam(user.LocalScore.PlayerId) == MultiplayerTeam.Blue)
                    {
                        user.Scoreboard = ScoreboardRight;
                    }
                    else
                    {
                        user.Scoreboard = ScoreboardLeft;
                    }

                    user.SetImage();

                    for (var j = 0; j < Screen.Ruleset.ScoreProcessor.TotalJudgementCount && i < judgements.Count; j++)
                    {
                        var processor = user.Processor as ScoreProcessorKeys;
                        processor?.CalculateScore(judgements[i]);
                    }
                }

                if (OnlineManager.CurrentGame != null && OnlineManager.CurrentGame.Ruleset == MultiplayerGameRuleset.Team &&
                    OnlineManager.GetTeam(user.LocalScore.PlayerId) == MultiplayerTeam.Blue)
                {
                    ScoreboardRight.Users.Add(user);
                }
                else
                {
                    ScoreboardLeft.Users.Add(user);
                }
            }

            ScoreboardLeft.SetTargetYPositions();
            ScoreboardRight?.SetTargetYPositions();

            if (ProgressBar != null)
                ProgressBar.Parent = Container;

            Transitioner.Parent = Container;

            if (PauseScreen != null)
                PauseScreen.Parent = Container;

            StopCheckingForScoreboardUsers = true;
            Screen.SetRichPresence();
        }

        private void HandlePlayCompletion(GameTime gameTime)
        {
            if (!Screen.Failed && !Screen.IsPlayComplete || Screen.IsSongSelectPreview || Screen is TournamentGameplayScreen)
                return;

            if (Screen.Failed && !Screen.HasQuit && Screen.Ruleset.ScoreProcessor.Mods.HasFlag(ModIdentifier.NoMiss))
            {
                Screen.Retry();
                return;
            }

            Screen.TimeSincePlayEnded += gameTime.ElapsedGameTime.TotalMilliseconds;

            if (Screen.Exiting && Screen.Failed)
            {
                if (Screen.TimeSincePlayEnded >= Screen.FailFadeTime && !AudioEngine.Track.IsDisposed)
                {
                    AudioEngine.Track?.Dispose();
                    Screen.IsPaused = true;
                }

                return;
            }

            if (Screen.Failed && !ScreenChangedToRedOnFailure)
            {
                var tint = Screen.HasQuit ? Color.Black : Color.Red;
                Transitioner.FadeTo(Screen.HasQuit ? 1 : 0.75f, Easing.Linear, Screen.FailFadeTime);
                Transitioner.Tint = tint;
                ScreenChangedToRedOnFailure = true;

                if (!Screen.HasQuit)
                    SkinManager.Skin.SoundFailure.CreateChannel().Play();
            }

            if (!ResultsScreenLoadInitiated)
            {
                Screen.TimePlayEnd = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                if (OnlineManager.IsBeingSpectated)
                {
                    Screen.SendReplayFramesToServer(true, !OnlineManager.IsSpectatingSomeone);
                }

                if (Screen.IsPlayTesting)
                {
                    if (AudioEngine.Track.IsPlaying)
                    {
                        AudioEngine.Track.Pause();
                        AudioEngine.Track.Seek(Screen.PlayTestAudioTime);
                    }

                    if (Screen.IsTestPlayingInNewEditor)
                        Screen.ExitToNewEditor();
                    else
                        Screen.Exit(() => new EditorScreen(Screen.OriginalEditorMap));

                    ResultsScreenLoadInitiated = true;
                    return;
                }

                if (Screen.IsCalibratingOffset)
                {
                    Screen.HandleSuggestedOffsetCalculations();
                    ResultsScreenLoadInitiated = true;
                    return;
                }

                if (OnlineManager.CurrentGame != null)
                {
                    try
                    {
                        Screen.Ruleset.UpdateStandardizedScoreProcessor(true);
                        Screen.SendJudgementsToServer(true);

                        OnlineManager.Client?.FinishMultiplayerGameSession();
                        ResultsScreenLoadInitiated = true;
                    }
                    catch (Exception e)
                    {
                    }

                    return;
                }

                Screen.Exit(() =>
                {
                    if (Screen.HasQuit && ConfigManager.SkipResultsScreenAfterQuit.Value)
                    {
                        if (ModManager.Mods.HasFlag(ModIdentifier.Paused))
                            ModManager.RemoveMod(ModIdentifier.Paused);

                        return new SelectionScreen();
                    }

                    if (Screen.InReplayMode && Screen.LoadedReplay != null)
                    {
                        Screen.LoadedReplay.FromScoreProcessor(Screen.Ruleset.ScoreProcessor);
                        return new ResultsScreen(MapManager.Selected.Value, Screen.LoadedReplay);
                    }

                    return new ResultsScreen(Screen);
                }, Screen.Failed ? Screen.FailFadeTime : 500);

                ResultsScreenLoadInitiated = true;
            }

            if (Screen.TimeSincePlayEnded <= 1200 || !ClearToExitScreen)
                return;

            if (Screen.Failed)
                Transitioner.FadeToColor(Color.Black, gameTime.ElapsedGameTime.TotalMilliseconds, 150);

            if (!FadingOnPlayCompletion)
            {
                Transitioner.Animations.Clear();

                var initialAlpha = Screen.Failed ? 0.65f : 0;

                Transitioner.Animations.Add(new Animation(AnimationProperty.Alpha, Easing.Linear, initialAlpha, 1, 1000));
                FadingOnPlayCompletion = true;
            }

            if (Screen.TimeSincePlayEnded >= 3000)
            {
                BackgroundManager.Background.Dim = 0;
            }
        }

        public void UpdateGradeDisplay()
        {
            GradeDisplay.X = AccuracyDisplay.X - AccuracyDisplay.Width - 8;
            GradeDisplay.Height = AccuracyDisplay.Height;
            GradeDisplay.UpdateWidth();
        }

        private void OnBackgroundLoaded(object sender, BackgroundLoadedEventArgs e)
        {
            if (e.Map != MapManager.Selected.Value)
                return;

            FadeBackgroundToDim();
        }

        private void FadeBackgroundToDim()
        {
            BackgroundManager.Background.BrightnessSprite.Animations.Clear();

            var t = new Animation(AnimationProperty.Alpha, Easing.Linear, BackgroundManager.Background.BrightnessSprite.Alpha,
                (100 - ConfigManager.BackgroundBrightness.Value) / 100f, 300);

            BackgroundManager.Background.BrightnessSprite.Animations.Add(t);
        }

        private void OnGameEnded(object sender, GameEndedEventArgs e)
        {
            var manager = (HitObjectManagerKeys)Screen.Ruleset.HitObjectManager;

            Screen.MultiplayerMatchEndedPrematurely = !Screen.IsPlayComplete && manager.NextHitObject != null
                                                      && (Screen.Timing.Time >= Screen.Map.Length || AudioEngine.Track.Time >= AudioEngine.Track.Length);

            if (Screen is TournamentGameplayScreen)
            {
                if (!Screen.Exiting && e.Force)
                    Screen.Exit(() => new MultiplayerGameScreen());
                return;
            }

            Screen.IsPaused = true;

            Screen.Exit(() => new ResultsScreen(Screen, OnlineManager.CurrentGame,
                GetProcessorsFromScoreboard(ScoreboardLeft), GetProcessorsFromScoreboard(ScoreboardRight)));
        }

        private List<ScoreboardUser> GetScoreboardUsers()
        {
            var scoreboardUsers = new List<ScoreboardUser>();

            if (ScoreboardLeft.Users.Count != 0)
            {
                var users = ScoreboardLeft.Users.Where(x => !x.HasQuit);
                scoreboardUsers.AddRange(users);
            }

            if (ScoreboardRight != null && ScoreboardRight.Users.Count != 0)
            {
                var users = ScoreboardRight.Users.Where(x => !x.HasQuit);
                scoreboardUsers.AddRange(users);
            }

            return scoreboardUsers;
        }

        public List<ScoreProcessor> GetProcessorsFromScoreboard(Scoreboard scoreboard)
        {
            var processors = new List<ScoreProcessor>();

            if (scoreboard == null)
                return processors;

            foreach (var user in scoreboard.Users)
            {
                if (user.HasQuit)
                    continue;

                processors.Add(user.Processor);
            }

            return processors;
        }

        private void HandleWaitingForPlayersDialog()
        {
            if (MultiplayerEndTime == null)
                return;

            var previouslyVisible = MultiplayerEndTime.Visible;

            MultiplayerEndTime.Visible = Screen.IsPlayComplete;

            if (!previouslyVisible && MultiplayerEndTime.Visible)
            {
                MultiplayerEndTime.ClearAnimations();
                MultiplayerEndTime.Alpha = 0;
                MultiplayerEndTime.FadeTo(1, Easing.Linear, 400);
            }
        }
    }
}
