using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Membuat pohon di Terrain bisa roboh saat gempa.
///
/// Pohon Terrain bukan GameObject, jadi tidak bisa dirobohkan langsung. Cara kerjanya:
/// saat gempa mulai, pohon yang terpilih (berdasarkan SR) di sekitar pemain
/// DIGANTI menjadi GameObject biasa di posisi yang sama, lalu dirobohkan.
///
/// Animasi roboh memakai fisika batang berengsel di pangkalnya:
///  1) Retak    : pohon bergetar dan miring sedikit, terdengar suara derak batang.
///  2) Jatuh    : makin lama makin cepat (pohon tinggi lebih lambat), sedikit berputar.
///  3) Tumbukan : ujung pohon menyentuh permukaan Terrain (ikut kemiringan tanah),
///                muncul asap, suara benturan, lalu memantul kecil dan diam.
///
/// TerrainData asli TIDAK diubah: skrip ini bekerja di salinan saat Play, jadi semua pohon
/// kembali seperti semula setiap kali Play dimulai.
/// </summary>
public class PohonRobohManager : MonoBehaviour
{
    private enum Status { Menunggu, Retak, Jatuh }

    private class PohonAktif
    {
        public Transform tf;
        public Terrain terrain;
        public Quaternion rotasiAwal;
        public Vector3 sumbu;
        public Vector3 arah;
        public Vector3 pangkal;
        public float tinggi;
        public float waktuMulai;
        public Status status = Status.Menunggu;

        // Fase retak
        public float durasiRetak;
        public float waktuRetakMulai;
        public float sudutMiring;      // radian
        public float twistAkhir;       // derajat
        public float seed;

        // Fase jatuh
        public float theta;            // radian, sudut dari posisi tegak
        public float thetaAman;        // radian, sudut terakhir yang belum menembus tanah
        public float omega;            // radian/detik
        public float waktuJatuhMulai;
        public bool sudahTumbuk;

        // Deteksi gerakan (untuk memudarkan suara saat pohon sudah diam)
        public Quaternion rotasiTerakhir;
        public float waktuDiam;
        public bool selesai;
    }

    private class SuaraAktif
    {
        public AudioSource sumber;
        public PohonAktif pohon;
        public float waktuMulai;       // Time.time saat suara benar-benar mulai terdengar
        public float waktuDiamSuara;   // berapa lama suara sudah terdengar saat pohonnya diam
        public bool memudar;
        public float waktuMulaiFade;
        public float volumeSaatFade;
    }

    [Header("Ambang SR Roboh")]
    [Tooltip("SR di bawah angka ini: tidak ada pohon yang roboh")]
    public float srMinimalRoboh = 6f;
    [Tooltip("SR di angka ini atau lebih: persentase pohon roboh mencapai maksimum")]
    public float srPuncakRoboh = 8f;
    [Tooltip("Persentase pohon (di dalam radius) yang roboh pada SR minimal")]
    [Range(0f, 1f)] public float persentaseMinimal = 0.1f;
    [Tooltip("Persentase pohon (di dalam radius) yang roboh pada SR puncak")]
    [Range(0f, 1f)] public float persentasePuncak = 0.6f;

    [Header("Jangkauan")]
    [Tooltip("Hanya pohon dalam radius ini dari pemain (saat gempa mulai) yang bisa roboh, dalam meter")]
    public float radiusRoboh = 80f;
    [Tooltip("Batas maksimum pohon yang roboh dalam satu gempa (menjaga performa VR)")]
    public int maksPohonRoboh = 40;

    [Header("Waktu Mulai Roboh")]
    [Tooltip("Pohon paling awal mulai berderak pada persen ini dari durasi gempa")]
    [Range(0f, 1f)] public float mulaiRobohPalingAwal = 0.25f;
    [Tooltip("Pohon paling akhir mulai berderak pada persen ini dari durasi gempa")]
    [Range(0f, 1f)] public float mulaiRobohPalingAkhir = 0.8f;

