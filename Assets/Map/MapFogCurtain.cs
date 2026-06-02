using UnityEngine;

/// <summary>
/// Туман-занавес по периметру прямоугольной карты.
/// Скрывает обрыв края / фундамент. Позиционируется автоматически
/// из размеров HeightMapGenerator + tileSize, поэтому при расширении
/// карты сам встаёт на место. Вызывать BuildCurtain() из TerrainManager
/// после построения меша.
///
/// Статичный занавес (без реакции на игрока), молочный, выше игрока.
/// </summary>
public class MapFogCurtain : MonoBehaviour
{
    [Header("Источники размеров")]
    public HeightMapGenerator heightSource;
    public ChunkedTerrainBuilder chunkedBuilder;     // приоритетный источник tileSize
    public SeamlessTerrainBuilder seamlessBuilder;   // запасной

    [Header("Геометрия занавеса")]
    [Tooltip("Высота стены тумана над землёй (м). Должна перекрывать игрока.")]
    public float curtainHeight = 6f;
    [Tooltip("Толщина полосы тумана (м).")]
    public float curtainThickness = 10f;
    [Tooltip("Насколько занавес смещён внутрь карты от самого края (м).")]
    public float inset = 2f;
    [Tooltip("Нижняя граница тумана по Y (м).")]
    public float baseY = -1f;

    [Header("Вид")]
    public Color fogColor = new Color(0.88f, 0.90f, 0.94f, 1f); // молочный
    [Tooltip("Пиковая непрозрачность частицы (0..1).")]
    [Range(0f, 1f)] public float maxAlpha = 0.5f;
    [Tooltip("Размер частицы (м). Крупные мягкие частицы = меньше нагрузка.")]
    public float particleSize = 12f;
    public float particleLifetime = 6f;
    [Tooltip("Частиц в секунду на каждый метр кромки. Меньше = легче для GPU.")]
    public float particlesPerMeter = 0.12f;
    [Tooltip("Лёгкий подъём тумана вверх (м/с).")]
    public float upwardDrift = 0.15f;
    [Tooltip("Сила турбулентности (колыхание).")]
    public float turbulence = 0.4f;

    [Header("Материал (опционально)")]
    [Tooltip("Если не задан — создаётся мягкая полупрозрачная частица.")]
    public Material fogMaterial;

    private GameObject container;
    private Texture2D softTex;

    public void BuildCurtain()
    {
        if (heightSource == null || !heightSource.isGenerated)
        {
            Debug.LogError("MapFogCurtain: карта высот не готова!");
            return;
        }

        ClearCurtain();

        float ts = GetTileSize();
        float halfW = heightSource.width * ts / 2f;
        float halfD = heightSource.depth * ts / 2f;

        container = new GameObject("FogCurtain");
        container.transform.SetParent(transform, false);

        Material mat = fogMaterial != null ? fogMaterial : CreateDefaultMaterial();

        float ex = (halfW - inset) * 2f; // длина кромки вдоль X
        float ez = (halfD - inset) * 2f; // длина кромки вдоль Z
        float yc = baseY + curtainHeight * 0.5f;

        // Z- (перед) и Z+ (зад): тянутся вдоль X
        CreateEdge("Edge_Front", new Vector3(0, yc, -(halfD - inset)),
                   new Vector3(ex, curtainHeight, curtainThickness), ex, mat);
        CreateEdge("Edge_Back", new Vector3(0, yc, (halfD - inset)),
                   new Vector3(ex, curtainHeight, curtainThickness), ex, mat);

        // X- (лево) и X+ (право): тянутся вдоль Z
        CreateEdge("Edge_Left", new Vector3(-(halfW - inset), yc, 0),
                   new Vector3(curtainThickness, curtainHeight, ez), ez, mat);
        CreateEdge("Edge_Right", new Vector3((halfW - inset), yc, 0),
                   new Vector3(curtainThickness, curtainHeight, ez), ez, mat);

        Debug.Log($"MapFogCurtain: занавес построен по периметру {halfW * 2f:F0}×{halfD * 2f:F0} м");
    }

    public void ClearCurtain()
    {
        if (container != null)
        {
            if (Application.isPlaying) Destroy(container);
            else DestroyImmediate(container);
            container = null;
        }

        // подчистить возможные остатки от прошлых сборок
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var c = transform.GetChild(i);
            if (c != null && c.name == "FogCurtain")
            {
                if (Application.isPlaying) Destroy(c.gameObject);
                else DestroyImmediate(c.gameObject);
            }
        }
    }

    private void CreateEdge(string name, Vector3 localPos, Vector3 boxScale, float edgeLength, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(container.transform, false);
        go.transform.localPosition = localPos;

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop();

        var main = ps.main;
        main.loop = true;
        main.startLifetime = particleLifetime;
        main.startSpeed = 0f;
        main.startSize = particleSize;
        main.startColor = fogColor;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake = true;

        float rate = Mathf.Max(1f, edgeLength * particlesPerMeter);
        main.maxParticles = Mathf.CeilToInt(rate * particleLifetime * 1.5f);

        var emission = ps.emission;
        emission.rateOverTime = rate;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = boxScale;

        // Плавное появление и затухание непрозрачности по времени жизни
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(maxAlpha, 0.3f),
                new GradientAlphaKey(maxAlpha, 0.7f),
                new GradientAlphaKey(0f, 1f)
            });
        col.color = grad;

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.y = new ParticleSystem.MinMaxCurve(upwardDrift);

        var noise = ps.noise;
        noise.enabled = turbulence > 0f;
        noise.strength = turbulence;
        noise.frequency = 0.2f;
        noise.scrollSpeed = 0.1f;

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.material = mat;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.alignment = ParticleSystemRenderSpace.View;

        ps.Play();
    }

    private float GetTileSize()
    {
        if (chunkedBuilder != null) return chunkedBuilder.TileSize;
        if (seamlessBuilder != null) return seamlessBuilder.tileSize;
        return 4f;
    }

    private Material CreateDefaultMaterial()
    {
        // Built-in RP: подбираем доступный шейдер частиц по убыванию качества
        Shader sh = Shader.Find("Particles/Standard Unlit");
        if (sh == null) sh = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
        if (sh == null) sh = Shader.Find("Sprites/Default");

        var m = new Material(sh);
        if (softTex == null) softTex = CreateSoftTexture(64);

        if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", softTex);
        if (m.HasProperty("_BaseColorMap")) m.SetTexture("_BaseColorMap", softTex);
        if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);

        return m;
    }

    // Мягкая круглая текстура — даёт «пушистый» туман без отдельного ассета.
    private Texture2D CreateSoftTexture(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float r = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) - r;
                float dy = (y + 0.5f) - r;
                float dist = Mathf.Sqrt(dx * dx + dy * dy) / r;
                float a = Mathf.Clamp01(1f - dist);
                a *= a; // мягче к краю
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        return tex;
    }

    void OnDestroy() => ClearCurtain();
}
