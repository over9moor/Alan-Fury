using UnityEngine;
using Unity.Jobs;
using Unity.Collections;
using Unity.Burst;
using Unity.Mathematics;

/// <summary>
/// Генерирует карту высот через шум Перлина.
/// Единственный источник данных о высотах для всех остальных скриптов.
/// </summary>
public class HeightMapGenerator : MonoBehaviour
{
    [Header("Размеры карты")]
    public int width = 20;
    public int depth = 20;

    [Header("Настройки шума")]
    public float noiseScale = 0.08f;
    public float maxHeight = 0.5f;

    [Header("Сид")]
    public bool randomSeed = true;
    public int seed;

    [Header("Производительность")]
    public bool useJobs = true;
    // Минимальный размер карты для использования Job System
    public int jobsMinCells = 10000;

    // Публичные данные — доступ для всех скриптов
    public float[,] heightMap { get; private set; }
    public bool isGenerated { get; private set; }

    // ============ СОБЫТИЯ ============
    public System.Action onHeightMapReady;
    public System.Action<float[,]> onHeightMapGenerated;

    /// <summary>
    /// Запускает генерацию карты высот.
    /// </summary>
    public void Generate()
    {
        if (randomSeed)
            seed = UnityEngine.Random.Range(0, 100000);

        heightMap = new float[width, depth];

        if (useJobs && width * depth > jobsMinCells)
            GenerateWithJobs();
        else
            GenerateSimple();

        isGenerated = true;

        onHeightMapReady?.Invoke();
        onHeightMapGenerated?.Invoke(heightMap);
    }

    private void GenerateSimple()
    {
        float seedOffset = seed * 0.1f;

        const int octaves = 4;
        const float persistence = 0.5f;
        const float lacunarity = 2f;

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                float bx = x * noiseScale + seedOffset;
                float bz = z * noiseScale + seedOffset;

                // Фрактальный шум (fBm): крупная форма + детализация.
                // Крупные октавы дают связные бассейны (низины) и водоразделы.
                float amp = 1f, freq = 1f, sum = 0f, ampMax = 0f;
                for (int o = 0; o < octaves; o++)
                {
                    sum += Mathf.PerlinNoise(bx * freq, bz * freq) * amp;
                    ampMax += amp;
                    amp *= persistence;
                    freq *= lacunarity;
                }

                heightMap[x, z] = (sum / ampMax) * maxHeight;
            }
        }
    }

    private void GenerateWithJobs()
    {
        int totalCells = width * depth;
        NativeArray<float> heightsArray = new NativeArray<float>(totalCells, Allocator.TempJob);

        var job = new HeightMapJob
        {
            width = width,
            noiseScale = noiseScale,
            maxHeight = maxHeight,
            seed = seed,
            heights = heightsArray
        };

        JobHandle handle = job.Schedule(totalCells, 64);
        handle.Complete();

        for (int i = 0; i < totalCells; i++)
        {
            int x = i % width;
            int z = i / width;
            heightMap[x, z] = heightsArray[i];
        }

        heightsArray.Dispose();
    }

    /// <summary>
    /// Burst-совместимая реализация шума Перлина через Unity.Mathematics.
    /// noise.cnoise возвращает значения в [-1, 1], нормализуем в [0, 1].
    /// </summary>
    [BurstCompile]
    private struct HeightMapJob : IJobParallelFor
    {
        public int width;
        public float noiseScale;
        public float maxHeight;
        public int seed;
        public NativeArray<float> heights;

        public void Execute(int index)
        {
            int x = index % width;
            int z = index / width;

            float seedOffset = seed * 0.1f;
            float bx = x * noiseScale + seedOffset;
            float bz = z * noiseScale + seedOffset;

            // Фрактальный шум (fBm): крупная форма + детализация.
            const int octaves = 4;
            const float persistence = 0.5f;
            const float lacunarity = 2f;

            float amp = 1f, freq = 1f, sum = 0f, ampMax = 0f;
            for (int o = 0; o < octaves; o++)
            {
                float2 coord = new float2(bx * freq, bz * freq);
                // cnoise: [-1, 1] → нормализуем в [0, 1]
                float n = Unity.Mathematics.noise.cnoise(coord) * 0.5f + 0.5f;
                sum += n * amp;
                ampMax += amp;
                amp *= persistence;
                freq *= lacunarity;
            }

            heights[index] = (sum / ampMax) * maxHeight;
        }
    }

    /// <summary>
    /// Очищает карту высот.
    /// </summary>
    public void Clear()
    {
        heightMap = null;
        isGenerated = false;
    }

    /// <summary>
    /// Безопасно возвращает высоту по индексам ячейки.
    /// </summary>
    public float GetHeight(int x, int z)
    {
        if (heightMap == null) return 0f;
        if (x < 0 || x >= width || z < 0 || z >= depth) return 0f;
        return heightMap[x, z];
    }

    /// <summary>
    /// Возвращает высоту с билинейной интерполяцией.
    /// </summary>
    public float GetHeightBilinear(float worldX, float worldZ, float tileSize, Vector3 mapOrigin)
    {
        if (heightMap == null) return 0f;

        float localX = worldX - mapOrigin.x;
        float localZ = worldZ - mapOrigin.z;

        float cellXFloat = localX / tileSize;
        float cellZFloat = localZ / tileSize;

        int cellX = Mathf.FloorToInt(cellXFloat);
        int cellZ = Mathf.FloorToInt(cellZFloat);

        float fx = cellXFloat - cellX;
        float fz = cellZFloat - cellZ;

        float h00 = GetHeight(cellX, cellZ);
        float h10 = GetHeight(Mathf.Min(cellX + 1, width - 1), cellZ);
        float h01 = GetHeight(cellX, Mathf.Min(cellZ + 1, depth - 1));
        float h11 = GetHeight(Mathf.Min(cellX + 1, width - 1), Mathf.Min(cellZ + 1, depth - 1));

        float h0 = Mathf.Lerp(h00, h10, fx);
        float h1 = Mathf.Lerp(h01, h11, fx);

        return Mathf.Lerp(h0, h1, fz);
    }

    /// <summary>
    /// Возвращает высоту поверхности в произвольной мировой точке.
    /// </summary>
    public float GetHeightAtWorldPos(Vector3 worldPos, float tileSize, Vector3 mapOrigin)
    {
        return GetHeightBilinear(worldPos.x, worldPos.z, tileSize, mapOrigin);
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!isGenerated || heightMap == null) return;

        float tileSize = 1f;
        var builder = GetComponent<SeamlessTerrainBuilder>();
        if (builder != null)
            tileSize = builder.tileSize;

        Vector3 mapOrigin = new Vector3(-width * tileSize / 2f, 0, -depth * tileSize / 2f);

        for (int x = 0; x < width; x += Mathf.Max(1, width / 20))
        {
            for (int z = 0; z < depth; z += Mathf.Max(1, depth / 20))
            {
                float h = heightMap[x, z];
                Vector3 pos = mapOrigin + new Vector3(x * tileSize, h, z * tileSize);
                Gizmos.color = Color.Lerp(Color.blue, Color.red, h / maxHeight);
                Gizmos.DrawWireCube(pos, Vector3.one * 0.2f);
            }
        }
    }
#endif
}
