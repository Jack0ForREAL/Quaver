using System;
using System.Collections.Generic;
using Quaver.API.Maps.Structures;
using Quaver.Shared.Config;
using Quaver.Shared.Screens.Gameplay.Rulesets.HitObjects;
using Quaver.Shared.Screens.Gameplay.Rulesets.Keys.Playfield;

namespace Quaver.Shared.Screens.Gameplay.Rulesets.Keys.HitObjects
{
    /// <summary>
    ///     Controls a <see cref="HitObjectInfo"/>
    /// </summary>
    public abstract class NoteControllerKeys : NoteController
    {
        private HitObjectState _state;

        public HitObjectState State
        {
            get => _state;
            set
            {
                // v2: Optimization - Check for Dead state explicitly before accessing HitObject to save a null check cycle
                if (value == HitObjectState.Dead)
                    HitObject?.Kill();

                _state = value;
            }
        }

        public GameplayHitObjectKeys HitObject { get; private set; }
        public HitObjectInfo HitObjectInfo { get; private set; }
        public ScrollDirection ScrollDirection { get; private set; }
        
        // v2: Optimization - Cache these locally to avoid property lookups
        public int StartTime; 
        public int Lane;
        public int EndTime;
        public bool IsLongNote;

        protected HitObjectManagerKeys Manager { get; set; }
        public TimingGroupControllerKeys TimingGroupController { get; protected set; }

        public bool LegacyLNRendering => Manager.LegacyLNRendering;

        public NoteState NoteState { get; protected set; }

        public long InitialTrackPosition
        {
            get => NoteState.InitialTrackPosition;
            protected set => NoteState = NoteState with { InitialTrackPosition = value };
        }

        public long EndTrackPosition
        {
            get => NoteState.EndTrackPosition;
            protected set => NoteState = NoteState with { EndTrackPosition = value };
        }

        public long LatestTrackPosition { get; protected set; }
        public long EarliestTrackPosition { get; protected set; }

        public long EarliestHeldPosition
        {
            get => NoteState.EarliestHeldPosition;
            protected set => NoteState = NoteState with { EarliestHeldPosition = value };
        }

        public long LatestHeldPosition
        {
            get => NoteState.LatestHeldPosition;
            protected set => NoteState = NoteState with { LatestHeldPosition = value };
        }

        public abstract bool ShouldFlipLongNoteEnd { get; }

        // v2: Optimization - Direct field access for ScrollSpeed to avoid double indirection
        public virtual float CurrentLongNoteBodySize => (LatestHeldPosition - EarliestHeldPosition) *
            TimingGroupController.ScrollSpeed / HitObjectManagerKeys.TrackRounding;

        protected NoteControllerKeys(HitObjectInfo hitObjectInfo, TimingGroupControllerKeys timingGroupController,
            GameplayHitObjectKeys hitObject)
        {
            Initialize(hitObjectInfo, timingGroupController, hitObject);
        }

        /// <summary>
        ///     v2: Separated init logic for Object Pooling reuse
        /// </summary>
        public void Initialize(HitObjectInfo hitObjectInfo, TimingGroupControllerKeys timingGroupController, GameplayHitObjectKeys hitObject)
        {
            HitObjectInfo = hitObjectInfo;
            TimingGroupController = timingGroupController;
            HitObject = hitObject;
            Manager = TimingGroupController.Manager;
            
            // v2: Cache basic info fields
            StartTime = HitObjectInfo.StartTime;
            Lane = HitObjectInfo.Lane;
            EndTime = HitObjectInfo.EndTime;
            IsLongNote = HitObjectInfo.IsLongNote;

            var playfield = (GameplayPlayfieldKeys)Manager.Ruleset.Playfield;
            ScrollDirection = playfield.ScrollDirections[HitObjectInfo.Lane - 1];
        }

        /// <summary>
        ///     v2: Object Pooling Reset
        ///     Cleans the controller so it can be put back into the pool.
        /// </summary>
        public override void Reset()
        {
            base.Reset(); // Resets IsVisible, IsJudged
            _state = HitObjectState.Alive;
            
            // Note: We do NOT unlink HitObject here automatically; the manager handles sprite lifecycle.
            // Reset state struct
            NoteState = default;
            
            // Clear references to help GC (optional, but good practice)
            // HitObject = null; // Handled by Unlink() usually
        }

        public abstract void UpdatePositions(double curTime);

        public abstract void UpdateLongNoteSize(double curTime);

        public void Link(GameplayHitObjectKeys hitObject)
        {
            HitObject = hitObject;
            HitObject.InitializeObject(Manager, this);
        }

        public GameplayHitObjectKeys Unlink()
        {
            if (HitObject is null)
                throw new InvalidOperationException("GameplayHitObjectInfo is not linked to a GameplayHitObjectKeys");

            HitObject.Hide();
            var temp = HitObject;
            HitObject = null;
            return temp;
        }

        public abstract float GetSpritePosition(float hitPosition, float initialPos);
        public abstract void InitializeLongNoteSize();

        public bool InRange()
        {
            // v2: This check is critical. 
            // The TimingGroupController will eventually use the IsVisible flag we added to the base class
            // to skip this check entirely if the note is known to be far off-screen.
            return TimingGroupController.InRange(this);
        }
    }

    public enum HitObjectState
    {
        Alive,
        Held,
        Dead,
        Removed
    }
}