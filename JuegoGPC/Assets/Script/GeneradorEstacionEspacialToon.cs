using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// Estacion espacial habitable de 2 pisos para shaders Toon (URP).
// Uso: agrega este script a un GameObject vacio y usa el boton del inspector o clic derecho > "Generar Estación Espacial".
// Requiere EstacionVida.cs (fuera de Editor) para las animaciones.
public class GeneradorEstacionEspacialToon : MonoBehaviour
{
    [Header("General")]
    public bool generarLucesInteriores = true;
    public bool generarLetreros = true;
    public bool generarExteriorEspacial = true;
    public bool generarAnimaciones = true;
    public bool configurarAmbienteEspacial = true;
    public int semilla = 2026;

    [Header("Materiales (uno por categoria, listos para tus Toon Shaders)")]
    [Tooltip("Tu shader Toon. Si queda vacio se usa URP Lit.")]
    public Shader miShaderToon;
    [Range(0f, 0.02f)] public float grosorContornoToon = 0.005f;
    public bool guardarMaterialesComoAsset = true;
    public string carpetaMateriales = "Assets/EstacionEspacial/Materials";
    [Tooltip("Si ya existe el material en la carpeta (con tu toon shader asignado) se reutiliza tal cual.")]
    public bool reutilizarMaterialesExistentes = true;

    // ---- constantes de arquitectura ----
    const float PISO2 = 5f;     // altura del segundo piso (cara superior de la losa)
    const float H = 4.7f;       // altura libre de cada planta
    const float LOSA = 0.3f;
    const float ESP = 0.3f;     // grosor de paredes

    static readonly Dictionary<string, Material> paleta = new Dictionary<string, Material>();
    static Mesh mCubo, mCil, mEsf, mOcto;
    static readonly Color COL_TEXTO = new Color(0.6f, 0.97f, 1f);

    Transform raiz;
    System.Random rng;

    // =====================================================================
    // CONSTRUCTOR DE MALLAS: acumula primitivas y las fusiona en UNA malla por material
    // =====================================================================
    class Malla
    {
        struct Col { public Vector3 p, s; public Quaternion q; }
        struct Txt { public string t; public Vector3 p; public float ry, tam; public Color c; }

        readonly Dictionary<string, List<CombineInstance>> porMat = new Dictionary<string, List<CombineInstance>>();
        readonly List<string> orden = new List<string>();
        readonly List<Col> cols = new List<Col>();
        readonly List<Txt> txts = new List<Txt>();

        public Malla Parte(string mat, Mesh mesh, Vector3 p, Quaternion q, Vector3 s)
        {
            List<CombineInstance> l;
            if (!porMat.TryGetValue(mat, out l)) { l = new List<CombineInstance>(); porMat[mat] = l; orden.Add(mat); }
            CombineInstance ci = new CombineInstance();
            ci.mesh = mesh;
            ci.transform = Matrix4x4.TRS(p, q, s);
            l.Add(ci);
            return this;
        }

        public Malla Caja(string mat, Vector3 p, Vector3 s, float ry = 0f, bool solido = false)
        {
            return CajaQ(mat, p, s, Quaternion.Euler(0f, ry, 0f), solido);
        }

        public Malla CajaQ(string mat, Vector3 p, Vector3 s, Quaternion q, bool solido = false)
        {
            Parte(mat, mCubo, p, q, s);
            if (solido) { Col c = new Col(); c.p = p; c.s = s; c.q = q; cols.Add(c); }
            return this;
        }

        public Malla Cil(string mat, Vector3 p, float diam, float alto, Quaternion? q = null)
        {
            return Parte(mat, mCil, p, q ?? Quaternion.identity, new Vector3(diam, alto * 0.5f, diam));
        }

        public Malla CilX(string mat, Vector3 p, float diam, float largo)
        {
            return Cil(mat, p, diam, largo, Quaternion.Euler(0f, 0f, 90f));
        }

        public Malla CilZ(string mat, Vector3 p, float diam, float largo)
        {
            return Cil(mat, p, diam, largo, Quaternion.Euler(90f, 0f, 0f));
        }

        public Malla Esf(string mat, Vector3 p, float d) { return Parte(mat, mEsf, p, Quaternion.identity, Vector3.one * d); }
        public Malla Esf(string mat, Vector3 p, Vector3 d, Quaternion? q = null) { return Parte(mat, mEsf, p, q ?? Quaternion.identity, d); }

        public Malla Tubo(string mat, Vector3 a, Vector3 b, float diam)
        {
            Vector3 d = b - a; float len = d.magnitude;
            if (len < 0.001f) return this;
            return Parte(mat, mCil, (a + b) * 0.5f, Quaternion.FromToRotation(Vector3.up, d / len), new Vector3(diam, len * 0.5f, diam));
        }

        public Malla Viga(string mat, Vector3 a, Vector3 b, float grosor)
        {
            Vector3 d = b - a; float len = d.magnitude;
            if (len < 0.001f) return this;
            return Parte(mat, mCubo, (a + b) * 0.5f, Quaternion.LookRotation(d), new Vector3(grosor, grosor, len));
        }

        // prisma octogonal (eje local Y)
        public Malla Octo(string mat, Vector3 p, float diam, float grosor, Quaternion q)
        {
            return Parte(mat, mOcto, p, q, new Vector3(diam, grosor * 0.5f, diam));
        }

        public Malla Texto(string t, Vector3 p, float ry, Color c, float tam)
        {
            Txt x = new Txt(); x.t = t; x.p = p; x.ry = ry; x.c = c; x.tam = tam; txts.Add(x);
            return this;
        }

        public GameObject Construir(string nombre, Transform padre, Vector3 pos, float ry = 0f)
        {
            GameObject go = new GameObject(nombre);
            go.transform.SetParent(padre, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0f, ry, 0f);

            foreach (string key in orden)
            {
                Material mt;
                if (!paleta.TryGetValue(key, out mt)) { Debug.LogWarning("Material no definido: " + key); mt = paleta["MetalBase"]; }
                Mesh m = new Mesh();
                m.name = nombre + "_" + key;
                m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                m.CombineMeshes(porMat[key].ToArray(), true, true);
                m.RecalculateBounds();
                GameObject h = new GameObject(key);
                h.transform.SetParent(go.transform, false);
                h.AddComponent<MeshFilter>().sharedMesh = m;
                h.AddComponent<MeshRenderer>().sharedMaterial = mt;
            }
            if (cols.Count > 0)
            {
                GameObject cc = new GameObject("Colisiones");
                cc.transform.SetParent(go.transform, false);
                foreach (Col c in cols)
                {
                    GameObject g = new GameObject("Col");
                    g.transform.SetParent(cc.transform, false);
                    g.transform.localPosition = c.p;
                    g.transform.localRotation = c.q;
                    g.AddComponent<BoxCollider>().size = c.s;
                }
            }
            foreach (Txt t in txts)
            {
                GameObject g = new GameObject("Letrero_" + t.t);
                g.transform.SetParent(go.transform, false);
                g.transform.localPosition = t.p;
                g.transform.localRotation = Quaternion.Euler(0f, t.ry, 0f);
                TextMesh tm = g.AddComponent<TextMesh>();
                tm.text = t.t; tm.fontSize = 64; tm.characterSize = t.tam;
                tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center; tm.color = t.c;
            }
            return go;
        }
    }

    // =====================================================================
    // GENERACION PRINCIPAL
    // =====================================================================
    [ContextMenu("Generar Estación Espacial")]
    public void GenerarEstacion()
    {
        LimpiarEstacion();
        rng = new System.Random(semilla);
        PrepararMallas();
        InicializarPaleta();

        GameObject go = new GameObject("EstacionEspacial_Toon");
        go.transform.SetParent(transform, false);
        raiz = go.transform;

        Transform est = C("00_Estructura_Muros_Losas", raiz);
        Transform p1 = C("01_Planta_Baja", raiz);
        Transform p2 = C("02_Planta_Alta", raiz);
        Transform ext = C("03_Exterior_Espacial", raiz);
        Transform lz = C("04_Iluminacion_Global", raiz);

        ConstruirEstructura(est);

        // PLANTA BAJA
        ConstruirPuenteMando(C("Puente_De_Mando", p1));
        ConstruirComedor(C("Comedor_Cantina", p1));
        ConstruirBiodomo(C("Biodomo_Invernadero", p1));
        ConstruirHub(C("Hub_Central_Escalera", p1));
        ConstruirBodega(C("Bodega_Carga", p1));
        ConstruirEsclusa(C("Esclusa_Taller", p1));

        // PLANTA ALTA
        ConstruirDormitorios(C("Dormitorios_Capsulas", p2));
        ConstruirLaboratorio(C("Laboratorio_Investigacion", p2));
        ConstruirMirador(C("Mirador_Astronomico", p2));
        ConstruirPasarela(C("Pasarela_Superior", p2));

        if (generarExteriorEspacial) ConstruirExterior(ext);
        ConstruirIluminacionGlobal(lz);

#if UNITY_EDITOR
        if (guardarMaterialesComoAsset) AssetDatabase.SaveAssets();
#endif
        Debug.Log("[GeneradorEstacionEspacialToon] Estación generada. Materiales en: " + carpetaMateriales);
    }

    [ContextMenu("Limpiar Estación")]
    public void LimpiarEstacion()
    {
        Transform e = transform.Find("EstacionEspacial_Toon");
        if (e != null)
        {
            if (Application.isPlaying) Destroy(e.gameObject); else DestroyImmediate(e.gameObject);
        }
    }

    // =====================================================================
    // UTILIDADES
    // =====================================================================
    static Vector3 V(float x, float y, float z) { return new Vector3(x, y, z); }
    float R(float a, float b) { return (float)(a + (b - a) * rng.NextDouble()); }
    static float RyHacia(Vector3 n) { return Mathf.Atan2(-n.x, -n.z) * Mathf.Rad2Deg; }

    Transform C(string n, Transform p)
    {
        GameObject g = new GameObject(n);
        g.transform.SetParent(p, false);
        return g.transform;
    }

    GameObject Prop(Transform padre, string nombre, Vector3 pos, float ry, System.Action<Malla> f)
    {
        Malla b = new Malla();
        f(b);
        return b.Construir(nombre, padre, pos, ry);
    }

    void Anim(GameObject go, EstacionVida.Tipo tipo, float vel, float amp = 0.2f, Vector3? eje = null)
    {
        if (!generarAnimaciones || go == null) return;
        EstacionVida v = go.AddComponent<EstacionVida>();
        v.tipo = tipo; v.velocidad = vel; v.amplitud = amp; v.fase = R(0f, 10f);
        v.eje = eje ?? Vector3.up;
    }

    void Luz(Transform p, string n, Vector3 pos, Color c, float inten, float rango, bool pulso = false)
    {
        if (!generarLucesInteriores) return;
        GameObject g = new GameObject(n);
        g.transform.SetParent(p, false);
        g.transform.localPosition = pos;
        Light l = g.AddComponent<Light>();
        l.type = LightType.Point; l.color = c; l.intensity = inten; l.range = rango; l.shadows = LightShadows.None;
        if (pulso) Anim(g, EstacionVida.Tipo.Pulso, 1.5f, 0.25f);
    }

    static Color Calida { get { return new Color(1f, 0.82f, 0.6f); } }
    static Color Fria { get { return new Color(0.75f, 0.92f, 1f); } }

    // =====================================================================
    // MALLAS BASE
    // =====================================================================
    static Mesh MeshPrim(PrimitiveType t)
    {
        GameObject tmp = GameObject.CreatePrimitive(t);
        Mesh m = tmp.GetComponent<MeshFilter>().sharedMesh;
        if (Application.isPlaying) Destroy(tmp); else DestroyImmediate(tmp);
        return m;
    }

    static void PrepararMallas()
    {
        if (mCubo == null) mCubo = MeshPrim(PrimitiveType.Cube);
        if (mCil == null) mCil = MeshPrim(PrimitiveType.Cylinder);
        if (mEsf == null) mEsf = MeshPrim(PrimitiveType.Sphere);
        if (mOcto == null) mOcto = CrearPrisma(8);
    }

    // prisma regular, radio 0.5, altura 2 (y de -1 a 1), caras planas
    static Mesh CrearPrisma(int n)
    {
        List<Vector3> v = new List<Vector3>();
        List<Vector3> nr = new List<Vector3>();
        List<int> t = new List<int>();
        float rad = 0.5f;
        for (int i = 0; i < n; i++)
        {
            float a0 = Mathf.PI * 2f * i / n, a1 = Mathf.PI * 2f * (i + 1) / n;
            Vector3 b0 = new Vector3(Mathf.Cos(a0) * rad, -1f, Mathf.Sin(a0) * rad);
            Vector3 b1 = new Vector3(Mathf.Cos(a1) * rad, -1f, Mathf.Sin(a1) * rad);
            Vector3 t0 = new Vector3(b0.x, 1f, b0.z), t1 = new Vector3(b1.x, 1f, b1.z);
            float am = (a0 + a1) * 0.5f;
            Vector3 nm = new Vector3(Mathf.Cos(am), 0f, Mathf.Sin(am));
            int s = v.Count;
            v.Add(b0); v.Add(b1); v.Add(t0); v.Add(t1);
            for (int k = 0; k < 4; k++) nr.Add(nm);
            t.AddRange(new[] { s, s + 2, s + 1, s + 1, s + 2, s + 3 });
        }
        int cTop = v.Count; v.Add(new Vector3(0, 1, 0)); nr.Add(Vector3.up);
        int rTop = v.Count;
        for (int i = 0; i <= n; i++) { float a = Mathf.PI * 2f * i / n; v.Add(new Vector3(Mathf.Cos(a) * rad, 1f, Mathf.Sin(a) * rad)); nr.Add(Vector3.up); }
        for (int i = 0; i < n; i++) { t.Add(cTop); t.Add(rTop + i + 1); t.Add(rTop + i); }
        int cBot = v.Count; v.Add(new Vector3(0, -1, 0)); nr.Add(Vector3.down);
        int rBot = v.Count;
        for (int i = 0; i <= n; i++) { float a = Mathf.PI * 2f * i / n; v.Add(new Vector3(Mathf.Cos(a) * rad, -1f, Mathf.Sin(a) * rad)); nr.Add(Vector3.down); }
        for (int i = 0; i < n; i++) { t.Add(cBot); t.Add(rBot + i); t.Add(rBot + i + 1); }

        Mesh m = new Mesh();
        m.name = "Prisma" + n;
        m.SetVertices(v); m.SetNormals(nr); m.SetTriangles(t, 0);
        m.RecalculateBounds();
        return m;
    }

