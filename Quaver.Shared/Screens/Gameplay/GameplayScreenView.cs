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
        private class DecodedFrame { public int Index; public byte[] PixelData; }

// Ring Buffer
private Texture2D[] RingTextures;
private int CurrentRingIndex = 0;
private int DrawRingIndex = -1;

// Config & State
private long MaxRamUsageBytes;
private int DecoderThreadCount;
private ConcurrentDictionary<int, DecodedFrame> VideoBuffer = new ConcurrentDictionary<int, DecodedFrame>();
private CancellationTokenSource VideoLoaderToken = new CancellationTokenSource();
private Process FfmpegProcess;

private SpriteBatch VideoBatch;
private int VideoWidth, VideoHeight;
private double FrameTimeMs;
private int LastUploadedIndex = -1;
private long CurrentMemoryUsage = 0;


public GameplayScreenView(Screen screen) : base(screen)
{
Screen = (GameplayScreen)screen;
RatingProcessor = new RatingProcessorKeys(Screen.Map.SolveDifficulty(
screen is TournamentGameplayScreen ? Screen.Ruleset.ScoreProcessor.Mods : ModManager.Mods,
true).OverallDifficulty);

CreateBackground();

// Init Video Batch
VideoBatch = new SpriteBatch(GameBase.Game.GraphicsDevice);

ConfigureVideoMod();

if (ConfigManager.VideoModEnabled.Value)
{
RunVideoLoaderMp4(VideoLoaderToken.Token);
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
}

private async void RunVideoLoaderMp4(CancellationToken token)
        {
            if (Screen.IsSongSelectPreview) return;

            // 1. Check for FFmpeg
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
                 else
                 {
                     if (ConfigManager.VideoModEnabled.Value && !Screen.IsSongSelectPreview)
                        NotificationManager.Show(NotificationLevel.Info, "No video file found for this map.", null, true);
                     return;
                 }
            }

            // 2. Get Original Info
            var (width, height, frameTime) = VideoUtils.GetVideoInfo(videoPath);
            if (width == 0) return;

            // 3. CALCULATE OPTIMIZED RESOLUTION
            int targetH = ConfigManager.VideoModTargetHeight.Value;
            if (targetH <= 0) targetH = height;
            
            // Don't upscale small videos
            targetH = Math.Min(targetH, height);

            // Calculate Width while maintaining Aspect Ratio
            float aspect = (float)width / height;
            int targetW = (int)(targetH * aspect);

            // FFmpeg requires dimensions to be divisible by 2
            if (targetW % 2 != 0) targetW++;
            if (targetH % 2 != 0) targetH++;

            VideoWidth = targetW;
            VideoHeight = targetH;
            FrameTimeMs = frameTime;

            // 4. Start FFmpeg with Scaling
            try 
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = VideoUtils.FFmpegPath,
                    // OPTIMIZATION: -pix_fmt rgb565le forces 16-bit color (2 bytes per pixel)
                    Arguments = $"-threads {DecoderThreadCount} -i \"{videoPath}\" -vf scale={VideoWidth}:{VideoHeight} -f rawvideo -pix_fmt rgb565le -v quiet -",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                FfmpegProcess = Process.Start(startInfo);
                var stream = FfmpegProcess.StandardOutput.BaseStream;
                
                // CHANGE: 2 bytes per pixel
                int frameSize = VideoWidth * VideoHeight * 2;
                int frameIndex = 0;

                int maxFramesToBuffer = (int)(ConfigManager.VideoModPreloadSeconds.Value * (1000.0 / FrameTimeMs));

                while (!token.IsCancellationRequested && !FfmpegProcess.HasExited)
                {
                    // Smoother sleep logic
                    if (VideoBuffer.Count >= maxFramesToBuffer)
                    {
                        Thread.Sleep(2);
                        continue;
                    }

                    byte[] data = System.Buffers.ArrayPool<byte>.Shared.Rent(frameSize);
                    
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
                            System.Buffers.ArrayPool<byte>.Shared.Return(data);
                            break; 
                        }

                        var frame = new DecodedFrame { Index = frameIndex++, PixelData = data };
                        if (!VideoBuffer.TryAdd(frame.Index, frame))
                        {
                             System.Buffers.ArrayPool<byte>.Shared.Return(data);
                        }
                    }
                    catch 
                    {
                        System.Buffers.ArrayPool<byte>.Shared.Return(data);
                        break;
                    }
                }
            }
            catch (Exception e) { LogVideoError("Loader Error: " + e.Message); }
            finally 
            {
                 if (FfmpegProcess != null && !FfmpegProcess.HasExited)
                 {
                     try { FfmpegProcess.Kill(); } catch {}
                 }
            }
        }

