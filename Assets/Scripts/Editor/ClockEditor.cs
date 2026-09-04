#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System;
using System.Linq;

namespace RobotSNAP.Core
{
    /// <summary>
    /// Custom editor for Clock to provide a dropdown for time zone selection.
    /// </summary>
    [CustomEditor(typeof(Clock))]
    public class ClockEditor : Editor
    {
        private string[] _timeZoneIds;
        private int _selectedIndex;

        private void OnEnable()
        {
            RefreshTimeZoneList();
        }

        private void RefreshTimeZoneList()
        {
            try
            {
                var timeZones = TimeZoneInfo.GetSystemTimeZones();
                _timeZoneIds = timeZones.Select(tz => tz.Id).ToArray();
                if (_timeZoneIds.Length == 0)
                {
                    _timeZoneIds = new[] { "UTC" };
                }

                Clock clock = (Clock)target;
                _selectedIndex = Array.IndexOf(_timeZoneIds, clock.GetTimeZoneId());
                if (_selectedIndex < 0) _selectedIndex = 0;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"ClockEditor: Failed to retrieve system time zones: {e.Message}. Falling back to UTC.");
                _timeZoneIds = new[] { "UTC" };
                _selectedIndex = 0;
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Draw all properties except _timeZoneId (we handle it manually)
            SerializedProperty property = serializedObject.GetIterator();
            property.NextVisible(true);
            while (property.NextVisible(false))
            {
                if (property.name == "_timeZoneId") continue;
                EditorGUILayout.PropertyField(property, true);
            }

            // Time zone selector
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Time Zone", EditorStyles.boldLabel);

            if (_timeZoneIds == null || _timeZoneIds.Length == 0)
                RefreshTimeZoneList();

            Clock clock = (Clock)target;
            int newIndex = EditorGUILayout.Popup("Time Zone ID", _selectedIndex, _timeZoneIds);
            if (newIndex != _selectedIndex)
            {
                _selectedIndex = newIndex;
                string newId = _timeZoneIds[newIndex];
                clock.SetTimeZone(newId);
                EditorUtility.SetDirty(clock);
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
#endif