    [Header("Animasi Roboh")]
    [Tooltip("Lama pohon bergetar dan berderak sebelum benar-benar roboh (detik, diacak per pohon)")]
    public Vector2 rentangDurasiRetak = new Vector2(0.6f, 1.4f);
    [Tooltip("Sudut miring awal sebelum roboh (derajat, diacak per pohon)")]
    public Vector2 rentangSudutMiring = new Vector2(3f, 6f);
    [Tooltip("Kekuatan getaran pohon saat berderak (derajat)")]
    public float getaranPohon = 0.8f;
    [Tooltip("Pengali kecepatan jatuh. 1 = realistis (pohon tinggi jatuh lebih lambat), makin besar makin cepat.")]
    public float kecepatanRoboh = 1f;
    [Tooltip("Seberapa besar pantulan saat ujung pohon menghantam tanah. 0 = langsung diam, 0.25 = pantulan kecil.")]
    [Range(0f, 0.6f)] public float pantulan = 0.25f;
    [Tooltip("Batang berputar sedikit saat jatuh, maksimum sekian derajat (arah acak)")]
    public float putaranBatang = 15f;
    public float gravitasi = 9.81f;
    [Tooltip("Hambatan udara. Makin besar, jatuh makin lambat di ujung.")]
    public float redamanUdara = 0.15f;

    [Header("Asap Saat Menyentuh Tanah")]
    [Tooltip("Prefab particle asap/debu yang muncul saat ujung pohon menghantam tanah")]
    public ParticleSystem prefabAsapJatuh;
    [Tooltip("Berapa detik asap dibiarkan sebelum dihapus")]
    public float umurAsap = 8f;

    [Header("Suara (tiap pohon punya suara sendiri)")]
    [Tooltip("Suara batang berderak/patah, diputar saat pohon mulai bergetar (opsional)")]
    public AudioClip[] sfxRetak;
    [Tooltip("Suara dedaunan/ranting berdesir, diputar saat pohon mulai jatuh (opsional)")]
    public AudioClip[] sfxDedaunan;
    [Tooltip("Suara benturan pohon jatuh untuk pemain yang DEKAT. Satu dipilih acak per pohon.")]
    [FormerlySerializedAs("sfxPohonJatuh")]
    public AudioClip[] sfxJatuhDekat;
    [Tooltip("Suara benturan versi JAUH (lebih tumpul, dentum rendah). Kosongkan untuk memakai suara dekat yang diredam otomatis.")]
    public AudioClip[] sfxJatuhJauh;
    [Range(0f, 1f)] public float volumeSfx = 1f;
    [Tooltip("Rentang variasi nada. Pohon yang lebih besar otomatis terdengar lebih dalam.")]
    public Vector2 rentangPitch = new Vector2(0.85f, 1.1f);

    [Header("Suara: Jarak")]
    [Tooltip("Di dalam jarak ini (meter) dipakai suara DEKAT. Di luarnya dipakai suara JAUH (kalau ada).")]
    public float jarakDekat = 40f;
    [Tooltip("Di luar jarak ini (meter) suara tidak diputar sama sekali")]
    public float jarakMaksSfx = 200f;
    [Tooltip("Frekuensi suara pada jarak dekat (Hz). 22000 = jernih.")]
    public float cutoffDekat = 22000f;
    [Tooltip("Frekuensi suara pada jarak maksimum (Hz). Makin kecil, makin tumpul dan teredam.")]
    public float cutoffJauh = 1500f;
    [Tooltip("Suara datang terlambat sesuai jarak, seperti suara asli (343 m/detik)")]
    public bool simulasiKecepatanSuara = true;

    [Header("Suara: Memudar Saat Pohon Diam")]
    [Tooltip("Suara dipudarkan perlahan begitu pohonnya sudah tidak bergerak")]
    public bool fadeSaatPohonDiam = true;
    [Tooltip("Kecepatan putar pohon (derajat/detik) di bawah angka ini dianggap tidak bergerak")]
    public float ambangDiam = 1f;
    [Tooltip("Pohon harus tidak bergerak selama ini (detik, berturut-turut) sebelum dianggap sudah diam. Mencegah salah deteksi di puncak pantulan.")]
    public float jedaDeteksiDiam = 0.4f;
    [Tooltip("Setelah pohon diam, suara tetap penuh selama ini (detik, dihitung sejak suara benar-benar terdengar), baru mulai memudar")]
    public float jedaSebelumFade = 0.6f;
    [Tooltip("Lama suara memudar sampai hening (detik)")]
    public float durasiFadeOut = 2.5f;

