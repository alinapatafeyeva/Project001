using UnityEngine;

namespace Project001.Gameplay.Presentation
{
    /// <summary>
    /// Fits the attached orthographic Camera to GameplayLayout's fixed
    /// composition, using the real runtime Screen.width/Screen.height — so
    /// the same scene frames correctly on a 9:16 phone, a 9:19.5 phone, or
    /// any other portrait aspect, without per-device tuning. Composition
    /// itself never changes: this component only ever adapts orthographic
    /// size (and the resulting vertical position) to the current aspect
    /// ratio, never to how much content currently exists in the scene — it
    /// reads no Renderer, no generated collector, and no level data.
    ///
    /// Safe to run in Awake: unlike a bounds-measuring approach, this has no
    /// dependency on anything else having generated content first, so it
    /// carries no script-execution-order requirement.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class PortraitCameraFitter : MonoBehaviour
    {
        private void Awake()
        {
            Fit(Screen.width, Screen.height);
        }

        /// <summary>
        /// Applies GameplayLayout's fixed frame to this camera for the given
        /// screen size. Exposed (not just called from Awake) so a future
        /// resolution/orientation-change handler could call it again without
        /// duplicating this logic.
        /// </summary>
        public void Fit(float screenWidth, float screenHeight)
        {
            var orthoCamera = GetComponent<Camera>();
            orthoCamera.orthographicSize = GameplayLayout.ComputeOrthographicSize(screenWidth, screenHeight);

            Vector3 position = transform.position;
            position.y = GameplayLayout.CameraVerticalCenter;
            transform.position = position;
        }
    }
}
