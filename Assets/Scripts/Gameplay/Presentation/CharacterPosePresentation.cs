using System;
using System.Collections;
using UnityEngine;

namespace Project001.Gameplay.Presentation
{
    /// <summary>
    /// The two presentation-level poses a configured character can be asked
    /// to show. Not species-specific by name on purpose - gameplay code
    /// (CollectorPresentation) only ever requests one of these two states,
    /// never a bone name or rotation; which bones (if any) actually move,
    /// and by how much, is entirely this component's own configured data.
    /// </summary>
    public enum CharacterPresentationPose
    {
        Waiting,
        Conveyor,
    }

    /// <summary>
    /// Generic, species-agnostic presentation-only bone pose blender. Lives
    /// on a CharacterVisual instance (see CollectorView.Initialize) and
    /// blends a fixed, explicit set of bone local rotations between two
    /// named absolute poses on request - never a bone-name lookup, never a
    /// cumulative/relative rotation, never anything gameplay code has to
    /// know about beyond the CharacterPresentationPose enum.
    ///
    /// Configured once, at build time, by CharacterAssetBuilder (currently
    /// only for Crab, whose claws otherwise render mid-swing-wide while
    /// waiting - see BonePose's own remarks). A CharacterVisual with no
    /// bonePoses configured (every other species today) is a silent no-op:
    /// SetPose returns immediately without starting a coroutine or touching
    /// any transform, so those species behave exactly as before this
    /// component existed.
    ///
    /// Deliberately absolute, not delta-based, unlike the build-time-only
    /// adjustment this replaces (CharacterAssetBuilder's old
    /// ApplyCrabClawPoseAdjustment multiplied a fixed delta onto whatever
    /// rotation a bone already had): each BonePose entry stores the two
    /// complete target rotations directly, so applying a pose can never
    /// accumulate drift no matter how many times or how rapidly it is
    /// requested.
    /// </summary>
    public class CharacterPosePresentation : MonoBehaviour
    {
        /// <summary>
        /// One controlled bone plus its two absolute target local rotations.
        /// A plain field, not a property, so CharacterAssetBuilder can build
        /// these directly as struct literals; Unity serializes both the
        /// Transform reference and the two Quaternions as ordinary prefab
        /// data, so a saved Character_XX.prefab carries this configuration
        /// with it - no runtime bone-name lookup ever happens again after
        /// build time.
        /// </summary>
        [Serializable]
        public struct BonePose
        {
            public Transform bone;
            public Quaternion waitingLocalRotation;
            public Quaternion conveyorLocalRotation;
        }

        [SerializeField, Tooltip("Seconds a pose change takes to blend in, so a claw fold/unfold reads as a small deliberate motion rather than a harsh snap. Applied uniformly to every configured bone.")]
        [Min(0f)]
        private float blendDurationSeconds = 0.16f;

        [SerializeField]
        private BonePose[] bonePoses = Array.Empty<BonePose>();

        // Unset until the first SetPose call - a freshly instantiated
        // CharacterVisual's bones already sit at each entry's own
        // conveyorLocalRotation (the authored, unmodified pose
        // CharacterAssetBuilder captured before ever touching the bone), so
        // there is nothing to blend FROM until gameplay actually requests a
        // pose for the first time.
        private CharacterPresentationPose? _targetPose;
        private Coroutine _blendRoutine;

        /// <summary>
        /// True only when this instance was actually configured with at
        /// least one controlled bone - CollectorPresentation does not need
        /// to check this itself (SetPose is already a safe no-op when
        /// false), but it is exposed for callers that want to branch on
        /// whether a species has claw-pose presentation at all.
        /// </summary>
        public bool HasConfiguredPoses => bonePoses.Length > 0;