    // =====================================================================
    // PALETA DE MATERIALES POR CATEGORIA
    // =====================================================================
    static Color Co(float r, float g, float b, float a = 1f) { return new Color(r, g, b, a); }

    void InicializarPaleta()
    {
        paleta.Clear();

        // --- Estructura / Metal ---
        Def("Pared", Co(0.86f, 0.89f, 0.94f), Co(0.42f, 0.48f, 0.58f));
        Def("MetalBase", Co(0.50f, 0.56f, 0.64f), Co(0.22f, 0.26f, 0.32f));
        Def("MetalOscuro", Co(0.16f, 0.18f, 0.23f), Co(0.07f, 0.08f, 0.11f));
        Def("Suelo", Co(0.30f, 0.34f, 0.42f), Co(0.15f, 0.18f, 0.24f));
        Def("SueloClaro", Co(0.56f, 0.62f, 0.70f), Co(0.30f, 0.35f, 0.42f));

        // --- Detalles amarillos de seguridad ---
        Def("Amarillo", Co(0.98f, 0.78f, 0.12f), Co(0.55f, 0.40f, 0.05f));

        // --- Tuberias codificadas por color ---
        Def("TuboNaranja", Co(0.95f, 0.48f, 0.16f), Co(0.50f, 0.22f, 0.08f));
        Def("TuboCian", Co(0.15f, 0.75f, 0.90f), Co(0.07f, 0.35f, 0.45f));
        Def("TuboAmarillo", Co(0.95f, 0.82f, 0.20f), Co(0.50f, 0.40f, 0.08f));

        // --- Emisivos / Neon ---
        Def("NeonCian", Co(0.2f, 0.95f, 1f), Co(0.1f, 0.5f, 0.6f), Co(0.15f, 1.3f, 1.6f));
        Def("NeonNaranja", Co(1f, 0.55f, 0.1f), Co(0.6f, 0.25f, 0.05f), Co(1.5f, 0.65f, 0.1f));
        Def("NeonVerde", Co(0.2f, 1f, 0.45f), Co(0.1f, 0.5f, 0.2f), Co(0.2f, 1.4f, 0.5f));
        Def("NeonMagenta", Co(1f, 0.25f, 0.85f), Co(0.5f, 0.1f, 0.4f), Co(1.4f, 0.25f, 1.1f));
        Def("NeonBlanco", Co(1f, 1f, 1f), Co(0.8f, 0.85f, 0.95f), Co(1.3f, 1.35f, 1.45f));
        Def("NeonAmarillo", Co(1f, 0.9f, 0.2f), Co(0.5f, 0.45f, 0.1f), Co(1.3f, 1.1f, 0.15f));
        Def("PantallaOscura", Co(0.06f, 0.18f, 0.28f), Co(0.03f, 0.09f, 0.16f), Co(0.10f, 0.40f, 0.65f));

        // --- Cristal y hologramas (translucidos) ---
        Def("Cristal", Co(0.55f, 0.85f, 1f, 0.25f), Co(0.05f, 0.15f, 0.25f), Co(0.02f, 0.06f, 0.1f), true);
        Def("CristalBio", Co(0.6f, 1f, 0.8f, 0.22f), Co(0.05f, 0.25f, 0.15f), Co(0.03f, 0.12f, 0.08f), true);
        Def("Holograma", Co(0.15f, 0.95f, 1f, 0.40f), Co(0.05f, 0.4f, 0.5f), Co(0.15f, 0.95f, 1.2f), true);

        // --- Vegetacion del biodomo ---
        Def("VegHoja", Co(0.18f, 0.78f, 0.52f), Co(0.07f, 0.38f, 0.28f));
        Def("VegHojaMorada", Co(0.58f, 0.32f, 0.88f), Co(0.28f, 0.14f, 0.45f));
        Def("VegTronco", Co(0.38f, 0.26f, 0.40f), Co(0.18f, 0.12f, 0.20f));
        Def("MusgoBio", Co(0.25f, 1f, 0.65f), Co(0.10f, 0.5f, 0.35f), Co(0.2f, 1.2f, 0.7f));
        Def("Tierra", Co(0.36f, 0.24f, 0.16f), Co(0.18f, 0.11f, 0.07f));

        // --- Mobiliario / acentos ---
        Def("Blanco", Co(0.96f, 0.97f, 0.99f), Co(0.58f, 0.63f, 0.72f));
        Def("Tela", Co(0.28f, 0.42f, 0.62f), Co(0.14f, 0.20f, 0.34f));
        Def("TelaNaranja", Co(0.95f, 0.50f, 0.20f), Co(0.50f, 0.22f, 0.08f));
        Def("Azul", Co(0.20f, 0.48f, 0.85f), Co(0.09f, 0.22f, 0.44f));
        Def("Rojo", Co(0.85f, 0.24f, 0.24f), Co(0.42f, 0.10f, 0.12f));
        Def("Madera", Co(0.68f, 0.46f, 0.28f), Co(0.35f, 0.22f, 0.12f));

        // --- Exterior espacial ---
        Def("Planeta", Co(0.24f, 0.58f, 0.86f), Co(0.08f, 0.14f, 0.32f));
        Def("AnilloPlaneta", Co(0.92f, 0.74f, 0.52f), Co(0.45f, 0.32f, 0.22f));
        Def("Nebulosa1", Co(0.85f, 0.25f, 0.85f, 0.12f), Co(0.3f, 0.1f, 0.3f), Co(0.5f, 0.12f, 0.55f), true);
        Def("Nebulosa2", Co(0.2f, 0.45f, 1f, 0.12f), Co(0.1f, 0.2f, 0.4f), Co(0.1f, 0.25f, 0.7f), true);
    }

    void Def(string key, Color baseC, Color sombra, Color? emi = null, bool transp = false)
    {
        Material m = null;
#if UNITY_EDITOR
        string path = carpetaMateriales + "/Mat_" + key + ".mat";
        if (guardarMaterialesComoAsset && reutilizarMaterialesExistentes) m = AssetDatabase.LoadAssetAtPath<Material>(path);
#endif
        if (m == null)
        {
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) lit = Shader.Find("Standard");
            Shader sh = (!transp && miShaderToon != null) ? miShaderToon : lit;
            m = new Material(sh);
            m.name = "Mat_" + key;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", baseC);
            if (m.HasProperty("_Color")) m.SetColor("_Color", baseC);
            if (m.HasProperty("_ShadowColor")) m.SetColor("_ShadowColor", sombra);
            if (m.HasProperty("_OutlineWidth")) m.SetFloat("_OutlineWidth", transp ? 0f : grosorContornoToon);
            if (emi.HasValue && emi.Value.maxColorComponent > 0.01f && m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emi.Value);
            }
            if (transp)
            {
                if (m.HasProperty("_Surface"))
                {
                    m.SetFloat("_Surface", 1f);
                    m.SetFloat("_Blend", 0f);
                    m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                }
                if (m.HasProperty("_SrcBlend")) m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                if (m.HasProperty("_DstBlend")) m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                if (m.HasProperty("_ZWrite")) m.SetInt("_ZWrite", 0);
                m.renderQueue = 3000;
            }
#if UNITY_EDITOR
            if (guardarMaterialesComoAsset)
            {
                AsegurarCarpeta(carpetaMateriales);
                AssetDatabase.CreateAsset(m, path);
            }
#endif
        }
        paleta[key] = m;
    }

#if UNITY_EDITOR
    static void AsegurarCarpeta(string ruta)
    {
        string[] partes = ruta.Split('/');
        string acc = partes[0];
        for (int i = 1; i < partes.Length; i++)
        {
            string sig = acc + "/" + partes[i];
            if (!AssetDatabase.IsValidFolder(sig)) AssetDatabase.CreateFolder(acc, partes[i]);
            acc = sig;
        }
    }
