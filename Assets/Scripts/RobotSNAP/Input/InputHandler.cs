using UnityEngine;
using RobotSNAP.Core;

namespace RobotSNAP.Core
{
    /// <summary>
    /// Gère les entrées clavier pour contrôler la simulation.
    /// Toutes les actions sont publiées sur l'EventBus.
    /// </summary>
    public class InputHandler : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private bool enabledInPlayMode = true;

        private void Update()
        {
            if (!Application.isPlaying || !enabledInPlayMode) return;
            HandleInput();
        }

        private void HandleInput()
        {
            // Touche R : Reset complet (Stop + Start)
            if (Input.GetKeyDown(KeyCode.R))
            {
                Debug.Log("[InputHandler] Reset requested (R key)");
                // On arrête puis on redémarre la simulation
                EventBus.Instance.Publish(new StopSimulationCommand());
                EventBus.Instance.Publish(new StartSimulationCommand());
            }

            // Touche C : Créer un environnement (commande générique, peut être utilisée ailleurs)
            if (Input.GetKeyDown(KeyCode.C))
            {
                Debug.Log("[InputHandler] Create environment requested (C key)");
                EventBus.Instance.Publish(new CreateEnvironmentCommand()); // à définir si besoin
                // Sinon, on peut ignorer cette touche ou la réaffecter
            }

            // Touche Espace : Pause / Resume (toggle)
            if (Input.GetKeyDown(KeyCode.Space))
            {
                Debug.Log("[InputHandler] Toggle pause requested (Space)");
                // On consulte le Supervisor pour connaître l'état actuel
                var supervisor = Supervisor.Instance;
                if (supervisor != null)
                {
                    if (supervisor.IsPaused)
                        EventBus.Instance.Publish(new ResumeSimulationCommand());
                    else
                        EventBus.Instance.Publish(new PauseSimulationCommand());
                }
                else
                {
                    Debug.LogWarning("[InputHandler] Supervisor not available, cannot toggle pause.");
                }
            }
        }
    }

    // Optionnel : si tu veux conserver la touche C pour autre chose
    public struct CreateEnvironmentCommand { }
}