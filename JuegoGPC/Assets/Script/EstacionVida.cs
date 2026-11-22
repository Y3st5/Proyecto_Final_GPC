using UnityEngine;

// Animaciones sencillas para la Estacion Espacial Toon (LEDs, hologramas, luces).
// Va FUERA de la carpeta Editor.
public class EstacionVida : MonoBehaviour
{
    public enum Tipo { Parpadeo, Girar, Flotar, Pulso }

    public Tipo tipo = Tipo.Parpadeo;
    public Vector3 eje = Vector3.up;
    public float velocidad = 1f;
    public float amplitud = 0.2f;
    public float fase;

    Renderer[] rends;
    Light luz;
    Vector3 posIni;
    float intIni;

    void Awake()
    {
        rends = GetComponentsInChildren<Renderer>(true);
        luz = GetComponent<Light>();
        posIni = transform.localPosition;
        if (luz != null) intIni = luz.intensity;
    }

    void Update()
    {
        float t = Time.time * velocidad + fase;
        switch (tipo)
        {
            case Tipo.Parpadeo:
                bool on = Mathf.PerlinNoise(t, fase * 3.1f) > 0.42f;
                for (int i = 0; i < rends.Length; i++)
                    if (rends[i] != null) rends[i].enabled = on;
                break;
            case Tipo.Girar:
                transform.Rotate(eje, velocidad * Time.deltaTime, Space.Self);
                break;
            case Tipo.Flotar:
                transform.localPosition = posIni + Vector3.up * (Mathf.Sin(t) * amplitud);
                break;
            case Tipo.Pulso:
                if (luz != null) luz.intensity = intIni * (1f + Mathf.Sin(t) * amplitud);
                break;
        }
    }
}
