using System;
using Microsoft.Xna.Framework;
using Wobble.Graphics.Sprites;

namespace Quaver.Shared.Screens.Gameplay.Rulesets.Keys.Playfield
{
    public class ColumnLighting : Sprite
    {
        public bool Active { get; set; }
        public int AnimationScale => Active ? 2 : 60;
        public int TargetAlpha => Active ? 1 : 0;

        public override void Update(GameTime gameTime)
        {
            // v2: Optimization - Idle check
            // If we are already at the target alpha, don't run the math.
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (Alpha == TargetAlpha)
            {
                base.Update(gameTime);
                return;
            }

            var dt = gameTime.ElapsedGameTime.TotalMilliseconds;
            Alpha = MathHelper.Lerp(Alpha, TargetAlpha, (float) Math.Min(dt / AnimationScale, 1));

            base.Update(gameTime);
        }
    }
}