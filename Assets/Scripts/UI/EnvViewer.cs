using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

namespace RobotSNAP
{
    public class EnvViewer : MonoBehaviour
    {
        [Header("UI")]
        public Canvas mosaicCanvas;           // Canvas assigné (World ou Screen Space - Camera)
        public GameObject rawImagePrefab;     // Un prefab contenant un RawImage (assigné dans l’éditeur)

        [Header("RenderTexture")]
        public int textureWidth = 512;
        public int textureHeight = 512;

        private List<RenderTexture> rawImageTextures = new List<RenderTexture>();

        public GameObject cameraPanelPrefab;

        public int maxCameras = 12;

        void Start()
        {
            // Affiche la caméra sur Display 8
            Camera observerCamera = GetComponent<Camera>();
            // observerCamera.targetDisplay = 7;

            // Active le Display 8 si disponible
            if (Display.displays.Length > 7)
            {
                Display.displays[7].Activate();
            }

            SetupSurveillanceClones();
            GenerateMosaicUI();
        }

        void SetupSurveillanceClones()
        {
            Camera[] allCams = FindObjectsOfType<Camera>();
            int offset = 0;
            foreach (Camera cam in allCams)
            {
                if (cam == this.GetComponent<Camera>()) continue; // Skip caméra d'observation
                if (cam.targetDisplay >= 7)
                {
                    // offset += 1;
                    cam.targetDisplay = cam.targetDisplay + 1;
                }
                GameObject clone = Instantiate(cam.gameObject);
                clone.name = "SurveillanceClone_" + cam.name;

                // Récupère la caméra dans le clone (au cas où il y en a plusieurs composants)
                Camera cloneCam = clone.GetComponent<Camera>();

                // Ne pas afficher sur display
                cloneCam.targetDisplay = 7; // Non utilisé
                cloneCam.enabled = true;

                // Assigne une RenderTexture
                RenderTexture rt = new RenderTexture(textureWidth, textureHeight, 16);
                rt.Create();
                cloneCam.targetTexture = rt;

                rawImageTextures.Add(rt);
            }
        }

        void GenerateMosaicUI()
        {
            int camCount = Mathf.Min(rawImageTextures.Count, maxCameras);

            int maxCols = 4;
            int cols = Mathf.Min(camCount, maxCols);
            int rows = Mathf.CeilToInt((float)camCount / maxCols);

            float panelWidth = textureWidth;
            float panelHeight = textureHeight;

            RectTransform canvasRect = mosaicCanvas.GetComponent<RectTransform>();

            canvasRect.pivot = new Vector2(0, 1);
            canvasRect.anchorMin = new Vector2(0, 1);
            canvasRect.anchorMax = new Vector2(0, 1);

            canvasRect.sizeDelta = new Vector2(cols * panelWidth, rows * panelHeight);

            foreach (Transform child in mosaicCanvas.transform)
            {
                Destroy(child.gameObject);
            }

            for (int i = 0; i < camCount; i++)
            {
                RenderTexture rt = rawImageTextures[i];

                GameObject panelObj = Instantiate(cameraPanelPrefab, mosaicCanvas.transform);

                RectTransform panelRect = panelObj.GetComponent<RectTransform>();
                panelRect.pivot = new Vector2(0, 1);
                panelRect.anchorMin = new Vector2(0, 1);
                panelRect.anchorMax = new Vector2(0, 1);
                panelRect.sizeDelta = new Vector2(panelWidth, panelHeight);

                int row = i / maxCols;
                int col = i % maxCols;

                panelRect.anchoredPosition = new Vector2(
                    col * panelWidth,
                    -row * panelHeight
                );

                // Trouve le RawImage enfant dans ce prefab et assigne la texture
                RawImage rawImg = panelObj.GetComponentInChildren<RawImage>();
                rawImg.texture = rt;


                // GameObject imgObj = Instantiate(rawImagePrefab, mosaicCanvas.transform);
                // RawImage rawImg = imgObj.GetComponent<RawImage>();
                // rawImg.texture = rt;

                // RectTransform rtImg = imgObj.GetComponent<RectTransform>();

                // rtImg.pivot = new Vector2(0, 1);
                // rtImg.anchorMin = new Vector2(0, 1);
                // rtImg.anchorMax = new Vector2(0, 1);

                // rtImg.sizeDelta = new Vector2(panelWidth, panelHeight);

                // int row = i / cols;
                // int col = i % cols;

                // rtImg.anchoredPosition = new Vector2(
                //     col * panelWidth,
                //     -row * panelHeight
                // );
            }
        }
    }
}