using Microsoft.Xna.Framework;
using Quaver.API.Enums;
using Quaver.Shared.Config;
using Quaver.Shared.Database.Maps;
using Quaver.Shared.Screens.Gameplay.Rulesets.Keys.HitObjects;
using Quaver.Shared.Skinning;
using System;
using System.Linq;
using Microsoft.Xna.Framework.Graphics;
using Quaver.Shared.Assets;
using Quaver.Shared.Screens.Gameplay.UI;
using Quaver.Shared.Window;
using Wobble;
using Wobble.Graphics;
using Wobble.Graphics.Sprites;
using Wobble.Window;

namespace Quaver.Shared.Screens.Gameplay.Rulesets.Keys.Playfield
{
    public class GameplayPlayfieldKeys : IGameplayPlayfield
    {
        public GameplayScreen Screen { get; }
        public GameplayRulesetKeys Ruleset { get; }
        public Container Container { get; set; }
        public Container BackgroundContainer { get; private set; }
        public Container ForegroundContainer { get; private set; }
        public Sprite PlayfieldMask { get; private set; }
        public GameplayPlayfieldKeysStage Stage { get; private set; }

        // v2: Optimization - Cache these values to prevent dictionary lookups every frame
        private float _cachedWidth;
        private float _cachedPadding;
        private float _cachedLaneSize;
        private float _cachedReceptorPadding;

        public float Width => _cachedWidth;
        public float Padding => _cachedPadding;
        public float LaneSize => _cachedLaneSize;
        internal float ReceptorPadding => _cachedReceptorPadding;

        public static float PREVIEW_PLAYFIELD_WIDTH
        {
            get
            {
                var game = GameBase.Game as QuaverGame;
                if (game?.CurrentScreen?.Type == QuaverScreenType.Editor) return 400;
                return 420;
            }
        }

        internal float[] ReceptorPositionY { get; private set; }
        internal float[] ColumnLightingPositionY { get; private set; }
        internal float[] HitPositionY { get; private set; }
        internal float[] HoldHitPositionY { get; private set; }
        internal float[] HoldEndHitPositionY { get; private set; }
        internal float[] TimingLinePositionY { get; private set; }
        internal float[] LongNoteSizeAdjustment { get; private set; }
        public ScrollDirection[] ScrollDirections { get; private set; }

        public GameplayPlayfieldKeys(GameplayScreen screen, GameplayRulesetKeys ruleset)
        {
            Screen = screen;
            Ruleset = ruleset;

            // v2: Calculate cached values immediately
            CalculateCachedDimensions();

            Container = new Container
            {
                Pivot = new Vector2(0.5f, 0.5f)
            };
            SetLaneScrollDirections();
            SetReferencePositions();
            CreateElementContainers();
            
            if (!Screen.IsSongSelectPreview)
                Container.Scale = Vector2.One * (ConfigManager.PlayfieldScale.Value / 100f);
        }

        private void CalculateCachedDimensions()
        {
            var skin = SkinManager.Skin.Keys[Screen.Map.Mode];
            
            // Receptor Padding
            _cachedReceptorPadding = Screen.IsSongSelectPreview ? 0 : skin.NotePadding;

            // Padding
            _cachedPadding = Screen.IsSongSelectPreview ? 0 : skin.StageReceptorPadding;

            // Lane Size
            var previewWidth = PREVIEW_PLAYFIELD_WIDTH / Screen.Map.GetKeyCount();
            var columnWidth = skin.ColumnSize * WindowManager.BaseToVirtualRatio;

            if (Screen.IsSongSelectPreview && previewWidth < columnWidth)
                _cachedLaneSize = previewWidth;
            else
                _cachedLaneSize = columnWidth;

            // Width
            var padding = _cachedPadding * 2 - _cachedReceptorPadding;
            var width = (_cachedLaneSize + _cachedReceptorPadding) * Screen.Map.GetKeyCount(false) + padding;

            if (Screen.Map.HasScratchKey)
            {
                var size = skin.ScratchLaneSize <= 0 ? _cachedLaneSize : skin.ScratchLaneSize;
                width += size + _cachedReceptorPadding;
            }
            _cachedWidth = width;
        }

        private void CreateElementContainers()
        {
            BackgroundContainer = new Container
            {
                Parent = Container,
                Size = new ScalableVector2(Width, WindowManager.Height),
                Alignment = Alignment.TopCenter,
                X = SkinManager.Skin.Keys[Screen.Map.Mode].ColumnAlignment,
            };

            ForegroundContainer = new Container
            {
                Parent = Container,
                Size = new ScalableVector2(Width, WindowManager.Height),
                Alignment = Alignment.TopCenter,
                X = SkinManager.Skin.Keys[Screen.Map.Mode].ColumnAlignment
            };

            PlayfieldMask = new Sprite
            {
                Parent = Container,
                Image = UserInterface.PlayfieldMask,
                Alignment = Alignment.MidCenter,
                Size = new ScalableVector2(WindowManager.Width * 4, WindowManager.Height * 4),
                Visible = ConfigManager.PlayfieldScale.Value < 100 && !Screen.IsSongSelectPreview,
                SpriteBatchOptions = new SpriteBatchOptions
                {
                    SamplerState = new SamplerState { Filter = TextureFilter.Point }
                }
            };

            Stage = new GameplayPlayfieldKeysStage(Screen, this);
        }

