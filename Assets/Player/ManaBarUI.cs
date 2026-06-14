using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Полоска маны. Вешается на Image (Type = Filled) внутри Canvas.
/// Подписывается на PlayerResources.onManaChanged, при изменении обновляет fillAmount.
///
/// Настройка в редакторе:
///  Canvas → пустой объект ManaBar → два Image:
///   - фон (тёмный),
///   - заливка (этот скрипт): Image Type = Filled, Fill Method = Horizontal.
/// </summary>
public class ManaBarUI : MonoBehaviour
{
    [Header("Источник")]
    [Tooltip("Пусто → ищется по тегу Player.")]
    public PlayerResources resources;
    public string playerTag = "Player";

    [Header("Заливка")]
    [Tooltip("Пусто → берётся Image с этого объекта.")]
    public Image fillImage;

    [Header("Сглаживание")]
    [Tooltip("Скорость движения полоски к целевому значению (доли/сек). 0 = мгновенно.")]
    public float smoothSpeed = 6f;

    private float _target = 1f;

    void Awake()
    {
        if (fillImage == null) fillImage = GetComponent<Image>();
    }

    void Start()
    {
        if (resources == null && !string.IsNullOrEmpty(playerTag))
        {
            var go = GameObject.FindGameObjectWithTag(playerTag);
            if (go != null) resources = go.GetComponent<PlayerResources>();
        }

        if (resources != null)
        {
            resources.onManaChanged += OnManaChanged;
            _target = resources.ManaPercent;
            if (fillImage != null) fillImage.fillAmount = _target;
        }
    }

    void OnDestroy()
    {
        if (resources != null)
            resources.onManaChanged -= OnManaChanged;
    }

    private void OnManaChanged(float _) => _target = resources.ManaPercent;

    void Update()
    {
        if (fillImage == null) return;

        fillImage.fillAmount = smoothSpeed > 0f
            ? Mathf.MoveTowards(fillImage.fillAmount, _target, smoothSpeed * Time.deltaTime)
            : _target;
    }
}