    private const float KecepatanSuara = 343f;
    private const float KecepatanTumbukReferensi = 20f;

    private readonly List<PohonAktif> daftarPohon = new List<PohonAktif>();
    private readonly List<SuaraAktif> daftarSuara = new List<SuaraAktif>();
    private readonly HashSet<Terrain> terrainSiap = new HashSet<Terrain>();
    private AudioListener listenerCache;

    void Start()
    {
        foreach (Terrain t in Terrain.activeTerrains) SiapkanTerrain(t);
    }

    /// <summary>
    /// Ganti TerrainData dengan salinan di memori, supaya perubahan pohon saat Play
    /// tidak menimpa asset TerrainData asli di project.
    /// </summary>
    private void SiapkanTerrain(Terrain t)
    {
        if (t == null || t.terrainData == null || terrainSiap.Contains(t)) return;

        TerrainData salinan = Instantiate(t.terrainData);
        t.terrainData = salinan;

        TerrainCollider tc = t.GetComponent<TerrainCollider>();
        if (tc != null) tc.terrainData = salinan;

        terrainSiap.Add(t);
    }

    /// <summary>
    /// Dipanggil EarthquakeSimulator saat gempa dimulai.
    /// </summary>
    public void MulaiGempa(float sr, float durasiGempa, Vector3 posisiPemain)
    {
        if (sr < srMinimalRoboh)
        {
            Debug.Log($"<color=#8B4513>[Pohon Roboh]</color> SR {sr:F2} di bawah ambang {srMinimalRoboh:F1}, tidak ada pohon yang roboh.");
            return;
        }

        float tSR = Mathf.InverseLerp(srMinimalRoboh, srPuncakRoboh, sr);
        float persentase = Mathf.Lerp(persentaseMinimal, persentasePuncak, tSR);
        int sisaKuota = maksPohonRoboh;
        int total = 0;

        foreach (Terrain terrain in Terrain.activeTerrains)
        {
            if (sisaKuota <= 0) break;
            SiapkanTerrain(terrain);
            total += ProsesTerrain(terrain, persentase, durasiGempa, posisiPemain, ref sisaKuota);
        }

        Debug.Log($"<color=#8B4513>[Pohon Roboh]</color> SR {sr:F2}: {total} pohon dijadwalkan roboh (persentase {persentase:P0}, radius {radiusRoboh:F0} m).");
    }

    /// <summary>
    /// Batalkan pohon yang belum mulai berderak (misal saat gempa dihentikan paksa).
    /// Pohon yang sudah mulai berderak atau jatuh dibiarkan selesai.
    /// </summary>
    public void BatalkanJadwalJatuh()
    {
        daftarPohon.RemoveAll(p => p.status == Status.Menunggu);
    }

