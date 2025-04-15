using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(NoiseSettings))]
public class NoiseSettingsEditor : Editor
{
    private Texture2D previewTexture;
    private const int PREVIEW_SIZE = 256;

    private void OnEnable()
    {
        previewTexture = new Texture2D(PREVIEW_SIZE, PREVIEW_SIZE);
    }

    public override void OnInspectorGUI()
    {
        NoiseSettings settings = (NoiseSettings)target;

        // Dessiner l'interface par défaut
        DrawDefaultInspector();

        // Bouton pour appliquer les changements
        if (GUILayout.Button("Apply Changes"))
        {
            settings.ApplyToNoise();
            UpdatePreview(settings);
        }

        // Afficher la prévisualisation
        if (previewTexture != null)
        {
            GUILayout.Label("Noise Preview:");
            float aspectRatio = 1.0f;
            float width = EditorGUIUtility.currentViewWidth - 30;
            float height = width / aspectRatio;
            Rect rect = GUILayoutUtility.GetRect(width, height);
            EditorGUI.DrawPreviewTexture(rect, previewTexture);
        }
    }

    private void UpdatePreview(NoiseSettings settings)
    {
        float[,] noiseMap = Noise.GenerateHeightmap(PREVIEW_SIZE, Vector2.zero);

        for (int y = 0; y < PREVIEW_SIZE; y++)
        {
            for (int x = 0; x < PREVIEW_SIZE; x++)
            {
                float noiseValue = noiseMap[x, y];
                previewTexture.SetPixel(x, y, new Color(noiseValue, noiseValue, noiseValue));
            }
        }
        previewTexture.Apply();
    }

    private void OnDisable()
    {
        if (previewTexture != null)
        {
            DestroyImmediate(previewTexture);
        }
    }
}