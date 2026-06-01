using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [Header("Цель")]
    public Transform target;

    [Header("Смещение")]
    public Vector3 offset = new Vector3(0, 35, -35);

    [Header("Плавность")]
    public float smoothSpeed = 5f;

    [Header("Вращение")]
    public bool lockRotation = true;
    public Vector3 fixedRotation = new Vector3(40f, 45f, 0f);

    [Header("Ортографическая камера")]
    public bool isOrthographic = true;
    public float orthographicSize = 15f;

    [Header("Границы карты (опционально)")]
    public Bounds mapBounds;
    public bool clampToMap = false;

    private Camera cam;

    void Start()
    {
        cam = GetComponent<Camera>();

        if (isOrthographic && cam != null)
        {
            cam.orthographic = true;
            cam.orthographicSize = orthographicSize;
        }

        if (target == null)
            target = GameObject.FindGameObjectWithTag("Player")?.transform;

        // Автоопределение границ карты
        if (clampToMap && mapBounds.size == Vector3.zero)
        {
            GameObject map = GameObject.FindGameObjectWithTag("Map");
            if (map != null)
            {
                Renderer renderer = map.GetComponent<Renderer>();
                if (renderer != null)
                    mapBounds = renderer.bounds;
            }
        }
    }

    void LateUpdate()
    {
        if (target == null) return;

        Vector3 desiredPosition = target.position + offset;

        if (clampToMap && mapBounds.size != Vector3.zero)
        {
            float verticalHalf = cam.orthographicSize;
            float horizontalHalf = verticalHalf * Screen.width / Screen.height;

            desiredPosition.x = Mathf.Clamp(desiredPosition.x,
                mapBounds.min.x + horizontalHalf,
                mapBounds.max.x - horizontalHalf);

            desiredPosition.y = Mathf.Clamp(desiredPosition.y,
                mapBounds.min.y + verticalHalf,
                mapBounds.max.y - verticalHalf);
        }

        transform.position = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed * Time.deltaTime);

        if (lockRotation)
            transform.rotation = Quaternion.Euler(fixedRotation);
    }

    // Для визуализации границ в редакторе
    void OnDrawGizmosSelected()
    {
        if (clampToMap && mapBounds.size != Vector3.zero)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(mapBounds.center, mapBounds.size);
        }
    }
}