    private int ProsesTerrain(Terrain terrain, float persentase, float durasiGempa, Vector3 posisiPemain, ref int sisaKuota)
    {
        TerrainData data = terrain.terrainData;
        TreeInstance[] semua = data.treeInstances;
        if (semua.Length == 0) return 0;

        Vector3 originTerrain = terrain.transform.position;
        Vector3 ukuran = data.size;
        float radiusKuadrat = radiusRoboh * radiusRoboh;

        // 1) Kumpulkan pohon yang berada dalam radius dari pemain
        List<int> kandidat = new List<int>();
        for (int i = 0; i < semua.Length; i++)
        {
            Vector3 posDunia = originTerrain + Vector3.Scale(semua[i].position, ukuran);
            Vector3 selisih = posDunia - posisiPemain;
            selisih.y = 0f;
            if (selisih.sqrMagnitude <= radiusKuadrat) kandidat.Add(i);
        }
        if (kandidat.Count == 0) return 0;

        int jumlah = Mathf.Min(Mathf.RoundToInt(kandidat.Count * persentase), sisaKuota);
        if (jumlah <= 0) return 0;

        // 2) Acak urutan kandidat, ambil sebanyak 'jumlah'
        for (int i = kandidat.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            int tmp = kandidat[i];
            kandidat[i] = kandidat[j];
            kandidat[j] = tmp;
        }

        // 3) Ganti pohon terpilih menjadi GameObject
        HashSet<int> terpilih = new HashSet<int>();
        for (int i = 0; i < jumlah; i++)
        {
            int idx = kandidat[i];
            if (BuatPohonGameObject(terrain, semua[idx], durasiGempa)) terpilih.Add(idx);
        }
        if (terpilih.Count == 0) return 0;

        // 4) Hapus pohon terpilih dari Terrain (satu kali, sekaligus)
        List<TreeInstance> sisa = new List<TreeInstance>(semua.Length - terpilih.Count);
        for (int i = 0; i < semua.Length; i++)
        {
            if (!terpilih.Contains(i)) sisa.Add(semua[i]);
        }

        data.SetTreeInstances(sisa.ToArray(), false);
        terrain.Flush();
        RefreshColliderTerrain(terrain);

        sisaKuota -= terpilih.Count;
        return terpilih.Count;
    }

    private bool BuatPohonGameObject(Terrain terrain, TreeInstance inst, float durasiGempa)
    {
        TreePrototype[] prototipe = terrain.terrainData.treePrototypes;
        if (inst.prototypeIndex < 0 || inst.prototypeIndex >= prototipe.Length) return false;

        GameObject prefab = prototipe[inst.prototypeIndex].prefab;
        if (prefab == null) return false;

        Vector3 posisi = terrain.transform.position + Vector3.Scale(inst.position, terrain.terrainData.size);
        Quaternion rotasi = Quaternion.Euler(0f, inst.rotation * Mathf.Rad2Deg, 0f);

        GameObject pohon = Instantiate(prefab, posisi, rotasi);
        Vector3 skalaPrefab = prefab.transform.localScale;
        pohon.transform.localScale = new Vector3(
            skalaPrefab.x * inst.widthScale,
            skalaPrefab.y * inst.heightScale,
            skalaPrefab.z * inst.widthScale);

        Vector3 arah = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;

        float awal = durasiGempa * mulaiRobohPalingAwal;
        float akhir = Mathf.Max(awal + 0.1f, durasiGempa * mulaiRobohPalingAkhir);

        PohonAktif p = new PohonAktif
        {
            tf = pohon.transform,
            terrain = terrain,
            rotasiAwal = pohon.transform.rotation,
            arah = arah,
            sumbu = Vector3.Cross(Vector3.up, arah).normalized,
            pangkal = posisi,
            tinggi = HitungTinggi(pohon),
            waktuMulai = Time.time + Random.Range(awal, akhir),
            durasiRetak = Random.Range(rentangDurasiRetak.x, rentangDurasiRetak.y),
            sudutMiring = Random.Range(rentangSudutMiring.x, rentangSudutMiring.y) * Mathf.Deg2Rad,
            twistAkhir = Random.Range(-putaranBatang, putaranBatang),
            seed = Random.Range(0f, 100f)
        };
        daftarPohon.Add(p);
        return true;
    }

    private float HitungTinggi(GameObject pohon)
    {
        Renderer[] renderers = pohon.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return 8f;

        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
        return Mathf.Max(b.size.y, 1f);
    }

    // ---------------------------------------------------------------
    //  ANIMASI
    // ---------------------------------------------------------------

