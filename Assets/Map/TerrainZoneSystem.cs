using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Делит ландшафт на зоны по высоте (болото/полусухая/сухая).
/// InitializeZones() вызывается из TerrainManager после BuildTerrain().
/// </summary>
public class TerrainZoneSystem : MonoBehaviour
{
    public enum ZoneType
    {
        Swamp,
        SemiDry,
        Dry
    }

    [System.Serializable]
    public class Zone
    {
        public ZoneType type;
        public string zoneName;
        public Color zoneColor = Color.white;
        public float minHeight;
        public float maxHeight;
    }

    [Header("Источники данных")]
    public HeightMapGenerator heightSource;
    public SeamlessTerrainBuilder terrainBuilder;

    [Header("Настройки зон")]
    public List<Zone> zones = new List<Zone>();

    [Header("Визуализация в редакторе")]
    public bool showGizmos = true;
    public bool showZoneLabels = true;
    public float gizmoHeight = 0.05f;
    public float gizmoAlpha = 0.5f;

    [Header("Обновление цветов")]
    public bool updateColorsOnMesh = true;

    private ZoneType[,] zoneMap;
    private bool isInitialized = false;

    // NOTE: Инициализация намеренно убрана из Start.
    // TerrainZoneSystem.Start() может выполниться раньше TerrainManager.Start(),
    // и в этот момент карта высот ещё не готова.
    // Вместо этого вызывайте InitializeZones() из TerrainManager.GenerateAll()
    // после вызова terrainBuilder.BuildTerrain().

    public void InitializeZones()
    {
        if (heightSource == null || !heightSource.isGenerated)
        {
            Debug.LogError("TerrainZoneSystem: нужен инициализированный HeightMapGenerator!");
            return;
        }

        if (terrainBuilder == null)
        {
            Debug.LogError("TerrainZoneSystem: нужен SeamlessTerrainBuilder!");
            return;
        }

        if (zones.Count == 0)
            SetupDefaultZones();

        int w = heightSource.width;
        int d = heightSource.depth;
        zoneMap = new ZoneType[w, d];

        for (int x = 0; x < w; x++)
            for (int z = 0; z < d; z++)
                zoneMap[x, z] = GetZoneForHeight(heightSource.GetHeight(x, z));

        isInitialized = true;

        if (updateColorsOnMesh)
            UpdateTerrainColors();

        Debug.Log($"TerrainZoneSystem: создано {zones.Count} зон, размер карты {w}x{d}");
    }

    private void SetupDefaultZones()
    {
        zones.Clear();

        // NOTE: высоты привязаны к heightSource.maxHeight — масштабируются корректно.
        float max = heightSource != null ? heightSource.maxHeight : 1f;

        zones.Add(new Zone
        {
            type = ZoneType.Swamp,
            zoneName = "Болото",
            zoneColor = new Color(0.3f, 0.2f, 0.1f),
            minHeight = 0f,
            maxHeight = max * 0.25f
        });

        zones.Add(new Zone
        {
            type = ZoneType.SemiDry,
            zoneName = "Полусухая",
            zoneColor = new Color(0.5f, 0.4f, 0.25f),
            minHeight = max * 0.25f,
            maxHeight = max * 0.6f
        });

        zones.Add(new Zone
        {
            type = ZoneType.Dry,
            zoneName = "Сухая",
            zoneColor = new Color(0.7f, 0.6f, 0.4f),
            minHeight = max * 0.6f,
            maxHeight = max
        });
    }

    private ZoneType GetZoneForHeight(float height)
    {
        foreach (Zone zone in zones)
        {
            if (height >= zone.minHeight && height <= zone.maxHeight)
                return zone.type;
        }
        return ZoneType.SemiDry;
    }

