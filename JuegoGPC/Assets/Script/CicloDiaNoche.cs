using System.Collections.Generic;
using UnityEngine;

// Ciclo de dia y noche para la Villa Toon.
// Uso: crea un GameObject vacio, agregale este script y arrastra tu Directional Light al campo "Sol".
// Va FUERA de la carpeta Editor (por ejemplo Assets/Scripts/CicloDiaNoche.cs).
public class CicloDiaNoche : MonoBehaviour
{
    [Header("Tiempo")]
    [Range(0f, 24f)] public float hora = 10f;
    [Tooltip("Segundos reales que dura un dia completo")]
    public float duracionDiaSegundos = 180f;
    [Tooltip("Marca esto para congelar la hora y moverla a mano con el slider")]
    public bool pausar = false;

    [Header("Sol y luna")]
    public Light sol;
    [Tooltip("Opcional. Si lo dejas vacio se crea una luna automaticamente")]
    public Light luna;
    public float intensidadSolMax = 1.2f;
    public float intensidadLunaMax = 0.25f;
    public float inclinacionY = 30f;
    public Color colorLuna = new Color(0.55f, 0.65f, 1f);

    [Header("Colores segun la hora (0 = 00:00, 1 = 24:00)")]
    public Gradient colorSol = CrearGradiente(
        new float[] { 0.00f, 0.22f, 0.27f, 0.50f, 0.65f, 0.73f, 0.79f, 1.00f },
        new Color[] {
            new Color(0.10f, 0.12f, 0.25f), new Color(0.20f, 0.20f, 0.40f), new Color(1.00f, 0.55f, 0.30f),
            new Color(1.00f, 0.96f, 0.88f), new Color(1.00f, 0.90f, 0.72f), new Color(1.00f, 0.50f, 0.25f),
            new Color(0.25f, 0.20f, 0.40f), new Color(0.10f, 0.12f, 0.25f) });
    public Gradient colorAmbiente = CrearGradiente(
        new float[] { 0.00f, 0.22f, 0.30f, 0.50f, 0.70f, 0.79f, 1.00f },
        new Color[] {
            new Color(0.07f, 0.09f, 0.20f), new Color(0.12f, 0.14f, 0.30f), new Color(0.60f, 0.55f, 0.55f),
            new Color(0.75f, 0.80f, 0.85f), new Color(0.65f, 0.55f, 0.55f), new Color(0.12f, 0.14f, 0.30f),
            new Color(0.07f, 0.09f, 0.20f) });
    public Gradient colorNiebla = CrearGradiente(
        new float[] { 0.00f, 0.22f, 0.28f, 0.50f, 0.72f, 0.80f, 1.00f },
        new Color[] {
            new Color(0.05f, 0.07f, 0.15f), new Color(0.08f, 0.10f, 0.22f), new Color(0.90f, 0.65f, 0.55f),
            new Color(0.70f, 0.85f, 0.95f), new Color(0.90f, 0.55f, 0.45f), new Color(0.08f, 0.10f, 0.22f),
            new Color(0.05f, 0.07f, 0.15f) });

    [Header("Niebla y cielo")]
    public bool usarNiebla = true;
    public float densidadNiebla = 0.010f;
    [Tooltip("Si tu skybox es Procedural, se oscurece de noche (se usa una copia, el asset no se modifica)")]
    public bool oscurecerSkybox = true;

    [Header("Villa (luces y ventanas)")]
    [Tooltip("Si esta vacio busca 'Villa_Toon_Generada' en la escena")]
    public Transform villa;
    public string nombreMaterialLuz = "Mat_Luz_Farol";
    [Tooltip("Nombres de propiedad del shader (cambialos si tu toon shader usa otros)")]
    public string propColor = "_BaseColor";
    public string propEmision = "_EmissionColor";
    public string propFactorNoche = "_NightFactor";
    public Color ventanaDia = new Color(0.55f, 0.72f, 0.82f);
    [ColorUsage(false, true)] public Color ventanaNoche = new Color(1.00f, 0.84f, 0.42f);

    [Header("Luciernagas")]
    public bool luciernagas = true;
    [Tooltip("Opcional: tu propio material emisivo/toon. Si esta vacio se crea uno Unlit brillante")]
    public Material materialLuciernaga;
    public int maxLuciernagas = 150;
    public Vector3 centroLuciernagas = new Vector3(0f, 1.6f, -3f);
    public Vector3 areaLuciernagas = new Vector3(44f, 3f, 40f);
    [ColorUsage(false, true)] public Color colorLuciernaga = new Color(0.75f, 1f, 0.3f);
    public float brilloLuciernaga = 4f;
    public float tamanoLuciernaga = 0.14f;

