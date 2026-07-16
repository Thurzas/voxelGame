using UnityEditor;
using UnityEngine;

// Outil éditeur pour configurer le LOD par distance (roadmap phase SVO sous-étape 3) — l'inspecteur
// par défaut de LODSettings.levels suffit déjà à éditer/réordonner les seuils, cette surcouche
// ajoute juste un résumé lisible ("0-128: LOD0 ... ") pour vérifier d'un coup d'œil la config
// effective (triée), sans avoir à faire le calcul mentalement à partir de la liste brute.
[CustomEditor(typeof(LODSettings))]
public class LODSettingsEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var settings = (LODSettings)target;
        if (settings.levels == null || settings.levels.Count == 0)
        {
            EditorGUILayout.HelpBox("Aucun niveau défini : tous les chunks seront rendus en détail plein (LOD 0).", MessageType.Info);
            return;
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Résumé (trié par distance)", EditorStyles.boldLabel);

        float previousDistance = 0f;
        for (int i = 0; i < settings.levels.Count; i++)
        {
            LODSettings.Level level = settings.levels[i];
            int stride = 1 << Mathf.Max(0, level.lodLevel);
            EditorGUILayout.LabelField($"  {previousDistance:0}–{level.maxDistance:0} : LOD {level.lodLevel} (1 voxel sur {stride})");
            previousDistance = level.maxDistance;
        }
        EditorGUILayout.LabelField($"  au-delà de {previousDistance:0} : LOD {settings.levels[settings.levels.Count - 1].lodLevel} (dernier niveau, pas de limite de rendu)");
    }
}
