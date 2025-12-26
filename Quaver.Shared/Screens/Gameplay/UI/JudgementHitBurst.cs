using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended.Timers;
using Quaver.API.Enums;
using Quaver.Shared.Audio;
using Quaver.Shared.Skinning;
using Wobble.Graphics;
using Wobble.Graphics.Animations;
using Wobble.Graphics.Sprites;

namespace Quaver.Shared.Screens.Gameplay.UI
{
    public class JudgementHitBurst : AnimatableSprite
    {
        private GameplayScreen Screen { get; }
        public bool IsAnimatingWithOneFrame { get; private set; }
        private readonly CountdownTimer bumpTimer;
        private readonly TimeSpan bumpTime;
        private float bumpY;
        public float OriginalPosY { get; set; }
        private SkinKeys Skin => SkinManager.Skin.Keys[Screen.Map.Mode];

        public JudgementHitBurst(GameplayScreen screen, List<Texture2D> frames, Vector2 size, float posY) : base(frames)
        {
            Screen = screen;
            OriginalPosY = posY;
            Size = new ScalableVector2(size.X, size.Y);
            Y = OriginalPosY;
            Visible = false;

            FinishedLooping += (o, e) => Visible = false;

            bumpTime = TimeSpan.FromMilliseconds(Skin.JudgementHitBurstBumpTime);
            bumpTimer = new CountdownTimer(bumpTime);
            bumpTimer.TimeRemainingChanged += LerpY;
            bumpTimer.Stopped += LerpY;
        }

        private void LerpY(object sender, EventArgs e)
        {
            var t = 1 - bumpTimer.TimeRemaining / bumpTime;
            Y = EasingFunctions.EaseOutExpo(bumpY, OriginalPosY, (float)t);
        }

        public override void Update(GameTime gameTime)
        {
            if (IsAnimatingWithOneFrame)
                PerformOneFrameAnimation(gameTime);

            base.Update(gameTime);
            bumpTimer.Update(gameTime);
        }

        public void ChangeJudgementFrames(Judgement j)
        {
            if (IsLooping) StopLoop();
            ReplaceFrames(SkinManager.Skin.Judgements[j]);
        }

        public void PerformJudgementAnimation(Judgement j)
        {
            ChangeJudgementFrames(j);
            Visible = true;
            Alpha = 1;

            if (Frames.Count != 1)
            {
                ChangeTo(0);
                StartLoop(Direction.Forward, Skin.JudgementHitBurstFps, 1);
                IsAnimatingWithOneFrame = false;
            }
            else
            {
                bumpY = OriginalPosY + Skin.JudgementHitBurstBumpY;
                bumpTimer.Restart();
                IsAnimatingWithOneFrame = true;
            }

            var firstFrame = Frames[0];
            var scale = SkinManager.Skin.Keys[Screen.Map.Mode].JudgementHitBurstScale / firstFrame.Height;
            
            // Reverted to standard assignment to fix CS1612
            Size = new ScalableVector2(firstFrame.Width * scale, firstFrame.Height * scale);
        }

        private void PerformOneFrameAnimation(GameTime gameTime)
        {
            if (!IsAnimatingWithOneFrame) return;

            var dt = gameTime.ElapsedGameTime.TotalMilliseconds;
            if (bumpTimer.State == TimerState.Completed)
            {
                Alpha = MathHelper.Lerp(Alpha, 0, (float)Math.Min(dt / 240, 1));
                if (Alpha <= 0) IsAnimatingWithOneFrame = false;
            }
        }
    }
}