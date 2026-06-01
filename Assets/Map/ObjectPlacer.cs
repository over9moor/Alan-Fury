using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Расставляет объекты на процедурно сгенерированном ландшафте.
/// Использует HeightMapGenerator для получения высот и границ зон.
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
        public float heightOffset = 0f;
        [Range(0f, 1f)]
        public float spawnChance = 1f; // Вероятность спауна каждого отдельного объекта
        public float minDistanceBetweenObjects = 1f; // Минимальная дистанция, своя для каждого типа
    }

    [Header("Источник высот")]
    public HeightMapGenerator heightSource;

    [Header("Источник меша")]
    public SeamlessTerrainBuilder terrainBuilder;

    [Header("Типы объектов")]
    public List<ObjectType> objectTypes = new List<ObjectType>();

    [Header("Родитель для объектов")]
    public Transform objectsParent;

    [Header("Настройки размещения")]
    public int maxAttemptsPerObject = 30;

    [Header("Производительность")]
    public bool useSpacialGrid = true;
    public float gridCellSize = 10f;

    // Внутренние данные
    private float[,] heights;
    private int width, depth;
    private float tileSize;
    private Vector3 mapOrigin;
    private List<Vector3> placedPositions = new List<Vector3>();
    private Dictionary<Vector2Int, List<Vector3>> spatialGrid;

    /// <summary>
    /// Запускает расстановку всех объектов.
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
            PlaceObjectsOfType(objType);
        }
    }

    /// <summary>
    /// Удаляет все ранее размещённые объекты.
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

    // ==================== Приватные методы ====================

    private bool ValidateInputs()
    {
        if (heightSource == null || !heightSource.isGenerated)
        {
            Debug.LogError("ObjectPlacer: нужен инициализированный HeightMapGenerator!");
            return false;
        }
        if (terrainBuilder == null)
        {
            Debug.LogError("ObjectPlacer: нужна ссылка на SeamlessTerrainBuilder!");
            return false;
        }
        if (objectTypes.Count == 0)
        {
            Debug.LogWarning("ObjectPlacer: нет типов объектов для расстановки.");
            return false;
        }
        return true;
    }

    private void FetchData()
    {
        heights = heightSource.heightMap;
        width = heights.GetLength(0);
        depth = heights.GetLength(1);
        tileSize = terrainBuilder.tileSize;
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
            Debug.LogWarning($"ObjectPlacer: у типа '{objType.name}' нет префаба, пропускаем.");
            return;
        }

        // Защита от невалидного margin когда карта слишком маленькая
        float margin = tileSize * 2;
        float rangeX = width * tileSize - margin * 2;
        float rangeZ = depth * tileSize - margin * 2;
        if (rangeX <= 0 || rangeZ <= 0)
        {
            Debug.LogWarning($"ObjectPlacer: карта слишком мала для отступов, margin сброшен в 0 для '{objType.name}'.");
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

            // FIX: spawnChance проверяется для каждого объекта отдельно
            if (Random.value > objType.spawnChance)
                continue;

            float rx = Random.Range(margin, margin + rangeX);
            float rz = Random.Range(margin, margin + rangeZ);
            Vector3 worldPos = mapOrigin + new Vector3(rx, 0, rz);

            float h = heightSource.GetHeightAtWorldPos(worldPos, tileSize, mapOrigin);

            if (h < objType.minHeight || h > objType.maxHeight)
                continue;

            worldPos.y = h + objType.heightOffset;

            // FIX: используем minDistanceBetweenObjects из самого типа объекта
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

            placedPositions.Add(worldPos);
            AddToSpatialGrid(worldPos);
            placed++;
        }

        Debug.Log($"ObjectPlacer: размещено {placed}/{objType.count} объектов '{objType.name}' за {attempts} попыток.");
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
