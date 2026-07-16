using System.Collections.Generic;
using UnityEngine;

// Configuration data-driven du LOD par distance (roadmap phase SVO sous-étape 3) : un
// ScriptableObject plutôt que des constantes en dur, pour que le mapping distance -> niveau de
// LOD soit ajustable dans l'Éditeur sans recompiler (voir aussi LODSettingsEditor pour l'outil
// associé). Mécanisme choisi avec l'utilisateur : sous-échantillonnage par pas de puissance de 2
// (1 voxel sur "pas" lu depuis scratch, échantillon le plus proche — pas de vote majoritaire pour
// cette première version), PAS une fusion octree façon OctreeCompaction (qui ne s'applique qu'à
// des régions parfaitement uniformes et ne simplifierait pas un terrain non-uniforme).
[CreateAssetMenu(fileName = "LODSettings", menuName = "VoxelGame/LOD Settings")]
public class LODSettings : ScriptableObject
{
    [System.Serializable]
    public struct Level
    {
        [Tooltip("Distance maximale (unités monde, depuis le joueur) à laquelle ce niveau de LOD s'applique.")]
        public float maxDistance;

        [Tooltip("Niveau de LOD : 0 = détail plein, 1 = 1 voxel sur 2 par axe, 2 = 1 sur 4, ...")]
        [Min(0)]
        public int lodLevel;
    }

    [Tooltip("Triés automatiquement par distance croissante. Au-delà du dernier seuil, le niveau du dernier élément s'applique (pas de limite de rendu).")]
    public List<Level> levels = new List<Level>
    {
        new Level { maxDistance = 128f, lodLevel = 0 },
        new Level { maxDistance = 256f, lodLevel = 1 },
        new Level { maxDistance = 512f, lodLevel = 2 },
    };

    private void OnValidate()
    {
        levels.Sort((a, b) => a.maxDistance.CompareTo(b.maxDistance));
    }

    // Retourne le pas d'échantillonnage (puissance de 2, 1 = détail plein) pour une distance
    // donnée. Toujours borné à [1, maxStride] : une config vide, ou une distance au-delà de tous
    // les seuils définis, retombe respectivement sur le détail plein ou le dernier niveau connu
    // (jamais d'exception, jamais de rendu manquant faute de configuration).
    public int GetStrideForDistance(float distance, int maxStride)
    {
        if (levels == null || levels.Count == 0)
        {
            return 1;
        }

        int lodLevel = levels[levels.Count - 1].lodLevel;
        for (int i = 0; i < levels.Count; i++)
        {
            if (distance <= levels[i].maxDistance)
            {
                lodLevel = levels[i].lodLevel;
                break;
            }
        }

        int stride = 1 << Mathf.Clamp(lodLevel, 0, 30);
        return Mathf.Clamp(stride, 1, maxStride);
    }
}