        /// <summary>
        /// The pose this instance is currently showing or blending toward -
        /// null only before the first-ever SetPose call. Read-only; exposed
        /// so tests and tooling can verify a transition actually happened
        /// without reaching into bone transforms directly (see
        /// CollectorView.HungerDisplayText for the same pattern elsewhere in
        /// this codebase). Never read by gameplay code itself - SetPose's
        /// own idempotency check is what actually needs this value at
        /// runtime, via the private _targetPose field this mirrors.
        /// </summary>
        public CharacterPresentationPose? CurrentPose => _targetPose;

        /// <summary>
        /// Build-time-only configuration entry point - called exactly once
        /// per instance, by CharacterAssetBuilder, before the assembled
        /// prefab is saved. Never called at runtime.
        /// </summary>
        public void Configure(BonePose[] poses, float blendSeconds)
        {
            bonePoses = poses ?? Array.Empty<BonePose>();
            blendDurationSeconds = blendSeconds;
        }

        /// <summary>
        /// Requests this character show the given pose, blending every
        /// configured bone from its current live rotation to that pose's
        /// stored target over blendDurationSeconds. Idempotent: calling with
        /// the pose already active (whether settled or still mid-blend
        /// toward it) is a no-op, so CollectorPresentation can call this
        /// freely from ShowWaitingBackIdle/ShowConveyorFrontEating without
        /// worrying about redundant calls restarting or glitching an
        /// in-flight blend. Safe to interrupt: calling with the OTHER pose
        /// while a blend is still running stops it and starts a new blend
        /// from wherever the bones actually are that frame, never from a
        /// stale cached start - so rapid back-to-back requests (e.g. a
        /// boarding attempt immediately rolled back) can never leave a bone
        /// mid-way between poses or drift from either target.
        /// </summary>
        public void SetPose(CharacterPresentationPose pose)
        {
            if (bonePoses.Length == 0)
                return;

            if (_targetPose == pose)
                return;

            _targetPose = pose;

            if (_blendRoutine != null)
                StopCoroutine(_blendRoutine);

            _blendRoutine = blendDurationSeconds > 0f
                ? StartCoroutine(BlendRoutine(pose))
                : null;

            if (_blendRoutine == null)
                ApplyImmediate(pose);
        }

        private void ApplyImmediate(CharacterPresentationPose pose)
        {
            foreach (BonePose entry in bonePoses)
            {
                if (entry.bone == null)
                    continue;

                entry.bone.localRotation = pose == CharacterPresentationPose.Waiting
                    ? entry.waitingLocalRotation
                    : entry.conveyorLocalRotation;
            }
        }

        /// <summary>
        /// Runs only while a blend is actually in flight - once every bone
        /// reaches its exact target rotation this coroutine ends and clears
        /// _blendRoutine, so there is no per-frame cost at all while a pose
        /// is stable, matching every other presentation routine in this
        /// codebase (see CollectorAnimation's own reaction coroutines).
        /// Captures each bone's LIVE rotation as the blend start on this
        /// exact frame, never a value cached from an earlier call, so
        /// interrupting a blend already in progress (see SetPose) always
        /// resumes smoothly from wherever the bone visually is.
        /// </summary>
        private IEnumerator BlendRoutine(CharacterPresentationPose pose)
        {
            int count = bonePoses.Length;
            var startRotations = new Quaternion[count];
            for (int i = 0; i < count; i++)
                startRotations[i] = bonePoses[i].bone != null ? bonePoses[i].bone.localRotation : Quaternion.identity;

            float elapsed = 0f;
            while (elapsed < blendDurationSeconds)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / blendDurationSeconds);

                for (int i = 0; i < count; i++)
                {
                    Transform bone = bonePoses[i].bone;
                    if (bone == null)
                        continue;

                    Quaternion target = pose == CharacterPresentationPose.Waiting
                        ? bonePoses[i].waitingLocalRotation
                        : bonePoses[i].conveyorLocalRotation;
                    bone.localRotation = Quaternion.Slerp(startRotations[i], target, t);
                }

                yield return null;
            }

            ApplyImmediate(pose);
            _blendRoutine = null;
        }
    }
}