    void Update()
    {
        float dt = Mathf.Min(Time.deltaTime, 0.033f);

        for (int i = daftarPohon.Count - 1; i >= 0; i--)
        {
            PohonAktif p = daftarPohon[i];

            if (p.tf == null)
            {
                p.selesai = true;
                daftarPohon.RemoveAt(i);
                continue;
            }

            if (p.status == Status.Menunggu)
            {
                if (Time.time < p.waktuMulai) continue;

                p.status = Status.Retak;
                p.waktuRetakMulai = Time.time;
                MainkanSuara(sfxRetak, null, p.pangkal + Vector3.up * (p.tinggi * 0.35f), 1f, p);
            }

            if (p.status == Status.Retak)
            {
                PerbaruiRetak(p);
                continue;
            }

            bool selesai = PerbaruiJatuh(p, dt);
            DeteksiGerak(p);

            if (selesai)
            {
                p.selesai = true;
                daftarPohon.RemoveAt(i);
            }
        }

        // Dijalankan tiap frame, juga setelah semua pohon diam (suara masih perlu memudar)
        PerbaruiSuara(dt);
    }

    /// <summary>
    /// Rotasi pohon: miring sebesar theta di pangkalnya ke arah jatuh, plus putaran kecil pada batangnya.
    /// </summary>
    private Quaternion RotasiPohon(PohonAktif p, float theta)
    {
        float twist = p.twistAkhir * Mathf.Clamp01(theta / (Mathf.PI * 0.5f));
        return Quaternion.AngleAxis(theta * Mathf.Rad2Deg, p.sumbu) * p.rotasiAwal * Quaternion.AngleAxis(twist, Vector3.up);
    }

    /// <summary>
    /// Fase 1: pohon bergetar dan perlahan miring sebelum roboh.
    /// </summary>
    private void PerbaruiRetak(PohonAktif p)
    {
        float t = Mathf.Clamp01((Time.time - p.waktuRetakMulai) / p.durasiRetak);
        float miring = p.sudutMiring * t * t;

        float amplitudo = getaranPohon * t;
        float n1 = (Mathf.PerlinNoise(Time.time * 14f, p.seed) - 0.5f) * 2f;
        float n2 = (Mathf.PerlinNoise(p.seed + 31f, Time.time * 14f) - 0.5f) * 2f;
        Quaternion getar = Quaternion.Euler(n1 * amplitudo, 0f, n2 * amplitudo);

        p.tf.rotation = getar * RotasiPohon(p, miring);

        if (t >= 1f)
        {
            p.status = Status.Jatuh;
            p.theta = p.sudutMiring;
            p.thetaAman = p.theta;
            p.omega = 0.05f;
            p.waktuJatuhMulai = Time.time;
            p.rotasiTerakhir = p.tf.rotation;
            p.waktuDiam = 0f;
            MainkanSuara(sfxDedaunan, null, p.pangkal + Vector3.up * (p.tinggi * 0.8f), 1f, p);
        }
    }

    /// <summary>
    /// Fase 2 dan 3: batang berengsel di pangkal (gravitasi menarik makin kuat saat makin miring),
    /// berhenti dan memantul saat ujungnya menyentuh permukaan Terrain.
    /// Mengembalikan true kalau pohon sudah diam.
    /// </summary>
    private bool PerbaruiJatuh(PohonAktif p, float dt)
    {
        // Percepatan sudut batang seragam yang berengsel di ujung bawahnya: 3g / (2L) x sin(theta)
        float k = 1.5f * gravitasi * kecepatanRoboh / p.tinggi;
        p.omega += (k * Mathf.Sin(p.theta) - redamanUdara * p.omega) * dt;
        float thetaBaru = p.theta + p.omega * dt;

        Vector3 ujung = p.pangkal + Quaternion.AngleAxis(thetaBaru * Mathf.Rad2Deg, p.sumbu) * (Vector3.up * p.tinggi);
        bool kenaTanah = p.omega > 0f && thetaBaru > 0.35f && ujung.y <= TinggiTanah(p, ujung) + 0.1f;
        bool melewatiBatas = thetaBaru > 2.4f;

        if (kenaTanah || melewatiBatas)
        {
            float kecepatanUjung = Mathf.Abs(p.omega) * p.tinggi;
            SaatTumbukan(p, kecepatanUjung);
            p.sudahTumbuk = true;

            p.omega = -p.omega * pantulan;
            thetaBaru = p.thetaAman;

            if (Mathf.Abs(p.omega) < 0.2f)
            {
                p.tf.rotation = RotasiPohon(p, p.thetaAman);
                return true;
            }
        }
        else
        {
            p.thetaAman = thetaBaru;
        }

        p.theta = thetaBaru;
        p.tf.rotation = RotasiPohon(p, p.theta);

        // Pengaman: jangan animasi selamanya
        return Time.time - p.waktuJatuhMulai > 12f;
    }

