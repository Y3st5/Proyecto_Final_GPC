using UnityEngine;

// Animaciones simples para que la villa se sienta viva en Play.
// Va FUERA de la carpeta Editor (por ejemplo Assets/Scripts/VillaVida.cs).
public class VillaVida : MonoBehaviour
{
    public enum Tipo { Girar, Balancear, ParpadeoLuz, Flotar, Deriva }

    public Tipo tipo = Tipo.Balancear;
    public Vector3 eje = Vector3.forward;   // Girar: eje local
    public float velocidad = 1f;            // Girar: grados/seg | Balancear/Flotar/Parpadeo: frecuencia | Deriva: unidades/seg
    public float amplitud = 1f;             // Balancear: grados | Flotar: metros | Parpadeo: variacion (0-1) | Deriva: limite en X
    public float fase;
    [HideInInspector] public float factor = 1f;   // lo controla CicloDiaNoche (0 = apagado, 1 = encendido)

    Quaternion rotIni;
    Vector3 posIni;
    Light luz;
    float intensidadIni;

    void Awake()
    {
        rotIni = transform.localRotation;
        posIni = transform.localPosition;
        luz = GetComponent<Light>();
        if (luz != null) intensidadIni = luz.intensity;
    }

    void Update()
    {
        float t = Time.time + fase;
        switch (tipo)
        {
            case Tipo.Girar:
                transform.Rotate(eje, velocidad * Time.deltaTime, Space.Self);
                break;

            case Tipo.Balancear:
                transform.localRotation = rotIni * Quaternion.Euler(
                    Mathf.Sin(t * velocidad) * amplitud, 0f,
                    Mathf.Cos(t * velocidad * 0.8f) * amplitud);
                break;

            case Tipo.ParpadeoLuz:
                if (luz != null)
                    luz.intensity = intensidadIni * factor * (1f + (Mathf.PerlinNoise(t * velocidad, 0.5f) - 0.5f) * amplitud);
                break;

            case Tipo.Flotar:
                transform.localPosition = posIni + Vector3.up * (Mathf.Sin(t * velocidad) * amplitud);
                break;

            case Tipo.Deriva:
                transform.position += Vector3.right * (velocidad * Time.deltaTime);
                if (transform.position.x > amplitud)
                {
                    Vector3 p = transform.position;
                    p.x = -amplitud;
                    transform.position = p;
                }
                break;
        }
    }
}