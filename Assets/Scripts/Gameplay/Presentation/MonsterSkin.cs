using System;
using UnityEngine;

namespace Project001.Gameplay.Presentation
{
    /// <summary>
    /// One complete set of sprites for a single monster skin (e.g. Purple,
    /// Green, Orange). Holds only sprite references — no color tinting, no
    /// gameplay data. Only BackIdle and FrontIdle are consumed by any
    /// gameplay code today (CollectorPresentation); FrontEating,
    /// FrontSatisfied, and Heart are captured now so the data model does not
    /// need to change again when those states are wired up later.
    ///
    /// A plain serializable class rather than a MonoBehaviour or
    /// ScriptableObject: this project configures every runtime reference via
    /// [SerializeField] + BootstrapSceneCreator's SerializedObject wiring, and
    /// a MonsterSkin only ever needs to live nested inside a
    /// MonsterSkinDatabase entry, never as its own free-standing object.
    /// </summary>
    [Serializable]
    public sealed class MonsterSkin
    {
        [SerializeField, Tooltip("Shown while a collector is queued on CollectorQueueBoard.")]
        private Sprite backIdle;

        [SerializeField, Tooltip("Shown from the moment a collector boards the Conveyor onward (Conveyor, Waiting Line, Recovery Row).")]
        private Sprite frontIdle;

        [SerializeField, Tooltip("Reserved for a future eating-state sprite swap. Not yet applied by any gameplay code.")]
        private Sprite frontEating;

        [SerializeField, Tooltip("Reserved for a future satisfied-state sprite swap. Not yet applied by any gameplay code.")]
        private Sprite frontSatisfied;

        [SerializeField, Tooltip("Reserved for a future reward/VFX use. Not yet applied by any gameplay code.")]
        private Sprite heart;

        public Sprite BackIdle => backIdle;

        public Sprite FrontIdle => frontIdle;

        public Sprite FrontEating => frontEating;

        public Sprite FrontSatisfied => frontSatisfied;

        public Sprite Heart => heart;
    }
}
