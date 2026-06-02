using UnityEngine;

/// <summary>
/// Координирует весь процесс генерации ландшафта.
/// Поддерживает два режима:
/// - SeamlessTerrainBuilder (один меш, маленькие карты)
/// - ChunkedTerrainBuilder (чанки, большие карты) + InstancedObjectPlacer
/// </summary>
public class TerrainManager : MonoBehaviour
{
    [Header("Источник высот")]
    public HeightMapGenerator heightGenerator;

    [Header("Меш — выберите один из двух")]
    public SeamlessTerrainBuilder terrainBuilder;       // маленькие карты
    public ChunkedTerrainBuilder chunkedTerrainBuilder; // большие карты (рекомендуется)

    [Header("Объекты — выберите один из двух")]
    public ObjectPlacer objectPlacer;                   // GameObject'ы, нужны коллайдеры
    public InstancedObjectPlacer instancedObjectPlacer; // GPU instancing, без коллайдеров, быстро

    [Header("Опциональные системы")]
    public TerrainZoneSystem zoneSystem;
    public MapFogCurtain fogCurtain;                    // туман-занавес по краям карты

    [Header("Настройки запуска")]
    public bool generateOnStart = true;
    public bool clearBeforeGenerate = true;
    public bool logTimings = true;

    void OnEnable()
    {
        if (heightGenerator != null)
        {
            heightGenerator.onHeightMapReady += OnHeightMapReady;
            heightGenerator.onHeightMapGenerated += OnHeightMapGenerated;
        }
    }

    void OnDisable()
    {
        if (heightGenerator != null)
        {
            heightGenerator.onHeightMapReady -= OnHeightMapReady;
            heightGenerator.onHeightMapGenerated -= OnHeightMapGenerated;
        }
    }

    void Start()
    {
        if (generateOnStart) GenerateAll();
    }

    private void OnHeightMapReady()
        => Debug.Log("=== Событие: Карта высот готова! ===");

    private void OnHeightMapGenerated(float[,] heightMap)
        => Debug.Log($"=== Карта высот: {heightMap.GetLength(0)}×{heightMap.GetLength(1)} ===");

    [ContextMenu("Generate All")]
    public void GenerateAll()
    {
        AutoAssignComponents();
        if (!ValidateComponents()) return;

        Debug.Log("=== Начинаем генерацию ===");
        if (clearBeforeGenerate) ClearAll();

        var sw = System.Diagnostics.Stopwatch.StartNew();

        // 1. Карта высот
        heightGenerator.Generate();
        LogStep("Карта высот", ref sw);

        // 2. Меш
        if (chunkedTerrainBuilder != null)
        {
            chunkedTerrainBuilder.BuildTerrain();
            LogStep("Чанки меша", ref sw);
        }
        else if (terrainBuilder != null)
        {
            terrainBuilder.BuildTerrain();
            LogStep("Меш", ref sw);
        }

        // 3. Зоны
        if (zoneSystem != null)
        {
            zoneSystem.InitializeZones();
            LogStep("Зоны", ref sw);
        }

        // 4. Объекты
        if (instancedObjectPlacer != null)
        {
            instancedObjectPlacer.PlaceAllObjects();
            LogStep("Instanced объекты", ref sw);
        }
        else if (objectPlacer != null)
        {
            objectPlacer.PlaceAllObjects();
            LogStep("Объекты", ref sw);
        }

        // 5. Туман по краям
        if (fogCurtain != null)
        {
            fogCurtain.BuildCurtain();
            LogStep("Туман-занавес", ref sw);
        }

        Debug.Log("=== Генерация завершена! ===");
    }

    [ContextMenu("Clear All")]
    public void ClearAll()
    {
        if (chunkedTerrainBuilder != null) chunkedTerrainBuilder.ClearTerrain();
        if (terrainBuilder != null) terrainBuilder.ClearTerrain();
        if (objectPlacer != null) objectPlacer.ClearOldObjects();
        if (instancedObjectPlacer != null) instancedObjectPlacer.ClearObjects();
        if (heightGenerator != null) heightGenerator.Clear();
        if (zoneSystem != null) zoneSystem.ClearZones();
        if (fogCurtain != null) fogCurtain.ClearCurtain();
    }

    private void LogStep(string name, ref System.Diagnostics.Stopwatch sw)
    {
        if (!logTimings) return;
        sw.Stop();
        Debug.Log($"   {name}: {sw.ElapsedMilliseconds}ms");
        sw.Restart();
    }

    private void AutoAssignComponents()
    {
        if (heightGenerator == null) heightGenerator = GetComponent<HeightMapGenerator>();
        if (terrainBuilder == null) terrainBuilder = GetComponent<SeamlessTerrainBuilder>();
        if (chunkedTerrainBuilder == null) chunkedTerrainBuilder = GetComponent<ChunkedTerrainBuilder>();
        if (objectPlacer == null) objectPlacer = GetComponent<ObjectPlacer>();
        if (instancedObjectPlacer == null) instancedObjectPlacer = GetComponent<InstancedObjectPlacer>();
        if (zoneSystem == null) zoneSystem = GetComponent<TerrainZoneSystem>();
        if (fogCurtain == null) fogCurtain = GetComponent<MapFogCurtain>();
    }

    private bool ValidateComponents()
    {
        if (heightGenerator == null)
        {
            Debug.LogError("TerrainManager: HeightMapGenerator не найден!");
            return false;
        }
        if (chunkedTerrainBuilder == null && terrainBuilder == null)
        {
            Debug.LogError("TerrainManager: нужен хотя бы один из TerrainBuilder!");
            return false;
        }
        return true;
    }
}
