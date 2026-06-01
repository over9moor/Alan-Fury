using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

/// <summary>
/// Расставляет объекты через GPU Instancing — один draw call на тип объекта
/// вместо одного GameObject на каждый экземпляр.
/// 
/// Ключевые отличия от ObjectPlacer:
/// - Центры объектов привязаны к центрам клеток (важно для путей и логики)
/// - Один тип = один DrawMeshInstanced вызов (макс. 1023 на батч, автоматически разбивается)
/// - Нет GameObject'ов в сцене = нет overhead от Transform/Renderer компонентов
/// 
/// ВАЖНО: объекты не участвуют в физике и не имеют коллайдеров.
/// Для коллайдеров используйте обычный ObjectPlacer для важных объектов (деревья, стены),
/// и этот — для декора (трава, мелкие камни).
/// </summary>
public class InstancedObjectPlacer : MonoBehaviour
{
    [System.Serializable]
    public class InstancedObjectType
    {
        public string name;
        public Mesh mesh;
        public Material material; // должен иметь Enable GPU Instancing = true
        public int subMeshIndex = 0;

        [Header("Размещение")]
        [Tooltip("Сколько клеток карты занимает один объект (1 = один объект максимум на клетку)")]
        public int cellsPerObject = 1;
        [Tooltip("Доля клеток карты, где появится объект (0–1)")]
        [Range(0f, 1f)] public float density = 0.3f;
        public float minHeight = 0f;
        public float maxHeight = 1f;

        [Header("Случайность в пределах клетки")]
        [Tooltip("Насколько сильно смещать от центра клетки (0 = строго по центру)")]
        [Range(0f, 0.5f)] public float cellJitter = 0.3f;

        [Header("Трансформ")]
        public bool randomRotationY = true;
        public float minScale = 0.8f;
        public float maxScale = 1.2f;
        public float heightOffset = 0f;

        [Header("Рендер")]
        public ShadowCastingMode shadowCasting = ShadowCastingMode.On;
        public bool receiveShadows = true;

        // Данные для рендеринга (заполняются при генерации)
        [HideInInspector] public List<Matrix4x4> matrices = new List<Matrix4x4>();
        [HideInInspector] public MaterialPropertyBlock propertyBlock;
    }

    [Header("Источник высот")]
    public HeightMapGenerator heightSource;

    [Header("Источник меша (для tileSize)")]
    public ChunkedTerrainBuilder terrainBuilder;

    [Header("Типы объектов")]
    public List<InstancedObjectType> objectTypes = new List<InstancedObjectType>();

    [Header("Сид генерации")]
    public bool randomSeed = true;
    public int seed = 42;

    private bool isGenerated = false;

    // =================== Публичный API ===================

    public void PlaceAllObjects()
    {
        if (!Validate()) return;

        if (randomSeed) seed = UnityEngine.Random.Range(0, 100000);
        UnityEngine.Random.InitState(seed);

        float ts = terrainBuilder.TileSize;
        int w = heightSource.width;
        int d = heightSource.depth;
        Vector3 origin = new Vector3(-w * ts / 2f, 0, -d * ts / 2f);

        foreach (var objType in objectTypes)
        {
            objType.matrices.Clear();
            objType.propertyBlock = new MaterialPropertyBlock();

            if (objType.mesh == null || objType.material == null)
            {
                Debug.LogWarning($"InstancedObjectPlacer: '{objType.name}' — нет меша или материала.");
                continue;
            }

            PlaceType(objType, w, d, ts, origin);
            Debug.Log($"InstancedObjectPlacer: '{objType.name}' — {objType.matrices.Count} экземпляров");
        }

        isGenerated = true;
    }

    public void ClearObjects()
    {
        foreach (var t in objectTypes)
            t.matrices.Clear();
        isGenerated = false;
    }

    // =================== Генерация ===================

