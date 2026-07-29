using System;
using UnityEngine;

namespace Project001.Gameplay.Presentation
{
    /// <summary>
    /// One complete set of sprites for a single monster skin (e.g. Purple,
    /// Green, Orange). Holds only sprite references — no color tinting, no
    /// gameplay data. All five sprites are consumed by CollectorPresentation,
    /// which CollectorQueueBoard.GenerateBoard resolves this skin for and
    /// passes every field of it into.
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
        [SerializeField, Tooltip("Shown while a collector waits anywhere — CollectorQueueBoard, Waiting Line, or Recovery Row — with idle breathing.")]
        private Sprite backIdle;

        [SerializeField, Tooltip("Not currently shown by the gameplay presentation lifecycle. Kept for possible future use.")]
        private Sprite frontIdle;

        [SerializeField, Tooltip("Shown for the entire time a collector actively rides the Conveyor, from boarding through every normal bite, including the mouth-open moment before the final bite's completion sequence.")]
        private Sprite frontEating;

        [SerializeField, Tooltip("Shown briefly, with a happy reaction, right after this collector's final pixel is consumed.")]
        private Sprite frontSatisfied;

        [SerializeField, Tooltip("Shown as this collector's visual completion: one pulse, then a collapse, before it is destroyed.")]
        private Sprite heart;

        public Sprite BackIdle => backIdle;

        public Sprite FrontIdle => frontIdle;

        public Sprite FrontEating => frontEating;

        public Sprite FrontSatisfied => frontSatisfied;

        public Sprite Heart => heart;
    }
}
