
namespace Quaver.Shared.Screens.Gameplay.Rulesets.HitObjects
{
    /// <summary>
    ///     v2: Enhanced NoteController
    ///     Includes culling flags to help the renderer skip processing for off-screen notes.
    /// </summary>
    public abstract class NoteController
    {
        /// <summary>
        ///     Is this note currently visible on the screen?
        ///     Used for Culling/Optimization in the Draw loop.
        /// </summary>
        public bool IsVisible { get; set; }

        /// <summary>
        ///     Has this note already been judged/hit?
        ///     Prevents redundant logic checks.
        /// </summary>
        public bool IsJudged { get; set; }

        /// <summary>
        ///     For Object Pooling: Resets the state of the note so it can be reused.
        /// </summary>
        public virtual void Reset()
        {
            IsVisible = false;
            IsJudged = false;
        }
    }
}