        private void SetLaneScrollDirections()
        {
            var keys = Ruleset.Screen.Map?.GetKeyCount() ?? 4;
            var direction = ConfigManager.ScrollDirections[Ruleset.Map.Mode].Value;

            if (direction.Equals(ScrollDirection.Split))
            {
                var halfIndex = (int)Math.Ceiling(keys / 2.0);
                ScrollDirections = new ScrollDirection[keys];
                for (var i = 0; i < keys; i++)
                {
                    if (i >= halfIndex) ScrollDirections[i] = ScrollDirection.Up;
                    else ScrollDirections[i] = ScrollDirection.Down;
                }
                return;
            }

            ScrollDirections = Enumerable.Repeat(direction, keys).ToArray();
        }

        private void SetReferencePositions()
        {
            var skin = SkinManager.Skin.Keys[Screen.Map.Mode];
            var dirLength = ScrollDirections.Length;

            ReceptorPositionY = new float[dirLength];
            ColumnLightingPositionY = new float[dirLength];
            HitPositionY = new float[dirLength];
            HoldHitPositionY = new float[dirLength];
            HoldEndHitPositionY = new float[dirLength];
            TimingLinePositionY = new float[dirLength];
            LongNoteSizeAdjustment = new float[dirLength];

            var defaultLaneSize = skin.WidthForNoteHeightScale > 0 ? skin.WidthForNoteHeightScale : LaneSize;

            // v2: Optimization - Pre-fetch scalar values to use in loop
            var baseHitOffset = skin.HitPosOffsetY * (Ruleset.Screen.IsSongSelectPreview ? (LaneSize / skin.ColumnSize) : WindowManager.BaseToVirtualRatio);

            for (var i = 0; i < dirLength; i++)
            {
                var hitObOffset = defaultLaneSize * skin.NoteHitObjects[i][0].Height / skin.NoteHitObjects[i][0].Width;
                var holdHitObOffset = defaultLaneSize * skin.NoteHoldHitObjects[i][0].Height / skin.NoteHoldHitObjects[i][0].Width;
                var holdEndOffset = LaneSize * skin.NoteHoldEnds[i].Height / skin.NoteHoldEnds[i].Width;
                var receptorOffset = LaneSize * skin.NoteReceptorsUp[i].Height / skin.NoteReceptorsUp[i].Width;

                LongNoteSizeAdjustment[i] = SkinManager.Skin.Keys[Screen.Map.Mode].DrawLongNoteEnd 
                    ? (holdHitObOffset - holdEndOffset) / 2 
                    : holdHitObOffset / 2;

                switch (ScrollDirections[i])
                {
                    case ScrollDirection.Down:
                        ReceptorPositionY[i] = WindowManager.Height - skin.ReceptorPosOffsetY - receptorOffset;
                        ColumnLightingPositionY[i] = ReceptorPositionY[i] - skin.ColumnLightingOffsetY - skin.ColumnLightingScale * LaneSize * skin.ColumnLighting.Height / skin.ColumnLighting.Width;
                        HitPositionY[i] = ReceptorPositionY[i] + baseHitOffset - hitObOffset;
                        HoldHitPositionY[i] = ReceptorPositionY[i] + baseHitOffset - holdHitObOffset;
                        HoldEndHitPositionY[i] = ReceptorPositionY[i] + baseHitOffset - holdEndOffset;
                        TimingLinePositionY[i] = ReceptorPositionY[i] + baseHitOffset;
                        break;
                    case ScrollDirection.Up:
                        ReceptorPositionY[i] = skin.ReceptorPosOffsetY;
                        HitPositionY[i] = ReceptorPositionY[i] - baseHitOffset + receptorOffset;
                        HoldHitPositionY[i] = ReceptorPositionY[i] - baseHitOffset + receptorOffset;
                        HoldEndHitPositionY[i] = ReceptorPositionY[i] - baseHitOffset + receptorOffset;
                        ColumnLightingPositionY[i] = ReceptorPositionY[i] + receptorOffset + skin.ColumnLightingOffsetY;
                        TimingLinePositionY[i] = HitPositionY[i];
                        break;
                }
            }
        }

        public void Update(GameTime gameTime)
        {
            Stage.Update(gameTime);
            Container?.Update(gameTime);
        }

        public void Draw(GameTime gameTime) => Container.Draw(gameTime);
        public void Destroy() => Container?.Destroy();
        public void HandleFailure(GameTime gameTime) {}
    }
}