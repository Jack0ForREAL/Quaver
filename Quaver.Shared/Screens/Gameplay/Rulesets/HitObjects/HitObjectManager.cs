using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Quaver.API.Enums;
using Quaver.API.Maps;
using Quaver.API.Maps.Structures;
using Quaver.Shared.Audio;
using Quaver.Shared.Config;
using Quaver.Shared.Screens.Selection.UI;
using Quaver.Shared.Skinning;
using Wobble;

namespace Quaver.Shared.Screens.Gameplay.Rulesets.HitObjects
{
    public abstract class HitObjectManager
    {
        public abstract bool IsComplete { get; }
        public abstract HitObjectInfo NextHitObject { get; }
        public abstract bool OnBreak { get; }

        private static int[] BeatSnaps { get; } = { 48, 24, 16, 12, 8, 6, 4, 3 };
        public Dictionary<int, int> SnapIndices { get; set; }

        public HitObjectManager(Qua map)
        {
            SnapIndices = new Dictionary<int, int>(map.HitObjects.Count); 
            var timingPoints = map.TimingPoints;
            
            foreach (var hitObject in map.HitObjects)
            {
                if (!SnapIndices.ContainsKey(hitObject.StartTime))
                    SnapIndices.Add(hitObject.StartTime, GetBeatSnap(hitObject, hitObject.GetTimingPoint(timingPoints)));
            }
        }

        public abstract void Update(GameTime gameTime);

        public virtual void Destroy() { }

        /// <summary>
        /// v2: Stub for object pooling support.
        /// </summary>
        public virtual void ReturnToPool(NoteController note) { }

        public static void PlayObjectHitSounds(HitObjectInfo hitObject, SkinStore skin = null, int volume = -1)
        {
            // v2: Quick exit if volume is muted, saving instantiation costs
            if (volume == 0) return;

            if (skin == null) skin = SkinManager.Skin;
            var game = GameBase.Game as QuaverGame;

            // Optimization: Inline the check
            if (game?.CurrentScreen is IHasLeftPanel screen && screen.ActiveLeftPanel.Value != SelectContainerPanel.MapPreview)
                return;

            var sound = hitObject.HitSound;
            
            // v2: Aggressive null coalescence to keep code clean and fast
            if (sound == 0 || (HitSounds.Normal & sound) != 0) 
                skin?.SoundHit?.CreateChannel().Play();

            if ((HitSounds.Clap & sound) != 0) 
                skin?.SoundHitClap?.CreateChannel().Play();

            if ((HitSounds.Whistle & sound) != 0) 
                skin?.SoundHitWhistle?.CreateChannel().Play();

            if ((HitSounds.Finish & sound) != 0) 
                skin?.SoundHitFinish?.CreateChannel().Play();
        }

        public static void PlayObjectKeySounds(HitObjectInfo hitObject)
        {
            // v2: Skip iteration if empty
            if (hitObject.KeySounds.Count == 0) return;

            var game = GameBase.Game as QuaverGame;

            if (game?.CurrentScreen is IHasLeftPanel screen && screen.ActiveLeftPanel.Value != SelectContainerPanel.MapPreview)
                return;

            foreach (var keySound in hitObject.KeySounds)
                CustomAudioSampleCache.Play(keySound.Sample - 1, keySound.Volume);
        }

        /// <summary>
        ///     v2: Optimized math for beat snaps
        /// </summary>
        public static int GetBeatSnap(HitObjectInfo info, TimingPointInfo timingPoint)
        {
            double pos = info.StartTime - timingPoint.StartTime;
            double beatlength = 60000 / timingPoint.Bpm;

            // Optimization: Use multiplication instead of division where possible
            int index = (int)Math.Round(48 * pos / beatlength, MidpointRounding.AwayFromZero);

            // Unrolled loop for common snaps to avoid array allocation overhead in tight loops
            if (index % 48 == 0) return 0; // 1/1
            if (index % 24 == 0) return 1; // 1/2
            if (index % 16 == 0) return 2; // 1/3
            if (index % 12 == 0) return 3; // 1/4
            
            for (var i = 4; i < 8; i++)
            {
                if (index % BeatSnaps[i] == 0) return i;
            }

            return 8;
        }
    }
}