using UnityEngine;
using UnityEngine.UI;

public class WaterOverlay : MonoBehaviour
{
    public Image overlayImage; // À assigner dans l'inspecteur
    public Color underwaterColor = new Color(0.2f, 0.5f, 0.7f, 0.3f);

    void Update()
    {
        if (overlayImage == null || World.Instance == null) return;
        Vector3 camPos = Camera.main.transform.position;
        Vector3Int voxelPos = Vector3Int.FloorToInt(camPos);
        bool isInWater = World.Instance.GetVoxel(voxelPos).type == VoxelType.Water;
        overlayImage.enabled = isInWater;
        if (isInWater)
            overlayImage.color = underwaterColor;
    }
}
