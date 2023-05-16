#if UNITY_EDITOR
using UnityEngine;
using VRC.Dynamics;
using UnityEditor;
using System.Reflection;

namespace VRC.SDK3.Dynamics.PhysBone
{
    [AddComponentMenu("")]
    public class PhysBoneGrabHelper : MonoBehaviour
    {
        Camera currentCamera;

        System.Type gameViewType;
        FieldInfo targetDisplayField;

        void Update()
        {
            currentCamera = FindCamera();

            //Process mouse input
            SetMouseDown(Input.GetMouseButton(0));
            if (mouseIsDown && Input.GetMouseButtonDown(1))
            {
                if(grab != null)
                {
                    PhysBoneManager.Inst.ReleaseGrab(grab, true);
                    grab = null;
                }
            }
            UpdateGrab();
        }
        void Start()
        {
            var assembly = typeof(EditorWindow).Assembly;
            gameViewType = assembly.GetType("UnityEditor.PlayModeView");
            targetDisplayField = gameViewType.GetField("m_TargetDisplay", BindingFlags.DeclaredOnly | BindingFlags.NonPublic | BindingFlags.Instance);
        }

        //In Unity 2021.2 this can be replaced with Display.activeEditorGameViewTarget, allowing us to remove all dependencies on UnityEditor
        Camera FindCamera()
        {
            //Get target display
            var gameView = UnityEditor.EditorWindow.focusedWindow;
            if(gameView == null || !gameViewType.IsInstanceOfType(gameView))
                return null;
            var targetDisplay = (int)targetDisplayField.GetValue(gameView);

            //Find camera with matching target
            Camera result = null;
            var cameras = Camera.allCameras;
            foreach(var camera in cameras) //Get the last active camera
            {
                if(camera.isActiveAndEnabled && camera.targetDisplay == targetDisplay)
                    result = camera;
            }
            return result;
        }

        bool mouseIsDown = false;
        PhysBoneManager.Grab grab;
        Vector3 grabOrigin;
        void SetMouseDown(bool state)
        {
            if (state == mouseIsDown)
                return;

            mouseIsDown = state;

            if (mouseIsDown)
            {
                var ray = GetMouseRay();
                grab = PhysBoneManager.Inst.AttemptGrab(-1, ray, out grabOrigin);
                #if VERBOSE_LOGGING
                if (grab != null)
                {
                    Debug.Log($"Grabbing - Chain:{grab.chainId} Bone:{grab.bone}");
                }
                #endif
            }
            else
            {
                if (grab != null)
                {
                    PhysBoneManager.Inst.ReleaseGrab(grab);
                    grab = null;
                }
            }
        }
        Ray GetMouseRay()
        {
            if(currentCamera != null)
                return currentCamera.ScreenPointToRay(Input.mousePosition);
            else
                return default;
        }
        void UpdateGrab()
        {
            if(currentCamera == null)
                return;

            if (grab != null)
            {
                var ray = GetMouseRay();
                Vector3 hit;
                if (PlaneLineIntersection(grabOrigin, -currentCamera.transform.forward, ray.origin, ray.origin + ray.direction * 1000f, out hit))
                {
                    grab.globalPosition = hit + (Vector3)grab.localOffset;
                }
            }
        }
        public static bool PlaneLineIntersection(Vector3 planeOrigin, Vector3 planeNormal, Vector3 lineA, Vector3 lineB, out Vector3 hit)
        {
            float delta;

            //Make sure the line is not parallel
            delta = Vector3.Dot(planeNormal, (lineB - lineA) - planeOrigin);
            if (delta == 0.0f)
            {
                hit = Vector3.zero;
                return false;
            }

            //Find the delta
            delta = Vector3.Dot(planeNormal, lineA - planeOrigin) / delta;
            hit = lineA + ((lineB - lineA) * -delta);
            return true;
        }
    }
}
#endif