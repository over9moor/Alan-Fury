using UnityEngine;

/// <summary>
/// Заклинание света ("ведьмин огонёк") на клавишу E.
/// Создаёт точечный источник света на игроке. Тратит ману при касте
/// и (опционально) расходует ману, пока горит. Повторное E — выключить.
/// Гаснет сам, когда мана заканчивается.
///
/// Вешается на объект игрока (рядом с PlayerResources).
/// </summary>
public class WitchLight : MonoBehaviour
{
    [Header("Ввод")]
    public KeyCode castKey = KeyCode.E;

    [Header("Ресурсы")]
    [Tooltip("Пусто → ищется PlayerResources на этом объекте.")]
    public PlayerResources resources;
    [Tooltip("Стоимость включения (мана). 0 = бесплатно.")]
    public float castCost = 15f;
    [Tooltip("Расход маны в секунду, пока горит. 0 = горит без расхода.")]
    public float drainPerSecond = 3f;

    [Header("Свет")]
    [Tooltip("Куда крепить огонёк. Пусто = этот объект.")]
    public Transform attachTo;
    public Vector3 localOffset = new Vector3(0f, 1.5f, 0f);
    public Color lightColor = new Color(0.7f, 0.85f, 1f);
    public float intensity = 2.2f;
    public float range = 14f;
    public LightShadows shadows = LightShadows.Soft;

    [Header("Появление / угасание")]
    [Tooltip("Время плавного включения/выключения (сек).")]
    public float fadeTime = 0.25f;

    [Header("Мерцание (опционально)")]
    public bool flicker = true;
    [Range(0f, 1f)] public float flickerAmount = 0.12f;
    public float flickerSpeed = 9f;

    public bool IsOn { get; private set; }

    private Light _light;
    private float _current; // текущая яркость для плавного fade
    private float _seed;

    void Awake()
    {
        if (resources == null) resources = GetComponent<PlayerResources>();
        if (attachTo == null) attachTo = transform;
        _seed = Random.value * 100f;
        CreateLight();
    }

    void Update()
    {
        if (Input.GetKeyDown(castKey))
            Toggle();

        // Расход маны и автогашение при нехватке
        if (IsOn && drainPerSecond > 0f && resources != null)
        {
            if (!resources.SpendMana(drainPerSecond * Time.deltaTime))
                TurnOff();
        }

        UpdateLightVisual();
    }

    public void Toggle()
    {
        if (IsOn) TurnOff();
        else TurnOn();
    }

    public void TurnOn()
    {
        if (IsOn) return;

        // Каст: списываем стоимость, если маны не хватает — не зажигаем
        if (resources != null && castCost > 0f && !resources.SpendMana(castCost))
            return;

        IsOn = true;
        if (_light != null) _light.enabled = true;
    }

    public void TurnOff() => IsOn = false;

    private void CreateLight()
    {
        var go = new GameObject("WitchLight");
        go.transform.SetParent(attachTo, false);
        go.transform.localPosition = localOffset;

        _light = go.AddComponent<Light>();
        _light.type = LightType.Point;
        _light.color = lightColor;
        _light.range = range;
        _light.shadows = shadows;
        _light.intensity = 0f;
        _light.enabled = false;
    }

    private void UpdateLightVisual()
    {
        if (_light == null) return;

        float target = IsOn ? intensity : 0f;
        float speed = fadeTime > 0f ? intensity / fadeTime : float.MaxValue;
        _current = Mathf.MoveTowards(_current, target, speed * Time.deltaTime);

        float final = _current;
        if (flicker && IsOn)
        {
            float n = Mathf.PerlinNoise(_seed, Time.time * flickerSpeed) * 2f - 1f;
            final *= 1f + n * flickerAmount;
        }

        _light.intensity = Mathf.Max(0f, final);

        // Выключаем компонент света, когда полностью погас (экономия)
        if (!IsOn && _current <= 0.001f && _light.enabled)
            _light.enabled = false;
        else if (_current > 0f && !_light.enabled)
            _light.enabled = true;
    }
}
