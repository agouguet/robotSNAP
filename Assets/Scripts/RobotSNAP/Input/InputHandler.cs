// Scripts/RobotSNAP/Core/InputHandler.cs
using UnityEngine;

namespace RobotSNAP.Core
{
    /// <summary>
    /// Gère les entrées clavier pour le contrôle de la simulation
    /// </summary>
    public class InputHandler : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private EnvironmentManager environmentManager;
        [SerializeField] private Clock clock;
        
        [Header("Settings")]
        [SerializeField] private bool enabledInPlayMode = true;
        
        public event System.Action OnResetRequested;
        public event System.Action OnCreateRequested;
        public event System.Action OnDestroyRequested;
        public event System.Action OnPauseRequested;
        
        private void Update()
        {
            if (!Application.isPlaying || !enabledInPlayMode) return;
            
            HandleInput();
        }
        
        private void HandleInput()
        {
            if (Input.GetKeyDown(KeyCode.R))
            {
                OnResetRequested?.Invoke();
                environmentManager?.ResetAllEnvironments();
            }
            
            if (Input.GetKeyDown(KeyCode.C))
            {
                OnCreateRequested?.Invoke();
            }
            
            // if (Input.GetKeyDown(KeyCode.Delete))
            // {
            //     OnDestroyRequested?.Invoke();
            //     environmentManager?.ClearAllEnvironments();
            // }
            
            if (Input.GetKeyDown(KeyCode.Space))
            {
                OnPauseRequested?.Invoke();
                clock?.TogglePause();
            }
        }
    }
}