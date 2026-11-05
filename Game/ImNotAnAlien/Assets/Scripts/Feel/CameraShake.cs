using UnityEngine;

// Tremida de câmera baseada em "trauma": cada impacto soma trauma (0 a 1), que vai
// diminuindo com o tempo. A tremida é proporcional a trauma², então impactos fracos
// mal aparecem e impactos fortes chacoalham bastante.
// Fica na Main Camera e roda DEPOIS do controlador do jogador (que posiciona a câmera).
[DisallowMultipleComponent]
[DefaultExecutionOrder(1000)]
public sealed class CameraShake : MonoBehaviour
{
    private static CameraShake instance;

    [Tooltip("Rotação máxima (graus) com trauma no máximo.")]
    [SerializeField, Min(0f)] private float maxAngle = 4f;
    [Tooltip("Deslocamento máximo (metros) com trauma no máximo.")]
    [SerializeField, Min(0f)] private float maxOffset = 0.25f;
    [Tooltip("Velocidade do chacoalhar.")]
    [SerializeField, Min(0.1f)] private float frequency = 22f;
    [Tooltip("Quanto trauma some por segundo.")]
    [SerializeField, Min(0.01f)] private float recoveryPerSecond = 1.6f;

    private float trauma;
    private float seed;

    public static void AddTrauma(float amount)
    {
        if (instance != null)
        {
            instance.trauma = Mathf.Clamp01(instance.trauma + amount);
        }
    }

    private void Awake()
    {
        instance = this;
        seed = Random.value * 100f;
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    private void LateUpdate()
    {
        if (trauma <= 0f)
        {
            return;
        }

        float shake = trauma * trauma;
        float time = Time.unscaledTime * frequency; // continua tremendo durante a pausa de impacto

        float pitch = (Mathf.PerlinNoise(seed, time) * 2f - 1f) * maxAngle * shake;
        float yaw = (Mathf.PerlinNoise(seed + 1f, time) * 2f - 1f) * maxAngle * shake;
        float roll = (Mathf.PerlinNoise(seed + 2f, time) * 2f - 1f) * maxAngle * shake;
        Vector3 offset = new Vector3(
            Mathf.PerlinNoise(seed + 3f, time) * 2f - 1f,
            Mathf.PerlinNoise(seed + 4f, time) * 2f - 1f,
            0f) * (maxOffset * shake);

        // O controlador reposiciona a câmera a cada quadro, então a tremida não se acumula.
        transform.localRotation *= Quaternion.Euler(pitch, yaw, roll);
        transform.position += transform.rotation * offset;

        trauma = Mathf.Max(0f, trauma - recoveryPerSecond * Time.unscaledDeltaTime);
    }
}
