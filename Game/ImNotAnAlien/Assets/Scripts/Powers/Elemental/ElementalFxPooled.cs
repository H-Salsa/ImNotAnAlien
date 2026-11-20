using UnityEngine;

// Etiqueta dos efeitos que moram na "caixa" do ElementalFx (reaproveitamento, 18/11).
// Quando o sistema de partículas avisa que acabou (ou passa do tempo máximo), devolve o efeito
// para a caixa em vez de destruir. Criada sozinha pelo ElementalFx: não precisa pôr em nada.
[DisallowMultipleComponent]
public sealed class ElementalFxPooled : MonoBehaviour
{
    private ParticleSystem system;
    private float deadline;
    private bool stopped;

    // Guardado na caixa (desligado, esperando o próximo uso).
    public bool InBox { get; private set; }

    // Saiu da caixa. "seconds" > 0: volta sozinho depois desse tempo, mesmo sem o aviso de "acabei".
    public void Arm(float seconds)
    {
        InBox = false;
        stopped = false;
        deadline = seconds > 0f ? Time.time + seconds : 0f;
    }

    public void Store()
    {
        InBox = true;
        stopped = false;
        deadline = 0f;
    }

    // Chamado pela Unity quando o efeito termina (stopAction = Callback).
    private void OnParticleSystemStopped()
    {
        stopped = true;
    }

    // Devolve no fim do quadro (fora do aviso da Unity, que é o jeito seguro).
    private void LateUpdate()
    {
        if (InBox)
        {
            return;
        }

        if (stopped || (deadline > 0f && Time.time >= deadline))
        {
            if (system == null)
            {
                system = GetComponent<ParticleSystem>();
            }

            ElementalFx.Recycle(system);
        }
    }
}