private void LogVideoError(string msg)
{
try {
var logPath = Path.Combine(ConfigManager.LogsDirectory.Value, "video_mod.log");
File.AppendAllText(logPath, $"[{DateTime.Now}] {msg}{Environment.NewLine}");
} catch {}
}

private void UpdateAndUploadVideoTexture()
        {
            if (AudioEngine.Track == null || VideoWidth == 0 || FrameTimeMs == 0) return;

            var time = AudioEngine.Track.Time;
            int currentFrameIndex = (int)(Math.Max(0, time) / FrameTimeMs);

            if (currentFrameIndex == LastUploadedIndex || !VideoBuffer.TryRemove(currentFrameIndex, out var frameToUpload))
                return;
            
            try
            {
                if (RingTextures == null)
                {
                    RingTextures = new Texture2D[3];
                    for (int i = 0; i < 3; i++)
                        // CHANGE: Use Bgr565 (16-bit)
                        RingTextures[i] = new Texture2D(GameBase.Game.GraphicsDevice, VideoWidth, VideoHeight, false, SurfaceFormat.Bgr565);
                }

                // CHANGE: Upload 2 bytes per pixel
                RingTextures[CurrentRingIndex].SetData(frameToUpload.PixelData, 0, VideoWidth * VideoHeight * 2);
                DrawRingIndex = CurrentRingIndex;
                CurrentRingIndex = (CurrentRingIndex + 1) % 3;
                LastUploadedIndex = currentFrameIndex;
            }
            catch { }
            finally
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(frameToUpload.PixelData);
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

if (ConfigManager.VideoModEnabled.Value)
                UpdateAndUploadVideoTexture();

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

if (ConfigManager.VideoModEnabled.Value && DrawRingIndex != -1 && RingTextures != null && RingTextures[DrawRingIndex] != null)
{
if (Background != null) Background.Alpha = 0;

var currentTexture = RingTextures[DrawRingIndex];

VideoBatch.Begin();
int w = GameBase.Game.GraphicsDevice.Viewport.Width;
int h = GameBase.Game.GraphicsDevice.Viewport.Height;
VideoBatch.Draw(currentTexture, new Rectangle(0, 0, w, h), Color.White);
VideoBatch.End();
}
else
{
Background.Draw(gameTime);
}

BattleRoyaleBackgroundAlerter?.Draw(gameTime);
Screen.Ruleset?.Draw(gameTime);
Container?.Draw(gameTime);
}

public override void Destroy()
{
VideoLoaderToken.Cancel();

if (FfmpegProcess != null && !FfmpegProcess.HasExited)
{
try { FfmpegProcess.Kill(); } catch {}
}

            foreach(var kvp in VideoBuffer)
               System.Buffers.ArrayPool<byte>.Shared.Return(kvp.Value.PixelData);
VideoBuffer.Clear();

if (RingTextures != null)
{
foreach(var tex in RingTextures)
tex?.Dispose();
}
VideoBatch?.Dispose();

// Force GC to clean up large arrays immediately to prevent menu lag
GC.Collect();

if (OnlineManager.Client != null)
OnlineManager.Client.OnGameEnded -= OnGameEnded;

Screen.Ruleset?.Destroy();
Container?.Destroy();
}

private void CreateBackground()
{
var background = BackgroundHelper.RawTexture;

if (background == null)
background = UserInterface.MenuBackgroundClear;

Background = new BackgroundImage(background, 100 - ConfigManager.BackgroundBrightness.Value, false);
}

private void CreateProgressBar()
{
if (!ConfigManager.DisplaySongTimeProgress.Value)
return;

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
if (!ConfigManager.DisplaySongTimeProgress.Value)
return;

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
