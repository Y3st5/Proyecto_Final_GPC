using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

// Poner en Assets/Editor/. Requiere VillaVida.cs (fuera de Editor) para las animaciones.
public class GeneradorVillaToon : EditorWindow
{
    // ===================== AJUSTES =====================
    const bool TERRENO_FACETADO = false;   // false = normales suaves (mejor para toon). true = look low poly
    const bool REINICIAR_COLORES = true;   // true: al regenerar vuelve a pintar los colores base (NO toca tus shaders)
    const int SEMILLA = 42;
    const int CANT_PASTO = 2600;
    const int CANT_ARBOLES_BOSQUE = 46;
    const int CANT_PARCHES_FLORES = 40;

    const float NIVEL_AGUA = -0.30f;
    const float MEDIO_RIO = 5.6f;
    const float RAMPA_A = 19.4f, RAMPA_B = 22.0f;           // acantilado de la cascada
    const float ALT_BAJA = -1.4f, ALT_MESETA = 9.4f, ALT_LIP = 8.7f;

    // ===================== MATERIALES =====================
    static Material mTerreno, mPasto, mPastoClaro, mCamino, mCaminoOscuro, mRoca, mLecho;
    static Material mRio, mCascada, mEspuma;
    static Material mTronco, mHojas, mHojasClaras, mHojasRosa, mPino, mArbusto;
    static Material[] mParedes = new Material[3];
    static Material[] mTechos = new Material[3];
    static Material[] mTejas = new Material[3];
    static Material mMadera, mMaderaClara, mMaderaOsc, mPuerta, mPiedra, mPiedraClara, mLuz, mHumo;
    static Material mNenufar, mNube, mToldoA, mToldoB;
    static Material[] mFlores = new Material[4];

    // ===================== MALLAS CACHEADAS =====================
    static Mesh meshCubo, meshCilindro, meshEsfera;
    static Mesh[] poolBlobs, poolRocas;
    static Mesh meshTronco, meshCono8, meshPiramide, meshBarril, meshPozo, meshPasto;
    static Mesh meshTorre, meshTechoMolino, meshBaseMolino;

    // x, z, radio
    static List<Vector3> ocupados = new List<Vector3>();

    // ===================== CONSTRUCTOR DE MALLAS =====================
    // Acumula cajas/mallas y las fusiona en UNA malla por material (jerarquia limpia, ideal para shaders).
    class ConstructorMalla
    {
        readonly Dictionary<Material, List<CombineInstance>> partes = new Dictionary<Material, List<CombineInstance>>();
        readonly List<Material> orden = new List<Material>();

        public void Malla(Material mat, Mesh mesh, Vector3 pos, Quaternion rot, Vector3 scale)
        {
            List<CombineInstance> lista;
            if (!partes.TryGetValue(mat, out lista))
            {
                lista = new List<CombineInstance>();
                partes[mat] = lista;
                orden.Add(mat);
            }
            CombineInstance ci = new CombineInstance();
            ci.mesh = mesh;
            ci.transform = Matrix4x4.TRS(pos, rot, scale);
            lista.Add(ci);
        }

        public void Caja(Material mat, Vector3 pos, Vector3 scale, Quaternion? rot = null)
        {
            Malla(mat, meshCubo, pos, rot ?? Quaternion.identity, scale);
        }

        public void Cilindro(Material mat, Vector3 pos, float diamX, float alto, float diamZ, Quaternion? rot = null)
        {
            Malla(mat, meshCilindro, pos, rot ?? Quaternion.identity, new Vector3(diamX, alto * 0.5f, diamZ));
        }

        public void Esfera(Material mat, Vector3 pos, float diametro)
        {
            Malla(mat, meshEsfera, pos, Quaternion.identity, Vector3.one * diametro);
        }

