using System;
using Microsoft.Xna.Framework;
using MonoGame.Extended;
using Quaver.API.Enums;
using Quaver.Shared.Audio;
using Quaver.Shared.Config;
using Quaver.Shared.Database.Maps;
using Quaver.Shared.Screens.Gameplay.Rulesets.Keys.HitObjects;
using Quaver.Shared.Skinning;
using Wobble.Graphics;
using Wobble.Graphics.Sprites;

namespace Quaver.Shared.Screens.Gameplay.Rulesets.Keys.Playfield
{
    public class HitLighting : AnimatableSprite
    {
        private GameplayPlayfieldKeys Playfield { get; }
        private int ColumnIndex { get; }
        private bool IsHoldingLongNote { get; set; }
        private bool PerformingOneFrameAnimation { get; set; }
        private bool DecreasingAlphaInAnimation { get; set; }

        // v2: Optimization - Reusable objects
        private ScalableVector2 _cachedSize;
        private ScalableVector2 _cachedPosition;

        public HitLighting(GameplayPlayfieldKeys playfield, int columnIndex)
            : base(SkinManager.Skin.Keys[MapManager.Selected.Value.Mode].HitLighting)
        {
            Playfield = playfield;
            ColumnIndex = columnIndex;
            
            // v2: Initialize cache
            _cachedSize = new ScalableVector2(0, 0);
            _cachedPosition = new ScalableVector2(0, 0);
            Size = _cachedSize;
            Position = _cachedPosition;

            FinishedLooping += OnLoopCompletion;
        }

        public override void Update(GameTime gameTime)
        {
            if (PerformingOneFrameAnimation)
                PerformOneFrameAnimation(gameTime);

            base.Update(gameTime);
        }

        public void PerformHitAnimation(bool isLongNote, Judgement judgement = Judgement.Ghost)
        {
            var skin = SkinManager.Skin.Keys[MapManager.Selected.Value.Mode];
            IsHoldingLongNote = isLongNote;

            if (ConfigManager.TintHitLightingBasedOnJudgementColor.Value && judgement != Judgement.Ghost)
                Tint = skin.JudgeColors[judgement];
            else
                Tint = Color.White;

            ReplaceFrames(IsHoldingLongNote ? skin.HoldLighting : skin.HitLighting);
            ChangeTo(0);
            Visible = true;
            Alpha = 1;

            var skinScale = IsHoldingLongNote ? skin.HoldLightingScale : skin.HitLightingScale;
            var scale = skinScale / 100f;

            // v2: Optimization - Update existing Size object
            _cachedSize.X.Value = Image.Width * scale;
            _cachedSize.Y.Value = Image.Height * scale;

            // v2: Optimization - AlignRect optimization using stack vars
            // We avoid creating new RectangleF here if we can helper it, but the AlignRect returns Vector2
            var receptorRect = Playfield.Stage.Receptors[ColumnIndex].ScreenRectangle;
            var relativeRect = new RectangleF(0, 0, _cachedSize.X.Value, _cachedSize.Y.Value);
            var pos = GraphicsHelper.AlignRect(Alignment.MidCenter, relativeRect, receptorRect);
            var fgRect = Playfield.ForegroundContainer.ScreenRectangle;

            // v2: Optimization - Update existing Position object
            _cachedPosition.X.Value = pos.X - fgRect.X + skin.HitLightingX;
            _cachedPosition.Y.Value = pos.Y - fgRect.Y + skin.HitLightingY;

            var rotate = IsHoldingLongNote ? skin.HoldLightingColumnRotation : skin.HitLightingColumnRotation;
            Rotation = rotate ? GameplayHitObjectKeys.GetObjectRotation(Playfield.Ruleset.Map.Mode, ColumnIndex) : 0;

            PerformingOneFrameAnimation = Frames.Count == 1;

            if (PerformingOneFrameAnimation) return;

            if (!IsHoldingLongNote)
                StartLoop(Direction.Forward, skin.HitLightingFps, 1);
            else
                StartLoop(Direction.Forward, skin.HoldLightingFps);
        }

        public void StopHolding()
        {
            StopLoop();
            Visible = false;
            IsHoldingLongNote = false;
            PerformingOneFrameAnimation = false;
        }

        private void OnLoopCompletion(object sender, EventArgs e)
        {
            if (IsHoldingLongNote) return;
            Visible = false;
            PerformingOneFrameAnimation = false;
        }

        private void PerformOneFrameAnimation(GameTime gameTime)
        {
            var dt = gameTime.ElapsedGameTime.TotalMilliseconds;
            // v2: Optimization - Precompute change
            var change = (float)(dt / (120 * AudioEngine.Track.Rate));

            if (!IsHoldingLongNote)
            {
                Alpha -= change;
                if (Alpha <= 0) FinishedLooping?.Invoke(this, null);
            }
            else
            {
                if (Alpha >= 1) DecreasingAlphaInAnimation = true;

                if (DecreasingAlphaInAnimation)
                {
                    Alpha -= change;
                    if (Alpha <= 0) DecreasingAlphaInAnimation = false;
                }
                else
                {
                    Alpha += change;
                }
            }
        }
    }
}