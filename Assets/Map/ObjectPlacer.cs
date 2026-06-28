using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// ����������� ������� �� ���������� ��������������� ���������.
/// ���������� HeightMapGenerator ��� ��������� ����� � ������ ���.
/// </summary>
public class ObjectPlacer : MonoBehaviour
{
    [System.Serializable]
    public class ObjectType
    {
        public string name;
        public GameObject prefab;
        public int count = 50;
        public float minHeight = 0f;
        public float maxHeight = 1f;
        public bool randomRotation = true;
        public float minScale = 0.8f;
        public float maxScale = 1.2f;
        public bool alignToSurface = false;
        public bool snapToCellCenter = false; // ставить в центр клетки (для деревьев), пропуская воду/дорогу/занятые
        public float heightOffset = 0f;
        [Range(0f, 1f)]
        public float spawnChance = 1f; // ����������� ������ ������� ���������� �������
        public float minDistanceBetweenObjects = 1f; // ����������� ���������, ���� ��� ������� ����
        public LayerMask objectLayer; // слой для заспавненных объектов (Nothing = не менять; напр. Trees для деревьев)
    }

    [Header("�������� �����")]
    public HeightMapGenerator heightSource;

    [Header("�������� ����")]
    public SeamlessTerrainBuilder terrainBuilder;
    public ChunkedTerrainBuilder chunkedBuilder;   // используется для tileSize, если Seamless = None
    public TerrainZoneSystem zoneSystem;           // для пропуска воды/дороги при размещении по клеткам

    [Header("���� ��������")]
    public List<ObjectType> objectTypes = new List<ObjectType>();

    [Header("�������� ��� ��������")]
    public Transform objectsParent;

    [Header("��������� ����������")]
    public int maxAttemptsPerObject = 30;

    [Header("������������������")]
    public bool useSpacialGrid = true;
    public float gridCellSize = 10f;

    // ���������� ������
    private float[,] heights;
    private int width, depth;
    private float tileSize;
    private Vector3 mapOrigin;
    private List<Vector3> placedPositions = new List<Vector3>();
    private Dictionary<Vector2Int, List<Vector3>> spatialGrid;
    private bool[,] occupiedCells; // клетки, занятые объектами в текущем прогоне

    /// <summary>
    /// ��������� ����������� ���� ��������.
    /// </summary>
    public void PlaceAllObjects()
    {
        if (!ValidateInputs())
            return;

        FetchData();
        ClearOldObjects();

        if (useSpacialGrid)
            spatialGrid = new Dictionary<Vector2Int, List<Vector3>>();

        foreach (ObjectType objType in objectTypes)
        {
            if (objType.snapToCellCenter)
                PlaceObjectsOnCells(objType);
            else
                PlaceObjectsOfType(objType);
        }
    }

    /// <summary>
    /// ������� ��� ����� ����������� �������.
    /// </summary>
    public void ClearOldObjects()
    {
        if (objectsParent == null)
        {
            objectsParent = new GameObject("PlacedObjects").transform;
            objectsParent.SetParent(transform);
        }

        for (int i = objectsParent.childCount - 1; i >= 0; i--)
        {
            Transform child = objectsParent.GetChild(i);
            if (Application.isPlaying)
                Destroy(child.gameObject);
            else
                DestroyImmediate(child.gameObject);
        }

        placedPositions.Clear();
        if (spatialGrid != null)
            spatialGrid.Clear();
    }

    // ==================== ��������� ������ ====================

    private bool ValidateInputs()
    {
        if (heightSource == null || !heightSource.isGenerated)
        {
            Debug.LogError("ObjectPlacer: ����� ������������������ HeightMapGenerator!");
            return false;
        }
        if (terrainBuilder == null && chunkedBuilder == null)
        {
            Debug.LogError("ObjectPlacer: ����� ������ �� SeamlessTerrainBuilder!");
            return false;
        }
        if (objectTypes.Count == 0)
        {
            Debug.LogWarning("ObjectPlacer: ��� ����� �������� ��� �����������.");
            return false;
        }
        return true;
    }

    private void FetchData()
    {
        heights = heightSource.heightMap;
        width = heights.GetLength(0);
        depth = heights.GetLength(1);
        if (chunkedBuilder == null) chunkedBuilder = GetComponent<ChunkedTerrainBuilder>();
        if (zoneSystem == null) zoneSystem = GetComponent<TerrainZoneSystem>();
        tileSize = ResolveTileSize();
        occupiedCells = new bool[width, depth];
        mapOrigin = new Vector3(-width * tileSize / 2f, 0, -depth * tileSize / 2f);
    }