    private void PlaceType(InstancedObjectType objType, int w, int d, float ts, Vector3 origin)
    {
        // Перебираем клетки с шагом cellsPerObject
        int step = Mathf.Max(1, objType.cellsPerObject);

        for (int x = 0; x < w; x += step)
        {
            for (int z = 0; z < d; z += step)
            {
                // Проверяем плотность
                if (UnityEngine.Random.value > objType.density) continue;

                float h = heightSource.GetHeight(x, z);
                if (h < objType.minHeight || h > objType.maxHeight) continue;

                // Центр клетки + небольшой jitter
                float jitterRange = ts * objType.cellJitter;
                float px = origin.x + x * ts + ts * 0.5f + UnityEngine.Random.Range(-jitterRange, jitterRange);
                float pz = origin.z + z * ts + ts * 0.5f + UnityEngine.Random.Range(-jitterRange, jitterRange);
                float py = h + objType.heightOffset;

                float rotY = objType.randomRotationY ? UnityEngine.Random.Range(0f, 360f) : 0f;
                float scale = UnityEngine.Random.Range(objType.minScale, objType.maxScale);

                Matrix4x4 matrix = Matrix4x4.TRS(
                    new Vector3(px, py, pz),
                    Quaternion.Euler(0, rotY, 0),
                    Vector3.one * scale
                );

                objType.matrices.Add(matrix);
            }
        }
    }

    // =================== Рендеринг ===================

    void Update()
    {
        if (!isGenerated) return;
        DrawAll();
    }

    // DrawMeshInstanced принимает максимум 1023 матриц за вызов
    private const int BatchSize = 1023;

    private void DrawAll()
    {
        foreach (var objType in objectTypes)
        {
            if (objType.mesh == null || objType.material == null) continue;
            if (objType.matrices.Count == 0) continue;

            var matrices = objType.matrices;
            int total = matrices.Count;

            for (int i = 0; i < total; i += BatchSize)
            {
                int count = Mathf.Min(BatchSize, total - i);
                var batch = new Matrix4x4[count];

                for (int j = 0; j < count; j++)
                    batch[j] = matrices[i + j];

                Graphics.DrawMeshInstanced(
                    objType.mesh,
                    objType.subMeshIndex,
                    objType.material,
                    batch,
                    count,
                    objType.propertyBlock,
                    objType.shadowCasting,
                    objType.receiveShadows
                );
            }
        }
    }

    // =================== Утилиты ===================

    private bool Validate()
    {
        if (heightSource == null || !heightSource.isGenerated)
        {
            Debug.LogError("InstancedObjectPlacer: HeightMapGenerator не готов!");
            return false;
        }
        if (terrainBuilder == null)
        {
            Debug.LogError("InstancedObjectPlacer: нужен ChunkedTerrainBuilder!");
            return false;
        }
        return true;
    }

    /// <summary>
    /// Возвращает центр клетки в мировых координатах (удобно для NPC и путей).
    /// </summary>
    public Vector3 GetCellCenter(int x, int z)
    {
        float ts = terrainBuilder.TileSize;
        int w = heightSource.width;
        int d = heightSource.depth;
        Vector3 origin = new Vector3(-w * ts / 2f, 0, -d * ts / 2f);

        float h = heightSource.GetHeight(x, z);
        return new Vector3(
            origin.x + x * ts + ts * 0.5f,
            h,
            origin.z + z * ts + ts * 0.5f
        );
    }

    /// <summary>
    /// Возвращает индекс клетки по мировой позиции.
    /// </summary>
    public Vector2Int WorldToCell(Vector3 worldPos)
    {
        float ts = terrainBuilder.TileSize;
        int w = heightSource.width;
        int d = heightSource.depth;
        Vector3 origin = new Vector3(-w * ts / 2f, 0, -d * ts / 2f);

        int cx = Mathf.Clamp(Mathf.FloorToInt((worldPos.x - origin.x) / ts), 0, w - 1);
        int cz = Mathf.Clamp(Mathf.FloorToInt((worldPos.z - origin.z) / ts), 0, d - 1);
        return new Vector2Int(cx, cz);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (!isGenerated || terrainBuilder == null || heightSource == null) return;

        float ts = terrainBuilder.TileSize;
        int w = heightSource.width;
        int d = heightSource.depth;
        Vector3 origin = new Vector3(-w * ts / 2f, 0, -d * ts / 2f);

        // Рисуем сетку клеток (только в небольшом радиусе от камеры)
        Gizmos.color = new Color(1, 1, 0, 0.15f);
        int limit = Mathf.Min(w, 30);
        int limitD = Mathf.Min(d, 30);
        for (int x = 0; x < limit; x++)
        {
            for (int z = 0; z < limitD; z++)
            {
                float h = heightSource.GetHeight(x, z);
                Vector3 center = new Vector3(origin.x + x * ts + ts * 0.5f, h + 0.05f, origin.z + z * ts + ts * 0.5f);
                Gizmos.DrawWireCube(center, new Vector3(ts * 0.9f, 0.02f, ts * 0.9f));
            }
        }
    }
#endif
}