    private float TinggiTanah(PohonAktif p, Vector3 posisi)
    {
        if (p.terrain == null) return p.pangkal.y;
        return p.terrain.SampleHeight(posisi) + p.terrain.transform.position.y;
    }

    private void SaatTumbukan(PohonAktif p, float kecepatanUjung)
    {
        Vector3 ujung = p.pangkal + Quaternion.AngleAxis(p.thetaAman * Mathf.Rad2Deg, p.sumbu) * (Vector3.up * p.tinggi);
        ujung.y = TinggiTanah(p, ujung) + 0.2f;

        float kekuatan = Mathf.Clamp01(kecepatanUjung / KecepatanTumbukReferensi);

        if (!p.sudahTumbuk)
        {
            // Benturan pertama: keras, disertai asap
            if (prefabAsapJatuh != null)
            {
                ParticleSystem asap = Instantiate(prefabAsapJatuh, ujung, Quaternion.identity);
                asap.transform.localScale *= Mathf.Clamp(p.tinggi / 10f, 0.6f, 1.6f);
                asap.Play();
                Destroy(asap.gameObject, umurAsap);
            }

            MainkanSuara(sfxJatuhDekat, sfxJatuhJauh, ujung, Mathf.Lerp(0.5f, 1f, kekuatan), p);
        }
        else if (kecepatanUjung > 1f)
        {
            // Pantulan berikutnya: dentuman kecil
            MainkanSuara(sfxJatuhDekat, sfxJatuhJauh, ujung, Mathf.Clamp(kekuatan * 0.6f, 0.1f, 0.5f), p);
        }
    }

    // ---------------------------------------------------------------
    //  SUARA
    // ---------------------------------------------------------------

    private AudioListener AmbilListener()
    {
        if (listenerCache != null && listenerCache.enabled && listenerCache.gameObject.activeInHierarchy)
            return listenerCache;

        foreach (AudioListener l in FindObjectsOfType<AudioListener>())
        {
            if (l.enabled && l.gameObject.activeInHierarchy)
            {
                listenerCache = l;
                return l;
            }
        }
        return null;
    }

    /// <summary>
    /// Putar satu suara 3D di posisi pohon. Makin jauh dari pemain: suara makin pelan,
    /// makin tumpul (low-pass), datang lebih terlambat, dan (kalau ada) memakai versi 'jauh'.
    /// </summary>
    private void MainkanSuara(AudioClip[] dekat, AudioClip[] jauh, Vector3 posisi, float volumeRelatif, PohonAktif pohon)
    {
        float tinggiPohon = pohon != null ? pohon.tinggi : 10f;

        bool adaDekat = dekat != null && dekat.Length > 0;
        bool adaJauh = jauh != null && jauh.Length > 0;
        if (!adaDekat && !adaJauh) return;

        float jarak = 0f;
        AudioListener listener = AmbilListener();
        if (listener != null) jarak = Vector3.Distance(listener.transform.position, posisi);
        if (jarak > jarakMaksSfx) return;

        AudioClip[] daftar = (jarak > jarakDekat && adaJauh) ? jauh : (adaDekat ? dekat : jauh);
        AudioClip klip = daftar[Random.Range(0, daftar.Length)];
        if (klip == null) return;

        float jarakNorm = Mathf.Clamp01(jarak / Mathf.Max(1f, jarakMaksSfx));

        // Menghilang halus di 40% jarak terakhir supaya tidak berhenti mendadak
        float pudarUjung = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(jarakMaksSfx * 0.6f, jarakMaksSfx, jarak));

        // Pohon besar terdengar lebih dalam, pohon kecil lebih tinggi
        float pitch = Random.Range(rentangPitch.x, rentangPitch.y) * Mathf.Clamp(10f / Mathf.Max(tinggiPohon, 1f), 0.75f, 1.25f);
        float tunda = simulasiKecepatanSuara ? Mathf.Min(jarak / KecepatanSuara, 3f) : 0f;

