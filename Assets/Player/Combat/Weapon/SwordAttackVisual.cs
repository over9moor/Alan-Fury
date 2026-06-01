using UnityEngine;

public class SwordAttackVisual : MonoBehaviour
{
    [Header("Спрайт атаки")]
    public Sprite arcSprite;
    public Color arcColor = new Color(1f, 0.2f, 0.2f, 0.7f);
    public float arcRadius = 2f;
    public float arcHeight = 1.5f;

    [Header("Индикатор замаха")]
    public Color windupColor = new Color(1f, 1f, 0f, 0.4f);

    private SpriteRenderer arcRenderer;
    private SpriteRenderer windupRenderer;

    private bool isShowingArc;
    private float arcTimer;
    private float arcDuration;

    void Awake()
    {
        // --- Спрайт атаки ---
        GameObject arcObj = new GameObject("ArcSprite");
        arcObj.transform.SetParent(transform);
        arcObj.transform.localPosition = Vector3.zero;

        arcRenderer = arcObj.AddComponent<SpriteRenderer>();
        arcRenderer.sprite = arcSprite;
        arcRenderer.color = arcColor;
        arcRenderer.sortingOrder = 100;
        arcRenderer.enabled = false;

        // --- Индикатор замаха ---
        GameObject windupObj = new GameObject("WindupSprite");
        windupObj.transform.SetParent(transform);
        windupObj.transform.localPosition = Vector3.zero;

        windupRenderer = windupObj.AddComponent<SpriteRenderer>();
        windupRenderer.sprite = arcSprite;
        windupRenderer.color = windupColor;
        windupRenderer.sortingOrder = 99;
        windupRenderer.enabled = false;
    }

    void Update()
    {
        if (!isShowingArc) return;

        arcTimer -= Time.deltaTime;

        if (arcTimer <= 0f)
        {
            HideArc();
            return;
        }

        // Плавное затухание к концу
        float t = arcTimer / arcDuration;
        Color c = arcColor;
        c.a = arcColor.a * t;
        arcRenderer.color = c;
    }

    // Вызывается при начале замаха
    public void ShowWindup()
    {
        if (windupRenderer == null) return;
        windupRenderer.enabled = true;
        windupRenderer.transform.localScale = Vector3.one * arcRadius;
        // Плоско на земле (повёрнут горизонтально)
        windupRenderer.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
    }

    public void HideWindup()
    {
        if (windupRenderer != null)
            windupRenderer.enabled = false;
    }

    // Вызывается при самом ударе
    public void ShowArc(Vector3 direction, Vector3 offset, float duration, float chargePercent = 0f)
    {
        HideWindup();

        if (arcRenderer == null) return;

        isShowingArc = true;
        arcDuration = duration;
        arcTimer = duration;

        // Позиция — перед игроком
        Vector3 origin = transform.parent != null
            ? transform.parent.position + offset
            : transform.position + offset;

        arcRenderer.transform.position = origin + direction.normalized * (arcRadius * 0.8f);
        arcRenderer.transform.position += Vector3.up * (arcHeight * 0.4f);

        // Поворот по направлению атаки, плоско на земле
        float angle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        arcRenderer.transform.rotation = Quaternion.Euler(90f, angle, 0f);

        // Масштаб от заряда
        float scale = arcRadius * Mathf.Lerp(0.8f, 1.4f, chargePercent);
        arcRenderer.transform.localScale = new Vector3(scale, scale, 1f);

        // Цвет — белеет от заряда
        arcRenderer.color = Color.Lerp(arcColor, Color.white, chargePercent * 0.5f);
        arcRenderer.enabled = true;
    }

    public void HideArc()
    {
        isShowingArc = false;
        if (arcRenderer != null)
        {
            arcRenderer.enabled = false;
            arcRenderer.color = arcColor; // сбросить цвет
        }
    }
}
