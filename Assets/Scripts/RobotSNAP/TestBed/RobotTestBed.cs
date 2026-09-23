using System.Collections.Generic;
using RobotSNAP.Agents;
using UnityEngine;

namespace RobotSNAP.TestBed
{
    /// <summary>
    /// The foreman of a test course: it decides which robot the arrow keys are driving, and keeps the view on
    /// that robot.
    ///
    /// A course exists to be driven, and none of the application is here to do it - no simulation view, no
    /// agent list, no scenario. What is left is the part that drives a robot, the
    /// <see cref="RobotInputController"/> every prefab carries, and this component, which hands the keys to
    /// one robot at a time so that a course carrying five robots is five robots to try rather than five
    /// robots moving at once.
    ///
    /// <b>1..9</b> pick a robot, <b>Tab</b> takes the next one, the <b>arrows</b> drive it and <b>Space</b>
    /// stops it. The <b>left mouse button</b> orbits the view, the <b>wheel</b> zooms. A robot that is not
    /// the selected one has its controller switched off rather than ignored, so nothing else on the course
    /// reads the keyboard; the robot left behind is stopped, because a robot that keeps a command nobody is
    /// giving it any more is the one thing a course must not do.
    /// </summary>
    [DisallowMultipleComponent]
    public class RobotTestBed : MonoBehaviour
    {
        [Tooltip("Every robot of the course, in the order the number keys pick them.")]
        [SerializeField] private List<RobotInputController> drivers = new List<RobotInputController>();

        [Tooltip("The camera the course is watched through. Left empty, the main camera is taken.")]
        [SerializeField] private Camera view;

        [Header("View")]
        [Tooltip("How far behind the driven robot the view sits, in metres.")]
        [SerializeField] private float distance = 6f;

        [Tooltip("How high above it the view sits, in metres.")]
        [SerializeField] private float height = 2.6f;

        [Tooltip("How fast the view turns while the left mouse button is held, in degrees per pixel.")]
        [SerializeField] private float orbitSpeed = 0.3f;

        [Tooltip("How much of the distance one notch of the wheel takes away, as a fraction.")]
        [SerializeField] private float zoomStep = 0.12f;

        [Tooltip("How fast the view catches up with the robot it follows, in 1/s. Zero follows nothing.")]
        [SerializeField] private float followLag = 8f;

        [Tooltip("Where the view is aimed on the robot: the height above its base it looks at, in metres.")]
        [SerializeField] private float aimHeight = 0.4f;

        /// <summary>Which robot of the list the keys are driving, and the one the view follows.</summary>
        private int _selected = -1;

        /// <summary>Where the view stands around that robot: the angle it has been turned to, and its distance.</summary>
        private float _yaw;
        private float _pitch = 20f;
        private float _zoom = 1f;

        /// <summary>The pivot the view orbits, and the camera it is put on.</summary>
        private Vector3 _aim;

        /// <summary>Whether the view has been placed at all: the first frame snaps to the robot, the rest follow it.</summary>
        private bool _placed;

        private void Reset()
        {
            GatherDrivers();
        }

        private void Awake()
        {
            if (drivers == null || drivers.Count == 0)
                GatherDrivers();

            drivers.RemoveAll(driver => driver == null);

            if (view == null)
                view = Camera.main;
        }

        private void Start()
        {
            Select(0);
        }

        private void Update()
        {
            if (drivers.Count == 0)
                return;

            for (int key = 0; key < 9; key++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + key) && key < drivers.Count)
                    Select(key);
            }

            if (Input.GetKeyDown(KeyCode.Tab))
                Select((_selected + 1) % drivers.Count);

            if (Input.GetMouseButton(0))
            {
                _yaw += Input.GetAxis("Mouse X") * orbitSpeed * 20f;
                _pitch = Mathf.Clamp(_pitch - Input.GetAxis("Mouse Y") * orbitSpeed * 20f, -5f, 80f);
            }

            float wheel = Input.GetAxis("Mouse ScrollWheel");
            if (!Mathf.Approximately(wheel, 0f))
                _zoom = Mathf.Clamp(_zoom * (1f - wheel * zoomStep * 10f), 0.25f, 6f);
        }

        private void LateUpdate()
        {
            if (view == null || _selected < 0 || _selected >= drivers.Count)
                return;

            Transform robot = drivers[_selected].transform;
            Vector3 aim = robot.position + Vector3.up * aimHeight;

            if (!_placed || followLag <= 0f)
            {
                _aim = aim;
                _placed = true;
            }
            else
            {
                _aim = Vector3.Lerp(_aim, aim, 1f - Mathf.Exp(-followLag * Time.deltaTime));
            }

            Quaternion around = Quaternion.Euler(_pitch, robot.eulerAngles.y + _yaw, 0f);
            view.transform.position = _aim + around * new Vector3(0f, 0f, -distance * _zoom) + Vector3.up * height;
            view.transform.LookAt(_aim);
        }

        /// <summary>
        /// Hands the keys to one robot and takes them away from the others. The one that loses them is
        /// stopped: a robot nobody is driving any more must not keep the speed of the last key it saw.
        /// </summary>
        private void Select(int index)
        {
            if (drivers.Count == 0)
                return;

            index = Mathf.Clamp(index, 0, drivers.Count - 1);
            if (index == _selected)
                return;

            if (_selected >= 0 && _selected < drivers.Count)
            {
                drivers[_selected].enabled = false;
                Robot left = drivers[_selected].GetComponent<Robot>();
                if (left != null)
                    left.Stop();
            }

            _selected = index;
            drivers[_selected].enabled = true;

            // The view swings behind the robot that has just been picked, rather than sliding across the
            // course to it.
            _placed = false;
        }

        /// <summary>Every robot of this course, in the order of the hierarchy, for a course built by hand.</summary>
        private void GatherDrivers()
        {
            drivers = new List<RobotInputController>(FindObjectsByType<RobotInputController>(FindObjectsSortMode.None));
            drivers.Sort((left, right) => string.CompareOrdinal(left.name, right.name));
        }

        /// <summary>The course's legend, drawn where a scene with no interface of its own needs one.</summary>
        private void OnGUI()
        {
            if (drivers.Count == 0)
                return;

            var style = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true };
            GUILayout.BeginArea(new Rect(12f, 12f, 520f, 130f), GUI.skin.box);
            GUILayout.Label($"<b>{_selected + 1}/{drivers.Count}</b>  {drivers[_selected].name}", style);
            GUILayout.Label("1..9 pick a robot   Tab next", style);
            GUILayout.Label("Arrows drive   Space stops", style);
            GUILayout.Label("Left mouse orbits the view   Wheel zooms", style);
            GUILayout.EndArea();
        }
    }
}
