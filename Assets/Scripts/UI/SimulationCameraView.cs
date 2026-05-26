using UnityEngine;
using UnityEngine.UIElements;

public class SimulationCameraView : MonoBehaviour
{
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private Camera simulationCamera;

    private VisualElement _cameraContainer;
    private Camera _activeCamera;
    private RenderTexture _currentRenderTexture;

    private void OnEnable()
    {
        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();

        if (uiDocument == null)
        {
            Debug.LogError("SimulationCameraView: Aucun UIDocument trouvé !");
            return;
        }

        // Recherche périodique du CameraContainer (qui est chargé dynamiquement)
        uiDocument.rootVisualElement.schedule.Execute(() =>
        {
            if (_cameraContainer == null)
            {
                _cameraContainer = uiDocument.rootVisualElement.Q<VisualElement>("CameraContainer");
                if (_cameraContainer != null)
                {
                    InitializeCamera();
                }
            }
        }).Every(50); // vérifie toutes les 50ms
    }

    private void InitializeCamera()
    {
        // Récupérer ou créer la caméra
        if (simulationCamera == null)
        {
            GameObject camGO = new GameObject("SimulationCamera");
            _activeCamera = camGO.AddComponent<Camera>();
            _activeCamera.clearFlags = CameraClearFlags.Skybox;
        }
        else
        {
            _activeCamera = simulationCamera;
        }

        // Abonnement au redimensionnement du conteneur
        _cameraContainer.RegisterCallback<GeometryChangedEvent>(OnCameraContainerResized);
        
        // Création initiale de la RenderTexture
        UpdateRenderTextureSize();
    }

    private void OnCameraContainerResized(GeometryChangedEvent evt)
    {
        UpdateRenderTextureSize();
    }

    private void UpdateRenderTextureSize()
    {
        if (_cameraContainer == null || _activeCamera == null)
            return;

        float width = _cameraContainer.resolvedStyle.width;
        float height = _cameraContainer.resolvedStyle.height;

        if (width <= 0 || height <= 0)
            return;

        int w = Mathf.Max(1, (int)width);
        int h = Mathf.Max(1, (int)height);

        // Libérer l'ancienne texture
        if (_currentRenderTexture != null)
        {
            _activeCamera.targetTexture = null;
            _currentRenderTexture.Release();
            Destroy(_currentRenderTexture);
        }

        // Créer la nouvelle RenderTexture
        _currentRenderTexture = new RenderTexture(w, h, 24);
        _currentRenderTexture.Create();
        _activeCamera.targetTexture = _currentRenderTexture;

        // Appliquer la texture en arrière-plan du VisualElement
        _cameraContainer.style.backgroundImage = Background.FromRenderTexture(_currentRenderTexture);
    }

    private void OnDisable()
    {
        // Nettoyage
        if (_activeCamera != null && _currentRenderTexture != null)
        {
            _activeCamera.targetTexture = null;
            _currentRenderTexture.Release();
            Destroy(_currentRenderTexture);
        }
    }

    // Méthode publique pour changer de caméra dynamiquement
    public void SetCamera(Camera newCamera)
    {
        if (_activeCamera != null && _currentRenderTexture != null)
            _activeCamera.targetTexture = null;

        _activeCamera = newCamera;

        if (_activeCamera != null && _cameraContainer != null)
            UpdateRenderTextureSize();
    }

    public void ForceRefresh()
    {
        if (_cameraContainer != null && _activeCamera != null)
        {
            UpdateRenderTextureSize();  // Recrée la texture si nécessaire
        }
        else
        {
            // Si CameraContainer a été perdu (réinitialisation), on le recherche
            if (uiDocument != null)
            {
                _cameraContainer = uiDocument.rootVisualElement.Q<VisualElement>("CameraContainer");
                if (_cameraContainer != null && _activeCamera == null)
                    InitializeCamera();
                else if (_cameraContainer != null)
                    UpdateRenderTextureSize();
            }
        }
    }
}