#endif

    // =====================================================================
    // MUROS CON HUECOS (puertas, arcos, ventanas)
    // =====================================================================
    struct Hueco { public float s, w, inf, sup; public int tipo; public string neon, rot; }
    static Hueco Puerta(float s, float w, float h, string neon, string rot) { return new Hueco { s = s, w = w, inf = 0f, sup = h, tipo = 0, neon = neon, rot = rot }; }
    static Hueco Arco(float s, float w, float h, string neon, string rot) { return new Hueco { s = s, w = w, inf = 0f, sup = h, tipo = 2, neon = neon, rot = rot }; }
    static Hueco Vent(float s, float w, float inf, float sup) { return new Hueco { s = s, w = w, inf = inf, sup = sup, tipo = 1, neon = "NeonCian", rot = "" }; }

    void Pared(Malla b, Vector3 a, Vector3 c, float y0, float alto, string mat, params Hueco[] hs)
    {
        Vector3 d = c - a;
        float L = d.magnitude;
        Vector3 u = d / L;
        float ry = Mathf.Atan2(u.x, u.z) * Mathf.Rad2Deg;
        Vector3 right = new Vector3(u.z, 0f, -u.x);
        Vector3 up = Vector3.up;

        System.Array.Sort(hs, (p, q) => p.s.CompareTo(q.s));
        float cur = 0f;
        foreach (Hueco h in hs)
        {
            float s0 = h.s - h.w * 0.5f, s1 = h.s + h.w * 0.5f;
            if (s0 > cur) Seg(b, a, u, right, ry, y0, alto, mat, cur, s0, 0f, alto, true);
            if (h.inf > 0.01f) Seg(b, a, u, right, ry, y0, alto, mat, s0, s1, 0f, h.inf, false);
            if (h.sup < alto - 0.01f) Seg(b, a, u, right, ry, y0, alto, mat, s0, s1, h.sup, alto, false);

            Vector3 cenTop = a + u * h.s + up * (y0 + h.sup);
            if (h.tipo == 1)
            {
                float hg = h.sup - h.inf;
                Vector3 cen = a + u * h.s + up * (y0 + h.inf + hg * 0.5f);
                b.Caja("Cristal", cen, V(0.06f, hg, h.w), ry, true);
                b.Caja("MetalOscuro", a + u * h.s + up * (y0 + h.inf), V(ESP + 0.1f, 0.14f, h.w + 0.2f), ry);
                b.Caja("MetalOscuro", cenTop, V(ESP + 0.1f, 0.14f, h.w + 0.2f), ry);
                int n = Mathf.Max(1, Mathf.RoundToInt(h.w / 2.4f));
                for (int i = 0; i <= n; i++)
                {
                    float sp = h.s - h.w * 0.5f + h.w * i / n;
                    b.Caja("MetalOscuro", a + u * sp + up * (y0 + h.inf + hg * 0.5f), V(ESP + 0.1f, hg, 0.12f), ry);
                }
                b.Caja("NeonCian", a + u * h.s + up * (y0 + h.sup + 0.1f), V(ESP + 0.12f, 0.04f, h.w * 0.9f), ry);
            }
            else
            {
                for (int sg = -1; sg <= 1; sg += 2)
                    b.Caja("MetalOscuro", a + u * (h.s + sg * h.w * 0.5f) + up * (y0 + h.sup * 0.5f), V(ESP + 0.14f, h.sup, 0.18f), ry);
                b.Caja("MetalOscuro", cenTop + up * 0.09f, V(ESP + 0.14f, 0.2f, h.w + 0.36f), ry);
                b.Caja(h.neon, cenTop + up * 0.24f, V(ESP + 0.18f, 0.06f, h.w * 0.9f), ry);
                if (generarLetreros && !string.IsNullOrEmpty(h.rot))
                    for (int sd = -1; sd <= 1; sd += 2)
                        b.Texto(h.rot, cenTop + up * 0.6f + right * sd * (ESP * 0.5f + 0.12f), RyHacia(right * sd), COL_TEXTO, 0.06f);
            }
            cur = s1;
        }
        if (cur < L - 0.02f) Seg(b, a, u, right, ry, y0, alto, mat, cur, L, 0f, alto, true);
    }

    void Seg(Malla b, Vector3 a, Vector3 u, Vector3 right, float ry, float y0, float alto, string mat, float sA, float sB, float yA, float yB, bool panel)
    {
        float l = sB - sA;
        if (l < 0.02f) return;
        Vector3 mid = a + u * ((sA + sB) * 0.5f);
        b.Caja(mat, mid + Vector3.up * (y0 + (yA + yB) * 0.5f), V(ESP, yB - yA, l), ry, true);
        for (int sd = -1; sd <= 1; sd += 2)
        {
            Vector3 off = right * sd * (ESP * 0.5f + 0.05f);
            if (yA < 0.01f) b.Caja("MetalOscuro", mid + off + Vector3.up * (y0 + 0.15f), V(0.1f, 0.3f, l), ry);
            if (panel && l > 1.8f && (yB - yA) > 3f)
            {
                b.Caja("MetalBase", mid + right * sd * (ESP * 0.5f + 0.02f) + Vector3.up * (y0 + (yA + yB) * 0.5f), V(0.05f, (yB - yA) - 1.6f, l - 0.6f), ry);
                b.Caja("NeonCian", mid + right * sd * (ESP * 0.5f + 0.05f) + Vector3.up * (y0 + yB - 0.7f), V(0.02f, 0.04f, l - 0.9f), ry);
            }
        }
    }

    void Columna(Malla b, float x, float z, float y0, string neon)
    {
        b.Caja("MetalOscuro", V(x, y0 + H * 0.5f, z), V(0.6f, H, 0.6f), 0f, true);
        b.Caja(neon, V(x, y0 + H * 0.5f, z), V(0.66f, H - 1.0f, 0.08f));
        b.Caja(neon, V(x, y0 + H * 0.5f, z), V(0.08f, H - 1.0f, 0.66f));
    }

    void VigasTecho(Malla b, float x0, float x1, float z0, float z1, float y0, bool alongX, float paso, string neon)
    {
        float yv = y0 + H - 0.2f;
        if (alongX)
        {
            for (float z = z0 + paso * 0.5f; z < z1; z += paso)
            {
                b.Caja("MetalOscuro", V((x0 + x1) * 0.5f, yv, z), V(x1 - x0 - 0.3f, 0.4f, 0.3f));
                b.Caja(neon, V((x0 + x1) * 0.5f, yv - 0.22f, z), V(x1 - x0 - 0.8f, 0.04f, 0.12f));
            }
        }
        else
        {
            for (float x = x0 + paso * 0.5f; x < x1; x += paso)
            {
                b.Caja("MetalOscuro", V(x, yv, (z0 + z1) * 0.5f), V(0.3f, 0.4f, z1 - z0 - 0.3f));
                b.Caja(neon, V(x, yv - 0.22f, (z0 + z1) * 0.5f), V(0.12f, 0.04f, z1 - z0 - 0.8f));
            }
        }
    }

    void Baranda(Malla b, Vector3 a, Vector3 c, float alto)
    {
        Vector3 d = c - a; float L = d.magnitude;
        if (L < 0.1f) return;
        int n = Mathf.Max(1, Mathf.RoundToInt(L / 1.2f));
        for (int i = 0; i <= n; i++)
            b.Caja("Amarillo", a + d * (i / (float)n) + V(0, alto * 0.5f, 0), V(0.08f, alto, 0.08f));
        b.Viga("Amarillo", a + V(0, alto, 0), c + V(0, alto, 0), 0.07f);
        b.Viga("Amarillo", a + V(0, alto * 0.55f, 0), c + V(0, alto * 0.55f, 0), 0.05f);
        b.Viga("MetalOscuro", a + V(0, 0.08f, 0), c + V(0, 0.08f, 0), 0.14f);
        b.Viga("NeonCian", a + V(0, alto * 0.8f, 0), c + V(0, alto * 0.8f, 0), 0.025f);
    }

    // =====================================================================
    // ESTRUCTURA: losas, techo, muros, columnas
    // =====================================================================
    void ConstruirEstructura(Transform padre)
    {
        Malla b = new Malla();
        float yL = PISO2 - LOSA * 0.5f, yR = PISO2 + H + LOSA * 0.5f;

        // Losas
        b.Caja("Suelo", V(0, -LOSA * 0.5f, 0), V(32, LOSA, 24), 0, true);
        b.Caja("Suelo", V(-10, yL, 0), V(12, LOSA, 24), 0, true);
        b.Caja("Suelo", V(10, yL, 0), V(12, LOSA, 24), 0, true);
        b.Caja("Suelo", V(0, yL, -7), V(8, LOSA, 10), 0, true);
        // Pasarela en T sobre el vacio del hub (tramo E-O + ramal hacia el Mirador)
        b.Caja("SueloClaro", V(0, yL, 2), V(8f, LOSA, 2.4f), 0, true);
        b.Caja("SueloClaro", V(0, yL, -0.6f), V(2.4f, LOSA, 2.8f), 0, true);
        // Techo con tragaluz sobre el hub
        b.Caja("MetalOscuro", V(-10, yR, 0), V(12, LOSA, 24));
        b.Caja("MetalOscuro", V(10, yR, 0), V(12, LOSA, 24));
        b.Caja("MetalOscuro", V(0, yR, -7), V(8, LOSA, 10));
        b.Caja("Cristal", V(0, yR, 5), V(8, 0.08f, 14));
        for (float z = -1f; z <= 11f; z += 2f) b.Caja("MetalOscuro", V(0, yR - 0.2f, z), V(8, 0.25f, 0.3f));
        b.Caja("MetalOscuro", V(0, yR - 0.2f, 5), V(0.3f, 0.25f, 14));

        // ---------- PLANTA BAJA ----------
        float y0 = 0f;
        Pared(b, V(-16, 0, -12), V(16, 0, -12), y0, H, "Pared", Vent(3, 3.4f, 1.2f, 3.6f), Vent(16, 19f, 0.7f, 4.2f), Vent(29, 3.4f, 1.2f, 3.6f));
        Pared(b, V(16, 0, -12), V(16, 0, 12), y0, H, "Pared", Vent(5, 3.4f, 1.2f, 3.6f), Vent(18, 8f, 0.8f, 4.0f));
        Pared(b, V(-16, 0, 12), V(16, 0, 12), y0, H, "Pared", Vent(6, 5f, 1f, 3.8f), Vent(16, 4f, 1f, 4.4f), Vent(26, 5f, 1f, 3.8f));
        Pared(b, V(-16, 0, -12), V(-16, 0, 12), y0, H, "Pared", Vent(5, 3.4f, 1.2f, 3.6f), Vent(14, 4.5f, 0.9f, 3.9f), Vent(20, 4.5f, 0.9f, 3.9f));
        // interiores
        Pared(b, V(-16, 0, -2), V(16, 0, -2), y0, H, "Pared", Arco(16, 5f, 3.6f, "NeonCian", "PUENTE DE MANDO"));
        Pared(b, V(-10, 0, -12), V(-10, 0, -2), y0, H, "Pared", Puerta(5, 2.4f, 3.2f, "NeonAmarillo", "BODEGA"));
        Pared(b, V(10, 0, -12), V(10, 0, -2), y0, H, "Pared", Puerta(5, 2.4f, 3.2f, "NeonNaranja", "ESCLUSA"));
        Pared(b, V(-4, 0, -2), V(-4, 0, 12), y0, H, "Pared", Arco(7, 6f, 3.6f, "NeonAmarillo", "COMEDOR"));
        Pared(b, V(4, 0, -2), V(4, 0, 12), y0, H, "Pared", Vent(4, 4f, 0.6f, 4.0f), Puerta(9, 3f, 3.4f, "NeonVerde", "BIODOMO"));

        // ---------- PLANTA ALTA ----------
        y0 = PISO2;
        Pared(b, V(-16, 0, -12), V(16, 0, -12), y0, H, "Pared", Vent(6, 6f, 1f, 3.8f), Vent(16, 6f, 0.9f, 4.2f), Vent(26, 6f, 1f, 3.8f));
        Pared(b, V(16, 0, -12), V(16, 0, 12), y0, H, "Pared", Vent(6, 5f, 1f, 3.8f), Vent(18, 5f, 1f, 3.8f));
        Pared(b, V(-16, 0, 12), V(16, 0, 12), y0, H, "Pared", Vent(6, 5f, 1f, 3.8f), Vent(16, 4f, 1f, 4.4f), Vent(26, 5f, 1f, 3.8f));
        Pared(b, V(-16, 0, -12), V(-16, 0, 12), y0, H, "Pared");
        Pared(b, V(-4, 0, -12), V(-4, 0, 12), y0, H, "Pared", Puerta(14, 2.4f, 3.2f, "NeonCian", "PASARELA"), Vent(20, 4f, 0.9f, 3.8f));
        Pared(b, V(4, 0, -12), V(4, 0, 12), y0, H, "Pared", Puerta(14, 2.4f, 3.2f, "NeonVerde", "LABORATORIO"), Vent(20, 4f, 0.9f, 3.8f));
        Pared(b, V(-4, 0, -2), V(4, 0, -2), y0, H, "Pared", Puerta(4, 2.4f, 3.2f, "NeonMagenta", "MIRADOR"));

        // Columnas perimetrales con neon
        for (float z = -12f; z <= 12.1f; z += 6f)
            for (int sx = -1; sx <= 1; sx += 2)
            {
                Columna(b, sx * 16f, z, 0f, "NeonCian");
                Columna(b, sx * 16f, z, PISO2, "NeonCian");
            }
        for (float x = -8f; x <= 8.1f; x += 8f)
            foreach (float zz in new[] { -12f, 12f })
            {
                Columna(b, x, zz, 0f, "NeonNaranja");
                Columna(b, x, zz, PISO2, "NeonNaranja");
            }
        b.Construir("Estructura_Combinada", padre, Vector3.zero);
    }

    // =====================================================================
    // PLANTA BAJA — PUENTE DE MANDO
    // =====================================================================
    void ConstruirPuenteMando(Transform padre)
    {
        Prop(padre, "Techo_Vigas_Tuberias", Vector3.zero, 0f, b =>
        {
            VigasTecho(b, -10f, 10f, -12f, -2f, 0f, true, 2.5f, "NeonCian");
            b.Tubo("TuboCian", V(-9.7f, 4.15f, -2.4f), V(9.7f, 4.15f, -2.4f), 0.22f);
            b.Tubo("TuboNaranja", V(9.7f, 4.0f, -11.6f), V(9.7f, 4.0f, -2.4f), 0.2f);
            b.Tubo("TuboAmarillo", V(-9.7f, 4.0f, -11.6f), V(-9.7f, 4.0f, -2.4f), 0.2f);
            for (float x = -9f; x <= 9.1f; x += 3f) b.CilX("MetalOscuro", V(x, 4.15f, -2.4f), 0.32f, 0.12f);
            b.Caja("Amarillo", V(0, 0.012f, -11.2f), V(19f, 0.02f, 0.25f));
            b.Cil("MetalOscuro", V(0, 0.05f, -6.4f), 7.6f, 0.1f);
            b.Cil("NeonCian", V(0, 0.105f, -6.4f), 7.7f, 0.02f);
            // rejilla del suelo
            for (float x = -8.8f; x <= 8.9f; x += 0.4f) b.Caja("MetalOscuro", V(x, 0.012f, -3.2f), V(0.1f, 0.02f, 1.4f));
        });

        // Consola curva con pantallas inclinadas, botones y palancas
        Prop(padre, "Consola_Curva_Principal", V(0, 0, -6.4f), 0f, b =>
        {
            string[] btn = { "NeonVerde", "NeonAmarillo", "NeonNaranja", "NeonMagenta" };
            for (int i = 0; i < 11; i++)
            {
                float a = -80f + i * 16f, ra = a * Mathf.Deg2Rad;
                Vector3 p = V(3.9f * Mathf.Sin(ra), 0f, -3.9f * Mathf.Cos(ra));
                Quaternion q = Quaternion.Euler(0f, -a, 0f);
                b.CajaQ("MetalOscuro", p + q * V(0, 0.45f, 0), V(1.2f, 0.9f, 0.9f), q);
                b.CajaQ("MetalBase", p + q * V(0, 0.95f, 0.05f), V(1.2f, 0.1f, 0.8f), q * Quaternion.Euler(20f, 0f, 0f));
                float hs = (i % 2 == 0) ? 0.55f : 0.8f;
                b.CajaQ("MetalOscuro", p + q * V(0, 1.5f, -0.12f), V(1.1f, hs + 0.1f, 0.05f), q * Quaternion.Euler(-15f, 0f, 0f));
                b.CajaQ("Holograma", p + q * V(0, 1.5f, -0.09f), V(1.0f, hs, 0.03f), q * Quaternion.Euler(-15f, 0f, 0f));
                b.CajaQ("MetalBase", p + q * V(-0.5f, 1.15f, -0.12f), V(0.08f, 0.6f, 0.08f), q);
                b.CajaQ("MetalBase", p + q * V(0.5f, 1.15f, -0.12f), V(0.08f, 0.6f, 0.08f), q);
                b.CajaQ(i % 3 == 0 ? "NeonNaranja" : "NeonCian", p + q * V(0, 0.6f, 0.46f), V(1.1f, 0.05f, 0.04f), q);
                for (int k = 0; k < 4; k++)
                    b.Esf(btn[(i + k) % 4], p + q * V(-0.4f + k * 0.27f, 1.0f, 0.14f), 0.07f);
                if (i == 3 || i == 7)
                {
                    b.Cil("MetalOscuro", p + q * V(0, 1.03f, 0.2f), 0.14f, 0.08f, q);
                    b.Tubo("MetalBase", p + q * V(0, 1.06f, 0.2f), p + q * V(0, 1.38f, 0.1f), 0.05f);
                    b.Esf("NeonNaranja", p + q * V(0, 1.4f, 0.09f), 0.11f);
                }
                if (i == 1 || i == 9)
                    for (int k = 0; k < 3; k++)
                    {
                        float xo = -0.3f + k * 0.3f;
                        b.CajaQ("MetalOscuro", p + q * V(xo, 1.02f, 0.12f), V(0.05f, 0.02f, 0.45f), q * Quaternion.Euler(20f, 0f, 0f));
                        b.CajaQ("Rojo", p + q * V(xo, 1.07f, 0.04f + 0.1f * k), V(0.1f, 0.1f, 0.14f), q);
                    }
            }
        });

        // Asientos ergonomicos de piloto
        SillaPiloto(padre, V(-2.0f, 0, -6.0f), 180f, "Tela", false);
        SillaPiloto(padre, V(2.0f, 0, -6.0f), 180f, "Tela", false);
        SillaPiloto(padre, V(0, 0, -5.4f), 180f, "Rojo", true);

        // Mapa estelar 3D flotante
        Prop(padre, "Proyector_Mapa_Estelar", V(0, 0, -8.4f), 0f, b =>
        {
            b.Cil("MetalOscuro", V(0, 0.15f, 0), 1.0f, 0.3f);
            b.Cil("MetalBase", V(0, 0.4f, 0), 0.55f, 0.25f);
            b.Cil("NeonCian", V(0, 0.55f, 0), 0.6f, 0.03f);
            b.Cil("Holograma", V(0, 1.0f, 0), 0.25f, 0.8f);
        });
        GameObject mapa = Prop(padre, "Mapa_Estelar_3D", V(0, 1.9f, -8.4f), 0f, b =>
        {
            b.Esf("Holograma", Vector3.zero, 1.5f);
            b.Cil("NeonCian", Vector3.zero, 1.9f, 0.02f, Quaternion.Euler(20f, 0f, 15f));
            b.Cil("NeonNaranja", Vector3.zero, 1.5f, 0.02f, Quaternion.Euler(-30f, 0f, -20f));
            Vector3 prev = Vector3.zero;
            for (int i = 0; i < 14; i++)
            {
                Vector3 pt = new Vector3(R(-1f, 1f), R(-1f, 1f), R(-1f, 1f)).normalized * R(0.15f, 0.65f);
                b.Esf(i % 4 == 0 ? "NeonAmarillo" : (i % 3 == 0 ? "NeonNaranja" : "NeonBlanco"), pt, R(0.05f, 0.11f));
                if (i > 0 && i < 7) b.Tubo("NeonNaranja", prev, pt, 0.015f);
                prev = pt;
            }
        });
        Anim(mapa, EstacionVida.Tipo.Girar, 15f, 0f, V(0, 1, 0));

        // Estaciones laterales y pantallas de pared
        EstacionLateral(padre, V(-9.3f, 0, -4.6f), 90f);
        EstacionLateral(padre, V(9.3f, 0, -4.6f), -90f);
        PantallaPared(padre, V(-9.82f, 2.7f, -8.4f), 90f, 2.6f, 1.2f, "NeonCian");
        PantallaPared(padre, V(9.82f, 2.7f, -8.4f), -90f, 2.6f, 1.2f, "NeonNaranja");
        PantallaPared(padre, V(-9.82f, 2.7f, -4.6f), 90f, 2.0f, 1.0f, "NeonVerde");
        PantallaPared(padre, V(9.82f, 2.7f, -4.6f), -90f, 2.0f, 1.0f, "NeonMagenta");

        Luz(padre, "Luz_Puente_Fria", V(0, 3.8f, -6f), Fria, 2.0f, 12f);
        Luz(padre, "Luz_Puente_Naranja_L", V(-7f, 3.0f, -4f), new Color(1f, 0.6f, 0.3f), 1.4f, 8f, true);
        Luz(padre, "Luz_Puente_Naranja_R", V(7f, 3.0f, -4f), new Color(1f, 0.6f, 0.3f), 1.4f, 8f, true);
    }

    void SillaPiloto(Transform padre, Vector3 pos, float ry, string tela, bool capitan)
    {
        float e = capitan ? 1.12f : 1f;
        Prop(padre, capitan ? "Silla_Capitan" : "Silla_Piloto", pos, ry, b =>
        {
            b.Cil("MetalOscuro", V(0, 0.05f, 0), 0.8f * e, 0.1f);
            b.Cil("MetalBase", V(0, 0.3f, 0), 0.16f, 0.4f);
            b.Caja(tela, V(0, 0.52f * e, 0), V(0.64f * e, 0.14f, 0.64f * e));
            b.CajaQ(tela, V(0, 1.0f * e, -0.3f * e), V(0.6f * e, 0.85f * e, 0.14f), Quaternion.Euler(-8f, 0f, 0f));
            b.Caja("MetalOscuro", V(0, 1.52f * e, -0.35f * e), V(0.4f, 0.22f, 0.1f));
            b.Caja("NeonCian", V(0, 1.36f * e, -0.24f * e), V(0.3f, 0.03f, 0.02f));
            for (int s = -1; s <= 1; s += 2)
            {
                b.Caja("MetalOscuro", V(s * 0.4f * e, 0.74f * e, 0.05f), V(0.1f, 0.1f, 0.5f));
                b.Caja("NeonCian", V(s * 0.4f * e, 0.8f * e, 0.12f), V(0.06f, 0.02f, 0.2f));
            }
            b.Tubo("MetalBase", V(0.4f * e, 0.8f * e, 0.2f), V(0.4f * e, 1.05f * e, 0.3f), 0.04f);
            b.Esf("NeonNaranja", V(0.4f * e, 1.07f * e, 0.31f), 0.09f);
            b.Tubo("MetalBase", V(-0.4f * e, 0.8f * e, 0.2f), V(-0.4f * e, 1.0f * e, 0.12f), 0.04f);
            b.Esf("NeonVerde", V(-0.4f * e, 1.02f * e, 0.11f), 0.09f);
        });
    }

    void EstacionLateral(Transform padre, Vector3 pos, float ry)
    {
        Prop(padre, "Estacion_Lateral", pos, ry, b =>
        {
            b.Caja("MetalOscuro", V(0, 0.45f, 0), V(2.2f, 0.9f, 0.7f));
            b.CajaQ("MetalBase", V(0, 0.95f, 0.02f), V(2.1f, 0.1f, 0.65f), Quaternion.Euler(20f, 0f, 0f));
            b.CajaQ("PantallaOscura", V(0, 1.0f, 0.02f), V(1.8f, 0.04f, 0.4f), Quaternion.Euler(20f, 0f, 0f));
            b.Caja("NeonCian", V(0, 0.7f, 0.36f), V(1.9f, 0.05f, 0.04f));
            for (int k = 0; k < 5; k++) b.Esf(k % 2 == 0 ? "NeonVerde" : "NeonNaranja", V(-0.8f + k * 0.4f, 0.98f, 0.28f), 0.06f);
        });
        SillaSimple(padre, pos + Quaternion.Euler(0f, ry, 0f) * V(0, 0, 0.95f), ry + 180f, "Azul");
    }

    void SillaSimple(Transform padre, Vector3 pos, float ry, string mat)
    {
        Prop(padre, "Silla", pos, ry, b =>
        {
            b.Cil("MetalOscuro", V(0, 0.04f, 0), 0.55f, 0.06f);
            b.Cil("MetalBase", V(0, 0.24f, 0), 0.1f, 0.4f);
            b.Caja(mat, V(0, 0.46f, 0), V(0.52f, 0.1f, 0.5f));
            b.Caja(mat, V(0, 0.84f, -0.23f), V(0.5f, 0.7f, 0.1f));
        });
    }

    void PantallaPared(Transform padre, Vector3 pos, float ry, float w, float h, string neon)
    {
        Prop(padre, "Pantalla_Pared", pos, ry, b =>
        {
            b.Caja("MetalOscuro", Vector3.zero, V(w + 0.14f, h + 0.14f, 0.08f));
            b.Caja("PantallaOscura", V(0, 0, 0.045f), V(w, h, 0.03f));
            b.Caja(neon, V(0, h * 0.5f + 0.09f, 0.03f), V(w * 0.7f, 0.04f, 0.04f));
            for (int i = 0; i < 4; i++)
                b.Caja(neon, V(-w * 0.2f + (i % 2) * 0.15f, -h * 0.35f + i * h * 0.22f, 0.07f), V(w * R(0.25f, 0.6f), 0.05f, 0.02f));
        });
    }

    // =====================================================================
    // PLANTA BAJA — COMEDOR Y CANTINA
    // =====================================================================
    void ConstruirComedor(Transform padre)
    {
        Prop(padre, "Techo_Vigas_Tuberias", Vector3.zero, 0f, b =>
        {
            VigasTecho(b, -16f, -4f, -2f, 12f, 0f, true, 3.5f, "NeonAmarillo");
            b.Tubo("TuboNaranja", V(-15.6f, 4.2f, 11.6f), V(-4.4f, 4.2f, 11.6f), 0.22f);
            b.Tubo("TuboCian", V(-15.6f, 4.0f, -1.6f), V(-15.6f, 4.0f, 11.6f), 0.2f);
            for (float z = 0f; z <= 11f; z += 3f) b.CilZ("MetalOscuro", V(-15.6f, 4.0f, z), 0.32f, 0.12f);
            for (float x = -14f; x <= -5f; x += 3f) b.CilX("MetalOscuro", V(x, 4.2f, 11.6f), 0.32f, 0.12f);
            b.Caja("Amarillo", V(-9.9f, 0.012f, 7.9f), V(11.6f, 0.02f, 0.14f));
        });

        // Mesas modulares con bancos integrados (2 filas x 3)
        float[] xs = { -13.4f, -10.2f, -7.0f };
        float[] zs = { 1.2f, 4.4f };
        for (int fz = 0; fz < zs.Length; fz++)
            for (int fx = 0; fx < xs.Length; fx++)
                MesaModular(padre, V(xs[fx], 0, zs[fz]), (fx + fz) % 2 == 0);

        // Lamparas colgantes
        for (int fx = 0; fx < xs.Length; fx++)
            for (int fz = 0; fz < zs.Length; fz++)
                Prop(padre, "Lampara_Colgante", V(xs[fx], 0, zs[fz]), 0f, b =>
                {
                    b.Tubo("MetalOscuro", V(0, 3.4f, 0), V(0, 4.55f, 0), 0.03f);
                    b.Cil("MetalBase", V(0, 3.32f, 0), 0.9f, 0.12f);
                    b.Cil("NeonBlanco", V(0, 3.25f, 0), 0.7f, 0.04f);
                });

        // Barra con taburetes
        Prop(padre, "Barra_Cantina", V(-11.5f, 0, 8.6f), 180f, b =>
        {
            b.Caja("MetalOscuro", V(0, 0.52f, 0), V(6f, 1.04f, 0.9f));
            b.Caja("MetalBase", V(0, 1.07f, 0), V(6.1f, 0.07f, 1.05f));
            b.Caja("Amarillo", V(0, 0.6f, 0.46f), V(5.8f, 0.12f, 0.03f));
            b.Caja("NeonCian", V(0, 1.0f, 0.5f), V(5.9f, 0.04f, 0.04f));
            string[] bot = { "NeonVerde", "NeonNaranja", "NeonCian", "NeonMagenta", "NeonAmarillo" };
            for (int i = 0; i < 5; i++) b.Cil(bot[i], V(-2.3f + i * 1.1f, 1.28f, -0.1f), 0.14f, 0.42f);
            b.Caja("Blanco", V(2.2f, 1.14f, 0.1f), V(0.5f, 0.06f, 0.35f));
            b.Cil("Amarillo", V(2.2f, 1.22f, 0.1f), 0.11f, 0.12f);
        });
        for (int i = 0; i < 5; i++)
            Prop(padre, "Taburete", V(-13.5f + i * 1.0f, 0, 7.4f), 0f, b =>
            {
                b.Cil("MetalBase", V(0, 0.04f, 0), 0.4f, 0.06f);
                b.Cil("MetalOscuro", V(0, 0.36f, 0), 0.08f, 0.7f);
                b.Cil("NeonCian", V(0, 0.66f, 0), 0.45f, 0.02f);
                b.Cil("TelaNaranja", V(0, 0.73f, 0), 0.42f, 0.1f);
            });

        // Dispensadores de alimentos y bebidas con luces emisivas
        for (int i = 0; i < 3; i++) Dispensador(padre, V(-13.5f + i * 1.6f, 0, 11.4f), 180f, i);
        Dispensador(padre, V(-15.3f, 0, 4.0f), 90f, 1);
        Dispensador(padre, V(-15.3f, 0, -0.3f), 90f, 2);

        PantallaPared(padre, V(-9.2f, 2.9f, 11.82f), 180f, 3.0f, 1.2f, "NeonVerde");

        Luz(padre, "Luz_Comedor_Calida_A", V(-11.7f, 3.6f, 2.8f), Calida, 2.2f, 10f);
        Luz(padre, "Luz_Comedor_Calida_B", V(-8.7f, 3.6f, 6.0f), Calida, 2.0f, 10f);
        Luz(padre, "Luz_Comedor_Barra_Cian", V(-11.5f, 2.6f, 8.0f), Fria, 1.2f, 6f, true);
    }

    void MesaModular(Transform padre, Vector3 pos, bool alterno)
    {
        Prop(padre, "Mesa_Modular", pos, 0f, b =>
        {
            b.Caja("MetalBase", V(0, 0.78f, 0), V(1.2f, 0.08f, 2.6f));
            b.Caja("MetalOscuro", V(0, 0.38f, 0), V(0.3f, 0.76f, 2.2f));
            b.Caja("NeonCian", V(0, 0.83f, 1.31f), V(1.1f, 0.02f, 0.03f));
            b.Caja("NeonCian", V(0, 0.83f, -1.31f), V(1.1f, 0.02f, 0.03f));
            for (int sg = -1; sg <= 1; sg += 2)
            {
                b.Caja(sg < 0 ? (alterno ? "TelaNaranja" : "Tela") : (alterno ? "Tela" : "TelaNaranja"), V(sg * 0.95f, 0.46f, 0), V(0.42f, 0.1f, 2.4f));
                for (int zz = -1; zz <= 1; zz += 2)
                {
                    b.Caja("MetalOscuro", V(sg * 0.6f, 0.3f, zz * 0.9f), V(0.8f, 0.1f, 0.14f));
                    b.Caja("MetalOscuro", V(sg * 0.95f, 0.2f, zz * 0.9f), V(0.1f, 0.4f, 0.14f));
                }
            }
            b.Caja("MetalBase", V(-0.25f, 0.84f, -0.6f), V(0.5f, 0.03f, 0.38f));
            b.Cil("Amarillo", V(-0.1f, 0.92f, -0.55f), 0.11f, 0.14f);
            b.Esf("TelaNaranja", V(-0.35f, 0.88f, -0.65f), V(0.2f, 0.1f, 0.2f));
            b.Caja("MetalBase", V(0.25f, 0.84f, 0.7f), V(0.5f, 0.03f, 0.38f));
            b.Cil("Rojo", V(0.1f, 0.92f, 0.65f), 0.11f, 0.14f);
            b.Esf("VegHoja", V(0.35f, 0.88f, 0.75f), V(0.2f, 0.1f, 0.2f));
        });
    }

    void Dispensador(Transform padre, Vector3 pos, float ry, int idx)
    {
        string[] neon = { "NeonCian", "NeonNaranja", "NeonVerde" };
        GameObject g = Prop(padre, "Dispensador_" + idx, pos, ry, b =>
        {
            b.Caja("MetalBase", V(0, 1.15f, 0), V(1.3f, 2.3f, 0.9f));
            b.Caja("MetalOscuro", V(0, 1.1f, 0.47f), V(1.1f, 1.9f, 0.06f));
            b.Caja("PantallaOscura", V(0, 1.75f, 0.5f), V(0.7f, 0.5f, 0.04f));
            for (int k = -1; k <= 1; k++) b.Caja(neon[(idx + k + 3) % 3], V(k * 0.4f, 1.1f, 0.5f), V(0.12f, 0.5f, 0.04f));
            b.Caja("MetalOscuro", V(0, 0.8f, 0.5f), V(0.3f, 0.12f, 0.2f));
            b.Caja("MetalBase", V(0, 0.6f, 0.62f), V(0.8f, 0.05f, 0.35f));
            b.Cil("Blanco", V(-0.15f, 0.7f, 0.62f), 0.1f, 0.14f);
            b.Cil("Amarillo", V(0.15f, 0.7f, 0.62f), 0.1f, 0.14f);
            b.Caja("Amarillo", V(0, 2.35f, 0), V(1.34f, 0.1f, 0.94f));
        });
        Luz(g.transform, "Luz_Dispensador", V(0, 1.6f, 1.0f), idx == 1 ? new Color(1f, 0.6f, 0.3f) : Fria, 0.9f, 4f);
    }

    // =====================================================================
    // PLANTA BAJA — BIODOMO HIDROPONICO
    // =====================================================================
    void ConstruirBiodomo(Transform padre)
    {
        Prop(padre, "Techo_Tuberias_Riego", Vector3.zero, 0f, b =>
        {
            VigasTecho(b, 4f, 16f, -2f, 12f, 0f, false, 3.0f, "NeonVerde");
            // tuberia de riego transparente con fluido verde
            b.Tubo("Cristal", V(4.5f, 4.15f, -1.3f), V(15.5f, 4.15f, -1.3f), 0.34f);
            b.Tubo("NeonVerde", V(4.5f, 4.15f, -1.3f), V(15.5f, 4.15f, -1.3f), 0.18f);
            b.Tubo("Cristal", V(15.5f, 4.15f, -1.3f), V(15.5f, 4.15f, 11.3f), 0.34f);
            b.Tubo("NeonCian", V(15.5f, 4.15f, -1.3f), V(15.5f, 4.15f, 11.3f), 0.18f);
            b.Tubo("Cristal", V(4.5f, 4.15f, 11.3f), V(15.5f, 4.15f, 11.3f), 0.34f);
            b.Tubo("NeonVerde", V(4.5f, 4.15f, 11.3f), V(15.5f, 4.15f, 11.3f), 0.18f);
            foreach (float x in new[] { 6.6f, 10f, 13.8f })
            {
                b.Tubo("Cristal", V(x, 4.15f, -1.3f), V(x, 3.9f, 0.2f), 0.26f);
                b.Cil("Amarillo", V(x, 4.15f, -0.5f), 0.3f, 0.06f);
            }
            for (float x = 5f; x <= 15f; x += 2.5f) b.CilX("MetalOscuro", V(x, 4.15f, -1.3f), 0.4f, 0.1f);
            b.Tubo("TuboNaranja", V(4.4f, 3.9f, -1.7f), V(4.4f, 3.9f, 11.6f), 0.2f);
            b.Tubo("TuboAmarillo", V(4.4f, 3.6f, -1.7f), V(4.4f, 3.6f, 11.6f), 0.14f);
            // musgo bioluminiscente en el piso
            for (int i = 0; i < 26; i++)
            {
                float mx = R(4.6f, 15.5f), mz = R(-1.5f, 11.5f);
                b.Esf("MusgoBio", V(mx, 0.02f, mz), V(R(0.3f, 0.7f), 0.06f, R(0.25f, 0.55f)));
            }
        });

        // Capsulas de cristal con plantas alienigenas
        Capsula(padre, "Capsula_Cristal_A", V(6.6f, 0, 0.2f), 2.6f, 3.4f, 0);
        Capsula(padre, "Capsula_Cristal_B", V(13.8f, 0, 0.2f), 2.6f, 3.4f, 1);
        Capsula(padre, "Capsula_Cristal_C", V(6.6f, 0, 10.4f), 2.6f, 3.4f, 2);
        Capsula(padre, "Capsula_Cristal_D", V(13.8f, 0, 10.4f), 2.6f, 3.4f, 3);
        Capsula(padre, "Capsula_Central_Gigante", V(10.2f, 0, 5.2f), 4.6f, 4.0f, 4);

        // Bancal de cultivo con luces UV calidas
        Prop(padre, "Bancal_Hidroponico", V(10.2f, 0, 10.6f), 0f, b =>
        {
            b.Caja("MetalBase", V(0, 0.4f, 0), V(3.2f, 0.8f, 0.9f));
            b.Caja("Tierra", V(0, 0.82f, 0), V(3.0f, 0.06f, 0.7f));
            b.Caja("NeonNaranja", V(0, 1.55f, 0), V(2.8f, 0.05f, 0.14f));
            b.Caja("MetalOscuro", V(-1.45f, 1.0f, 0), V(0.08f, 1.2f, 0.08f));
            b.Caja("MetalOscuro", V(1.45f, 1.0f, 0), V(0.08f, 1.2f, 0.08f));
            for (int i = 0; i < 6; i++)
            {
                float px = -1.2f + i * 0.48f;
                b.Esf(i % 2 == 0 ? "VegHoja" : "VegHojaMorada", V(px, 1.0f, 0), V(0.34f, 0.3f, 0.34f));
                b.Esf("MusgoBio", V(px + 0.1f, 0.9f, 0.18f), 0.1f);
            }
        });

        // Bancos
        for (int i = 0; i < 2; i++)
            Prop(padre, "Banco_Biodomo", V(7.6f + i * 5.2f, 0, 5.2f), 90f, b =>
            {
                b.Caja("Madera", V(0, 0.45f, 0), V(1.8f, 0.1f, 0.5f));
                b.Caja("MetalOscuro", V(-0.7f, 0.22f, 0), V(0.1f, 0.44f, 0.4f));
                b.Caja("MetalOscuro", V(0.7f, 0.22f, 0), V(0.1f, 0.44f, 0.4f));
                b.Caja("NeonVerde", V(0, 0.4f, 0.26f), V(1.7f, 0.03f, 0.02f));
            });

        Luz(padre, "Luz_UV_Calida_Centro", V(10.2f, 3.9f, 5.2f), new Color(1f, 0.62f, 0.3f), 3.0f, 12f, true);
        Luz(padre, "Luz_Bio_Verde", V(10.2f, 1.0f, 5.2f), new Color(0.4f, 1f, 0.6f), 1.6f, 7f, true);
        Luz(padre, "Luz_UV_Calida_N", V(10.2f, 3.5f, 10.6f), new Color(1f, 0.7f, 0.3f), 1.6f, 7f);
        Luz(padre, "Luz_UV_Calida_S", V(10.2f, 3.5f, 0.6f), new Color(1f, 0.7f, 0.3f), 1.6f, 7f);
    }

    void Capsula(Transform padre, string nombre, Vector3 pos, float diam, float alto, int sem)
    {
        bool grande = diam > 4f;
        GameObject g = Prop(padre, nombre, pos, 0f, b =>
        {
            b.Cil("MetalOscuro", V(0, 0.15f, 0), diam + 0.3f, 0.3f);
            b.Cil("Tierra", V(0, 0.38f, 0), diam - 0.15f, 0.16f);
            b.Cil("Cristal", V(0, 0.3f + alto * 0.5f, 0), diam, alto);
            b.Cil("MetalBase", V(0, 0.3f + alto * 0.33f, 0), diam + 0.08f, 0.08f);
            b.Cil("MetalBase", V(0, 0.3f + alto * 0.66f, 0), diam + 0.08f, 0.08f);
            b.Cil("MetalOscuro", V(0, 0.3f + alto + 0.15f, 0), diam + 0.3f, 0.3f);
            b.Cil("NeonNaranja", V(0, 0.3f + alto - 0.05f, 0), diam - 0.3f, 0.06f);
            b.Cil("Amarillo", V(0, 0.32f, 0), diam + 0.34f, 0.04f);
            // musgo y esporas
            for (int i = 0; i < (grande ? 9 : 5); i++)
            {
                float a = R(0f, 6.28f), rr = R(0.2f, diam * 0.42f);
                b.Esf("MusgoBio", V(Mathf.Cos(a) * rr, 0.48f, Mathf.Sin(a) * rr), V(R(0.25f, 0.5f), 0.07f, R(0.2f, 0.4f)));
            }
            for (int i = 0; i < 10; i++)
            {
                float a = R(0f, 6.28f), rr = R(0.1f, diam * 0.4f);
                b.Esf(i % 2 == 0 ? "NeonCian" : "NeonVerde", V(Mathf.Cos(a) * rr, R(1f, alto - 0.3f), Mathf.Sin(a) * rr), 0.05f);
            }
            if (grande)
            {
                b.Tubo("VegTronco", V(0, 0.4f, 0), V(0.2f, 1.9f, 0.1f), 0.4f);
                b.Tubo("VegTronco", V(0.2f, 1.9f, 0.1f), V(-0.1f, 3.2f, -0.1f), 0.28f);
                for (int i = 0; i < 9; i++)
                {
                    float ang = i * 40f * Mathf.Deg2Rad;
                    Vector3 dir = V(Mathf.Cos(ang), 0, Mathf.Sin(ang));
                    float hh = 2.2f + (i % 3) * 0.5f;
                    Quaternion q = Quaternion.Euler(0f, -ang * Mathf.Rad2Deg, 0f) * Quaternion.Euler(0f, 0f, -22f);
                    b.Esf(i % 2 == 0 ? "VegHoja" : "VegHojaMorada", V(0, hh, 0) + dir * 0.9f, V(1.8f, 0.07f, 0.7f), q);
                    b.Esf("NeonCian", V(0, hh - 0.35f, 0) + dir * 1.55f, 0.12f);
                }
                b.Esf("MusgoBio", V(0, 3.4f, 0), 0.4f);
            }
            else
            {
                for (int k = 0; k < 3; k++)
                    PlantaAlien(b, V(R(-0.5f, 0.5f), 0.46f, R(-0.5f, 0.5f)), R(0.9f, 1.5f), ((sem + k) % 2 == 0) ? "VegHoja" : "VegHojaMorada");
            }
        });
        Luz(g.transform, "Luz_UV_Capsula", V(0, alto - 0.3f, 0), new Color(1f, 0.6f, 0.25f), 1.1f, 5f);
    }

    void PlantaAlien(Malla b, Vector3 baseP, float esc, string hoja)
    {
        Vector3 top = baseP + V(R(-0.12f, 0.12f), 1.0f * esc, R(-0.12f, 0.12f));
        b.Tubo("VegTronco", baseP, top, 0.07f * esc);
        int n = 5;
        for (int k = 0; k < n; k++)
        {
            float ang = (k * 360f / n + R(-15f, 15f)) * Mathf.Deg2Rad;
            Vector3 dir = V(Mathf.Cos(ang), 0, Mathf.Sin(ang));
            Quaternion q = Quaternion.Euler(0f, -ang * Mathf.Rad2Deg, 0f) * Quaternion.Euler(0f, 0f, -22f);
            b.Esf(hoja, top + dir * 0.3f * esc, V(0.75f * esc, 0.05f * esc, 0.3f * esc), q);
        }
        b.Esf("MusgoBio", top + V(0, 0.05f, 0), 0.1f * esc);
    }

    // =====================================================================
    // PLANTA BAJA — HUB CENTRAL Y ESCALERA
    // =====================================================================
    void ConstruirHub(Transform padre)
    {
        // Rejilla de piso, guias neon, tuberias verticales
        Prop(padre, "Detalles_Hub", Vector3.zero, 0f, b =>
        {
            for (float z = 0.2f; z <= 11.8f; z += 0.26f) b.Caja("MetalOscuro", V(-2.1f, 0.012f, z), V(3.4f, 0.02f, 0.08f));
            b.Caja("NeonCian", V(0, 0.015f, 5f), V(0.12f, 0.02f, 14f));
            b.Caja("Amarillo", V(-3.6f, 0.012f, 5f), V(0.14f, 0.02f, 13f));
            b.Tubo("TuboNaranja", V(-3.7f, 0, 11.7f), V(-3.7f, 9.7f, 11.7f), 0.28f);
            b.Tubo("TuboCian", V(-3.3f, 0, 11.7f), V(-3.3f, 9.7f, 11.7f), 0.2f);
            b.Tubo("TuboAmarillo", V(3.7f, 0, -1.7f), V(3.7f, 9.7f, -1.7f), 0.2f);
            for (float y = 1f; y < 9.5f; y += 2f)
            {
                b.Cil("MetalOscuro", V(-3.7f, y, 11.7f), 0.38f, 0.12f);
                b.Cil("MetalOscuro", V(3.7f, y, -1.7f), 0.3f, 0.12f);
            }
        });

        // Holo-planeta flotante
        Prop(padre, "Pedestal_Holo", V(-1.8f, 0, 7f), 0f, b =>
        {
            b.Cil("MetalOscuro", V(0, 0.15f, 0), 1.8f, 0.3f);
            b.Cil("MetalBase", V(0, 0.55f, 0), 1.0f, 0.5f);
            b.Cil("NeonCian", V(0, 0.82f, 0), 1.1f, 0.03f);
        });
        GameObject holo = Prop(padre, "Holo_Planeta_Hub", V(-1.8f, 1.9f, 7f), 0f, b =>
        {
            b.Esf("Holograma", Vector3.zero, 1.0f);
            b.Cil("NeonCian", Vector3.zero, 1.7f, 0.02f, Quaternion.Euler(22f, 0f, 15f));
            b.Esf("NeonNaranja", V(0.9f, 0.3f, 0), 0.12f);
        });
        Anim(holo, EstacionVida.Tipo.Girar, 20f, 0f, V(0, 1, 0));
        Anim(holo, EstacionVida.Tipo.Flotar, 1.2f, 0.06f);

        // Escalera industrial recta hacia la pasarela
        Prop(padre, "Escalera_Industrial", Vector3.zero, 0f, b =>
        {
            int pasos = 20;
            for (int i = 0; i < pasos; i++)
            {
                float top = (i + 1) * 0.25f;
                float zc = 11.0f - i * 0.4f;
                b.Caja("SueloClaro", V(2.2f, top * 0.5f, zc), V(2.6f, top, 0.4f), 0f, true);
                b.Caja("NeonCian", V(2.2f, top + 0.01f, zc + 0.18f), V(2.5f, 0.03f, 0.04f));
                b.Caja("Amarillo", V(2.2f, top + 0.012f, zc + 0.1f), V(2.5f, 0.012f, 0.06f));
            }
            for (int sd = 0; sd < 2; sd++)
            {
                float x = sd == 0 ? 0.9f : 3.5f;
                b.Viga("Amarillo", V(x, 1.25f, 11.0f), V(x, 6.0f, 3.4f), 0.07f);
                b.Viga("Amarillo", V(x, 0.8f, 11.0f), V(x, 5.55f, 3.4f), 0.05f);
                b.Viga("NeonCian", V(x, 1.05f, 11.0f), V(x, 5.8f, 3.4f), 0.025f);
                for (int i = 0; i < pasos; i += 4)
                {
                    float top = (i + 1) * 0.25f, zc = 11.0f - i * 0.4f;
                    b.Caja("Amarillo", V(x, top + 0.5f, zc), V(0.07f, 1.0f, 0.07f));
                }
                b.Viga("MetalOscuro", V(x, 0.1f, 11.2f), V(x, 4.9f, 3.2f), 0.2f);
            }
        });

        // Bancos y señalizacion
        Prop(padre, "Banco_Hub", V(-2.6f, 0, 10.8f), 0f, b =>
        {
            b.Caja("Tela", V(0, 0.45f, 0), V(2.2f, 0.12f, 0.6f));
            b.Caja("MetalOscuro", V(0, 0.22f, 0), V(2.0f, 0.44f, 0.3f));
            b.Caja("NeonNaranja", V(0, 0.4f, 0.32f), V(2.0f, 0.03f, 0.02f));
        });
        Prop(padre, "Cartel_Nivel", V(0f, 3.2f, 11.8f), 180f, b =>
        {
            b.Caja("MetalOscuro", Vector3.zero, V(3.0f, 0.7f, 0.1f));
            b.Caja("NeonCian", V(0, 0, 0.06f), V(2.8f, 0.05f, 0.02f));
            if (generarLetreros) b.Texto("NIVEL 2  >>", V(0, 0.06f, 0.1f), 180f, COL_TEXTO, 0.07f);
        });

        Luz(padre, "Luz_Hub_Fria", V(0, 3.8f, 6f), Fria, 2.0f, 13f);
        Luz(padre, "Luz_Hub_Alta", V(0, 8.8f, 6f), Fria, 2.4f, 14f);
        Luz(padre, "Luz_Hub_Naranja", V(-3f, 3.5f, 11f), new Color(1f, 0.65f, 0.3f), 1.2f, 6f, true);
    }

    // =====================================================================
    // PLANTA BAJA — BODEGA Y ESCLUSA
    // =====================================================================
    void ConstruirBodega(Transform padre)
    {
        Prop(padre, "Techo_Tuberias", Vector3.zero, 0f, b =>
        {
            VigasTecho(b, -16f, -10f, -12f, -2f, 0f, true, 3.3f, "NeonAmarillo");
            b.Tubo("TuboAmarillo", V(-15.6f, 4.1f, -11.6f), V(-15.6f, 4.1f, -2.4f), 0.22f);
            b.Tubo("TuboNaranja", V(-15.6f, 3.8f, -11.6f), V(-10.4f, 3.8f, -11.6f), 0.18f);
            b.Caja("Amarillo", V(-13f, 0.012f, -4.2f), V(5.4f, 0.02f, 0.2f));
        });

        // Pilas de cajas
        PilaCajas(padre, V(-14.6f, 0, -10.4f), 0f, 4);
        PilaCajas(padre, V(-12.7f, 0, -10.6f), 10f, 3);
        PilaCajas(padre, V(-14.8f, 0, -3.2f), -8f, 3);
        PilaCajas(padre, V(-11.4f, 0, -3.4f), 5f, 2);
        // Barriles
        for (int i = 0; i < 5; i++)
            Barril(padre, V(-11.0f - (i % 2) * 0.1f, 0, -9.5f + i * 0.85f), new[] { "Rojo", "TelaNaranja", "TuboCian", "Rojo", "Amarillo" }[i]);
        // Estanteria
        Prop(padre, "Estanteria_Carga", V(-15.5f, 0, -7f), 90f, b =>
        {
            for (int sx = -1; sx <= 1; sx += 2) { b.Caja("MetalOscuro", V(sx * 1.45f, 1.1f, 0), V(0.1f, 2.2f, 0.5f)); }
            for (int i = 0; i < 4; i++)
            {
                b.Caja("MetalBase", V(0, 0.3f + i * 0.6f, 0), V(3.0f, 0.06f, 0.5f));
                b.Caja(i % 2 == 0 ? "Azul" : "TelaNaranja", V(-0.8f, 0.5f + i * 0.6f, 0), V(0.7f, 0.34f, 0.4f));
                b.Caja("MetalBase", V(0.4f, 0.48f + i * 0.6f, 0), V(0.5f, 0.3f, 0.4f));
                b.Cil("NeonVerde", V(1.0f, 0.5f + i * 0.6f, 0), 0.2f, 0.3f);
            }
        });
        // Compuerta octogonal de mantenimiento
        CompuertaOctogonal(padre, V(-13f, 1.5f, -2.2f), 180f, 2.4f, "NeonAmarillo");
        Luz(padre, "Luz_Bodega", V(-13f, 3.6f, -7f), new Color(1f, 0.85f, 0.55f), 1.8f, 9f);
    }

    void ConstruirEsclusa(Transform padre)
    {
        Prop(padre, "Techo_Tuberias", Vector3.zero, 0f, b =>
        {
            VigasTecho(b, 10f, 16f, -12f, -2f, 0f, true, 3.3f, "NeonNaranja");
            b.Tubo("TuboNaranja", V(15.6f, 4.1f, -11.6f), V(15.6f, 4.1f, -2.4f), 0.24f);
            b.Tubo("TuboCian", V(10.4f, 3.8f, -11.6f), V(15.6f, 3.8f, -11.6f), 0.18f);
            for (int i = 0; i < 8; i++) b.Caja(i % 2 == 0 ? "Amarillo" : "MetalOscuro", V(10.6f + i * 0.7f, 0.012f, -3.2f), V(0.35f, 0.02f, 0.6f), 30f);
        });
        CompuertaOctogonal(padre, V(15.7f, 1.7f, -3.4f), -90f, 2.4f, "NeonNaranja");
        // Taquillas de trajes espaciales
        for (int i = 0; i < 2; i++)
            Prop(padre, "Traje_Espacial_" + i, V(10.55f, 0, -10.8f + i * 1.3f), 90f, b =>
            {
                b.Caja("MetalBase", V(0, 1.15f, -0.3f), V(0.9f, 2.3f, 0.1f));
                b.Caja("MetalOscuro", V(0, 0.08f, 0), V(0.9f, 0.16f, 0.7f));
                b.Caja("Blanco", V(0, 0.6f, 0.05f), V(0.46f, 0.75f, 0.26f));
                b.Caja("Blanco", V(0, 1.2f, 0.05f), V(0.56f, 0.62f, 0.34f));
                b.Caja("TelaNaranja", V(0, 1.25f, -0.15f), V(0.42f, 0.55f, 0.18f));
                b.Esf("Blanco", V(0, 1.72f, 0.05f), V(0.4f, 0.42f, 0.4f));
                b.Esf("NeonAmarillo", V(0, 1.72f, 0.18f), V(0.32f, 0.24f, 0.22f));
            });
        // Banco de herramientas
        Prop(padre, "Banco_Herramientas", V(13f, 0, -11.2f), 0f, b =>
        {
            b.Caja("MetalOscuro", V(0, 0.45f, 0), V(3f, 0.9f, 0.8f));
            b.Caja("MetalBase", V(0, 0.93f, 0), V(3.1f, 0.06f, 0.9f));
            b.Caja("Rojo", V(-0.8f, 1.1f, 0), V(0.7f, 0.3f, 0.35f));
            b.Caja("NeonNaranja", V(0.7f, 1.0f, 0.2f), V(0.5f, 0.05f, 0.05f));
            b.Cil("Amarillo", V(1.1f, 1.05f, -0.1f), 0.2f, 0.2f);
        });
        // Bolardos amarillos
        for (int i = 0; i < 3; i++)
            Prop(padre, "Bolardo", V(11f + i * 2.2f, 0, -3.6f), 0f, b => { b.Cil("Amarillo", V(0, 0.4f, 0), 0.22f, 0.8f); b.Cil("NeonNaranja", V(0, 0.8f, 0), 0.2f, 0.04f); });
        Luz(padre, "Luz_Esclusa_Alarma", V(13f, 3.6f, -7f), new Color(1f, 0.55f, 0.2f), 1.8f, 9f, true);
    }

    void PilaCajas(Transform padre, Vector3 pos, float ry, int n)
    {
        string[] col = { "TelaNaranja", "Azul", "MetalBase", "Rojo" };
        Prop(padre, "Pila_Cajas", pos, ry, b =>
        {
            for (int i = 0; i < n; i++)
            {
                float s = 1.0f - i * 0.08f;
                float y = 0.5f + i * 1.0f;
                int idxCol = Mathf.Abs(i + (int)(pos.x * 3f)) % col.Length;
                b.Caja(col[idxCol], V(R(-0.05f, 0.05f), y, R(-0.05f, 0.05f)), V(s, 0.98f, s));
                b.Caja("Amarillo", V(0, y, 0), V(s + 0.03f, 0.2f, s + 0.03f));
                for (int sx = -1; sx <= 1; sx += 2) for (int sz = -1; sz <= 1; sz += 2)
                    b.Caja("MetalOscuro", V(sx * s * 0.5f, y, sz * s * 0.5f), V(0.08f, 1.0f, 0.08f));
            }
        });
    }

    void Barril(Transform padre, Vector3 pos, string mat)
    {
        Prop(padre, "Barril", pos, 0f, b =>
        {
            b.Cil(mat, V(0, 0.45f, 0), 0.7f, 0.9f);
            b.Cil("MetalOscuro", V(0, 0.2f, 0), 0.74f, 0.06f);
            b.Cil("MetalOscuro", V(0, 0.7f, 0), 0.74f, 0.06f);
            b.Cil("MetalBase", V(0, 0.92f, 0), 0.6f, 0.04f);
        });
    }

    void CompuertaOctogonal(Transform padre, Vector3 pos, float ry, float diam, string neon)
    {
        Prop(padre, "Compuerta_Octogonal", pos, ry, b =>
        {
            Quaternion q = Quaternion.Euler(90f, 0f, 0f);
            b.Octo("MetalOscuro", V(0, 0, 0.1f), diam + 0.4f, 0.2f, q);
            b.Octo("MetalBase", V(0, 0, 0.2f), diam, 0.2f, q);
            b.Octo(neon, V(0, 0, 0.31f), diam * 0.78f, 0.04f, q);
            b.Octo("MetalBase", V(0, 0, 0.34f), diam * 0.7f, 0.08f, q);
            b.Cil("Amarillo", V(0, 0, 0.4f), 0.5f, 0.1f, q);
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f * Mathf.Deg2Rad;
                b.Esf("Amarillo", V(Mathf.Cos(a) * diam * 0.55f, Mathf.Sin(a) * diam * 0.55f, 0.32f), 0.1f);
            }
            b.Caja("Amarillo", V(0, -diam * 0.5f - 0.35f, 0.1f), V(diam * 0.8f, 0.12f, 0.05f));
        });
    }

    // =====================================================================
    // PLANTA ALTA — DORMITORIOS
    // =====================================================================
    void ConstruirDormitorios(Transform padre)
    {
        float y = PISO2;
        Prop(padre, "Techo_Vigas_Tuberias", V(0, y, 0), 0f, b =>
        {
            VigasTecho(b, -16f, -4f, -12f, 12f, 0f, true, 3.0f, "NeonAmarillo");
            b.Tubo("TuboCian", V(-15.6f, 4.2f, -11.6f), V(-15.6f, 4.2f, 11.6f), 0.22f);
            b.Tubo("TuboNaranja", V(-4.4f, 4.0f, -11.6f), V(-4.4f, 4.0f, -2.4f), 0.2f);
            for (float z = -10f; z <= 10f; z += 4f) b.CilZ("MetalOscuro", V(-15.6f, 4.2f, z), 0.34f, 0.12f);
            b.Caja("Amarillo", V(-10f, 0.012f, 0f), V(0.14f, 0.02f, 22f));
        });

        // Literas empotradas con compuertas corredizas
        string[] mantas = { "Azul", "TelaNaranja", "Tela", "Rojo" };
        for (int i = 0; i < 8; i++)
        {
            string tag = ((char)('A' + i)).ToString();
            LiteraPod(padre, V(-15.2f, y, -10.5f + i * 3.0f), 90f, mantas[i % 4], tag);
        }
        // Taquillas numeradas
        for (int i = 0; i < 10; i++) Taquilla(padre, V(-14.5f + i * 1.0f, y, 11.5f), i + 1);
        // Escotillas circulares en pared sur
        for (int i = 0; i < 3; i++) Escotilla(padre, V(-13.5f + i * 4.2f, y + 2.4f, -11.8f), 0f, 1.3f);
        // Maletas y detalles cotidianos
        Maleta(padre, V(-13.4f, y, -8.6f), 20f, "Rojo");
        Maleta(padre, V(-13.5f, y, -2.2f), -10f, "Azul");
        Maleta(padre, V(-13.3f, y, 5.6f), 35f, "TelaNaranja");
        Maleta(padre, V(-13.6f, y, 8.9f), 0f, "Tela");

        // Zona comun: alfombra, puffs, mesita
        Prop(padre, "Zona_Descanso", V(-9.5f, y, -4.5f), 0f, b =>
        {
            b.Cil("Tela", V(0, 0.02f, 0), 4.2f, 0.04f);
            b.Cil("NeonAmarillo", V(0, 0.045f, 0), 4.3f, 0.01f);
            b.Cil("MetalBase", V(0, 0.28f, 0), 0.9f, 0.56f);
            b.Cil("NeonCian", V(0, 0.58f, 0), 0.95f, 0.03f);
            b.Cil("Blanco", V(0.1f, 0.68f, 0), 0.14f, 0.16f);
            string[] pf = { "TelaNaranja", "Azul", "Rojo", "VegHoja" };
            for (int i = 0; i < 4; i++)
            {
                float a = i * 90f * Mathf.Deg2Rad + 0.4f;
                b.Esf(pf[i], V(Mathf.Cos(a) * 1.4f, 0.28f, Mathf.Sin(a) * 1.4f), V(0.8f, 0.5f, 0.8f));
            }
        });
        Prop(padre, "Escritorio_Dormitorio", V(-9.5f, y, 6.5f), 0f, b =>
        {
            b.Caja("MetalBase", V(0, 0.76f, 0), V(2.4f, 0.08f, 0.9f));
            b.Caja("MetalOscuro", V(-1.1f, 0.38f, 0), V(0.1f, 0.76f, 0.8f));
            b.Caja("MetalOscuro", V(1.1f, 0.38f, 0), V(0.1f, 0.76f, 0.8f));
            b.CajaQ("PantallaOscura", V(-0.5f, 1.1f, 0.25f), V(0.7f, 0.42f, 0.05f), Quaternion.Euler(0f, 12f, 0f));
            b.CajaQ("PantallaOscura", V(0.5f, 1.1f, 0.25f), V(0.7f, 0.42f, 0.05f), Quaternion.Euler(0f, -12f, 0f));
            b.Caja("NeonAmarillo", V(0, 0.74f, 0.46f), V(2.2f, 0.03f, 0.03f));
        });
        SillaSimple(padre, V(-9.5f, y, 5.5f), 0f, "TelaNaranja");
        PantallaPared(padre, V(-15.82f, y + 3.1f, 0f), 90f, 2.4f, 0.9f, "NeonAmarillo");

        // Iluminacion ambiental suave
        for (int i = 0; i < 4; i++)
            Luz(padre, "Luz_Ambiental_" + i, V(-12.5f, y + 3.4f, -9f + i * 6f), new Color(1f, 0.72f, 0.5f), 1.1f, 8f);
        Luz(padre, "Luz_Zona_Comun", V(-9.5f, y + 3.3f, -4.5f), Calida, 1.4f, 9f);
    }

    void LiteraPod(Transform padre, Vector3 pos, float ry, string manta, string tag)
    {
        Prop(padre, "Litera_" + tag, pos, ry, b =>
        {
            b.Caja("MetalOscuro", V(-1.3f, 1.7f, 0), V(0.12f, 3.4f, 1.6f));
            b.Caja("MetalOscuro", V(1.3f, 1.7f, 0), V(0.12f, 3.4f, 1.6f));
            b.Caja("MetalBase", V(0, 1.15f, 0), V(2.6f, 0.12f, 1.6f));
            b.Caja("MetalOscuro", V(0, 3.4f, 0), V(2.7f, 0.12f, 1.6f));
            b.Caja("MetalOscuro", V(0, 0.1f, 0), V(2.7f, 0.2f, 1.6f));
            b.Caja("MetalBase", V(0, 1.7f, -0.76f), V(2.5f, 3.2f, 0.08f));
            for (int k = 0; k < 2; k++)
            {
                float yb = k == 0 ? 0.3f : 1.55f;
                b.Caja("Blanco", V(0, yb + 0.05f, 0), V(2.4f, 0.18f, 1.3f));
                b.Caja(manta, V(0.3f, yb + 0.16f, 0), V(1.5f, 0.1f, 1.32f));
                b.Caja("Blanco", V(-0.9f, yb + 0.22f, 0), V(0.5f, 0.12f, 0.8f));
                b.Caja("NeonAmarillo", V(0, yb + 0.75f, 0.5f), V(2.2f, 0.03f, 0.05f));
                // compuerta corrediza con ventanilla y rieles amarillos
                float dx = k == 0 ? 0.55f : -0.7f;
                b.Caja("MetalBase", V(dx, yb + 0.35f, 0.8f), V(1.5f, 0.9f, 0.06f));
                b.Caja("Cristal", V(dx, yb + 0.45f, 0.84f), V(1.2f, 0.28f, 0.03f));
                b.Caja("Amarillo", V(0, yb + 0.85f, 0.84f), V(2.5f, 0.04f, 0.04f));
                b.Caja("Amarillo", V(0, yb - 0.05f, 0.84f), V(2.5f, 0.04f, 0.04f));
            }
            // pantalla de estado y placa
            b.Caja("PantallaOscura", V(1.0f, 1.3f, 0.86f), V(0.4f, 0.22f, 0.03f));
            b.Caja("NeonVerde", V(1.0f, 1.17f, 0.88f), V(0.3f, 0.03f, 0.02f));
            if (generarLetreros) b.Texto("CAP-" + tag, V(-0.9f, 1.3f, 0.9f), 180f, COL_TEXTO, 0.05f);
            // escalerilla
            b.Viga("MetalBase", V(1.5f, 0.2f, 0.6f), V(1.5f, 2.0f, 0.6f), 0.05f);
            for (int i = 0; i < 4; i++) b.Caja("MetalBase", V(1.5f, 0.4f + i * 0.45f, 0.6f), V(0.05f, 0.05f, 0.3f));
        });
    }

    void Taquilla(Transform padre, Vector3 pos, int num)
    {
        Prop(padre, "Taquilla_" + num.ToString("00"), pos, 180f, b =>
        {
            b.Caja("MetalBase", V(0, 1.1f, 0), V(0.95f, 2.2f, 0.6f));
            b.Caja(num % 2 == 0 ? "Azul" : "MetalOscuro", V(0, 1.1f, 0.31f), V(0.84f, 2.08f, 0.04f));
            for (int i = 0; i < 3; i++) b.Caja("MetalOscuro", V(0, 1.75f + i * 0.1f, 0.34f), V(0.5f, 0.03f, 0.02f));
            b.Caja("NeonCian", V(0.3f, 1.1f, 0.35f), V(0.05f, 0.25f, 0.04f));
            b.Caja("PantallaOscura", V(0, 2.0f, 0.34f), V(0.32f, 0.14f, 0.02f));
            if (generarLetreros) b.Texto(num.ToString("00"), V(0, 1.45f, 0.36f), 180f, Color.white, 0.07f);
        });
    }

    void Escotilla(Transform padre, Vector3 pos, float ry, float diam)
    {
        Prop(padre, "Escotilla_Circular", pos, ry, b =>
        {
            Quaternion q = Quaternion.Euler(90f, 0f, 0f);
            b.Cil("MetalOscuro", V(0, 0, 0.1f), diam + 0.3f, 0.2f, q);
            b.Cil("MetalBase", V(0, 0, 0.16f), diam, 0.2f, q);
            b.Cil("Cristal", V(0, 0, 0.22f), diam * 0.7f, 0.1f, q);
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f * Mathf.Deg2Rad;
                b.Esf("Amarillo", V(Mathf.Cos(a) * diam * 0.55f, Mathf.Sin(a) * diam * 0.55f, 0.24f), 0.09f);
            }
            b.Cil("NeonCian", V(0, 0, 0.12f), diam + 0.34f, 0.03f, q);
        });
    }

    void Maleta(Transform padre, Vector3 pos, float ry, string mat)
    {
        Prop(padre, "Maleta", pos, ry, b =>
        {
            b.Caja(mat, V(0, 0.35f, 0), V(0.6f, 0.6f, 0.25f));
            b.Caja("MetalOscuro", V(0, 0.68f, 0), V(0.3f, 0.06f, 0.06f));
            b.Caja("NeonAmarillo", V(0, 0.35f, 0.13f), V(0.5f, 0.04f, 0.01f));
            b.Cil("MetalOscuro", V(-0.22f, 0.04f, 0.0f), 0.08f, 0.06f, Quaternion.Euler(0f, 0f, 90f));
            b.Cil("MetalOscuro", V(0.22f, 0.04f, 0.0f), 0.08f, 0.06f, Quaternion.Euler(0f, 0f, 90f));
            b.Caja("Amarillo", V(0.15f, 0.5f, 0.13f), V(0.12f, 0.1f, 0.01f));
        });
    }

    // =====================================================================
    // PLANTA ALTA — LABORATORIO
    // =====================================================================
    void ConstruirLaboratorio(Transform padre)
    {
        float y = PISO2;
        Prop(padre, "Techo_Vigas_Tuberias", V(0, y, 0), 0f, b =>
        {
            VigasTecho(b, 4f, 16f, -12f, 12f, 0f, true, 3.0f, "NeonCian");
            b.Tubo("TuboCian", V(15.6f, 4.2f, -11.6f), V(15.6f, 4.2f, 11.6f), 0.22f);
            b.Tubo("TuboNaranja", V(4.4f, 4.0f, -11.6f), V(4.4f, 4.0f, -2.4f), 0.2f);
            b.Tubo("TuboAmarillo", V(4.4f, 3.7f, -11.6f), V(15.6f, 3.7f, -11.6f), 0.16f);
            for (float z = -10f; z <= 10f; z += 4f) b.CilZ("MetalOscuro", V(15.6f, 4.2f, z), 0.34f, 0.12f);
            for (float zz = -9f; zz <= 9f; zz += 0.4f) b.Caja("MetalOscuro", V(10f, 0.012f, zz), V(9f, 0.02f, 0.08f));
        });

        // Mesas de trabajo con brazos roboticos articulados
        MesaLab(padre, V(10.2f, y, -7.5f), 0f, 0);
        MesaLab(padre, V(10.2f, y, -2.8f), 0f, 1);
        MesaLab(padre, V(10.2f, y, 6.2f), 0f, 2);

        // Contenedores de muestras con liquidos brillantes (pared sur)
        string[] liq = { "NeonVerde", "NeonMagenta", "NeonCian", "NeonNaranja", "NeonVerde" };
        for (int i = 0; i < 5; i++)
        {
            string l = liq[i];
            GameObject g = Prop(padre, "Contenedor_Muestra_" + i, V(7f + i * 2.1f, y, -11.0f), 0f, b =>
            {
                b.Cil("MetalOscuro", V(0, 0.2f, 0), 1.1f, 0.4f);
                b.Cil(l, V(0, 1.2f, 0), 0.7f, 1.4f);
                b.Cil("Cristal", V(0, 1.2f, 0), 0.9f, 1.8f);
                b.Cil("MetalOscuro", V(0, 2.2f, 0), 1.1f, 0.2f);
                b.Cil("Amarillo", V(0, 2.35f, 0), 0.4f, 0.1f);
                for (int k = 0; k < 6; k++) b.Esf("NeonBlanco", V(R(-0.2f, 0.2f), R(0.5f, 1.8f), R(-0.2f, 0.2f)), 0.05f);
            });
            Luz(g.transform, "Luz_Muestra", V(0, 1.3f, 0.6f), i % 2 == 0 ? new Color(0.3f, 1f, 0.5f) : new Color(1f, 0.3f, 0.9f), 0.8f, 3.5f, true);
        }

        // Servidores con luces parpadeantes (pared oeste del laboratorio)
        for (int i = 0; i < 5; i++) Rack(padre, V(4.6f, y, -10.4f + i * 1.4f), 90f);

        // Escritorios de analisis + monitores
        Escritorio(padre, V(7.2f, y, 8.8f), 180f, "NeonCian");
        Escritorio(padre, V(11.0f, y, 8.8f), 180f, "NeonVerde");
        PantallaPared(padre, V(15.82f, y + 2.8f, -2f), -90f, 2.6f, 1.2f, "NeonCian");
        PantallaPared(padre, V(15.82f, y + 2.8f, 1.8f), -90f, 2.6f, 1.2f, "NeonNaranja");
        PantallaPared(padre, V(15.82f, y + 2.8f, 5.6f), -90f, 2.6f, 1.2f, "NeonVerde");

        // Helice de ADN holografica
        Prop(padre, "Pedestal_ADN", V(14f, y, 9.4f), 0f, b =>
        {
            b.Cil("MetalOscuro", V(0, 0.15f, 0), 1.2f, 0.3f);
            b.Cil("NeonMagenta", V(0, 0.32f, 0), 0.9f, 0.03f);
        });
        GameObject adn = Prop(padre, "Helice_ADN", V(14f, y + 1.6f, 9.4f), 0f, b =>
        {
            for (int i = 0; i < 16; i++)
            {
                float t = i * 0.4f, yy = (i - 8) * 0.14f;
                Vector3 p1 = V(Mathf.Cos(t) * 0.35f, yy, Mathf.Sin(t) * 0.35f), p2 = V(-p1.x, yy, -p1.z);
                b.Esf("NeonCian", p1, 0.09f); b.Esf("NeonMagenta", p2, 0.09f);
                b.Tubo("NeonBlanco", p1, p2, 0.02f);
            }
        });
        Anim(adn, EstacionVida.Tipo.Girar, 40f, 0f, V(0, 1, 0));

        Luz(padre, "Luz_Lab_Fria_A", V(10f, y + 3.6f, -5f), new Color(0.85f, 0.97f, 1f), 2.0f, 12f);
        Luz(padre, "Luz_Lab_Fria_B", V(10f, y + 3.6f, 5f), new Color(0.85f, 0.97f, 1f), 2.0f, 12f);
        Luz(padre, "Luz_Lab_Servidores", V(5.5f, y + 2.8f, -7.5f), new Color(0.3f, 0.9f, 1f), 1.2f, 6f, true);
    }

    void MesaLab(Transform padre, Vector3 pos, float ry, int variante)
    {
        string[] neon = { "NeonCian", "NeonVerde", "NeonNaranja" };
        Prop(padre, "Mesa_Laboratorio_" + variante, pos, ry, b =>
        {
            b.Caja("Blanco", V(0, 0.45f, 0), V(3.4f, 0.9f, 1.3f));
            b.Caja("MetalOscuro", V(0, 0.92f, 0), V(3.46f, 0.06f, 1.36f));
            b.Caja(neon[variante], V(0, 0.55f, 0.66f), V(3.2f, 0.04f, 0.03f));
            b.Caja("Amarillo", V(0, 0.15f, 0.66f), V(3.4f, 0.08f, 0.02f));
            // microscopio
            b.Caja("MetalBase", V(1.2f, 0.99f, -0.3f), V(0.28f, 0.08f, 0.32f));
            b.Caja("Blanco", V(1.2f, 1.18f, -0.4f), V(0.1f, 0.34f, 0.1f));
            b.Cil("MetalOscuro", V(1.2f, 1.24f, -0.28f), 0.08f, 0.14f, Quaternion.Euler(25f, 0f, 0f));
            // tubos de ensayo
            string[] tb = { "NeonVerde", "NeonCian", "NeonMagenta", "NeonAmarillo" };
            b.Caja("MetalBase", V(0.2f, 1.0f, 0.4f), V(0.68f, 0.08f, 0.16f));
            for (int i = 0; i < 4; i++) b.Cil(tb[i], V(-0.04f + i * 0.14f, 1.12f, 0.4f), 0.07f, 0.16f);
            b.Esf("NeonBlanco", V(-1.2f, 1.1f, 0.3f), 0.14f);
            b.Cil("Cristal", V(-1.2f, 1.1f, 0.3f), 0.26f, 0.3f);

            // brazo robotico articulado
            Vector3 b0 = V(-0.3f, 0.95f, -0.1f);
            Vector3 hombro = b0 + V(0, 0.45f, 0);
            float a1 = 25f + variante * 18f, a2 = -50f + variante * 25f;
            Vector3 codo = hombro + Quaternion.Euler(0f, 0f, -a1) * V(0, 0.8f, 0);
            Vector3 muneca = codo + Quaternion.Euler(0f, 0f, -a1 - a2) * V(0, 0.7f, 0);
            b.Cil("MetalOscuro", b0 + V(0, 0.06f, 0), 0.5f, 0.12f);
            b.Cil("MetalBase", b0 + V(0, 0.28f, 0), 0.24f, 0.36f);
            b.Esf("NeonCian", hombro, 0.22f);
            b.Tubo("Amarillo", hombro, codo, 0.13f);
            b.Esf(neon[variante], codo, 0.2f);
            b.Tubo("MetalBase", codo, muneca, 0.1f);
            b.Esf("NeonCian", muneca, 0.14f);
            Vector3 dirG = (muneca - codo).normalized;
            Vector3 lado = Vector3.Cross(dirG, V(0, 0, 1)).normalized;
            b.Tubo("MetalOscuro", muneca + lado * 0.07f, muneca + lado * 0.07f + dirG * 0.22f - lado * 0.03f, 0.04f);
            b.Tubo("MetalOscuro", muneca - lado * 0.07f, muneca - lado * 0.07f + dirG * 0.22f + lado * 0.03f, 0.04f);
        });
    }

    void Rack(Transform padre, Vector3 pos, float ry)
    {
        GameObject g = Prop(padre, "Rack_Servidor", pos, ry, b =>
        {
            b.Caja("MetalOscuro", V(0, 1.25f, 0), V(1.1f, 2.5f, 0.9f));
            for (int i = 0; i < 6; i++) b.Caja("MetalBase", V(0, 0.38f + i * 0.35f, 0.46f), V(0.92f, 0.24f, 0.04f));
            b.Caja("Amarillo", V(0, 2.52f, 0), V(1.14f, 0.05f, 0.94f));
        });
        // LEDs parpadeantes en dos grupos con fases distintas
        for (int gp = 0; gp < 2; gp++)
        {
            int grupo = gp;
            GameObject leds = Prop(g.transform, "Leds_" + gp, Vector3.zero, 0f, b =>
            {
                for (int i = 0; i < 6; i++)
                {
                    if (i % 2 != grupo) continue;
                    b.Caja(i % 3 == 0 ? "NeonVerde" : (i % 3 == 1 ? "NeonCian" : "NeonNaranja"), V(0.32f, 0.38f + i * 0.35f, 0.5f), V(0.16f, 0.05f, 0.02f));
                    b.Caja("NeonVerde", V(-0.3f, 0.38f + i * 0.35f, 0.5f), V(0.06f, 0.06f, 0.02f));
                }
            });
            Anim(leds, EstacionVida.Tipo.Parpadeo, R(1.5f, 3.5f));
        }
    }

    void Escritorio(Transform padre, Vector3 pos, float ry, string neon)
    {
        Prop(padre, "Escritorio_Analisis", pos, ry, b =>
        {
            b.Caja("MetalBase", V(0, 0.76f, 0), V(1.9f, 0.08f, 0.9f));
            b.Caja("MetalOscuro", V(-0.85f, 0.38f, 0), V(0.1f, 0.76f, 0.8f));
            b.Caja("MetalOscuro", V(0.85f, 0.38f, 0), V(0.1f, 0.76f, 0.8f));
            b.CajaQ("PantallaOscura", V(-0.4f, 1.1f, 0.25f), V(0.7f, 0.42f, 0.05f), Quaternion.Euler(0f, 12f, 0f));
            b.CajaQ("PantallaOscura", V(0.4f, 1.1f, 0.25f), V(0.7f, 0.42f, 0.05f), Quaternion.Euler(0f, -12f, 0f));
            b.Caja(neon, V(0, 0.74f, 0.46f), V(1.7f, 0.03f, 0.03f));
            b.Caja("MetalOscuro", V(0, 0.82f, -0.15f), V(0.55f, 0.03f, 0.2f));
        });
        SillaSimple(padre, pos + Quaternion.Euler(0f, ry, 0f) * V(0, 0, -0.95f), ry, "Azul");
    }

    // =====================================================================
    // PLANTA ALTA — MIRADOR ASTRONOMICO
    // =====================================================================
    void ConstruirMirador(Transform padre)
    {
        float y = PISO2;
        Prop(padre, "Techo_Detalles", V(0, y, 0), 0f, b =>
        {
            VigasTecho(b, -4f, 4f, -12f, -2f, 0f, true, 2.5f, "NeonMagenta");
            b.Tubo("TuboCian", V(-3.7f, 4.1f, -11.6f), V(-3.7f, 4.1f, -2.4f), 0.2f);
            b.Tubo("TuboNaranja", V(3.7f, 4.1f, -11.6f), V(3.7f, 4.1f, -2.4f), 0.2f);
        });
        Prop(padre, "Telescopio", V(0, y, -9.2f), 0f, b =>
        {
            b.Viga("MetalOscuro", V(0, 1.4f, 0), V(-0.7f, 0, -0.5f), 0.07f);
            b.Viga("MetalOscuro", V(0, 1.4f, 0), V(0.7f, 0, -0.5f), 0.07f);
            b.Viga("MetalOscuro", V(0, 1.4f, 0), V(0, 0, 0.7f), 0.07f);
            b.Esf("MetalBase", V(0, 1.45f, 0), 0.22f);
            Quaternion q = Quaternion.Euler(-30f, 0f, 0f);
            b.Cil("Blanco", V(0, 1.6f, 0.35f), 0.3f, 1.4f, Quaternion.Euler(60f, 0f, 0f));
            b.Cil("Amarillo", V(0, 1.98f, 0.86f), 0.34f, 0.12f, Quaternion.Euler(60f, 0f, 0f));
            b.Cil("NeonCian", V(0, 2.05f, 0.98f), 0.26f, 0.03f, Quaternion.Euler(60f, 0f, 0f));
        });
        Prop(padre, "Sofa_Mirador_A", V(-2.4f, y, -5f), 90f, b => SofaGeom(b, 2.6f));
        Prop(padre, "Sofa_Mirador_B", V(2.4f, y, -5f), -90f, b => SofaGeom(b, 2.6f));
        Prop(padre, "Mesa_Baja", V(0, y, -5f), 0f, b =>
        {
            b.Cil("MetalBase", V(0, 0.25f, 0), 1.0f, 0.5f);
            b.Cil("NeonMagenta", V(0, 0.52f, 0), 1.05f, 0.03f);
            b.Cil("Amarillo", V(0.2f, 0.6f, 0.1f), 0.12f, 0.12f);
            b.Cil("Blanco", V(-0.2f, 0.6f, -0.1f), 0.12f, 0.12f);
        });
        PantallaPared(padre, V(-3.82f, y + 2.5f, -8f), 90f, 2.4f, 1.1f, "NeonMagenta");
        PantallaPared(padre, V(3.82f, y + 2.5f, -8f), -90f, 2.4f, 1.1f, "NeonCian");
        Luz(padre, "Luz_Mirador", V(0, y + 3.6f, -7f), new Color(0.7f, 0.6f, 1f), 1.8f, 10f, true);
    }

    void SofaGeom(Malla b, float largo)
    {
        b.Caja("Blanco", V(0, 0.22f, 0), V(largo, 0.44f, 0.9f));
        b.Caja("Tela", V(0, 0.48f, 0.04f), V(largo - 0.16f, 0.14f, 0.78f));
        b.Caja("Tela", V(0, 0.78f, -0.36f), V(largo, 0.68f, 0.22f));
        b.Caja("NeonMagenta", V(0, 0.2f, 0.46f), V(largo - 0.2f, 0.03f, 0.02f));
    }

    // =====================================================================
    // PLANTA ALTA — PASARELA SUPERIOR EN T
    // =====================================================================
    void ConstruirPasarela(Transform padre)
    {
        float y = PISO2;
        Prop(padre, "Pasarela_Detalles", Vector3.zero, 0f, b =>
        {
            // rejilla y neon en bordes
            b.Caja("NeonCian", V(0, y + 0.012f, 0.9f), V(8f, 0.02f, 0.06f));
            b.Caja("NeonCian", V(0, y + 0.012f, 3.1f), V(8f, 0.02f, 0.06f));
            for (float x = -3.8f; x <= 3.9f; x += 0.3f) b.Caja("MetalOscuro", V(x, y + 0.012f, 2f), V(0.08f, 0.02f, 2.2f));
            b.Caja("NeonMagenta", V(-1.15f, y + 0.012f, -0.6f), V(0.06f, 0.02f, 2.8f));
            b.Caja("NeonMagenta", V(1.15f, y + 0.012f, -0.6f), V(0.06f, 0.02f, 2.8f));
            // barandales de seguridad amarillos
            Baranda(b, V(-4f, y, 0.8f), V(-1.2f, y, 0.8f), 1.05f);
            Baranda(b, V(1.2f, y, 0.8f), V(4f, y, 0.8f), 1.05f);
            Baranda(b, V(-4f, y, 3.2f), V(0.8f, y, 3.2f), 1.05f);
            Baranda(b, V(3.6f, y, 3.2f), V(4f, y, 3.2f), 1.05f);
            Baranda(b, V(-1.2f, y, 0.8f), V(-1.2f, y, -2f), 1.05f);
            Baranda(b, V(1.2f, y, 0.8f), V(1.2f, y, -2f), 1.05f);
            // soportes estructurales bajo la pasarela
            for (int sx = -1; sx <= 1; sx += 2)
            {
                b.Viga("MetalOscuro", V(sx * 3.9f, y - 0.3f, 2f), V(sx * 1.0f, y - 2.4f, 2f), 0.2f);
                b.Caja("MetalOscuro", V(sx * 2.2f, y - 0.4f, 2f), V(0.25f, 0.2f, 2.4f));
            }
            b.Caja("MetalOscuro", V(0, y - 0.4f, -0.6f), V(2.4f, 0.2f, 0.3f));
            b.Tubo("MetalBase", V(0, y - 0.4f, -1.9f), V(0, y - 2.6f, -1.9f), 0.25f);
            b.Cil("Amarillo", V(0, y - 0.5f, 2f), 0.5f, 0.06f);
        });
        Luz(padre, "Luz_Pasarela_N", V(-2f, y + 2.8f, 2f), Fria, 1.6f, 8f);
        Luz(padre, "Luz_Pasarela_S", V(2f, y + 2.8f, 2f), Fria, 1.6f, 8f);
    }

    // =====================================================================
    // EXTERIOR ESPACIAL: planeta, nebulosas, estrellas, casco
    // =====================================================================
    void ConstruirExterior(Transform padre)
    {
        Malla b = new Malla();
        Vector3 cp = V(-14f, 14f, -110f);
        b.Esf("Planeta", cp, 70f);
        Quaternion qa = Quaternion.Euler(16f, 0f, -20f);
        for (int i = 0; i < 56; i++)
        {
            float a = i * Mathf.PI * 2f / 56f;
            Vector3 loc = V(Mathf.Cos(a) * 47f, 0f, Mathf.Sin(a) * 47f);
            Quaternion qt = qa * Quaternion.Euler(0f, -a * Mathf.Rad2Deg + 90f, 0f);
            b.CajaQ("AnilloPlaneta", cp + qa * loc, V(9f, 0.3f, 5.6f), qt);
        }
        b.Esf("Blanco", V(34f, 24f, -85f), 9f);

        // Nebulosas translucidas
        b.Esf("Nebulosa1", V(60f, 22f, -150f), 90f);
        b.Esf("Nebulosa1", V(75f, 40f, -140f), 55f);
        b.Esf("Nebulosa2", V(-70f, 30f, -140f), 80f);
        b.Esf("Nebulosa2", V(-45f, 50f, -160f), 60f);

        // Estrellas
        for (int i = 0; i < 170; i++)
        {
            double th = rng.NextDouble() * System.Math.PI * 2.0;
            double ph = System.Math.Acos(2.0 * rng.NextDouble() - 1.0);
            float rad = R(120f, 170f);
            Vector3 p = V((float)(rad * System.Math.Sin(ph) * System.Math.Cos(th)), (float)(rad * System.Math.Cos(ph)), (float)(rad * System.Math.Sin(ph) * System.Math.Sin(th)));
            if (p.y < -20f) p.y = -p.y;
            string m = (i % 9 == 0) ? "NeonCian" : (i % 13 == 0 ? "NeonAmarillo" : "NeonBlanco");
            b.Esf(m, p, R(0.35f, 0.9f));
        }
        b.Construir("Planeta_Nebulosas_Estrellas", padre, Vector3.zero);

        // Casco exterior
        Prop(padre, "Casco_Exterior", Vector3.zero, 0f, h =>
        {
            float yy = PISO2 - 0.1f;
            h.Caja("NeonCian", V(0, yy, -12.25f), V(32.6f, 0.08f, 0.06f));
            h.Caja("NeonCian", V(0, yy, 12.25f), V(32.6f, 0.08f, 0.06f));
            h.Caja("NeonCian", V(-16.25f, yy, 0), V(0.06f, 0.08f, 24.6f));
            h.Caja("NeonCian", V(16.25f, yy, 0), V(0.06f, 0.08f, 24.6f));
            h.Caja("MetalOscuro", V(0, yy, -12.3f), V(32.8f, 0.4f, 0.2f));
            h.Caja("MetalOscuro", V(0, yy, 12.3f), V(32.8f, 0.4f, 0.2f));
            h.Caja("MetalOscuro", V(-16.3f, yy, 0), V(0.2f, 0.4f, 24.8f));
            h.Caja("MetalOscuro", V(16.3f, yy, 0), V(0.2f, 0.4f, 24.8f));
            // antena y plato en el techo
            h.Cil("MetalBase", V(11f, 11f, -7f), 0.22f, 2.4f);
            h.Esf("NeonNaranja", V(11f, 12.3f, -7f), 0.38f);
            h.Esf("Blanco", V(8f, 10.4f, 6f), V(2.2f, 0.5f, 2.2f), Quaternion.Euler(-30f, 0f, 0f));
            h.Cil("MetalOscuro", V(8f, 10.2f, 6f), 0.3f, 0.5f);
            // paneles solares laterales
            for (int sg = -1; sg <= 1; sg += 2)
            {
                h.Caja("MetalOscuro", V(sg * 21f, PISO2, 0), V(10f, 0.35f, 0.35f));
                for (int k = -1; k <= 1; k += 2)
                {
                    h.Caja("Azul", V(sg * 23f, PISO2, k * 5f), V(5.5f, 0.08f, 8f));
                    h.Caja("NeonCian", V(sg * 23f, PISO2 + 0.05f, k * 5f), V(0.06f, 0.02f, 8f));
                    h.Caja("MetalOscuro", V(sg * 23f, PISO2 + 0.05f, k * 5f), V(5.6f, 0.02f, 0.06f));
                }
            }
        });
    }

    // =====================================================================
    // ILUMINACION GLOBAL: luz fria direccional exterior + ambiente espacial
    // =====================================================================
    void ConstruirIluminacionGlobal(Transform padre)
    {
        GameObject g = new GameObject("Sol_Estelar_Frio");
        g.transform.SetParent(padre, false);
        g.transform.rotation = Quaternion.Euler(35f, 15f, 0f);
        Light l = g.AddComponent<Light>();
        l.type = LightType.Directional;
        l.color = new Color(0.62f, 0.76f, 1f);
        l.intensity = 1.1f;
        l.shadows = LightShadows.Soft;

        GameObject r = new GameObject("Luz_Relleno_Planeta");
        r.transform.SetParent(padre, false);
        r.transform.rotation = Quaternion.Euler(-20f, 200f, 0f);
        Light l2 = r.AddComponent<Light>();
        l2.type = LightType.Directional;
        l2.color = new Color(0.9f, 0.55f, 0.95f);
        l2.intensity = 0.25f;
        l2.shadows = LightShadows.None;

        if (configurarAmbienteEspacial)
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.10f, 0.12f, 0.20f);
            RenderSettings.fog = false;
        }
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(GeneradorEstacionEspacialToon))]
public class GeneradorEstacionEspacialToonEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        GeneradorEstacionEspacialToon gen = (GeneradorEstacionEspacialToon)target;
        GUILayout.Space(12);
        GUI.backgroundColor = new Color(0.35f, 0.85f, 1f);
        if (GUILayout.Button("Generar Estación Espacial (2 Pisos)", GUILayout.Height(36))) gen.GenerarEstacion();
        GUI.backgroundColor = new Color(1f, 0.55f, 0.55f);
        if (GUILayout.Button("Limpiar Estación", GUILayout.Height(26))) gen.LimpiarEstacion();
        GUI.backgroundColor = Color.white;
    }
}
#endif