        GameObject go = new GameObject("SFX_Pohon");
        go.transform.position = posisi;

        AudioSource src = go.AddComponent<AudioSource>();
        src.clip = klip;
        src.spatialBlend = 1f;
        src.dopplerLevel = 0f;
        src.rolloffMode = AudioRolloffMode.Logarithmic;
        src.minDistance = 8f;
        src.maxDistance = Mathf.Max(jarakMaksSfx, 9f);
        src.volume = Mathf.Clamp01(volumeSfx * volumeRelatif * pudarUjung);
        src.pitch = pitch;

        AudioLowPassFilter lp = go.AddComponent<AudioLowPassFilter>();
        lp.cutoffFrequency = Mathf.Lerp(cutoffDekat, cutoffJauh, Mathf.Sqrt(jarakNorm));

        src.PlayDelayed(tunda);
        Destroy(go, tunda + klip.length / Mathf.Max(0.1f, pitch) + 0.5f);

        // Daftarkan supaya suara ini bisa dipudarkan saat pohonnya sudah diam
        if (fadeSaatPohonDiam && pohon != null)
        {
            daftarSuara.Add(new SuaraAktif
            {
                sumber = src,
                pohon = pohon,
                waktuMulai = Time.time + tunda
            });
        }
    }

    /// <summary>
    /// Ukur seberapa cepat pohon berputar. Kalau di bawah ambang secara terus-menerus
    /// selama jedaDeteksiDiam, pohon dianggap sudah tidak bergerak.
    /// </summary>
    private void DeteksiGerak(PohonAktif p)
    {
        float dtAsli = Mathf.Max(Time.deltaTime, 0.0001f);
        Quaternion sekarang = p.tf.rotation;
        float kecepatanSudut = Quaternion.Angle(p.rotasiTerakhir, sekarang) / dtAsli;
        p.rotasiTerakhir = sekarang;

        if (kecepatanSudut < ambangDiam) p.waktuDiam += dtAsli;
        else p.waktuDiam = 0f;
    }

    /// <summary>
    /// Untuk tiap suara yang sedang terdengar: kalau pohonnya sudah diam (dan suara sudah
    /// terdengar cukup lama), pudarkan perlahan lalu hentikan.
    /// </summary>
    private void PerbaruiSuara(float dt)
    {
        for (int i = daftarSuara.Count - 1; i >= 0; i--)
        {
            SuaraAktif s = daftarSuara[i];

            if (s.sumber == null)
            {
                daftarSuara.RemoveAt(i);
                continue;
            }

            // Suara masih dalam perjalanan (jarak jauh), belum terdengar
            if (Time.time < s.waktuMulai) continue;

            if (!s.memudar)
            {
                bool pohonDiam = s.pohon == null || s.pohon.selesai || s.pohon.waktuDiam >= jedaDeteksiDiam;
                s.waktuDiamSuara = pohonDiam ? s.waktuDiamSuara + dt : 0f;

                if (s.waktuDiamSuara < jedaSebelumFade) continue;

                s.memudar = true;
                s.waktuMulaiFade = Time.time;
                s.volumeSaatFade = s.sumber.volume;
            }

            float t = Mathf.Clamp01((Time.time - s.waktuMulaiFade) / Mathf.Max(0.05f, durasiFadeOut));
            s.sumber.volume = s.volumeSaatFade * (1f - Mathf.SmoothStep(0f, 1f, t));

            if (t >= 1f)
            {
                s.sumber.Stop();
                Destroy(s.sumber.gameObject);
                daftarSuara.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// Bangun ulang collider Terrain supaya collider pohon yang sudah dihapus ikut hilang.
    /// </summary>
    private void RefreshColliderTerrain(Terrain terrain)
    {
        TerrainCollider tc = terrain.GetComponent<TerrainCollider>();
        if (tc == null) return;

        tc.enabled = false;
        tc.enabled = true;
    }
}