        public void Viga(Material mat, Vector3 a, Vector3 b, float grosor)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 0.001f) return;
            Malla(mat, meshCubo, (a + b) * 0.5f, Quaternion.LookRotation(d), new Vector3(grosor, grosor, len));
        }

        public GameObject Construir(string nombre, Transform parent, Vector3 localPos, Quaternion localRot, Vector3 localScale)
        {
            GameObject go = new GameObject(nombre);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale = localScale;
            foreach (Material mat in orden)
            {
                Mesh m = new Mesh();
                m.name = nombre + "_" + mat.name;
                m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                m.CombineMeshes(partes[mat].ToArray(), true, true);
                m.RecalculateBounds();
                GameObject hijo = new GameObject(mat.name.Replace("Mat_", ""));
                hijo.transform.SetParent(go.transform, false);
                hijo.AddComponent<MeshFilter>().sharedMesh = m;
                hijo.AddComponent<MeshRenderer>().sharedMaterial = mat;
            }
            return go;
        }
    }

    // ===================== GENERADOR PRINCIPAL =====================
    [MenuItem("Tools/Generar Villa Toon")]
    public static void GenerarVilla()
    {
        GameObject existente = GameObject.Find("Villa_Toon_Generada");
        if (existente != null)
            Undo.DestroyObjectImmediate(existente);

        AsegurarCarpeta("Assets", "VillaGenerada");
        AsegurarCarpeta("Assets/VillaGenerada", "Materials");

        CrearMateriales();
        PrepararMallas();
        ocupados.Clear();
        Random.InitState(SEMILLA);

        GameObject root = new GameObject("Villa_Toon_Generada");
        Undo.RegisterCreatedObjectUndo(root, "Generar Villa Toon");

        Transform gTerreno = CrearGrupo("01_Terreno_Y_Camino", root.transform);
        Transform gAgua = CrearGrupo("02_Agua_Rio_Y_Cascada", root.transform);
        Transform gPuente = CrearGrupo("03_Puente", root.transform);
        Transform gCasas = CrearGrupo("04_Casas_Y_Molino", root.transform);
        Transform gProps = CrearGrupo("05_Props_Aldea", root.transform);
        Transform gArboles = CrearGrupo("06_Arboles_Y_Arbustos", root.transform);
        Transform gRocas = CrearGrupo("07_Rocas", root.transform);
        Transform gPasto = CrearGrupo("08_Pasto_Y_Flores", root.transform);
        Transform gFaroles = CrearGrupo("09_Faroles", root.transform);
        Transform gCielo = CrearGrupo("10_Nubes", root.transform);

        // ---- 1. Terreno y camino empedrado ----
        CrearTerreno(gTerreno);
        CrearCamino(gTerreno);

        // ---- 2. Agua ----
        CrearAgua(gAgua);

        // ---- 3. Puente ----
        CrearPuente(gPuente);

        // ---- 4. Casas (miran hacia el camino) ----
        //          nombre     x      z     esc  pisos var  anexo  humo  cerca
        CrearCasa("Casa_01", -12.5f, 7.0f, 1.05f, 1, 0, true, true, true, gCasas);
        CrearCasa("Casa_02", -14.5f, -7.5f, 1.00f, 2, 1, false, true, false, gCasas);
        CrearCasa("Casa_03", 12.5f, 7.0f, 1.00f, 1, 2, false, true, true, gCasas);
        CrearCasa("Casa_04", 14.5f, -7.5f, 1.10f, 2, 0, true, true, false, gCasas);
        CrearCasa("Casa_05", -21.0f, 8.5f, 0.95f, 1, 1, false, false, true, gCasas);
        CrearCasa("Casa_06", 20.5f, 9.0f, 0.95f, 1, 2, true, true, false, gCasas);
        CrearMolino(-19.5f, -15.5f, gCasas);

        // ---- 5. Props de aldea ----
        CrearPozo(new Vector3(9.2f, 0f, -3.8f), gProps);
        CrearPuesto(new Vector3(-9.0f, 0f, -4.2f), gProps);
        CrearBanco(new Vector3(7.8f, 0f, -1.9f), 0f, gProps);
        CrearBanco(new Vector3(-8.0f, 0f, 2.4f), 180f, gProps);
        CrearCartel(new Vector3(7.6f, 0f, 3.4f), -35f, gProps);
        CrearBarriles(new Vector3(9.4f, 0f, 4.0f), gProps);
        CrearBarriles(new Vector3(-11.4f, 0f, -4.2f), gProps);

        // ---- 6. Rocas ----
        CrearRocas(gRocas);

        // ---- 7. Arboles y arbustos ----
        CrearArboles(gArboles);
        CrearArbustos(gArboles);

        // ---- 8. Pasto, juncos, flores, nenufares ----
        CrearPastoYFlores(gPasto);

        // ---- 9. Faroles ----
        Vector2[] posFaroles = {
            new Vector2(-6.9f, 2.7f), new Vector2(-6.9f, -2.7f),
            new Vector2(6.9f, 2.7f),  new Vector2(6.9f, -2.7f),
            new Vector2(-13.5f, PathZ(-13.5f) + 2.3f), new Vector2(13.5f, PathZ(13.5f) - 2.3f),
            new Vector2(-20.5f, PathZ(-20.5f) - 2.3f), new Vector2(20.5f, PathZ(20.5f) + 2.3f)
        };
        for (int i = 0; i < posFaroles.Length; i++)
        {
            Vector2 p = posFaroles[i];
            CrearFarol($"Farol_{i + 1:00}", new Vector3(p.x, EvaluarAlturaTerreno(p.x, p.y), p.y), gFaroles);
        }

        // ---- 10. Nubes ----
        CrearNubes(gCielo);

        AssetDatabase.SaveAssets();
        Selection.activeGameObject = root;
        Debug.Log("Villa Toon generada. Materiales en Assets/VillaGenerada/Materials/ (asigna tu toon shader a cada uno).");
    }

    // ===================== MATERIALES =====================
    static Color C(float r, float g, float b) { return new Color(r, g, b, 1f); }

    static void CrearMateriales()
    {
        mTerreno = Mat("Mat_Terreno", C(0.36f, 0.68f, 0.28f));
        mPasto = Mat("Mat_Pasto_Detalle", C(0.26f, 0.62f, 0.22f));
        mPastoClaro = Mat("Mat_Pasto_Claro", C(0.58f, 0.82f, 0.30f));
        mCamino = Mat("Mat_Camino", C(0.78f, 0.66f, 0.48f));
        mCaminoOscuro = Mat("Mat_Camino_Oscuro", C(0.62f, 0.50f, 0.36f));
        mRoca = Mat("Mat_Roca", C(0.52f, 0.55f, 0.60f));
        mLecho = Mat("Mat_Lecho_Rio", C(0.60f, 0.52f, 0.38f));

        mRio = Mat("Mat_Agua_Rio", C(0.15f, 0.58f, 0.85f));
        mCascada = Mat("Mat_Agua_Cascada", C(0.55f, 0.86f, 0.98f));
        mEspuma = Mat("Mat_Espuma", C(0.95f, 0.99f, 1.00f));

        mTronco = Mat("Mat_Tronco", C(0.45f, 0.28f, 0.16f));
        mHojas = Mat("Mat_Hojas", C(0.20f, 0.58f, 0.25f));
        mHojasClaras = Mat("Mat_Hojas_Claras", C(0.42f, 0.74f, 0.27f));
        mHojasRosa = Mat("Mat_Hojas_Rosa", C(0.96f, 0.66f, 0.76f));
        mPino = Mat("Mat_Pino", C(0.12f, 0.46f, 0.34f));
        mArbusto = Mat("Mat_Arbusto", C(0.28f, 0.66f, 0.30f));

        mParedes[0] = Mat("Mat_Pared_Crema", C(0.96f, 0.90f, 0.76f));
        mParedes[1] = Mat("Mat_Pared_Celeste", C(0.74f, 0.86f, 0.92f));
        mParedes[2] = Mat("Mat_Pared_Durazno", C(0.97f, 0.78f, 0.66f));

        Color[] ct = { C(0.80f, 0.27f, 0.22f), C(0.25f, 0.48f, 0.72f), C(0.93f, 0.56f, 0.20f) };
        string[] nt = { "Rojo", "Azul", "Naranja" };
        for (int i = 0; i < 3; i++)
        {
            mTechos[i] = Mat("Mat_Techo_" + nt[i], ct[i]);
            Color d = ct[i] * 0.72f; d.a = 1f;
            mTejas[i] = Mat("Mat_Teja_" + nt[i], d);
        }

        mMadera = Mat("Mat_Madera", C(0.50f, 0.32f, 0.18f));
        mMaderaClara = Mat("Mat_Madera_Clara", C(0.80f, 0.60f, 0.38f));
        mMaderaOsc = Mat("Mat_Madera_Oscura", C(0.30f, 0.19f, 0.11f));
        mPuerta = Mat("Mat_Puerta_Postigos", C(0.25f, 0.52f, 0.58f));
        mPiedra = Mat("Mat_Piedra", C(0.68f, 0.68f, 0.72f));
        mPiedraClara = Mat("Mat_Piedra_Clara", C(0.82f, 0.82f, 0.84f));
        mLuz = Mat("Mat_Luz_Farol", C(1.00f, 0.84f, 0.42f), true);
        mHumo = Mat("Mat_Humo", C(0.93f, 0.93f, 0.96f));
        mNenufar = Mat("Mat_Nenufar", C(0.22f, 0.62f, 0.30f));
        mNube = Mat("Mat_Nube", C(1f, 1f, 1f));
        mToldoA = Mat("Mat_Toldo_Rojo", C(0.92f, 0.28f, 0.28f));
        mToldoB = Mat("Mat_Toldo_Crema", C(0.98f, 0.95f, 0.88f));

        mFlores[0] = Mat("Mat_Flor_Roja", C(0.93f, 0.25f, 0.25f));
        mFlores[1] = Mat("Mat_Flor_Amarilla", C(1.00f, 0.85f, 0.25f));
        mFlores[2] = Mat("Mat_Flor_Rosa", C(0.95f, 0.50f, 0.75f));
        mFlores[3] = Mat("Mat_Flor_Blanca", C(0.97f, 0.97f, 1.00f));
    }

    static Material Mat(string nombre, Color color, bool emisivo = false)
    {
        string ruta = $"Assets/VillaGenerada/Materials/{nombre}.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(ruta);
        bool nuevo = mat == null;
        if (nuevo)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            mat = new Material(sh);
            AssetDatabase.CreateAsset(mat, ruta);
        }
        if (nuevo || REINICIAR_COLORES)
        {
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            if (mat.HasProperty("_MainColor")) mat.SetColor("_MainColor", color);
            if (emisivo && mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", color * 2.5f);
            }
            EditorUtility.SetDirty(mat);
        }
        return mat;
    }

    // ===================== MALLAS BASE =====================
    static Mesh MeshPrim(PrimitiveType tipo)
    {
        GameObject tmp = GameObject.CreatePrimitive(tipo);
        Mesh m = tmp.GetComponent<MeshFilter>().sharedMesh;
        DestroyImmediate(tmp);
        return m;
    }

    static void PrepararMallas()
    {
        meshCubo = MeshPrim(PrimitiveType.Cube);
        meshCilindro = MeshPrim(PrimitiveType.Cylinder);
        meshEsfera = MeshPrim(PrimitiveType.Sphere);

        Random.InitState(SEMILLA + 1);
        poolBlobs = new Mesh[5];
        for (int i = 0; i < poolBlobs.Length; i++)
            poolBlobs[i] = CrearIcoEsfera(1, 0.14f, false, i * 7.3f);   // suaves y redondeadas (UV.y = gradiente vertical)

        poolRocas = new Mesh[4];
        for (int i = 0; i < poolRocas.Length; i++)
            poolRocas[i] = CrearIcoEsfera(1, 0.30f, true, 20f + i * 5.1f); // facetadas, look piedra

        meshTronco = Revolucion("Tronco", new[] { 0.50f, 0.33f, 0.25f, 0.20f }, new[] { 0f, 0.35f, 1.5f, 2.9f }, 7, false, false);
        meshCono8 = Revolucion("Cono", new[] { 1f, 0f }, new[] { 0f, 1f }, 8, false, true);
        meshPiramide = Revolucion("Piramide", new[] { 1f, 0f }, new[] { 0f, 1f }, 4, false, true);
        meshBarril = Revolucion("Barril", new[] { 0.32f, 0.40f, 0.40f, 0.32f }, new[] { 0f, 0.28f, 0.62f, 0.90f }, 10, true, true);
        meshPozo = Revolucion("Pozo", new[] { 1.0f, 1.0f, 0.76f, 0.76f }, new[] { 0f, 0.9f, 0.9f, 0.2f }, 10, false, false);
        meshTorre = Revolucion("Torre", new[] { 1.75f, 1.20f }, new[] { 0f, 5.4f }, 10, false, false);
        meshTechoMolino = Revolucion("TechoMolino", new[] { 1.55f, 0.12f }, new[] { 0f, 1.7f }, 10, false, true);
        meshBaseMolino = Revolucion("BaseMolino", new[] { 2.0f, 1.9f }, new[] { -0.6f, 0.8f }, 10, true, false);

        meshPasto = CrearMallaPasto();
    }

    // Superficie de revolucion con normales suaves y UV (u = angulo, v = altura 0..1)
    static Mesh Revolucion(string nombre, float[] radios, float[] alturas, int lados, bool tapaSup, bool tapaInf)
    {
        int anillos = radios.Length;
        int vpa = lados + 1;
        List<Vector3> verts = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<int> tris = new List<int>();

        float hMin = alturas[0], hMax = alturas[0];
        for (int k = 0; k < anillos; k++) { hMin = Mathf.Min(hMin, alturas[k]); hMax = Mathf.Max(hMax, alturas[k]); }

        for (int k = 0; k < anillos; k++)
        {
            for (int i = 0; i <= lados; i++)
            {
                float ang = (float)i / lados * Mathf.PI * 2f;
                verts.Add(new Vector3(Mathf.Cos(ang) * radios[k], alturas[k], Mathf.Sin(ang) * radios[k]));
                uvs.Add(new Vector2((float)i / lados, Mathf.InverseLerp(hMin, hMax, alturas[k])));
            }
        }
        for (int k = 0; k < anillos - 1; k++)
        {
            for (int i = 0; i < lados; i++)
            {
                int b0 = k * vpa + i, b1 = b0 + 1, t0 = b0 + vpa, t1 = t0 + 1;
                tris.Add(b0); tris.Add(t0); tris.Add(b1);
                tris.Add(b1); tris.Add(t0); tris.Add(t1);
            }
        }
        if (tapaSup)
        {
            int last = anillos - 1;
            int centro = verts.Count;
            verts.Add(new Vector3(0, alturas[last], 0)); uvs.Add(new Vector2(0.5f, 1f));
            int ini = verts.Count;
            for (int i = 0; i <= lados; i++)
            {
                float ang = (float)i / lados * Mathf.PI * 2f;
                verts.Add(new Vector3(Mathf.Cos(ang) * radios[last], alturas[last], Mathf.Sin(ang) * radios[last]));
                uvs.Add(new Vector2(0.5f, 1f));
            }
            for (int i = 0; i < lados; i++) { tris.Add(centro); tris.Add(ini + i + 1); tris.Add(ini + i); }
        }
        if (tapaInf)
        {
            int centro = verts.Count;
            verts.Add(new Vector3(0, alturas[0], 0)); uvs.Add(new Vector2(0.5f, 0f));
            int ini = verts.Count;
            for (int i = 0; i <= lados; i++)
            {
                float ang = (float)i / lados * Mathf.PI * 2f;
                verts.Add(new Vector3(Mathf.Cos(ang) * radios[0], alturas[0], Mathf.Sin(ang) * radios[0]));
                uvs.Add(new Vector2(0.5f, 0f));
            }
            for (int i = 0; i < lados; i++) { tris.Add(centro); tris.Add(ini + i); tris.Add(ini + i + 1); }
        }

        Mesh m = new Mesh();
        m.name = nombre;
        m.SetVertices(verts);
        m.SetUVs(0, uvs);
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();

        // Quitar la costura de normales y dejar la punta apuntando arriba
        Vector3[] n = m.normals;
        for (int k = 0; k < anillos; k++)
        {
            int s = k * vpa;
            Vector3 avg = (n[s] + n[s + lados]).normalized;
            n[s] = avg; n[s + lados] = avg;
            if (radios[k] < 0.0001f)
                for (int i = 0; i <= lados; i++) n[s + i] = Vector3.up;
        }
        m.normals = n;
        m.RecalculateBounds();
        return m;
    }

    // Icosfera subdividida con relieve organico (suave o facetada)
    static Mesh CrearIcoEsfera(int subdiv, float irregular, bool facetado, float semilla)
    {
        float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
        List<Vector3> v = new List<Vector3> {
            new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
            new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
            new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1)
        };
        for (int i = 0; i < v.Count; i++) v[i] = v[i].normalized;

        List<int> tr = new List<int> {
            0,11,5,  0,5,1,  0,1,7,  0,7,10,  0,10,11,
            1,5,9,   5,11,4, 11,10,2, 10,7,6, 7,1,8,
            3,9,4,   3,4,2,  3,2,6,  3,6,8,   3,8,9,
            4,9,5,   2,4,11, 6,2,10, 8,6,7,   9,8,1
        };

        for (int s = 0; s < subdiv; s++)
        {
            Dictionary<long, int> cache = new Dictionary<long, int>();
            List<int> nt = new List<int>();
            for (int i = 0; i < tr.Count; i += 3)
            {
                int a = tr[i], b = tr[i + 1], c = tr[i + 2];
                int ab = PuntoMedio(v, cache, a, b);
                int bc = PuntoMedio(v, cache, b, c);
                int ca = PuntoMedio(v, cache, c, a);
                nt.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            tr = nt;
        }

        List<Vector2> uvs = new List<Vector2>();
        for (int i = 0; i < v.Count; i++)
        {
            Vector3 d = v[i];
            float n = Mathf.PerlinNoise(d.x * 1.7f + semilla + 10f, d.y * 1.7f + d.z * 1.3f + semilla * 0.7f + 10f);
            v[i] = d * (1f + (n - 0.5f) * 2f * irregular);
            uvs.Add(new Vector2(0.5f + Mathf.Atan2(d.z, d.x) / (2f * Mathf.PI), d.y * 0.5f + 0.5f));
        }

        Mesh m = new Mesh();
        m.name = "Ico";
        m.SetVertices(v);
        m.SetUVs(0, uvs);
        m.SetTriangles(tr, 0);
        m.RecalculateNormals();
        return facetado ? ConvertirALowPolyFacetado(m) : m;
    }

    static int PuntoMedio(List<Vector3> v, Dictionary<long, int> cache, int a, int b)
    {
        long key = a < b ? (((long)a) << 32) + b : (((long)b) << 32) + a;
        int idx;
        if (cache.TryGetValue(key, out idx)) return idx;
        v.Add(((v[a] + v[b]) * 0.5f).normalized);
        idx = v.Count - 1;
        cache[key] = idx;
        return idx;
    }

    // Mechon de pasto: cuchillas curvas. UV.y = 0 en la base y 1 en la punta (para gradiente en el shader).
    // Normales hacia arriba para que el pasto se ilumine igual que el suelo.
    static Mesh CrearMallaPasto()
    {
        List<Vector3> v = new List<Vector3>();
        List<Vector2> uv = new List<Vector2>();
        List<Vector3> n = new List<Vector3>();
        List<int> t = new List<int>();
        int cuchillas = 7;
        for (int i = 0; i < cuchillas; i++)
        {
            float ang = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            Vector3 f = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
            Vector3 r = new Vector3(-f.z, 0f, f.x);
            float aoff = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            Vector3 c = new Vector3(Mathf.Cos(aoff), 0f, Mathf.Sin(aoff)) * Random.Range(0.02f, 0.18f);
            float h = Random.Range(0.45f, 0.95f);
            float w = Random.Range(0.05f, 0.085f);
            float b = Random.Range(0.10f, 0.28f);
            int s = v.Count;
            v.Add(c - r * w);
            v.Add(c + r * w);
            v.Add(c + f * b * 0.35f + Vector3.up * h * 0.55f - r * w * 0.65f);
            v.Add(c + f * b * 0.35f + Vector3.up * h * 0.55f + r * w * 0.65f);
            v.Add(c + f * b + Vector3.up * h);
            uv.Add(new Vector2(0f, 0f)); uv.Add(new Vector2(1f, 0f));
            uv.Add(new Vector2(0f, 0.55f)); uv.Add(new Vector2(1f, 0.55f));
            uv.Add(new Vector2(0.5f, 1f));
            for (int k = 0; k < 5; k++) n.Add(Vector3.up);
            t.AddRange(new[] { s, s + 2, s + 1, s + 1, s + 2, s + 3, s + 2, s + 4, s + 3 });   // cara frontal
            t.AddRange(new[] { s, s + 1, s + 2, s + 1, s + 3, s + 2, s + 2, s + 3, s + 4 });   // cara trasera
        }
        Mesh m = new Mesh();
        m.name = "Pasto_Mechon";
        m.SetVertices(v);
        m.SetUVs(0, uv);
        m.SetNormals(n);
        m.SetTriangles(t, 0);
        m.RecalculateBounds();
        return m;
    }

    static Mesh CrearMallaTecho(float w, float h, float d)
    {
        Mesh m = new Mesh { name = "Prisma" };
        float hw = w * 0.5f, hd = d * 0.5f;
        m.vertices = new[] {
            new Vector3(-hw, 0, -hd), new Vector3(hw, 0, -hd), new Vector3(0, h, -hd),
            new Vector3(-hw, 0,  hd), new Vector3(hw, 0,  hd), new Vector3(0, h,  hd)
        };
        m.triangles = new[] {
            0, 2, 1,  3, 4, 5,
            0, 3, 5,  0, 5, 2,
            1, 2, 5,  1, 5, 4,
            0, 1, 4,  0, 4, 3
        };
        return ConvertirALowPolyFacetado(m);
    }

    static Mesh ConvertirALowPolyFacetado(Mesh src)
    {
        int[] oldTris = src.triangles;
        Vector3[] oldVerts = src.vertices;
        Vector2[] oldUVs = src.uv;
        bool hasUV = oldUVs != null && oldUVs.Length == oldVerts.Length;

        Vector3[] newVerts = new Vector3[oldTris.Length];
        Vector2[] newUVs = new Vector2[oldTris.Length];
        int[] newTris = new int[oldTris.Length];

        for (int i = 0; i < oldTris.Length; i++)
        {
            newVerts[i] = oldVerts[oldTris[i]];
            if (hasUV) newUVs[i] = oldUVs[oldTris[i]];
            newTris[i] = i;
        }

        Mesh flat = new Mesh { name = src.name };
        flat.vertices = newVerts;
        if (hasUV) flat.uv = newUVs;
        flat.triangles = newTris;
        flat.RecalculateNormals();
        flat.RecalculateBounds();
        return flat;
    }

    static GameObject CrearPlanoSubdividido(string nombre, float width, float length, int segX, int segZ, Material mat, Transform parent)
    {
        GameObject obj = new GameObject(nombre);
        obj.transform.SetParent(parent, false);
        Mesh m = new Mesh { name = nombre + "_Mesh" };

        Vector3[] verts = new Vector3[(segX + 1) * (segZ + 1)];
        Vector2[] uvs = new Vector2[verts.Length];
        int[] tris = new int[segX * segZ * 6];

        for (int z = 0, i = 0; z <= segZ; z++)
        {
            for (int x = 0; x <= segX; x++, i++)
            {
                float fx = (float)x / segX;
                float fz = (float)z / segZ;
                verts[i] = new Vector3((fx - 0.5f) * width, 0f, (fz - 0.5f) * length);
                uvs[i] = new Vector2(fx, fz);
            }
        }
        for (int z = 0, ti = 0, vi = 0; z < segZ; z++, vi++)
        {
            for (int x = 0; x < segX; x++, ti += 6, vi++)
            {
                tris[ti] = vi; tris[ti + 1] = vi + segX + 1; tris[ti + 2] = vi + 1;
                tris[ti + 3] = vi + 1; tris[ti + 4] = vi + segX + 1; tris[ti + 5] = vi + segX + 2;
            }
        }
        m.vertices = verts; m.uv = uvs; m.triangles = tris;
        m.RecalculateNormals();
        m.RecalculateBounds();

        obj.AddComponent<MeshFilter>().sharedMesh = m;
        obj.AddComponent<MeshRenderer>().sharedMaterial = mat;
        return obj;
    }

    // ===================== TERRENO =====================
    static float PathZ(float x)
    {
        return Mathf.Sin(x * 0.22f) * 1.1f;
    }

    static float EvaluarAlturaTerreno(float x, float z)
    {
        float distRio = Mathf.Abs(x);

        // Cauce del rio y orillas
        float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(2.4f, 6.4f, distRio));
        float ruido = Mathf.PerlinNoise((x + 100f) * 0.07f, (z + 100f) * 0.07f);
        float fino = Mathf.PerlinNoise((x + 300f) * 0.30f, (z + 300f) * 0.30f);
        float orilla = 0.35f + ruido * 0.45f + fino * 0.12f;
        float h = Mathf.Lerp(ALT_BAJA, orilla, t);

        // Colinas laterales y del fondo
        float colinas = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(17f, 32f, distRio)) * (3.5f + ruido * 3f);
        float fondo = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-20f, -32f, z)) * (3f + ruido * 2f);
        h += colinas + fondo;

        // Acantilado de la cascada (rampa lineal para que la cascada se pegue a la pared)
        float c = Mathf.Clamp01(Mathf.InverseLerp(RAMPA_A, RAMPA_B, z));
        float canal = (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.5f, 4f, distRio))) * 0.7f;
        float ruidoMeseta = ruido * 0.8f * Mathf.InverseLerp(2f, 6f, distRio);
        float meseta = ALT_MESETA + ruidoMeseta + Mathf.Max(0f, z - RAMPA_B) * 0.15f + colinas * 0.5f - canal;
        return Mathf.Lerp(h, meseta, c);
    }

    // Terreno con 3 submeshes: [0] pasto, [1] roca (pendientes fuertes), [2] lecho del rio
    static void CrearTerreno(Transform parent)
    {
        const float W = 64f, D = 64f;
        const int SX = 100, SZ = 100;
        GameObject obj = new GameObject("Terreno_Villa");
        obj.transform.SetParent(parent, false);

        Vector3[] g = new Vector3[(SX + 1) * (SZ + 1)];
        Vector2[] guv = new Vector2[g.Length];
        for (int z = 0, i = 0; z <= SZ; z++)
        {
            for (int x = 0; x <= SX; x++, i++)
            {
                float fx = (float)x / SX, fz = (float)z / SZ;
                float wx = (fx - 0.5f) * W, wz = (fz - 0.5f) * D;
                g[i] = new Vector3(wx, EvaluarAlturaTerreno(wx, wz), wz);
                guv[i] = new Vector2(fx, fz);
            }
        }

        List<int> tP = new List<int>(), tR = new List<int>(), tL = new List<int>();
        for (int z = 0; z < SZ; z++)
        {
            for (int x = 0; x < SX; x++)
            {
                int i0 = z * (SX + 1) + x, i1 = i0 + 1, i2 = i0 + SX + 1, i3 = i2 + 1;
                ClasificarTri(g, i0, i2, i1, tP, tR, tL);
                ClasificarTri(g, i1, i2, i3, tP, tR, tL);
            }
        }

        Mesh m = new Mesh();
        m.name = "Terreno_Mesh";
        m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        if (!TERRENO_FACETADO)
        {
            m.vertices = g;
            m.uv = guv;
            m.subMeshCount = 3;
            m.SetTriangles(tP, 0);
            m.SetTriangles(tR, 1);
            m.SetTriangles(tL, 2);
            m.RecalculateNormals();
        }
        else
        {
            List<Vector3> fv = new List<Vector3>();
            List<Vector2> fu = new List<Vector2>();
            List<int> nP = Expandir(tP, g, guv, fv, fu);
            List<int> nR = Expandir(tR, g, guv, fv, fu);
            List<int> nL = Expandir(tL, g, guv, fv, fu);
            m.SetVertices(fv);
            m.SetUVs(0, fu);
            m.subMeshCount = 3;
            m.SetTriangles(nP, 0);
            m.SetTriangles(nR, 1);
            m.SetTriangles(nL, 2);
            m.RecalculateNormals();
        }
        m.RecalculateBounds();

        obj.AddComponent<MeshFilter>().sharedMesh = m;
        obj.AddComponent<MeshRenderer>().sharedMaterials = new[] { mTerreno, mRoca, mLecho };
        obj.AddComponent<MeshCollider>().sharedMesh = m;
    }

    static void ClasificarTri(Vector3[] g, int a, int b, int c, List<int> tP, List<int> tR, List<int> tL)
    {
        Vector3 nrm = Vector3.Cross(g[b] - g[a], g[c] - g[a]).normalized;
        float hMed = (g[a].y + g[b].y + g[c].y) / 3f;
        List<int> dest = tP;
        if (nrm.y < 0.70f) dest = tR;
        else if (hMed < -0.55f) dest = tL;
        dest.Add(a); dest.Add(b); dest.Add(c);
    }

    static List<int> Expandir(List<int> src, Vector3[] g, Vector2[] guv, List<Vector3> fv, List<Vector2> fu)
    {
        List<int> res = new List<int>();
        for (int i = 0; i < src.Count; i++)
        {
            res.Add(fv.Count);
            fv.Add(g[src[i]]);
            fu.Add(guv[src[i]]);
        }
        return res;
    }

    // Camino de adoquines que serpentea desde el puente hacia los extremos
    static void CrearCamino(Transform parent)
    {
        ConstructorMalla c = new ConstructorMalla();
        for (int sg = -1; sg <= 1; sg += 2)
        {
            for (float ax = 5.7f; ax < 27f; ax += 0.62f)
            {
                float px = sg * ax;
                float pz0 = PathZ(px);
                for (int k = 0; k < 4; k++)
                {
                    if (Random.value < 0.07f) continue;
                    float pz = pz0 + (k - 1.5f) * 0.74f + Random.Range(-0.1f, 0.1f);
                    float pxx = px + Random.Range(-0.12f, 0.12f);
                    float y = EvaluarAlturaTerreno(pxx, pz);
                    Material mt = Random.value < 0.5f ? mCamino : mCaminoOscuro;
                    c.Cilindro(mt, new Vector3(pxx, y, pz), Random.Range(0.52f, 0.72f), 0.16f, Random.Range(0.50f, 0.68f),
                        Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                }
            }
        }
        c.Construir("Camino_Adoquines", parent, Vector3.zero, Quaternion.identity, Vector3.one);
    }

    // ===================== AGUA =====================
    static void CrearAgua(Transform parent)
    {
        float zA = RAMPA_A + (NIVEL_AGUA - ALT_BAJA) / (ALT_LIP - ALT_BAJA) * (RAMPA_B - RAMPA_A);

        // Rio
        float zIni = -32f, zFin = zA + 0.5f;
        float largoRio = zFin - zIni;
        GameObject rio = CrearPlanoSubdividido("Rio_Principal", MEDIO_RIO * 2f, largoRio, 20, 60, mRio, parent);
        rio.transform.position = new Vector3(0f, NIVEL_AGUA, zIni + largoRio * 0.5f);

        // Poza al pie de la cascada
        GameObject poza = CrearPlanoSubdividido("Poza_Cascada", 8.4f, 4.5f, 14, 10, mCascada, parent);
        poza.transform.position = new Vector3(0f, NIVEL_AGUA + 0.03f, zA - 1.6f);

        // Cascada (plano inclinado pegado al acantilado)
        Vector3 pB = new Vector3(0f, NIVEL_AGUA - 0.15f, zA - 0.28f);
        Vector3 pT = new Vector3(0f, ALT_LIP + 0.08f, RAMPA_B - 0.10f);
        Vector3 dir = (pT - pB);
        float largo = dir.magnitude;
        Quaternion rot = Quaternion.LookRotation(dir.normalized, Vector3.up);
        Vector3 centro = (pB + pT) * 0.5f;
        GameObject cascada = CrearPlanoSubdividido("Cascada", 5.0f, largo, 14, 30, mCascada, parent);
        cascada.transform.position = centro;
        cascada.transform.rotation = rot;

        // Vetas de espuma sobre la cascada (muy toon)
        Vector3 normal = rot * Vector3.up;
        Vector3 der = rot * Vector3.right;
        for (int i = 0; i < 7; i++)
        {
            float x = Mathf.Lerp(-2.0f, 2.0f, i / 6f) + Random.Range(-0.15f, 0.15f);
            float len = Random.Range(2.5f, 6.0f);
            float off = Random.Range(-largo * 0.5f + len * 0.5f, largo * 0.5f - len * 0.5f);
            GameObject veta = CrearPlanoSubdividido($"Veta_{i + 1}", Random.Range(0.22f, 0.4f), len, 1, 5, mEspuma, parent);
            veta.transform.rotation = rot;
            veta.transform.position = centro + der * x + dir.normalized * off + normal * 0.05f;
        }

        // Espuma en la base
        ConstructorMalla c = new ConstructorMalla();
        for (int i = 0; i < 9; i++)
        {
            float x = Random.Range(-2.4f, 2.4f);
            float z = zA - 0.35f + Random.Range(-0.9f, 0.5f);
            float s = Random.Range(0.55f, 1.0f);
            c.Malla(mEspuma, poolBlobs[i % poolBlobs.Length], new Vector3(x, NIVEL_AGUA + 0.02f, z), Quaternion.identity, new Vector3(s, s * 0.32f, s * 0.8f));
        }
        c.Construir("Espuma_Base", parent, Vector3.zero, Quaternion.identity, Vector3.one);
    }

    // ===================== PUENTE =====================
    static void CrearPuente(Transform parent)
    {
        ConstructorMalla c = new ConstructorMalla();
        float L = 5.4f, H = 1.25f, baseY = 0.6f;
        System.Func<float, float> Y = x => baseY + H * (1f - (x / L) * (x / L));

        // Tablones del arco
        int n = 22;
        for (int i = 0; i < n; i++)
        {
            float x = Mathf.Lerp(-L + 0.25f, L - 0.25f, i / (float)(n - 1));
            float ang = Mathf.Atan(-2f * H * x / (L * L)) * Mathf.Rad2Deg;
            c.Caja(i % 2 == 0 ? mMadera : mMaderaClara, new Vector3(x, Y(x), 0f), new Vector3(0.46f, 0.14f, 3.4f), Quaternion.Euler(0f, 0f, ang));
        }

        // Largueros laterales bajo el tablero
        for (int sz = -1; sz <= 1; sz += 2)
        {
            int m = 14;
            for (int i = 0; i < m; i++)
            {
                float x0 = Mathf.Lerp(-L, L, i / (float)m);
                float x1 = Mathf.Lerp(-L, L, (i + 1) / (float)m);
                c.Viga(mMaderaOsc, new Vector3(x0, Y(x0) - 0.2f, sz * 1.55f), new Vector3(x1, Y(x1) - 0.2f, sz * 1.55f), 0.24f);
            }
        }

        // Barandas
        int postes = 9;
        for (int sz = -1; sz <= 1; sz += 2)
        {
            for (int k = 0; k < postes; k++)
            {
                float x = Mathf.Lerp(-4.9f, 4.9f, k / (float)(postes - 1));
                float y = Y(x);
                c.Caja(mMadera, new Vector3(x, y + 0.65f, sz * 1.7f), new Vector3(0.2f, 1.3f, 0.2f));
                c.Caja(mMaderaClara, new Vector3(x, y + 1.33f, sz * 1.7f), new Vector3(0.3f, 0.12f, 0.3f));
                if (k < postes - 1)
                {
                    float x2 = Mathf.Lerp(-4.9f, 4.9f, (k + 1) / (float)(postes - 1));
                    float y2 = Y(x2);
                    c.Viga(mMaderaClara, new Vector3(x, y + 1.1f, sz * 1.7f), new Vector3(x2, y2 + 1.1f, sz * 1.7f), 0.1f);
                    c.Viga(mMaderaClara, new Vector3(x, y + 0.6f, sz * 1.7f), new Vector3(x2, y2 + 0.6f, sz * 1.7f), 0.08f);
                }
            }
        }

        // Arco de piedra bajo el puente (dovelas alternadas)
        float rx = 4.3f, ry = 2.0f, cy = -0.45f;
        int seg = 16;
        for (int i = 0; i < seg; i++)
        {
            float a0 = Mathf.PI * i / seg, a1 = Mathf.PI * (i + 1) / seg;
            Vector3 p0 = new Vector3(rx * Mathf.Cos(a0), cy + ry * Mathf.Sin(a0), 0f);
            Vector3 p1 = new Vector3(rx * Mathf.Cos(a1), cy + ry * Mathf.Sin(a1), 0f);
            Vector3 d = p1 - p0;
            float angz = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            c.Caja(i % 2 == 0 ? mPiedra : mPiedraClara, (p0 + p1) * 0.5f, new Vector3(d.magnitude + 0.02f, 0.5f, 3.3f), Quaternion.Euler(0f, 0f, angz));
        }

        // Estribos de piedra
        c.Caja(mPiedra, new Vector3(-5.9f, -0.7f, 0f), new Vector3(2.4f, 2.6f, 3.8f));
        c.Caja(mPiedra, new Vector3(5.9f, -0.7f, 0f), new Vector3(2.4f, 2.6f, 3.8f));
        c.Caja(mPiedraClara, new Vector3(-5.9f, 0.62f, 0f), new Vector3(2.5f, 0.14f, 3.9f));
        c.Caja(mPiedraClara, new Vector3(5.9f, 0.62f, 0f), new Vector3(2.5f, 0.14f, 3.9f));

        c.Construir("Puente_Arqueado", parent, Vector3.zero, Quaternion.identity, Vector3.one);
    }

    // ===================== CASAS =====================
    static void CrearCasa(string nombre, float px, float pz, float escala, int pisos, int variante,
        bool anexo, bool humo, bool cerca, Transform parent)
    {
        Vector3 pos = new Vector3(px, EvaluarAlturaTerreno(px, pz), pz);
        float tx = px + Random.Range(-3f, 3f);
        float rotY = Mathf.Atan2(tx - px, PathZ(tx) - pz) * Mathf.Rad2Deg;
        ocupados.Add(new Vector3(px, pz, 4.2f * escala + (anexo ? 1.6f : 0f)));

        Material pared = mParedes[variante % mParedes.Length];
        Material techo = mTechos[variante % mTechos.Length];
        Material teja = mTejas[variante % mTejas.Length];
        float alt = pisos == 2 ? 4.9f : 2.6f;
        float yT = 0.65f + alt;
        float rise = pisos == 2 ? 2.2f : 1.9f;
        const float hw = 2.05f;
        const float prof = 5.55f;

        ConstructorMalla c = new ConstructorMalla();

        // --- Cimiento y paredes ---
        c.Caja(mPiedra, new Vector3(0f, -0.15f, 0f), new Vector3(4.5f, 1.7f, 5.3f));
        c.Caja(mPiedraClara, new Vector3(0f, 0.74f, 0f), new Vector3(4.56f, 0.12f, 5.36f));
        c.Caja(pared, new Vector3(0f, 0.65f + alt * 0.5f, 0f), new Vector3(4.1f, alt, 4.9f));

        // --- Entramado de madera ---
        for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
                c.Caja(mMadera, new Vector3(sx * 2.06f, 0.65f + alt * 0.5f, sz * 2.46f), new Vector3(0.24f, alt + 0.04f, 0.24f));

        float[] niveles = pisos == 2 ? new[] { 3.25f, yT - 0.08f } : new[] { yT - 0.08f };
        foreach (float yv in niveles)
        {
            c.Caja(mMadera, new Vector3(0f, yv, 2.48f), new Vector3(4.25f, 0.18f, 0.14f));
            c.Caja(mMadera, new Vector3(0f, yv, -2.48f), new Vector3(4.25f, 0.18f, 0.14f));
            c.Caja(mMadera, new Vector3(-2.08f, yv, 0f), new Vector3(0.14f, 0.18f, 5.0f));
            c.Caja(mMadera, new Vector3(2.08f, yv, 0f), new Vector3(0.14f, 0.18f, 5.0f));
        }
        for (int sx = -1; sx <= 1; sx += 2)
        {
            c.Viga(mMadera, new Vector3(sx * 2.09f, 0.8f, -2.3f), new Vector3(sx * 2.09f, yT - 0.15f, -0.65f), 0.1f);
            c.Viga(mMadera, new Vector3(sx * 2.09f, 0.8f, 2.3f), new Vector3(sx * 2.09f, yT - 0.15f, 0.65f), 0.1f);
        }

        // --- Techo a dos aguas con grosor, alero, tejas y cumbrera ---
        Vector3 rp = new Vector3(0f, yT, 0f);
        c.Malla(pared, CrearMallaTecho(4.1f, rise, 4.86f), rp, Quaternion.identity, Vector3.one);   // fronton

        float ang = Mathf.Atan2(rise, hw) * Mathf.Rad2Deg;
        float angRad = ang * Mathf.Deg2Rad;
        float largo = Mathf.Sqrt(hw * hw + rise * rise) + 0.45f;
        for (int lado = -1; lado <= 1; lado += 2)
        {
            Quaternion q = Quaternion.Euler(0f, 0f, lado < 0 ? ang : -ang);
            Vector3 abajo = new Vector3(lado * Mathf.Cos(angRad), -Mathf.Sin(angRad), 0f);
            Vector3 normal = q * Vector3.up;
            Vector3 centro = rp + new Vector3(lado * hw * 0.5f, rise * 0.5f, 0f) + abajo * 0.225f + normal * 0.1f;
            c.Caja(techo, centro, new Vector3(largo, 0.2f, prof), q);

            float[] fr = { 0.18f, 0.42f, 0.66f, 0.90f };
            foreach (float f in fr)
            {
                Vector3 p = rp + new Vector3(lado * hw * f, rise * (1f - f), 0f) + normal * 0.21f;
                c.Caja(teja, p, new Vector3(0.13f, 0.07f, prof + 0.02f), q);
            }

            // tablas del alero en frente y fondo
            Vector3 punta = rp + new Vector3(lado * hw, 0f, 0f) + abajo * 0.42f + normal * 0.05f;
            for (int sz = -1; sz <= 1; sz += 2)
                c.Viga(mMadera, rp + new Vector3(0f, rise + 0.12f, sz * (prof * 0.5f)), punta + new Vector3(0f, 0f, sz * (prof * 0.5f)), 0.14f);
        }
        c.Caja(mMadera, rp + new Vector3(0f, rise + 0.16f, 0f), new Vector3(0.3f, 0.22f, prof + 0.12f));

        // --- Chimenea ---
        c.Caja(mPiedra, new Vector3(1.15f, yT + 1.05f, -1.0f), new Vector3(0.75f, 2.8f, 0.75f));
        c.Caja(mPiedraClara, new Vector3(1.15f, yT + 2.5f, -1.0f), new Vector3(0.98f, 0.18f, 0.98f));

        // --- Puerta, marco, escalones ---
        c.Caja(mPuerta, new Vector3(0f, 1.53f, 2.5f), new Vector3(1.0f, 1.75f, 0.1f));
        c.Caja(mMadera, new Vector3(0f, 2.5f, 2.5f), new Vector3(1.42f, 0.2f, 0.16f));
        c.Caja(mMadera, new Vector3(-0.66f, 1.6f, 2.5f), new Vector3(0.14f, 1.95f, 0.16f));
        c.Caja(mMadera, new Vector3(0.66f, 1.6f, 2.5f), new Vector3(0.14f, 1.95f, 0.16f));
        c.Esfera(mLuz, new Vector3(0.32f, 1.5f, 2.58f), 0.12f);
        c.Caja(mPiedra, new Vector3(0f, 0.25f, 2.9f), new Vector3(1.7f, 0.45f, 0.7f));
        c.Caja(mPiedraClara, new Vector3(0f, 0.12f, 3.4f), new Vector3(2.1f, 0.25f, 0.5f));

        // --- Porche con techito (variantes pares) ---
        if (variante % 2 == 0)
        {
            c.Caja(techo, new Vector3(0f, 2.85f, 3.1f), new Vector3(2.2f, 0.12f, 1.5f), Quaternion.Euler(10f, 0f, 0f));
            c.Caja(teja, new Vector3(0f, 2.93f, 3.1f), new Vector3(2.2f, 0.05f, 0.1f), Quaternion.Euler(10f, 0f, 0f));
            c.Caja(mMadera, new Vector3(-1.0f, 1.25f, 3.7f), new Vector3(0.14f, 2.7f, 0.14f));
            c.Caja(mMadera, new Vector3(1.0f, 1.25f, 3.7f), new Vector3(0.14f, 2.7f, 0.14f));
        }

        // --- Ventanas ---
        float[] ys = pisos == 2 ? new[] { 2.1f, 4.45f } : new[] { 2.15f };
        for (int f = 0; f < ys.Length; f++)
        {
            float y = ys[f];
            Ventana(c, new Vector3(-1.3f, y, 2.45f), 0f, f);
            Ventana(c, new Vector3(1.3f, y, 2.45f), 0f, f + 1);
            if (f == 1) Ventana(c, new Vector3(0f, y, 2.45f), 0f, 2);
            Ventana(c, new Vector3(-1.0f, y, -2.45f), 180f, f + 2);
            Ventana(c, new Vector3(1.0f, y, -2.45f), 180f, f + 3);
            Ventana(c, new Vector3(-2.05f, y, 0f), -90f, f);
            Ventana(c, new Vector3(2.05f, y, 0f), 90f, f + 1);
        }

        // --- Anexo lateral con techo de una agua ---
        if (anexo)
        {
            float ax = -3.4f, az = -0.7f;
            c.Caja(mPiedra, new Vector3(ax, -0.2f, az), new Vector3(2.9f, 1.2f, 3.3f));
            c.Caja(pared, new Vector3(ax, 1.45f, az), new Vector3(2.7f, 2.1f, 3.1f));
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    c.Caja(mMadera, new Vector3(ax + sx * 1.36f, 1.45f, az + sz * 1.56f), new Vector3(0.2f, 2.14f, 0.2f));
            Quaternion qa = Quaternion.Euler(0f, 0f, 12f);
            c.Caja(techo, new Vector3(ax, 2.78f, az), new Vector3(3.4f, 0.16f, 3.7f), qa);
            for (int k = 0; k < 3; k++)
            {
                float fx = ax - 1.1f + k * 1.0f;
                c.Caja(teja, new Vector3(fx, 2.78f + (fx - ax) * Mathf.Tan(12f * Mathf.Deg2Rad) + 0.12f, az), new Vector3(0.12f, 0.06f, 3.72f), qa);
            }
            Ventana(c, new Vector3(ax, 1.7f, az + 1.55f), 0f, 3);
            Ventana(c, new Vector3(ax - 1.36f, 1.7f, az), -90f, 2);
        }

        // --- Cerquita del jardin ---
        System.Func<float, float, float> hLocal = (lx, lz) =>
        {
            Vector3 w = pos + Quaternion.Euler(0f, rotY, 0f) * new Vector3(lx * escala, 0f, lz * escala);
            return (EvaluarAlturaTerreno(w.x, w.z) - pos.y) / escala;
        };
        if (cerca)
        {
            Cerca(c, new Vector3(-3.4f, 0f, 4.6f), new Vector3(-1.3f, 0f, 4.6f), hLocal);
            Cerca(c, new Vector3(1.3f, 0f, 4.6f), new Vector3(3.4f, 0f, 4.6f), hLocal);
            Cerca(c, new Vector3(-3.4f, 0f, 4.6f), new Vector3(-3.4f, 0f, 2.9f), hLocal);
            Cerca(c, new Vector3(3.4f, 0f, 4.6f), new Vector3(3.4f, 0f, 2.9f), hLocal);
        }

        // --- Arbustos con flores junto a la casa ---
        ArbustoEn(c, new Vector3(2.55f, hLocal(2.55f, 2.9f) + 0.3f, 2.9f), 0.55f, variante);
        ArbustoEn(c, new Vector3(-2.5f, hLocal(-2.5f, 3.0f) + 0.3f, 3.0f), 0.5f, variante + 1);
        ArbustoEn(c, new Vector3(2.5f, hLocal(2.5f, -2.9f) + 0.3f, -2.9f), 0.6f, variante + 2);

        GameObject go = c.Construir(nombre, parent, pos, Quaternion.Euler(0f, rotY, 0f), Vector3.one * escala);
        BoxCollider col = go.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, (yT + rise - 1f) * 0.5f, 0f);
        col.size = new Vector3(4.6f, yT + rise + 1f, 5.4f);

        if (humo) CrearHumo(go.transform, new Vector3(1.15f, yT + 2.75f, -1.0f));
    }

    static void Ventana(ConstructorMalla c, Vector3 pos, float rotY, int variante)
    {
        Quaternion q = Quaternion.Euler(0f, rotY, 0f);
        c.Caja(mMadera, pos, new Vector3(1.0f, 1.0f, 0.12f), q);
        c.Caja(mLuz, pos + q * new Vector3(0f, 0f, 0.04f), new Vector3(0.72f, 0.72f, 0.1f), q);
        c.Caja(mMadera, pos + q * new Vector3(0f, 0f, 0.09f), new Vector3(0.76f, 0.07f, 0.05f), q);
        c.Caja(mMadera, pos + q * new Vector3(0f, 0f, 0.09f), new Vector3(0.07f, 0.76f, 0.05f), q);
        c.Caja(mMaderaClara, pos + q * new Vector3(0f, -0.56f, 0.1f), new Vector3(1.16f, 0.1f, 0.26f), q);
        // postigos
        c.Caja(mPuerta, pos + q * new Vector3(-0.72f, 0f, 0.06f), new Vector3(0.32f, 0.96f, 0.06f), q);
        c.Caja(mPuerta, pos + q * new Vector3(0.72f, 0f, 0.06f), new Vector3(0.32f, 0.96f, 0.06f), q);
        // macetero con flores
        c.Caja(mMadera, pos + q * new Vector3(0f, -0.72f, 0.2f), new Vector3(0.98f, 0.2f, 0.28f), q);
        c.Malla(mArbusto, poolBlobs[1], pos + q * new Vector3(0f, -0.58f, 0.2f), q, new Vector3(0.5f, 0.13f, 0.14f));
        for (int k = 0; k < 5; k++)
            c.Malla(mFlores[(k + variante) % mFlores.Length], poolBlobs[0],
                pos + q * new Vector3(-0.36f + k * 0.18f, -0.5f, 0.22f), Quaternion.identity, Vector3.one * 0.085f);
    }

    static void Cerca(ConstructorMalla c, Vector3 a, Vector3 b, System.Func<float, float, float> hy)
    {
        Vector3 d = b - a;
        float len = d.magnitude;
        Quaternion q = Quaternion.LookRotation(d.normalized);
        int n = Mathf.Max(2, Mathf.RoundToInt(len / 0.3f));
        for (int i = 0; i < n; i++)
        {
            float t = (i + 0.5f) / n;
            Vector3 p = Vector3.Lerp(a, b, t);
            c.Caja(mMaderaClara, new Vector3(p.x, hy(p.x, p.z) + 0.3f, p.z), new Vector3(0.05f, 1.0f, 0.16f), q);
        }
        Vector3 m = (a + b) * 0.5f;
        float ym = hy(m.x, m.z);
        c.Caja(mMaderaClara, new Vector3(m.x, ym + 0.55f, m.z), new Vector3(0.06f, 0.07f, len), q);
        c.Caja(mMaderaClara, new Vector3(m.x, ym + 0.15f, m.z), new Vector3(0.06f, 0.07f, len), q);
        c.Caja(mMadera, new Vector3(a.x, hy(a.x, a.z) + 0.4f, a.z), new Vector3(0.12f, 1.25f, 0.12f));
        c.Caja(mMadera, new Vector3(b.x, hy(b.x, b.z) + 0.4f, b.z), new Vector3(0.12f, 1.25f, 0.12f));
    }

    static void ArbustoEn(ConstructorMalla c, Vector3 p, float r, int semilla)
    {
        c.Malla(mArbusto, poolBlobs[semilla % poolBlobs.Length], p, Quaternion.identity, new Vector3(r, r * 0.8f, r));
        c.Malla(mHojasClaras, poolBlobs[(semilla + 2) % poolBlobs.Length], p + new Vector3(r * 0.7f, -r * 0.15f, r * 0.2f), Quaternion.identity, Vector3.one * r * 0.65f);
        for (int k = 0; k < 4; k++)
        {
            Vector3 dir = new Vector3(Random.Range(-1f, 1f), Random.Range(0.2f, 1f), Random.Range(-1f, 1f)).normalized;
            c.Malla(mFlores[(k + semilla) % mFlores.Length], poolBlobs[0], p + dir * r * 0.85f, Quaternion.identity, Vector3.one * 0.1f);
        }
    }

    static void CrearHumo(Transform parent, Vector3 localPos)
    {
        GameObject go = new GameObject("Humo_Chimenea");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = ps.main;
        main.duration = 5f;
        main.loop = true;
        main.startLifetime = 3.5f;
        main.startSpeed = 0.7f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
        main.gravityModifier = -0.02f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 40;

        ParticleSystem.EmissionModule em = ps.emission;
        em.rateOverTime = 3f;

        ParticleSystem.ShapeModule sh = ps.shape;
        sh.shapeType = ParticleSystemShapeType.Cone;
        sh.angle = 10f;
        sh.radius = 0.12f;

        ParticleSystem.SizeOverLifetimeModule sol = ps.sizeOverLifetime;
        sol.enabled = true;
        AnimationCurve curva = new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(0.35f, 1f), new Keyframe(1f, 0f));
        sol.size = new ParticleSystem.MinMaxCurve(1f, curva);

        ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Mesh;
        r.mesh = poolBlobs[2];
        r.sharedMaterial = mHumo;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    // ===================== MOLINO =====================
    static void CrearMolino(float px, float pz, Transform parent)
    {
        Vector3 pos = new Vector3(px, EvaluarAlturaTerreno(px, pz), pz);
        float rotY = Mathf.Atan2(-px, -pz) * Mathf.Rad2Deg;
        ocupados.Add(new Vector3(px, pz, 4.2f));

        ConstructorMalla c = new ConstructorMalla();
        c.Malla(mPiedra, meshBaseMolino, Vector3.zero, Quaternion.identity, Vector3.one);
        c.Malla(mParedes[0], meshTorre, new Vector3(0f, 0.7f, 0f), Quaternion.identity, Vector3.one);
        c.Malla(mTechos[1], meshTechoMolino, new Vector3(0f, 6.0f, 0f), Quaternion.identity, Vector3.one);
        c.Cilindro(mMaderaOsc, new Vector3(0f, 3.1f, 0f), 3.25f, 0.12f, 3.25f);
        c.Cilindro(mMaderaOsc, new Vector3(0f, 5.1f, 0f), 2.7f, 0.12f, 2.7f);
        c.Caja(mPuerta, new Vector3(0f, 1.5f, 1.68f), new Vector3(0.95f, 1.6f, 0.18f));
        c.Caja(mMadera, new Vector3(0f, 2.4f, 1.66f), new Vector3(1.2f, 0.14f, 0.22f));
        c.Caja(mLuz, new Vector3(0f, 3.5f, 1.5f), new Vector3(0.5f, 0.7f, 0.14f));
        c.Caja(mLuz, new Vector3(0f, 4.9f, 1.36f), new Vector3(0.45f, 0.6f, 0.14f));
        c.Cilindro(mMaderaOsc, new Vector3(0f, 5.2f, 1.35f), 0.35f, 1.0f, 0.35f, Quaternion.Euler(90f, 0f, 0f));
        GameObject go = c.Construir("Molino", parent, pos, Quaternion.Euler(0f, rotY, 0f), Vector3.one);

        ConstructorMalla cb = new ConstructorMalla();
        cb.Esfera(mMaderaOsc, Vector3.zero, 0.55f);
        for (int k = 0; k < 4; k++)
        {
            Quaternion q = Quaternion.Euler(0f, 0f, k * 90f);
            cb.Caja(mMadera, q * new Vector3(0f, 2.1f, 0f), new Vector3(0.18f, 4.2f, 0.12f), q);
            cb.Caja(mToldoB, q * new Vector3(0.55f, 2.3f, 0.02f), new Vector3(0.85f, 2.6f, 0.04f), q);
            cb.Caja(mMadera, q * new Vector3(0.55f, 1.0f, 0.05f), new Vector3(0.92f, 0.07f, 0.07f), q);
            cb.Caja(mMadera, q * new Vector3(0.55f, 2.0f, 0.05f), new Vector3(0.92f, 0.07f, 0.07f), q);
            cb.Caja(mMadera, q * new Vector3(0.55f, 3.0f, 0.05f), new Vector3(0.92f, 0.07f, 0.07f), q);
            cb.Caja(mMadera, q * new Vector3(0.98f, 2.3f, 0.05f), new Vector3(0.07f, 2.7f, 0.07f), q);
        }
        GameObject aspas = cb.Construir("Aspas", go.transform, new Vector3(0f, 5.2f, 1.95f), Quaternion.identity, Vector3.one);
        VillaVida v = aspas.AddComponent<VillaVida>();
        v.tipo = VillaVida.Tipo.Girar;
        v.eje = Vector3.forward;
        v.velocidad = 35f;
    }

    // ===================== PROPS =====================
    static void CrearPozo(Vector3 xz, Transform parent)
    {
        Vector3 pos = new Vector3(xz.x, EvaluarAlturaTerreno(xz.x, xz.z), xz.z);
        ocupados.Add(new Vector3(xz.x, xz.z, 1.8f));
        ConstructorMalla c = new ConstructorMalla();
        c.Malla(mPiedra, meshPozo, new Vector3(0f, -0.1f, 0f), Quaternion.identity, Vector3.one);
        c.Cilindro(mRio, new Vector3(0f, 0.5f, 0f), 1.5f, 0.05f, 1.5f);
        c.Cilindro(mPiedraClara, new Vector3(0f, 0.3f, 0f), 2.3f, 0.5f, 2.3f);
        c.Caja(mMadera, new Vector3(-0.95f, 1.75f, 0f), new Vector3(0.16f, 1.8f, 0.16f));
        c.Caja(mMadera, new Vector3(0.95f, 1.75f, 0f), new Vector3(0.16f, 1.8f, 0.16f));
        c.Caja(mMaderaClara, new Vector3(0f, 2.3f, 0f), new Vector3(2.0f, 0.12f, 0.12f));
        c.Caja(mTechos[0], new Vector3(-0.58f, 2.95f, 0f), new Vector3(1.45f, 0.1f, 1.7f), Quaternion.Euler(0f, 0f, 30f));
        c.Caja(mTechos[0], new Vector3(0.58f, 2.95f, 0f), new Vector3(1.45f, 0.1f, 1.7f), Quaternion.Euler(0f, 0f, -30f));
        c.Caja(mMadera, new Vector3(0f, 3.4f, 0f), new Vector3(0.14f, 0.14f, 1.75f));
        c.Caja(mMaderaOsc, new Vector3(0f, 1.75f, 0f), new Vector3(0.03f, 1.1f, 0.03f));
        c.Cilindro(mMadera, new Vector3(0f, 1.15f, 0f), 0.3f, 0.26f, 0.3f);
        c.Construir("Pozo", parent, pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), Vector3.one);
    }

    static void CrearPuesto(Vector3 xz, Transform parent)
    {
        Vector3 pos = new Vector3(xz.x, EvaluarAlturaTerreno(xz.x, xz.z), xz.z);
        ocupados.Add(new Vector3(xz.x, xz.z, 2.4f));
        ConstructorMalla c = new ConstructorMalla();
        c.Caja(mMaderaClara, new Vector3(0f, 0.95f, 0f), new Vector3(2.6f, 0.12f, 1.2f));
        c.Caja(mMadera, new Vector3(0f, 0.5f, 0.05f), new Vector3(2.5f, 0.9f, 1.0f));
        for (int sx = -1; sx <= 1; sx += 2)
        {
            c.Caja(mMaderaOsc, new Vector3(sx * 1.3f, 1.25f, -0.6f), new Vector3(0.12f, 2.5f, 0.12f));
            c.Caja(mMaderaOsc, new Vector3(sx * 1.3f, 1.1f, 0.62f), new Vector3(0.1f, 2.2f, 0.1f));
        }
        Quaternion qa = Quaternion.Euler(14f, 0f, 0f);
        for (int i = 0; i < 6; i++)
        {
            float x = -1.125f + i * 0.45f;
            Material mt = i % 2 == 0 ? mToldoA : mToldoB;
            c.Caja(mt, new Vector3(x, 2.38f, 0.1f), new Vector3(0.45f, 0.06f, 1.8f), qa);
            c.Caja(mt, new Vector3(x, 2.12f, 0.98f), new Vector3(0.45f, 0.24f, 0.05f));
        }
        Material[] frutas = { mFlores[0], mFlores[1], mHojasClaras, mFlores[0], mFlores[1] };
        for (int i = 0; i < 5; i++)
            c.Malla(frutas[i], poolBlobs[0], new Vector3(-0.95f + i * 0.45f, 1.14f, 0.1f), Quaternion.identity, Vector3.one * 0.16f);
        c.Caja(mMadera, new Vector3(-0.8f, 1.2f, -0.3f), new Vector3(0.6f, 0.3f, 0.45f));
        c.Construir("Puesto_Mercado", parent, pos, Quaternion.Euler(0f, 0f, 0f), Vector3.one);
    }

    static void CrearBanco(Vector3 xz, float rotY, Transform parent)
    {
        Vector3 pos = new Vector3(xz.x, EvaluarAlturaTerreno(xz.x, xz.z), xz.z);
        ocupados.Add(new Vector3(xz.x, xz.z, 1.2f));
        ConstructorMalla c = new ConstructorMalla();
        c.Caja(mMaderaClara, new Vector3(0f, 0.5f, 0f), new Vector3(1.6f, 0.08f, 0.5f));
        c.Caja(mMaderaClara, new Vector3(0f, 0.85f, -0.22f), new Vector3(1.6f, 0.35f, 0.06f), Quaternion.Euler(-8f, 0f, 0f));
        for (int sx = -1; sx <= 1; sx += 2)
        {
            c.Caja(mMadera, new Vector3(sx * 0.7f, 0.25f, 0f), new Vector3(0.1f, 0.5f, 0.45f));
            c.Caja(mMadera, new Vector3(sx * 0.7f, 0.65f, -0.22f), new Vector3(0.1f, 0.9f, 0.08f));
        }
        c.Construir("Banco", parent, pos, Quaternion.Euler(0f, rotY, 0f), Vector3.one);
    }

    static void CrearCartel(Vector3 xz, float rotY, Transform parent)
    {
        Vector3 pos = new Vector3(xz.x, EvaluarAlturaTerreno(xz.x, xz.z), xz.z);
        ocupados.Add(new Vector3(xz.x, xz.z, 1.0f));
        ConstructorMalla c = new ConstructorMalla();
        c.Caja(mMadera, new Vector3(0f, 1.25f, 0f), new Vector3(0.18f, 2.6f, 0.18f));
        c.Caja(mMaderaClara, new Vector3(0.45f, 2.1f, 0.12f), new Vector3(1.1f, 0.3f, 0.06f));
        c.Caja(mMaderaClara, new Vector3(1.03f, 2.1f, 0.12f), new Vector3(0.22f, 0.22f, 0.06f), Quaternion.Euler(0f, 0f, 45f));
        c.Caja(mMaderaClara, new Vector3(-0.4f, 1.65f, 0.12f), new Vector3(1.0f, 0.3f, 0.06f));
        c.Caja(mMaderaClara, new Vector3(-0.91f, 1.65f, 0.12f), new Vector3(0.22f, 0.22f, 0.06f), Quaternion.Euler(0f, 0f, 45f));
        c.Esfera(mMadera, new Vector3(0f, 2.65f, 0f), 0.26f);
        c.Construir("Cartel", parent, pos, Quaternion.Euler(0f, rotY, 0f), Vector3.one);
    }

    static void CrearBarriles(Vector3 xz, Transform parent)
    {
        Vector3 pos = new Vector3(xz.x, EvaluarAlturaTerreno(xz.x, xz.z), xz.z);
        ocupados.Add(new Vector3(xz.x, xz.z, 1.4f));
        ConstructorMalla c = new ConstructorMalla();
        Vector3[] offs = { new Vector3(0f, 0f, 0f), new Vector3(0.85f, 0f, 0.2f), new Vector3(0.35f, 0f, -0.8f) };
        foreach (Vector3 o in offs)
        {
            c.Malla(mMadera, meshBarril, o + new Vector3(0f, -0.05f, 0f), Quaternion.identity, Vector3.one);
            c.Cilindro(mMaderaOsc, o + new Vector3(0f, 0.17f, 0f), 0.80f, 0.06f, 0.80f);
            c.Cilindro(mMaderaOsc, o + new Vector3(0f, 0.68f, 0f), 0.80f, 0.06f, 0.80f);
        }
        c.Caja(mMaderaClara, new Vector3(-0.9f, 0.25f, 0.3f), new Vector3(0.7f, 0.5f, 0.6f), Quaternion.Euler(0f, 15f, 0f));
        c.Caja(mMaderaClara, new Vector3(-0.8f, 0.72f, 0.3f), new Vector3(0.5f, 0.45f, 0.45f), Quaternion.Euler(0f, -10f, 0f));
        c.Construir("Barriles_Cajas", parent, pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), Vector3.one);
    }

    // ===================== FAROLES =====================
    static void CrearFarol(string nombre, Vector3 pos, Transform parent)
    {
        float rotY = Mathf.Atan2(-(PathZ(pos.x) - pos.z), -pos.x * 0.3f + Mathf.Sign(pos.x) * 0.0001f) * Mathf.Rad2Deg;
        // brazo (+X local) mira hacia el camino
        float dx = -Mathf.Sign(pos.x) * 0.6f, dz = PathZ(pos.x) - pos.z;
        rotY = Mathf.Atan2(-dz, dx) * Mathf.Rad2Deg;

        ConstructorMalla c = new ConstructorMalla();
        c.Caja(mPiedra, new Vector3(0f, 0.3f, 0f), new Vector3(0.6f, 0.6f, 0.6f));
        c.Caja(mPiedraClara, new Vector3(0f, 0.65f, 0f), new Vector3(0.46f, 0.12f, 0.46f));
        c.Caja(mMadera, new Vector3(0f, 2.0f, 0f), new Vector3(0.2f, 2.6f, 0.2f));
        c.Viga(mMadera, new Vector3(0f, 3.2f, 0f), new Vector3(0.8f, 3.5f, 0f), 0.13f);
        c.Viga(mMadera, new Vector3(0f, 2.65f, 0f), new Vector3(0.5f, 3.3f, 0f), 0.09f);
        c.Caja(mMaderaOsc, new Vector3(0.8f, 3.36f, 0f), new Vector3(0.04f, 0.24f, 0.04f));
        c.Caja(mMadera, new Vector3(0.8f, 2.62f, 0f), new Vector3(0.44f, 0.07f, 0.44f));
        c.Caja(mLuz, new Vector3(0.8f, 2.85f, 0f), new Vector3(0.3f, 0.4f, 0.3f));
        for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
                c.Caja(mMadera, new Vector3(0.8f + sx * 0.17f, 2.85f, sz * 0.17f), new Vector3(0.05f, 0.44f, 0.05f));
        c.Malla(mMadera, meshPiramide, new Vector3(0.8f, 3.06f, 0f), Quaternion.Euler(0f, 45f, 0f), new Vector3(0.34f, 0.28f, 0.34f));

        GameObject go = c.Construir(nombre, parent, pos, Quaternion.Euler(0f, rotY, 0f), Vector3.one);

        GameObject luzObj = new GameObject("PointLight");
        luzObj.transform.SetParent(go.transform, false);
        luzObj.transform.localPosition = new Vector3(0.8f, 2.85f, 0f);
        Light l = luzObj.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = new Color(1.0f, 0.78f, 0.42f);
        l.intensity = 4.5f;
        l.range = 9f;
        VillaVida v = luzObj.AddComponent<VillaVida>();
        v.tipo = VillaVida.Tipo.ParpadeoLuz;
        v.velocidad = 2.5f;
        v.amplitud = 0.25f;
        v.fase = Random.Range(0f, 20f);
    }

    // ===================== ROCAS =====================
    static void CrearRoca(string nombre, Vector3 pos, float s, Transform parent)
    {
        ConstructorMalla c = new ConstructorMalla();
        Mesh m = poolRocas[Random.Range(0, poolRocas.Length)];
        c.Malla(mRoca, m, Vector3.zero, Quaternion.identity, new Vector3(s, s * Random.Range(0.6f, 0.95f), s * Random.Range(0.8f, 1.2f)));
        if (s > 1.1f)
            c.Malla(mRoca, poolRocas[Random.Range(0, poolRocas.Length)], new Vector3(s * 0.9f, -s * 0.15f, s * 0.35f),
                Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), Vector3.one * s * 0.5f);
        c.Construir(nombre, parent, pos, Quaternion.Euler(Random.Range(-8f, 8f), Random.Range(0f, 360f), Random.Range(-8f, 8f)), Vector3.one);
    }

    static void CrearRocas(Transform parent)
    {
        int n = 1;
        // Cascada (borde superior y base)
        Vector2[] cascada = {
            new Vector2(-3.4f, 22.3f), new Vector2(3.4f, 22.3f), new Vector2(-4.6f, 21.0f), new Vector2(4.6f, 21.0f),
            new Vector2(-4.4f, 18.6f), new Vector2(4.5f, 18.8f), new Vector2(-3.0f, 17.6f), new Vector2(3.2f, 17.4f)
        };
        for (int i = 0; i < cascada.Length; i++)
        {
            float s = i < 4 ? Random.Range(1.8f, 2.6f) : Random.Range(1.0f, 1.7f);
            float y = EvaluarAlturaTerreno(cascada[i].x, cascada[i].y);
            CrearRoca($"Roca_{n++:00}", new Vector3(cascada[i].x, y - s * 0.25f, cascada[i].y), s, parent);
        }
        // Orillas del rio
        for (int i = 0; i < 16; i++)
        {
            float sg = Random.value < 0.5f ? -1f : 1f;
            float x = sg * Random.Range(4.5f, 5.8f);
            float z = Random.Range(-28f, 16f);
            if (Mathf.Abs(z) < 3.5f) continue;
            float y = Mathf.Max(EvaluarAlturaTerreno(x, z), NIVEL_AGUA - 0.1f);
            float s = Random.Range(0.6f, 1.5f);
            CrearRoca($"Roca_{n++:00}", new Vector3(x, y - s * 0.2f, z), s, parent);
        }
        // Algunas rocas sueltas en el prado
        for (int i = 0; i < 8; i++)
        {
            float x = Random.Range(-27f, 27f), z = Random.Range(-26f, 16f);
            if (!Libre(x, z, 2.5f, 2.2f)) continue;
            float s = Random.Range(0.5f, 1.1f);
            CrearRoca($"Roca_{n++:00}", new Vector3(x, EvaluarAlturaTerreno(x, z) - s * 0.2f, z), s, parent);
        }
    }

    // ===================== ARBOLES =====================
    static void CrearArbol(string nombre, Vector3 pos, float esc, int tipo, Material hojas, Transform parent)
    {
        ConstructorMalla c = new ConstructorMalla();
        if (tipo == 1)
        {
            c.Malla(mTronco, meshTronco, Vector3.zero, Quaternion.identity, new Vector3(0.8f, 0.75f, 0.8f));
            for (int i = 0; i < 4; i++)
            {
                float s = 1.45f - i * 0.3f;
                c.Malla(mPino, meshCono8, new Vector3(0f, 1.1f + i * 0.95f, 0f), Quaternion.Euler(0f, i * 23f, 0f), new Vector3(s, s * 1.15f, s));
            }
        }
        else
        {
            float ht = Random.Range(0.9f, 1.2f);
            c.Malla(mTronco, meshTronco, Vector3.zero, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), new Vector3(1f, ht, 1f));
            float yc = 2.9f * ht + 0.5f;
            Vector3 centro = new Vector3(0f, yc, 0f);
            Vector3 radios = new Vector3(1.55f, 1.35f, 1.55f);
            c.Malla(hojas, poolBlobs[0], centro, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), radios);
            int sat = Random.Range(4, 7);
            for (int k = 0; k < sat; k++)
            {
                float a = Random.Range(0f, Mathf.PI * 2f);
                float d = Random.Range(0.8f, 1.25f);
                float r = Random.Range(0.8f, 1.15f);
                Vector3 p = new Vector3(Mathf.Cos(a) * d, yc + Random.Range(-0.5f, 0.55f), Mathf.Sin(a) * d);
                c.Malla(hojas, poolBlobs[Random.Range(0, poolBlobs.Length)], p,
                    Quaternion.Euler(Random.Range(0f, 360f), Random.Range(0f, 360f), 0f), Vector3.one * r);
            }
            if (tipo == 2)   // frutal con manzanas
            {
                for (int k = 0; k < 8; k++)
                {
                    Vector3 dir = new Vector3(Random.Range(-1f, 1f), Random.Range(-0.3f, 1f), Random.Range(-1f, 1f)).normalized;
                    Vector3 p = centro + Vector3.Scale(dir, radios) * 1.0f;
                    c.Malla(mFlores[0], poolBlobs[0], p, Quaternion.identity, Vector3.one * 0.14f);
                }
            }
        }
        GameObject go = c.Construir(nombre, parent, pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), Vector3.one * esc);
        VillaVida v = go.AddComponent<VillaVida>();
        v.tipo = VillaVida.Tipo.Balancear;
        v.velocidad = Random.Range(0.6f, 1.1f);
        v.amplitud = Random.Range(0.5f, 1.1f);
        v.fase = Random.Range(0f, 10f);
    }

    static void CrearArboles(Transform parent)
    {
        int n = 1;
        // Arboles de plaza y de jardin (tipo: 0 redondo, 1 pino, 2 frutal)
        object[][] fijos = {
            new object[] { -7.8f, 13.5f, 2 }, new object[] { 8.2f, 13.8f, 0 }, new object[] { -18.5f, 4.8f, 0 },
            new object[] { 19.0f, 2.4f, 2 }, new object[] { 8.0f, -9.5f, 0 }, new object[] { -8.5f, 9.5f, 0 },
            new object[] { 7.5f, 10.5f, 2 }, new object[] { -7.5f, -10.5f, 0 }, new object[] { -9.5f, -17.0f, 1 },
            new object[] { 9.2f, -16.0f, 1 }, new object[] { -11f, 25.5f, 1 }, new object[] { 11f, 25.5f, 1 },
            new object[] { -16.5f, 26f, 1 }, new object[] { 16.5f, 26f, 1 }
        };
        int rosa = 0;
        foreach (object[] f in fijos)
        {
            float x = (float)f[0], z = (float)f[1];
            int tipo = (int)f[2];
            if (!Libre(x, z, 3.2f, 2.4f)) continue;
            Material h = mHojas;
            if (tipo != 1) { h = (rosa % 3 == 2) ? mHojasRosa : (rosa % 2 == 0 ? mHojas : mHojasClaras); rosa++; }
            CrearArbol($"Arbol_{n++:00}", new Vector3(x, EvaluarAlturaTerreno(x, z) - 0.1f, z), Random.Range(0.95f, 1.35f), tipo, h, parent);
            ocupados.Add(new Vector3(x, z, 1.2f));
        }

        // Bosque de fondo (pinos en las alturas, redondos abajo)
        int colocados = 0, intentos = 0;
        while (colocados < CANT_ARBOLES_BOSQUE && intentos < CANT_ARBOLES_BOSQUE * 12)
        {
            intentos++;
            float x = Random.Range(-30f, 30f), z = Random.Range(-30f, 27f);
            if (Mathf.Abs(x) < 8f) continue;
            if (z > 18.6f && z < 24f) continue;
            if (!Libre(x, z, 4.8f, 2.6f)) continue;
            bool alto = Mathf.Abs(x) > 20f || z < -18f || z > 24f;
            int tipo = alto ? (Random.value < 0.8f ? 1 : 0) : (Random.value < 0.25f ? 1 : 0);
            Material h = Random.value < 0.5f ? mHojas : mHojasClaras;
            if (tipo == 0 && Random.value < 0.08f) h = mHojasRosa;
            CrearArbol($"Arbol_{n++:00}", new Vector3(x, EvaluarAlturaTerreno(x, z) - 0.1f, z), Random.Range(0.9f, 1.5f), tipo, h, parent);
            ocupados.Add(new Vector3(x, z, 1.4f));
            colocados++;
        }
    }

    static void CrearArbustos(Transform parent)
    {
        ConstructorMalla c = new ConstructorMalla();
        int puestos = 0, intentos = 0;
        while (puestos < 26 && intentos < 300)
        {
            intentos++;
            float x = Random.Range(-27f, 27f), z = Random.Range(-24f, 17f);
            if (!Libre(x, z, 2.4f, 2.0f)) continue;
            float y = EvaluarAlturaTerreno(x, z);
            ArbustoEn(c, new Vector3(x, y + 0.3f, z), Random.Range(0.5f, 0.85f), puestos);
            puestos++;
        }
        c.Construir("Arbustos_Florales", parent, Vector3.zero, Quaternion.identity, Vector3.one);
    }

    // ===================== PASTO, FLORES, JUNCOS, NENUFARES =====================
    static void CrearPastoYFlores(Transform parent)
    {
        // Pasto por celdas (mejor culling) con dos tonos
        Dictionary<Vector2Int, ConstructorMalla> celdas = new Dictionary<Vector2Int, ConstructorMalla>();
        int colocados = 0, intentos = 0;
        while (colocados < CANT_PASTO && intentos < CANT_PASTO * 6)
        {
            intentos++;
            float x = Random.Range(-30f, 30f), z = Random.Range(-30f, 28f);
            if (!Libre(x, z, 3.3f, 1.5f)) continue;
            if (z > 19.0f && z < 23.5f) continue;
            float y = EvaluarAlturaTerreno(x, z);
            float pend = Mathf.Abs(EvaluarAlturaTerreno(x + 0.5f, z) - y) + Mathf.Abs(EvaluarAlturaTerreno(x, z + 0.5f) - y);
            if (pend > 0.9f) continue;

            Vector2Int key = new Vector2Int(Mathf.FloorToInt((x + 32f) / 12f), Mathf.FloorToInt((z + 32f) / 12f));
            ConstructorMalla celda;
            if (!celdas.TryGetValue(key, out celda)) { celda = new ConstructorMalla(); celdas[key] = celda; }
            celda.Malla(Random.value < 0.5f ? mPasto : mPastoClaro, meshPasto, new Vector3(x, y - 0.03f, z),
                Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), Vector3.one * Random.Range(0.8f, 1.5f));
            colocados++;
        }
        foreach (KeyValuePair<Vector2Int, ConstructorMalla> kv in celdas)
            kv.Value.Construir($"Pasto_{kv.Key.x}_{kv.Key.y}", parent, Vector3.zero, Quaternion.identity, Vector3.one);

        // Flores en parches
        ConstructorMalla cF = new ConstructorMalla();
        int p = 0, intentos2 = 0;
        while (p < CANT_PARCHES_FLORES && intentos2 < 500)
        {
            intentos2++;
            float x = Random.Range(-27f, 27f), z = Random.Range(-24f, 17f);
            if (!Libre(x, z, 2.8f, 1.9f)) continue;
            int col = Random.Range(0, mFlores.Length);
            int cant = Random.Range(6, 12);
            for (int k = 0; k < cant; k++)
            {
                float fx = x + Random.Range(-1.1f, 1.1f), fz = z + Random.Range(-1.1f, 1.1f);
                float fy = EvaluarAlturaTerreno(fx, fz);
                float h = Random.Range(0.28f, 0.5f);
                cF.Caja(mPasto, new Vector3(fx, fy + h * 0.5f, fz), new Vector3(0.03f, h, 0.03f));
                Material mf = Random.value < 0.75f ? mFlores[col] : mFlores[Random.Range(0, mFlores.Length)];
                cF.Malla(mf, poolBlobs[k % poolBlobs.Length], new Vector3(fx, fy + h + 0.02f, fz), Quaternion.identity, Vector3.one * Random.Range(0.10f, 0.16f));
            }
            p++;
        }
        cF.Construir("Flores_Parches", parent, Vector3.zero, Quaternion.identity, Vector3.one);

        // Juncos en la orilla del rio
        ConstructorMalla cJ = new ConstructorMalla();
        for (int i = 0; i < 90; i++)
        {
            float sg = Random.value < 0.5f ? -1f : 1f;
            float x = sg * Random.Range(4.5f, 5.6f);
            float z = Random.Range(-30f, 18.5f);
            if (Mathf.Abs(z) < 2.8f) continue;
            float th = EvaluarAlturaTerreno(x, z);
            if (th < -0.9f) continue;
            float y = Mathf.Max(th, NIVEL_AGUA - 0.15f);
            float sx = Random.Range(0.7f, 1.1f);
            cJ.Malla(mPasto, meshPasto, new Vector3(x, y, z), Quaternion.Euler(0f, Random.Range(0f, 360f), 0f),
                new Vector3(sx, Random.Range(1.6f, 2.6f), sx));
        }
        cJ.Construir("Juncos_Orilla", parent, Vector3.zero, Quaternion.identity, Vector3.one);

        // Nenufares
        ConstructorMalla cN = new ConstructorMalla();
        for (int i = 0; i < 16; i++)
        {
            float x = Random.Range(-4.4f, 4.4f), z = Random.Range(-28f, 16f);
            if (Mathf.Abs(z) < 3.2f) continue;
            float d = Random.Range(0.55f, 0.95f);
            Vector3 pp = new Vector3(x, NIVEL_AGUA + 0.03f, z);
            cN.Cilindro(mNenufar, pp, d, 0.03f, d, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            if (Random.value < 0.4f)
                cN.Malla(mFlores[2], poolBlobs[0], pp + Vector3.up * 0.07f, Quaternion.identity, Vector3.one * 0.1f);
        }
        cN.Construir("Nenufares", parent, Vector3.zero, Quaternion.identity, Vector3.one);
    }

    // ===================== NUBES =====================
    static void CrearNubes(Transform parent)
    {
        for (int i = 0; i < 7; i++)
        {
            ConstructorMalla c = new ConstructorMalla();
            int nb = Random.Range(3, 6);
            for (int k = 0; k < nb; k++)
            {
                Vector3 p = new Vector3(k * 1.6f - nb * 0.8f + Random.Range(-0.3f, 0.3f), Random.Range(-0.2f, 0.4f), Random.Range(-0.6f, 0.6f));
                float r = Random.Range(1.2f, 2.0f);
                c.Malla(mNube, poolBlobs[k % poolBlobs.Length], p, Quaternion.identity, new Vector3(r * 1.3f, r * 0.8f, r));
            }
            GameObject go = c.Construir($"Nube_{i + 1:00}", parent,
                new Vector3(Random.Range(-45f, 45f), Random.Range(24f, 34f), Random.Range(-35f, 35f)),
                Quaternion.identity, Vector3.one * Random.Range(1.2f, 1.9f));
            VillaVida v = go.AddComponent<VillaVida>();
            v.tipo = VillaVida.Tipo.Deriva;
            v.velocidad = Random.Range(0.5f, 1.2f);
            v.amplitud = 60f;
        }
    }

    // ===================== UTILIDADES =====================
    // true si (x,z) esta libre de rio, casas/props, camino
    static bool Libre(float x, float z, float margenObjetos, float margenCamino)
    {
        if (Mathf.Abs(x) < MEDIO_RIO + 0.8f) return false;
        if (Mathf.Abs(x) < 28f && Mathf.Abs(z - PathZ(x)) < margenCamino) return false;
        for (int i = 0; i < ocupados.Count; i++)
        {
            Vector3 o = ocupados[i];
            float dx = x - o.x, dz = z - o.y;
            float r = o.z + margenObjetos;
            if (dx * dx + dz * dz < r * r) return false;
        }
        return true;
    }

    static Transform CrearGrupo(string nombre, Transform parent)
    {
        GameObject g = new GameObject(nombre);
        g.transform.SetParent(parent, false);
        return g.transform;
    }

    static void AsegurarCarpeta(string parent, string nueva)
    {
        if (!AssetDatabase.IsValidFolder($"{parent}/{nueva}"))
            AssetDatabase.CreateFolder(parent, nueva);
    }
}