    private void AddToSpatialGrid(Vector3 position)
    {
        if (!useSpacialGrid || spatialGrid == null) return;

        Vector2Int cell = new Vector2Int(
            Mathf.FloorToInt(position.x / gridCellSize),
            Mathf.FloorToInt(position.z / gridCellSize)
        );

        if (!spatialGrid.ContainsKey(cell))
            spatialGrid[cell] = new List<Vector3>();

        spatialGrid[cell].Add(position);
    }

    private bool IsTooClose(Vector3 position, float minDistance)
    {
        if (minDistance <= 0) return false;

        return useSpacialGrid && spatialGrid != null
            ? IsTooCloseGrid(position, minDistance)
            : IsTooCloseBruteForce(position, minDistance);
    }

    private bool IsTooCloseGrid(Vector3 position, float minDistance)
    {
        Vector2Int cell = new Vector2Int(
            Mathf.FloorToInt(position.x / gridCellSize),
            Mathf.FloorToInt(position.z / gridCellSize)
        );

        float minDistSqr = minDistance * minDistance;

        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                Vector2Int neighbor = new Vector2Int(cell.x + dx, cell.y + dz);
                if (spatialGrid.TryGetValue(neighbor, out var positions))
                {
                    foreach (var pos in positions)
                    {
                        if ((position - pos).sqrMagnitude < minDistSqr)
                            return true;
                    }
                }
            }
        }
        return false;
    }

    private bool IsTooCloseBruteForce(Vector3 position, float minDistance)
    {
        float minDistSqr = minDistance * minDistance;
        foreach (var pos in placedPositions)
        {
            if ((position - pos).sqrMagnitude < minDistSqr)
                return true;
        }
        return false;
    }

    private void PlaceObjectsOfType(ObjectType objType)
    {
        if (objType.prefab == null)
        {
            Debug.LogWarning($"ObjectPlacer: � ���� '{objType.name}' ��� �������, ����������.");
            return;
        }

        // ������ �� ����������� margin ����� ����� ������� ���������
        float margin = tileSize * 2;
        float rangeX = width * tileSize - margin * 2;
        float rangeZ = depth * tileSize - margin * 2;
        if (rangeX <= 0 || rangeZ <= 0)
        {
            Debug.LogWarning($"ObjectPlacer: ����� ������� ���� ��� ��������, margin ������� � 0 ��� '{objType.name}'.");
            margin = 0f;
            rangeX = width * tileSize;
            rangeZ = depth * tileSize;
        }

        int placed = 0;
        int attempts = 0;
        int maxTotalAttempts = objType.count * maxAttemptsPerObject;

        while (placed < objType.count && attempts < maxTotalAttempts)
        {
            attempts++;

            // FIX: spawnChance ����������� ��� ������� ������� ��������
            if (Random.value > objType.spawnChance)
                continue;

            float rx = Random.Range(margin, margin + rangeX);
            float rz = Random.Range(margin, margin + rangeZ);
            Vector3 worldPos = mapOrigin + new Vector3(rx, 0, rz);

            float h = heightSource.GetHeightAtWorldPos(worldPos, tileSize, mapOrigin);

            if (h < objType.minHeight || h > objType.maxHeight)
                continue;

            worldPos.y = h + objType.heightOffset;

            // FIX: ���������� minDistanceBetweenObjects �� ������ ���� �������
            if (IsTooClose(worldPos, objType.minDistanceBetweenObjects))
                continue;

            Quaternion rotation = objType.randomRotation
                ? Quaternion.Euler(0, Random.Range(0f, 360f), 0)
                : Quaternion.identity;

            if (objType.alignToSurface && !objType.randomRotation)
                rotation = GetSurfaceRotation(worldPos);

            GameObject obj = InstantiatePrefab(objType.prefab, worldPos, rotation);
            obj.name = $"{objType.name}_{placed}";
            float scale = Random.Range(objType.minScale, objType.maxScale);
            obj.transform.localScale = Vector3.one * scale;

            ApplyLayer(obj, objType);

            placedPositions.Add(worldPos);
            AddToSpatialGrid(worldPos);
            placed++;
        }

        Debug.Log($"ObjectPlacer: ��������� {placed}/{objType.count} �������� '{objType.name}' �� {attempts} �������.");
    }

    private float ResolveTileSize()
    {
        if (terrainBuilder != null) return terrainBuilder.tileSize;
        if (chunkedBuilder != null) return chunkedBuilder.tileSize;
        return 1f;
    }

    /// <summary>
    /// Размещает объекты строго в центрах клеток, по одному на клетку.
    /// Пропускает воду, дорогу и уже занятые клетки.
    /// </summary>
    private void PlaceObjectsOnCells(ObjectType objType)
    {
        if (objType.prefab == null)
        {
            Debug.LogWarning($"ObjectPlacer: у типа '{objType.name}' нет префаба, пропуск.");
            return;
        }

        const int cellMargin = 2; // отступ от края карты в клетках
        int marginX = width > cellMargin * 2 + 1 ? cellMargin : 0;
        int marginZ = depth > cellMargin * 2 + 1 ? cellMargin : 0;

        int placed = 0;
        int attempts = 0;
        int maxTotalAttempts = objType.count * maxAttemptsPerObject;

        while (placed < objType.count && attempts < maxTotalAttempts)
        {
            attempts++;

            if (Random.value > objType.spawnChance)
                continue;

            int cx = Random.Range(marginX, width - marginX);
            int cz = Random.Range(marginZ, depth - marginZ);

            if (occupiedCells != null && occupiedCells[cx, cz])
                continue;

            if (IsCellBlocked(cx, cz))
                continue;

            float h = heightSource.GetHeight(cx, cz);
            if (h < objType.minHeight || h > objType.maxHeight)
                continue;

            // Центр клетки в мире — той же формулой, что у ChunkedTerrainBuilder (без сдвига на полклетки).
            Vector3 worldPos = mapOrigin + new Vector3(
                cx * tileSize,
                0f,
                cz * tileSize
            );
            worldPos.y = h + objType.heightOffset;

            if (IsTooClose(worldPos, objType.minDistanceBetweenObjects))
                continue;

            Quaternion rotation = objType.randomRotation
                ? Quaternion.Euler(0, Random.Range(0f, 360f), 0)
                : Quaternion.identity;

            if (objType.alignToSurface && !objType.randomRotation)
                rotation = GetSurfaceRotation(worldPos);

            GameObject obj = InstantiatePrefab(objType.prefab, worldPos, rotation);
            obj.name = $"{objType.name}_{placed}";
            float scale = Random.Range(objType.minScale, objType.maxScale);
            obj.transform.localScale = Vector3.one * scale;

            ApplyLayer(obj, objType);

            placedPositions.Add(worldPos);
            AddToSpatialGrid(worldPos);
            if (occupiedCells != null) occupiedCells[cx, cz] = true;
            placed++;
        }

        Debug.Log($"ObjectPlacer: по клеткам размещено {placed}/{objType.count} '{objType.name}' за {attempts} попыток.");
    }

    /// <summary>Клетка непригодна для объекта: вода или дорога.</summary>
    private bool IsCellBlocked(int cx, int cz)
    {
        if (zoneSystem == null) return false;
        var t = zoneSystem.GetTileType(cx, cz);
        return t == TerrainZoneSystem.TileType.Water || t == TerrainZoneSystem.TileType.Road;
    }

    /// <summary>Назначает слой заспавненному объекту и его детям (если в типе задан objectLayer).</summary>
    private void ApplyLayer(GameObject obj, ObjectType objType)
    {
        int layer = MaskToLayer(objType.objectLayer);
        if (layer < 0) return;
        SetLayerRecursive(obj.transform, layer);
    }

    private static int MaskToLayer(LayerMask mask)
    {
        int v = mask.value;
        if (v == 0) return -1;                 // Nothing → не менять слой
        for (int i = 0; i < 32; i++)
            if ((v & (1 << i)) != 0) return i; // берём первый выбранный слой
        return -1;
    }

    private static void SetLayerRecursive(Transform t, int layer)
    {
        t.gameObject.layer = layer;
        for (int i = 0; i < t.childCount; i++)
            SetLayerRecursive(t.GetChild(i), layer);
    }

    private GameObject InstantiatePrefab(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        if (Application.isPlaying)
        {
            return Instantiate(prefab, position, rotation, objectsParent);
        }
        else
        {
#if UNITY_EDITOR
            GameObject obj = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, objectsParent);
            obj.transform.position = position;
            obj.transform.rotation = rotation;
            return obj;
#else
            return Instantiate(prefab, position, rotation, objectsParent);
#endif
        }
    }

    private Quaternion GetSurfaceRotation(Vector3 worldPos)
    {
        float rayLength = 20f;
        if (Physics.Raycast(worldPos + Vector3.up * rayLength, Vector3.down, out RaycastHit hit, rayLength * 2f))
            return Quaternion.FromToRotation(Vector3.up, hit.normal);

        return Quaternion.identity;
    }
}