    // ---- internos ----
    struct LuzVilla { public Light luz; public VillaVida vida; public float intensidadBase; }
    LuzVilla[] lucesVilla;
    Renderer[] ventanas;
    MaterialPropertyBlock mpb;
    Material skyMat;
    Material fireflyMat;
    Mesh fireflyMesh;
    ParticleSystem psLuciernagas;
    bool inicializado;
    bool luciernagasCreadas;

    static Gradient CrearGradiente(float[] t, Color[] c)
    {
        Gradient g = new Gradient();
        GradientColorKey[] keys = new GradientColorKey[t.Length];
        for (int i = 0; i < t.Length; i++) keys[i] = new GradientColorKey(c[i], t[i]);
        g.SetKeys(keys, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return g;
    }

    void Start()
    {
        Inicializar();
        if (Application.isPlaying && luciernagas) AsegurarLuciernagas();
        Aplicar();
    }

    void Update()
    {
        if (!pausar && duracionDiaSegundos > 0.01f)
            hora = (hora + Time.deltaTime * 24f / duracionDiaSegundos) % 24f;

        if (Application.isPlaying && luciernagas && !luciernagasCreadas)
            AsegurarLuciernagas();

        Aplicar();
    }

    void OnValidate()
    {
        if (luna != null) luna.color = colorLuna;
        if (inicializado) Aplicar();
    }

    void OnDestroy()
    {
        if (skyMat != null) Destroy(skyMat);
        if (fireflyMat != null) Destroy(fireflyMat);
    }

    [ContextMenu("Aplicar ahora (preview en editor)")]
    void AplicarAhora()
    {
        inicializado = false;
        Inicializar();
        Aplicar();
    }

    public void RefrescarReferenciasVilla()
    {
        inicializado = false;
        Inicializar();
        Aplicar();
    }

    void Inicializar()
    {
        if (inicializado) return;
        inicializado = true;
        if (mpb == null) mpb = new MaterialPropertyBlock();

        if (sol == null)
        {
            foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) { sol = l; break; }
        }
        if (sol != null) RenderSettings.sun = sol;

        if (luna == null && sol != null)
        {
            Transform existente = transform.Find("Luna_Generada");
            if (existente != null)
            {
                luna = existente.GetComponent<Light>();
            }
            else
            {
                GameObject go = new GameObject("Luna_Generada");
                go.transform.SetParent(transform, false);
                luna = go.AddComponent<Light>();
                luna.type = LightType.Directional;
                luna.shadows = LightShadows.Soft;
            }
        }
        if (luna != null) luna.color = colorLuna;

        if (villa == null)
        {
            GameObject g = GameObject.Find("Villa_Toon_Generada");
            if (g != null) villa = g.transform;
        }
        if (villa != null)
        {
            Light[] ls = villa.GetComponentsInChildren<Light>(true);
            List<LuzVilla> lista = new List<LuzVilla>();
            foreach (Light l in ls)
            {
                if (l.type == LightType.Directional) continue;
                LuzVilla lv = new LuzVilla();
                lv.luz = l;
                lv.vida = l.GetComponent<VillaVida>();
                lv.intensidadBase = l.intensity;
                lista.Add(lv);
            }
            lucesVilla = lista.ToArray();

            List<Renderer> rs = new List<Renderer>();
            foreach (Renderer r in villa.GetComponentsInChildren<Renderer>(true))
            {
                if (r.sharedMaterial != null && r.sharedMaterial.name.Contains(nombreMaterialLuz))
                    rs.Add(r);
            }
            ventanas = rs.ToArray();
        }

        if (Application.isPlaying && oscurecerSkybox && skyMat == null && RenderSettings.skybox != null && RenderSettings.skybox.HasProperty("_Exposure"))
        {
            skyMat = new Material(RenderSettings.skybox);
            RenderSettings.skybox = skyMat;
        }
    }

