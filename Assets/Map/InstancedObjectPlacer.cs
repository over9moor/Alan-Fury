using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

/// <summary>
/// Размещает объекты через GPU Instancing — один draw call на батч
/// вместо тысяч GameObject'ов со своими компонентами.
///
/// Отличие от ObjectPlacer:
/// - вместо инстансов хранятся только матрицы (позиция/поворот/масштаб)
/// - один тип = пачка DrawMeshInstanced вызовов (макс. 1023 за вызов)
/// - нет GameObject'ов = нет overhead'а на Transform/Renderer
///
/// Важно: объекты не реальные в сцене — у них нет коллайдеров.
/// Для интерактивных объектов используй ObjectPlacer (постройки, камни),
/// а этот — для массовки (трава, мелкие деревья).
///
/// Батчи нарезаются ОДИН РАЗ при генерации (без аллокаций в Update).
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

        [Header("Плотность")]
        [Tooltip("Сколько клеток сетки занимает один объект (1 = шанс в каждой клетке)")]
        public int cellsPerObject = 1;
        [Tooltip("Шанс спавна в подходящей клетке (0..1)")]
        [Range(0f, 1f)] public float density = 0.3f;
        public float minHeight = 0f;
        public float maxHeight = 1f;

        [Header("Смещение внутри клетки")]
        [Tooltip("Разброс позиции внутри клетки (0 = строго в центре)")]
        [Range(0f, 0.5f)] public float cellJitter = 0.3f;

        [Header("Внешность")]
        public bool randomRotationY = true;
        public float minScale = 0.8f;
        public float maxScale = 1.2f;
        public float heightOffset = 0f;

        [Header("Тени")]
        public ShadowCastingMode shadowCasting = ShadowCastingMode.On;
        public bool receiveShadows = true;

        // Данные для отрисовки (заполняются при генерации)
        [HideInInspector] public List<Matrix4x4> matrices = new List<Matrix4x4>();
        [HideInInspector] public List<Matrix4x4[]> batches = new List<Matrix4x4[]>();
        [HideInInspector] public MaterialPropertyBlock propertyBlock;
    }

    [Header("Источник высот")]
    public HeightMapGenerator heightSource;

    [Header("Источник tileSize")]
    public ChunkedTerrainBuilder terrainBuilder;

    [Header("Типы объектов")]
    public List<InstancedObjectType> objectTypes = new List<InstancedObjectType>();

    [Header("Сид генерации")]
    public bool randomSeed = true;
    public int seed = 42;

    [Header("Отступ от края (в клетках)")]
    [Tooltip("Не спавнить объекты в приграничных клетках. ~Совпадает с границей карты при tileSize=4.")]
    public int borderCells = 2;

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
            objType.batches.Clear();
            objType.propertyBlock = new MaterialPropertyBlock();

            if (objType.mesh == null || objType.material == null)
            {
                Debug.LogWarning($"InstancedObjectPlacer: '{objType.name}' — нет меша или материала.");
                continue;
            }

            PlaceType(objType, w, d, ts, origin);
            BuildBatches(objType);
            Debug.Log($"InstancedObjectPlacer: '{objType.name}' — {objType.matrices.Count} инстансов, {objType.batches.Count} батчей");
        }

        isGenerated = true;
    }

    public void ClearObjects()
    {
        foreach (var t in objectTypes)
        {
            t.matrices.Clear();
            t.batches.Clear();
        }
        isGenerated = false;
    }

    // =================== Генерация ===================

    private void PlaceType(InstancedObjectType objType, int w, int d, float ts, Vector3 origin)
    {
        // Шагаем сеткой с шагом cellsPerObject
        int step = Mathf.Max(1, objType.cellsPerObject);

        for (int x = 0; x < w; x += step)
        {
            for (int z = 0; z < d; z += step)
            {
                // Не спавнить в приграничных клетках (граница без леса)
                if (x < borderCells || x >= w - borderCells || z < borderCells || z >= d - borderCells)
                    continue;

                // Случайная плотность
                if (UnityEngine.Random.value > objType.density) continue;

                float h = heightSource.GetHeight(x, z);
                if (h < objType.minHeight || h > objType.maxHeight) continue;

                // Центр клетки + случайный jitter
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

    /// <summary>
    /// Нарезает matrices на готовые массивы-батчи один раз.
    /// В Update больше нет аллокаций.
    /// </summary>
    private void BuildBatches(InstancedObjectType objType)
    {
        int total = objType.matrices.Count;
        for (int i = 0; i < total; i += BatchSize)
        {
            int count = Mathf.Min(BatchSize, total - i);
            var batch = new Matrix4x4[count];
            objType.matrices.CopyTo(i, batch, 0, count);
            objType.batches.Add(batch);
        }
    }

    // =================== Отрисовка ===================

    void Update()
    {
        if (!isGenerated) return;
        DrawAll();
    }

    // DrawMeshInstanced рисует максимум 1023 матрицы за вызов
    private const int BatchSize = 1023;

    private void DrawAll()
    {
        foreach (var objType in objectTypes)
        {
            if (objType.mesh == null || objType.material == null) continue;

            var batches = objType.batches;
            for (int i = 0; i < batches.Count; i++)
            {
                var batch = batches[i];
                Graphics.DrawMeshInstanced(
                    objType.mesh,
                    objType.subMeshIndex,
                    objType.material,
                    batch,
                    batch.Length,
                    objType.propertyBlock,
                    objType.shadowCasting,
                    objType.receiveShadows
                );
            }
        }
    }

    // =================== Сервис ===================

    private bool Validate()
    {
        if (heightSource == null || !heightSource.isGenerated)
        {
            Debug.LogError("InstancedObjectPlacer: HeightMapGenerator не готов!");
            return false;
        }
        if (terrainBuilder == null)
        {
            Debug.LogError("InstancedObjectPlacer: нет ChunkedTerrainBuilder!");
            return false;
        }
        return true;
    }

    /// <summary>
    /// Возвращает центр клетки в мировых координатах (полезно для NPC и квестов).
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