    public void UpdateTerrainColors()
    {
        if (terrainBuilder == null)
        {
            Debug.LogWarning("TerrainZoneSystem: нет ссылки на SeamlessTerrainBuilder");
            return;
        }

        MeshFilter meshFilter = terrainBuilder.GetComponent<MeshFilter>();
        if (meshFilter == null || meshFilter.sharedMesh == null)
        {
            Debug.LogWarning("TerrainZoneSystem: меш не найден");
            return;
        }

        Mesh mesh = meshFilter.sharedMesh;
        Vector3[] verts = mesh.vertices;
        Color[] newColors = new Color[verts.Length];

        float ts = terrainBuilder.tileSize;
        Vector3 mapOrigin = new Vector3(
            -heightSource.width * ts / 2f,
            0,
            -heightSource.depth * ts / 2f
        );

        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 worldPos = transform.TransformPoint(verts[i]);

            float localX = worldPos.x - mapOrigin.x;
            float localZ = worldPos.z - mapOrigin.z;

            // FIX: FloorToInt вместо RoundToInt — устраняет смещение цветов на полтайла
            int cellX = Mathf.Clamp(Mathf.FloorToInt(localX / ts), 0, heightSource.width - 1);
            int cellZ = Mathf.Clamp(Mathf.FloorToInt(localZ / ts), 0, heightSource.depth - 1);

            newColors[i] = GetColorForZone(GetZoneAtCell(cellX, cellZ));
        }

        mesh.colors = newColors;
    }

    public ZoneType GetZoneAtWorldPosition(Vector3 worldPos)
    {
        if (!isInitialized || heightSource == null || terrainBuilder == null)
            return ZoneType.SemiDry;

        float ts = terrainBuilder.tileSize;
        Vector3 mapOrigin = new Vector3(
            -heightSource.width * ts / 2f,
            0,
            -heightSource.depth * ts / 2f
        );

        // FIX: FloorToInt здесь тоже
        int cellX = Mathf.Clamp(Mathf.FloorToInt((worldPos.x - mapOrigin.x) / ts), 0, heightSource.width - 1);
        int cellZ = Mathf.Clamp(Mathf.FloorToInt((worldPos.z - mapOrigin.z) / ts), 0, heightSource.depth - 1);

        return GetZoneAtCell(cellX, cellZ);
    }

    public ZoneType GetZoneAtCell(int x, int z)
    {
        if (!isInitialized || zoneMap == null) return ZoneType.SemiDry;

        if (x >= 0 && x < zoneMap.GetLength(0) && z >= 0 && z < zoneMap.GetLength(1))
            return zoneMap[x, z];

        return ZoneType.SemiDry;
    }

    public Color GetColorForZone(ZoneType zone)
    {
        foreach (Zone z in zones)
            if (z.type == zone) return z.zoneColor;

        return Color.gray;
    }

    public List<Vector2Int> GetZoneCells(ZoneType zone)
    {
        var cells = new List<Vector2Int>();
        if (!isInitialized || zoneMap == null) return cells;

        for (int x = 0; x < zoneMap.GetLength(0); x++)
            for (int z = 0; z < zoneMap.GetLength(1); z++)
                if (zoneMap[x, z] == zone)
                    cells.Add(new Vector2Int(x, z));

        return cells;
    }

    public void RegenerateZones() => InitializeZones();

    public void ClearZones()
    {
        zoneMap = null;
        isInitialized = false;
    }

    public ZoneType[,] GetZoneMap() => zoneMap;

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (!showGizmos || !Application.isPlaying) return;
        if (!isInitialized || zoneMap == null || terrainBuilder == null || heightSource == null) return;

        float ts = terrainBuilder.tileSize;
        Vector3 mapOrigin = new Vector3(
            -heightSource.width * ts / 2f,
            0,
            -heightSource.depth * ts / 2f
        );

        int w = zoneMap.GetLength(0);
        int d = zoneMap.GetLength(1);

        for (int x = 0; x < w; x++)
        {
            for (int z = 0; z < d; z++)
            {
                Color zoneColor = GetColorForZone(zoneMap[x, z]);
                zoneColor.a = gizmoAlpha;

                float height = heightSource.GetHeight(x, z);
                Vector3 center = mapOrigin + new Vector3(
                    x * ts + ts / 2f,
                    height + gizmoHeight,
                    z * ts + ts / 2f
                );

                Gizmos.color = zoneColor;
                Gizmos.DrawCube(center, new Vector3(ts * 0.95f, 0.05f, ts * 0.95f));
                zoneColor.a = 0.8f;
                Gizmos.color = zoneColor;
                Gizmos.DrawWireCube(center, new Vector3(ts * 0.95f, 0.05f, ts * 0.95f));
            }
        }
    }
#endif
}
