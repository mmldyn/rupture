using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

[System.Serializable]
public class RealEarthquakeData
{
    public string eventName = "Simulasi Sesar Lembang"; 
    public float duration = 15f;
    [Tooltip("Input nilai Skala Richter asli dari lapangan")]
    public float richterScale = 5.0f; 
    [Tooltip("Kedalaman hiposenter gempa dalam kilometer - makin dangkal, makin terasa kuat")]
    public float kedalamanKm = 10f;
    [Tooltip("Apakah lokasi berdiri di atas tanah lunak/aluvial (memperkuat guncangan)")]
    public bool tanahLunak = false;
    [HideInInspector] 
    public float unityMagnitude; 
    public float fadeInTime = 2f;
    public float fadeOutTime = 3f;
}

public class EarthquakeSimulator : MonoBehaviour
{
    [Header("Efek Partikel Lingkungan")]
    public ParticleSystem[] kumpulanEfekDebu;

    [Header("Referensi GPS Evakuasi")]
    public GameObject gpsLineObject; 

    [Header("Database Gempa Dunia Nyata")]
    public List<RealEarthquakeData> earthquakeDatabase;

    [Header("Referensi UI & Audio")]
    public PanicUIManager panicUI;
    public StatusGempaHUD statusHUD; 
    [Tooltip("Masukkan komponen AudioSource untuk suara gemuruh gempa")]
    public AudioSource earthquakeAudioSource; 
    [Tooltip("Volume maksimal suara saat gempa mencapai puncak (0.0 - 1.0)")]
    public float maxAudioVolume = 1f;         

    [Header("Variasi Dinamis")]
    public bool addRandomVariance = true;
    public float durationVariance = 2.0f;
    public float richterVariance = 0.2f;

    [Header("Pengaturan Fisika Objek")]
    public float objectShakeForce = 30f;

    [Header("Referensi Objek")]
    public Transform cameraOffset; 

    [Header("Haptic Feedback (VR)")]
    public XRBaseController leftController;
    public XRBaseController rightController;
    [Range(0f, 1f)] public float kekuatanHapticMaksimal = 0.8f;
    public float srReferensiMin = 3f;
    public float srReferensiMaks = 9f;
    [Range(0f, 1f)] public float kekuatanHapticMinimal = 0.15f;

    [Header("Retak Dinamis")]
    public ManajerRetakDinamis manajerRetakDinding;
    public ManajerRetakDinamis manajerRetakLantai;

    [Header("Rumus Perhitungan Kekuatan Gempa (HitungUM)")]
    [Tooltip("SR di bawah nilai ini dianggap tidak terasa sama sekali")]
    public float magnitudeThreshold = 3.0f;
    public float baseSlope = 0.075f;

    [Header("Koreksi Kedalaman Hiposenter")]
    public float referenceDepthKm = 15f;
    public float maxShallowBonus = 1.5f;

    [Header("Koreksi Kondisi Tanah")]
    public float softSoilBonus = 0.8f;

    [Header("Jarak dari Pusat Gempa")]
    [HideInInspector]
    public Transform pusatGempa;
    [Tooltip("Berapa unit Unity setara 1 km (default 1000, asumsi 1 unit = 1 meter). Selalu dipakai di semua mode.")]
    public float unitPerKm = 1000f;

    [Header("Episentrum Acak")]
    [HideInInspector]
    public bool jarakAcak = true;
    [HideInInspector]
    public float jarakMinKm = 2f;
    [HideInInspector]
    public float jarakMaksKm = 40f;

    [Header("Sampling dari Alur Sesar (opsional, prioritas tertinggi)")]
    [Tooltip("Kalau diisi, episentrum akan diambil dari titik acak SEPANJANG garis sesar ini, bukan lingkaran acak sembarang arah")]
    public SesarLembang sesarLembang;

    [Header("Bobot Guncangan Per-Sumbu (3 Axis)")]
    [Tooltip("Kalikan kekuatan guncangan per sumbu. X=kiri-kanan, Y=atas-bawah, Z=depan-belakang. Set ke 0 untuk mematikan sumbu tertentu.")]
    public Vector3 bobotSumbuGuncangan = new Vector3(1f, 1f, 1f);

    [Header("Referensi Player untuk Hitung Jarak (VR/PC otomatis)")]
    public Transform playerVR;
    public Transform playerPC;

    private Vector3 originalLocalPos;
    public bool isQuaking = false;
    private Coroutine gempaCoroutineAktif;
    private RealEarthquakeData dataGempaAktif;
    private Vector3 posisiEpisentrumSaatIni;

