using UnityEditor;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;


namespace ROS_DRL
{
    [CustomEditor(typeof(EnvController))]
    public class EnvControllerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EnvController controller = (EnvController)target;

            GUI.enabled = Application.isPlaying; // Désactive le bouton si la scène n’est pas en PlayMode
            if (GUILayout.Button("Reset Scene"))
            {
                if (controller != null)
                {
                    controller.EditorResetScene(); // Appelle une méthode publique qui démarre la coroutine
                }
            }
            if (GUILayout.Button("Play/Pause"))
            {
                if (controller != null)
                {
                    controller.EditorPausePlayScene(); // Appelle une méthode publique qui démarre la coroutine
                }
            }
            GUI.enabled = true; // Réactive les contrôles après
        }
    }
}