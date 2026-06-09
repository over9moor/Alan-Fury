using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Раскидывает префабы очагов тумана по карте. Отдельно от ObjectPlacer,
/// чтобы не конкурировать за слот «или ObjectPlacer / или InstancedObjectPlacer»
/// в TerrainManager. Вызывается из TerrainManager рядом с остальной генерацией.
/// </summary>
public class FogPlacer : MonoBehaviour
{
    [Header("Источники")]
    public HeightMapGenerator heightSource;
    public ChunkedTerrainBuilder terrainBuilder;

    [Header("Префаб тумана (с FogVolume)")]
    public GameObject fogPrefab;
    public Transform parent;

    [Header("Размещение")]
    public int count = 16;
    [Tooltip("Минимальная дистанция между очагами (м).")]
    public float minDistanceBetween = 25f;
    [Tooltip("Отступ от края карты (м).")]
    public float edgeMargin = 12f;
    public float heightOffset = 0f;

    [Header("Посадка на землю")]
    [Tooltip("Класть рейкастом вниз (точнее на стенах). Иначе — по карте высот.")]
    public bool raycastToGround = false;
    public LayerMask groundLayers = ~0;

    [Header("Сид")]
    public bool randomSeed = true;
    public int seed = 1337;
    public int maxAttemptsPerObject = 30;

    private readonly List<Vector3> _placed = new List<Vector3>();

    public void PlaceFog()
    {
        if (!Validate()) return;
        ClearFog();

        if (randomSeed) seed = Random.Range(0, 100000);
        Random.InitState(seed);

        float ts = terrainBuilder.TileSize;
        int w = heightSource.width;
        int d = heightSource.depth;
        Vector3 origin = new Vector3(-w * ts / 2f, 0, -d * ts / 2f);

        float halfW = w * ts / 2f;
        float halfD = d * ts / 2f;
        float minX = -halfW + edgeMargin, maxX = halfW - edgeMargin;
        float minZ = -halfD + edgeMargin, maxZ = halfD - edgeMargin;
        if (minX >= maxX || minZ >= maxZ)
        {
            Debug.LogWarning("FogPlacer: edgeMargin слишком велик для размера карты.");
            return;
        }

        float minSqr = minDistanceBetween * minDistanceBetween;
        int placed = 0, attempts = 0, maxTotal = count * maxAttemptsPerObject;

        while (placed < count && attempts < maxTotal)
        {
            attempts++;
            float x = Random.Range(minX, maxX);
            float z = Random.Range(minZ, maxZ);
            Vector3 pos = new Vector3(x, 0f, z);

            bool tooClose = false;
            for (int i = 0; i < _placed.Count; i++)
                if ((_placed[i] - pos).sqrMagnitude < minSqr) { tooClose = true; break; }
            if (tooClose) continue;

            float y;
            if (raycastToGround &&
                Physics.Raycast(new Vector3(x, 1000f, z), Vector3.down, out RaycastHit hit,
                                2000f, groundLayers, QueryTriggerInteraction.Ignore))
                y = hit.point.y;
            else
                y = heightSource.GetHeightAtWorldPos(pos, ts, origin);

            pos.y = y + heightOffset;

            var go = InstantiatePrefab(fogPrefab, pos);
            go.name = $"Fog_{placed}";
            _placed.Add(pos);
            placed++;
        }

        Debug.Log($"FogPlacer: размещено {placed}/{count} очагов за {attempts} попыток.");
    }

    public void ClearFog()
    {
        if (parent == null)
        {
            var existing = transform.Find("FogVolumes");
            parent = existing != null ? existing : new GameObject("FogVolumes").transform;
            parent.SetParent(transform);
        }

        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            var c = parent.GetChild(i).gameObject;
            if (Application.isPlaying) Destroy(c);
            else DestroyImmediate(c);
        }
        _placed.Clear();
    }

    private GameObject InstantiatePrefab(GameObject prefab, Vector3 pos)
    {
        if (Application.isPlaying)
            return Instantiate(prefab, pos, Quaternion.identity, parent);
#if UNITY_EDITOR
        var go = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, parent);
        go.transform.position = pos;
        return go;
#else
        return Instantiate(prefab, pos, Quaternion.identity, parent);
#endif
    }

    private bool Validate()
    {
        if (heightSource == null || !heightSource.isGenerated)
        { Debug.LogError("FogPlacer: HeightMapGenerator не готов!"); return false; }
        if (terrainBuilder == null)
        { Debug.LogError("FogPlacer: нет ChunkedTerrainBuilder!"); return false; }
        if (fogPrefab == null)
        { Debug.LogError("FogPlacer: не задан fogPrefab!"); return false; }
        return true;
    }
}
