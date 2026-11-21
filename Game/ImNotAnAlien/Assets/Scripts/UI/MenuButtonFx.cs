using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Animação dos botões do menu inicial (20/11): cresce um pouco, clareia e acende a pílula de luz
// quando o mouse passa por cima ou quando é selecionado pelo teclado/controle.
[DisallowMultipleComponent]
public sealed class MenuButtonFx : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    public Image face;
    public Color baseColor = Color.white;
    public Image glow;
    public float hoverScale = 1.05f;

    private bool pointer;
    private bool selected;
    private float t;
    private Button button;

    private void Awake()
    {
        button = GetComponent<Button>();
    }

    private void OnDisable()
    {
        wasOn = false;
        pointer = false;
        selected = false;
        t = 0f;
        Refresh();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        pointer = true;
        if (button != null && button.interactable && EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(gameObject); // mouse e teclado andam juntos
        }
    }

    public void OnPointerExit(PointerEventData eventData) => pointer = false;
    public void OnSelect(BaseEventData eventData) => selected = true;
    public void OnDeselect(BaseEventData eventData) => selected = false;

    private bool wasOn;

    private void Update()
    {
        bool on = (pointer || selected) && (button == null || button.interactable);
        if (on && !wasOn)
        {
            MenuSounds.Hover(); // "plim" ao passar o mouse / mudar a seleção
        }

        wasOn = on;
        t = Mathf.MoveTowards(t, on ? 1f : 0f, Time.unscaledDeltaTime * 8f);
        Refresh();
    }

    private void Refresh()
    {
        float e = t * t * (3f - 2f * t);
        transform.localScale = Vector3.one * Mathf.Lerp(1f, hoverScale, e);
        if (face != null)
        {
            Color c = Color.Lerp(baseColor, Color.Lerp(baseColor, Color.white, 0.18f), e);
            if (button != null && !button.interactable)
            {
                c.a = 0.45f;
            }

            face.color = c;
        }

        if (glow != null)
        {
            Color g = glow.color;
            g.a = Mathf.Lerp(0.35f, 1f, e);
            glow.color = g;
        }
    }
}