    void Aplicar()
    {
        float t = hora / 24f;
        float ang = (hora - 6f) / 24f * 360f;                 // 6:00 amanecer, 12:00 cenit, 18:00 atardecer
        float altura = Mathf.Sin(ang * Mathf.Deg2Rad);        // -1 (medianoche) .. 1 (mediodia)
        float dia = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.05f, 0.30f, altura));
        float noche = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.05f, 0.20f, altura));

        if (sol != null)
        {
            sol.transform.rotation = Quaternion.Euler(ang, inclinacionY, 0f);
            sol.color = colorSol.Evaluate(t);
            sol.intensity = intensidadSolMax * dia;
            sol.enabled = dia > 0.001f;
        }
        if (luna != null)
        {
            luna.transform.rotation = Quaternion.Euler(ang + 180f, inclinacionY, 0f);
            luna.color = colorLuna;
            luna.intensity = intensidadLunaMax * (1f - dia);
            luna.enabled = (1f - dia) > 0.001f;
        }

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = colorAmbiente.Evaluate(t);
        RenderSettings.fog = usarNiebla;
        if (usarNiebla)
        {
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = densidadNiebla;
            RenderSettings.fogColor = colorNiebla.Evaluate(t);
        }
        if (skyMat != null) skyMat.SetFloat("_Exposure", Mathf.Lerp(0.08f, 1.3f, dia));

        // Faroles: se encienden al atardecer
        if (lucesVilla != null)
        {
            for (int i = 0; i < lucesVilla.Length; i++)
            {
                LuzVilla lv = lucesVilla[i];
                if (lv.luz == null) continue;
                if (lv.vida != null) lv.vida.factor = noche;
                else lv.luz.intensity = lv.intensidadBase * noche;
                lv.luz.enabled = noche > 0.01f;
            }
        }

        // Ventanas / luces: el shader calcula el brillo nocturno usando _NightFactor
        if (ventanas != null && mpb != null)
        {
            for (int i = 0; i < ventanas.Length; i++)
            {
                Renderer r = ventanas[i];
                if (r == null) continue;
                r.GetPropertyBlock(mpb);
                if (!string.IsNullOrEmpty(propColor)) mpb.SetColor(propColor, ventanaDia);
                if (!string.IsNullOrEmpty(propEmision)) mpb.SetColor(propEmision, ventanaNoche);
                if (!string.IsNullOrEmpty(propFactorNoche)) mpb.SetFloat(propFactorNoche, noche);
                r.SetPropertyBlock(mpb);
            }
        }

        // Luciernagas: aparecen cuando ya esta bien oscuro
        if (psLuciernagas != null)
        {
            if (!luciernagas)
            {
                ParticleSystem.EmissionModule emOff = psLuciernagas.emission;
                emOff.rateOverTime = 0f;
            }
            else
            {
                float f = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.4f, 0.9f, noche));
                ParticleSystem.EmissionModule em = psLuciernagas.emission;
                em.rateOverTime = (maxLuciernagas / 11f) * f;
            }
        }
    }

    void AsegurarLuciernagas()
    {
        if (luciernagasCreadas && psLuciernagas != null) return;
        luciernagasCreadas = true;

        GameObject go = new GameObject("Luciernagas");
        go.transform.SetParent(transform, false);
        go.transform.position = centroLuciernagas;

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.duration = 5f;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(8f, 14f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(tamanoLuciernaga * 0.7f, tamanoLuciernaga * 1.3f);
        main.startColor = Color.white;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = maxLuciernagas;

        ParticleSystem.EmissionModule em = ps.emission;
        em.rateOverTime = 0f;

        ParticleSystem.ShapeModule sh = ps.shape;
        sh.shapeType = ParticleSystemShapeType.Box;
        sh.scale = areaLuciernagas;

        // Vuelo errante
        ParticleSystem.NoiseModule noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.8f;
        noise.frequency = 0.25f;
        noise.scrollSpeed = 0.3f;
        noise.damping = true;
        noise.octaveCount = 1;

        ParticleSystem.VelocityOverLifetimeModule vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(0f);
        vel.y = new ParticleSystem.MinMaxCurve(0.05f);
        vel.z = new ParticleSystem.MinMaxCurve(0f);

        // Aparecen, titilan y se desvanecen
        ParticleSystem.SizeOverLifetimeModule sol = ps.sizeOverLifetime;
        sol.enabled = true;
        AnimationCurve curva = new AnimationCurve(
            new Keyframe(0.00f, 0f), new Keyframe(0.10f, 1f), new Keyframe(0.25f, 0.35f),
            new Keyframe(0.40f, 1f), new Keyframe(0.60f, 0.45f), new Keyframe(0.80f, 1f),
            new Keyframe(0.92f, 0.7f), new Keyframe(1.00f, 0f));
        sol.size = new ParticleSystem.MinMaxCurve(1f, curva);

        ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Mesh;
        GameObject tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        fireflyMesh = tmp.GetComponent<MeshFilter>().sharedMesh;
        r.mesh = fireflyMesh;
        Destroy(tmp);
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;

        if (materialLuciernaga != null) r.sharedMaterial = materialLuciernaga;
        else
        {
            Shader shd = Shader.Find("Universal Render Pipeline/Unlit");
            if (shd == null) shd = Shader.Find("Unlit/Color");
            fireflyMat = new Material(shd);
            Color c = colorLuciernaga * brilloLuciernaga;
            c.a = 1f;
            if (fireflyMat.HasProperty("_BaseColor")) fireflyMat.SetColor("_BaseColor", c);
            if (fireflyMat.HasProperty("_Color")) fireflyMat.SetColor("_Color", c);
            r.sharedMaterial = fireflyMat;
        }

        ps.Play();
        psLuciernagas = ps;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.75f, 1f, 0.3f, 0.35f);
        Gizmos.DrawWireCube(centroLuciernagas, areaLuciernagas);
    }
}