    void Start()
    {
        if (cameraOffset != null) originalLocalPos = cameraOffset.localPosition;

        if (earthquakeDatabase.Count == 0)
        {
            earthquakeDatabase.Add(new RealEarthquakeData());
        }

        if (earthquakeAudioSource != null)
        {
            earthquakeAudioSource.volume = 0f;
            earthquakeAudioSource.loop = true; 
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.R) && !isQuaking)
        {
            MulaiGempaAcak();
        }
    }

    /// <summary>
    /// Menghitung kekuatan guncangan efektif (0-1) berdasarkan SR, kedalaman
    /// hiposenter, kondisi tanah, dan jarak dari pusat gempa.
    /// </summary>
    public float HitungUM(float sr, float kedalamanKm, bool tanahLunak, float jarakKm = 0f)
    {
        float koreksiDangkal = maxShallowBonus * Mathf.Clamp01((referenceDepthKm - kedalamanKm) / referenceDepthKm);
        float koreksiTanah = tanahLunak ? softSoilBonus : 0f;

        float srEfektif = sr + koreksiDangkal + koreksiTanah;
        float um = Mathf.Clamp01((srEfektif - magnitudeThreshold) * baseSlope);

        if (jarakKm > 0f)
            um *= 1f / (1f + Mathf.Pow(jarakKm / 8f, 1.5f));

        return um;
    }

    /// <summary>
    /// Hitung jarak dari pemain (VR/PC, otomatis pilih yang aktif) ke episentrum
    /// gempa saat ini, dalam kilometer. Kalau 'jarakAcak' aktif, pakai posisi
    /// episentrum acak yang di-generate saat gempa dimulai. Kalau tidak,
    /// pakai Transform 'pusatGempa' tetap. Mengembalikan 0 kalau keduanya
    /// tidak tersedia (jarak diabaikan, cuma SR yang berpengaruh).
    /// </summary>
    private float HitungJarakKmSaatIni()
    {
        Transform playerAktif = TentukanPlayerAktif();
        if (playerAktif == null) return 0f;

        if (jarakAcak || sesarLembang != null)
        {
            float jarakUnit = Vector3.Distance(posisiEpisentrumSaatIni, playerAktif.position);
            return jarakUnit / unitPerKm;
        }

        if (pusatGempa == null) return 0f;

        float jarakUnitTetap = Vector3.Distance(pusatGempa.position, playerAktif.position);
        return jarakUnitTetap / unitPerKm;
    }

    /// <summary>
    /// Generate posisi episentrum untuk gempa baru. Prioritas:
    /// 1. Kalau 'sesarLembang' diisi, ambil titik acak SEPANJANG garis sesar itu.
    /// 2. Kalau tidak, generate lingkaran acak (arah + jarak) dari posisi pemain.
    /// </summary>
    private void GenerateEpisentrumAcak()
    {
        if (sesarLembang != null)
        {
            posisiEpisentrumSaatIni = sesarLembang.AmbilTitikAcakSepanjangSesar();
            Debug.Log($"<color=magenta>[Episentrum]</color> Diambil dari alur Sesar Lembang: {posisiEpisentrumSaatIni}");
            return;
        }

        Transform playerAktif = TentukanPlayerAktif();
        Vector3 posisiAcuan = playerAktif != null ? playerAktif.position : Vector3.zero;

        float sudutAcak = Random.Range(0f, 360f);
        float jarakKmAcak = Random.Range(jarakMinKm, jarakMaksKm);
        float jarakUnitAcak = jarakKmAcak * unitPerKm;

        Vector3 arahAcak = Quaternion.Euler(0, sudutAcak, 0) * Vector3.forward;
        posisiEpisentrumSaatIni = posisiAcuan + arahAcak * jarakUnitAcak;

        Debug.Log($"<color=magenta>[Episentrum]</color> Lingkaran acak: {jarakKmAcak:F1} km, arah: {sudutAcak:F0}\u00b0");
    }

    private Transform TentukanPlayerAktif()
    {
        if (playerVR != null && playerVR.gameObject.activeInHierarchy)
            return playerVR;

        if (playerPC != null && playerPC.gameObject.activeInHierarchy)
            return playerPC;

        return null;
    }

    public void MulaiGempaAcak()
    {
        if (isQuaking) return;

        int randomIndex = Random.Range(0, earthquakeDatabase.Count);
        RealEarthquakeData selectedBaseData = earthquakeDatabase[randomIndex];

        RealEarthquakeData finalDataToPlay = new RealEarthquakeData
        {
            eventName = selectedBaseData.eventName,
            duration = selectedBaseData.duration,
            richterScale = selectedBaseData.richterScale,
            kedalamanKm = selectedBaseData.kedalamanKm,
            tanahLunak = selectedBaseData.tanahLunak,
            fadeInTime = selectedBaseData.fadeInTime,
            fadeOutTime = selectedBaseData.fadeOutTime
        };

        if (addRandomVariance)
        {
            finalDataToPlay.duration += Random.Range(-durationVariance, durationVariance);
            float minDuration = finalDataToPlay.fadeInTime + finalDataToPlay.fadeOutTime + 1f;
            finalDataToPlay.duration = Mathf.Max(finalDataToPlay.duration, minDuration);
            finalDataToPlay.richterScale += Random.Range(-richterVariance, richterVariance);
        }

        if (jarakAcak || sesarLembang != null) GenerateEpisentrumAcak();

        float jarakKmAwal = HitungJarakKmSaatIni();
        finalDataToPlay.unityMagnitude = HitungUM(finalDataToPlay.richterScale, finalDataToPlay.kedalamanKm, finalDataToPlay.tanahLunak, jarakKmAwal);

        // --- LOG RINGKASAN GEMPA YANG DIPILIH ---
        Debug.Log(
            $"<color=#FF0E0E><b>[Gempa Dipilih]</b></color> " +
            $"Event: <b>{finalDataToPlay.eventName}</b> | " +
            $"SR: <b>{finalDataToPlay.richterScale:F2}</b> | " +
            $"Durasi: <b>{finalDataToPlay.duration:F1} detik</b> | " +
            $"Jarak Episentrum: <b>{jarakKmAwal:F2} km</b> | " +
            $"Kedalaman: {finalDataToPlay.kedalamanKm:F1} km | " +
            $"Tanah Lunak: {finalDataToPlay.tanahLunak} | " +
            $"Kekuatan Terasa (UM): {finalDataToPlay.unityMagnitude:F3}"
        );

        gempaCoroutineAktif = StartCoroutine(SimulateEarthquake(finalDataToPlay));
    }

    public void HentikanPaksa()
    {
        if (!isQuaking) return;

        if (gempaCoroutineAktif != null)
        {
            StopCoroutine(gempaCoroutineAktif);
            gempaCoroutineAktif = null;
        }

        if (cameraOffset != null) cameraOffset.localPosition = originalLocalPos;
        isQuaking = false;

        if (earthquakeAudioSource != null)
        {
            earthquakeAudioSource.Stop();
            earthquakeAudioSource.volume = 0f;
        }

        PanicUIManager uiAktif = FindObjectOfType<PanicUIManager>();
        StatusGempaHUD statusAktif = FindObjectOfType<StatusGempaHUD>();
        if (statusAktif != null) statusAktif.ResetStatus();
        if (uiAktif != null) uiAktif.HentikanEfekPanik();

        foreach (ParticleSystem debu in kumpulanEfekDebu)
        {
            if (debu != null) debu.Stop();
        }

        Debug.Log("<color=orange>[Sistem Bencana]</color> Simulasi dihentikan paksa.");
    }

    IEnumerator SimulateEarthquake(RealEarthquakeData activeData)
    {
        isQuaking = true;
        dataGempaAktif = activeData;
        float elapsed = 0.0f;

        PanicUIManager uiAktif = FindObjectOfType<PanicUIManager>();
        StatusGempaHUD statusAktif = FindObjectOfType<StatusGempaHUD>();

        if (uiAktif != null) uiAktif.MulaiPeringatanAwal(); 

        bool isPanicUITriggered = false;

        if (statusAktif != null) statusAktif.TampilkanStatus(activeData.richterScale);

        foreach (ParticleSystem debu in kumpulanEfekDebu)
        {
            if (debu != null) debu.Play();
        }

        if (earthquakeAudioSource != null)
        {
            earthquakeAudioSource.volume = 0f;
            earthquakeAudioSource.Play();
        }

        Rigidbody[] allPhysicalObjects = FindObjectsOfType<Rigidbody>();

        if (manajerRetakDinding == null || manajerRetakLantai == null)
        {
            ManajerRetakDinamis[] semuaManajer = FindObjectsOfType<ManajerRetakDinamis>();
            foreach (ManajerRetakDinamis m in semuaManajer)
            {
                if (manajerRetakDinding == null && m.tagPermukaan == "Dinding") manajerRetakDinding = m;
                if (manajerRetakLantai == null && m.tagPermukaan == "Lantai") manajerRetakLantai = m;
            }
        }

        float waktuUpdateJarakBerikutnya = 0f;

        while (elapsed < activeData.duration)
        {
            // --- REAL-TIME: hitung ulang jarak & kekuatan setiap 0.5 detik ---
            // (tidak tiap frame, supaya tidak boros - jarak tidak berubah drastis dalam sepersekian detik)
            if (elapsed >= waktuUpdateJarakBerikutnya)
            {
                float jarakKmSekarang = HitungJarakKmSaatIni();
                activeData.unityMagnitude = HitungUM(activeData.richterScale, activeData.kedalamanKm, activeData.tanahLunak, jarakKmSekarang);
                waktuUpdateJarakBerikutnya = elapsed + 0.5f;
            }

            float currentMagnitude = activeData.unityMagnitude;
            float audioLerpProgress = 1f; 

            if (elapsed < activeData.fadeInTime)
            {
                float t = elapsed / activeData.fadeInTime;
                currentMagnitude = Mathf.Lerp(0f, activeData.unityMagnitude, t);
                audioLerpProgress = t; 
            }
            else 
            {
                if (!isPanicUITriggered)
                {
                    if (uiAktif != null) uiAktif.MulaiEfekPanik(); 
                    isPanicUITriggered = true;
                    Debug.Log("<color=orange>[Sistem Bencana]</color> Guncangan Puncak! UI Panik Penuh Aktif.");
                }

                if (elapsed > (activeData.duration - activeData.fadeOutTime))
                {
                    float fadeOutElapsed = elapsed - (activeData.duration - activeData.fadeOutTime);
                    float t = fadeOutElapsed / activeData.fadeOutTime;
                    currentMagnitude = Mathf.Lerp(activeData.unityMagnitude, 0f, t);
                    audioLerpProgress = 1f - t; 
                }
            }

            if (earthquakeAudioSource != null)
                earthquakeAudioSource.volume = audioLerpProgress * maxAudioVolume;

            float x = originalLocalPos.x + Random.Range(-1f, 1f) * currentMagnitude * bobotSumbuGuncangan.x;
            float y = originalLocalPos.y + Random.Range(-1f, 1f) * currentMagnitude * bobotSumbuGuncangan.y;
            float z = originalLocalPos.z + Random.Range(-1f, 1f) * currentMagnitude * bobotSumbuGuncangan.z;
            cameraOffset.localPosition = new Vector3(x, y, z);

            KirimHapticGempa(currentMagnitude, activeData.richterScale);
            if (manajerRetakDinding != null) manajerRetakDinding.PerbaruiRetak(currentMagnitude, Time.deltaTime);
            if (manajerRetakLantai != null) manajerRetakLantai.PerbaruiRetak(currentMagnitude, Time.deltaTime);

            foreach (Rigidbody rb in allPhysicalObjects)
            {
                if (rb != null && rb.gameObject.name != "Player_Dummy_Rig" && !rb.isKinematic)
                {
                    if (rb.IsSleeping()) rb.WakeUp();
                    Vector3 randomJolt = new Vector3(
                        Random.Range(-1f, 1f), Random.Range(-0.5f, 0.5f), Random.Range(-1f, 1f)
                    );
                    rb.AddForce(randomJolt * currentMagnitude * objectShakeForce * rb.mass, ForceMode.Force);
                }
            }
            
            elapsed += Time.deltaTime;
            float sisaWaktu = activeData.duration - elapsed;
            
            if (statusAktif != null) statusAktif.UpdateWaktuCountdown(sisaWaktu);

            yield return null;
        }

        cameraOffset.localPosition = originalLocalPos;
        isQuaking = false;

        if (earthquakeAudioSource != null)
        {
            earthquakeAudioSource.Stop();
            earthquakeAudioSource.volume = 0f;
        }
        
        Debug.Log($"<color=green>[Sistem Bencana]</color> Simulasi selesai.");

        if (statusAktif != null) statusAktif.ResetStatus();
        if (uiAktif != null) uiAktif.HentikanEfekPanik();

        foreach (ParticleSystem debu in kumpulanEfekDebu)
        {
            if (debu != null) debu.Stop();
        }

        if (gpsLineObject != null) gpsLineObject.SetActive(true); 
    }

    private void KirimHapticGempa(float magnitudeSaatIni, float srGempa)
    {
        float bentukGuncangan = Mathf.Clamp01(magnitudeSaatIni / 0.6f);
        float posisiSR = Mathf.InverseLerp(srReferensiMin, srReferensiMaks, srGempa);
        float kekuatanDasar = Mathf.Lerp(kekuatanHapticMinimal, 1f, posisiSR);

        float amplitude = bentukGuncangan * kekuatanDasar * kekuatanHapticMaksimal;
        float durasi = Time.deltaTime;

        if (leftController != null) leftController.SendHapticImpulse(amplitude, durasi);
        if (rightController != null) rightController.SendHapticImpulse(amplitude, durasi);
    }
}