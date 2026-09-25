using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteInEditMode]
public class TimeOfDayManager : MonoBehaviour
{
    [Header("Referensi Objek")]
    [Tooltip("Tarik Directional Light (Matahari) dari Hierarchy ke sini")]
    public Light sunLight; 
    
    [Header("Efek Langit Malam")]
    [Tooltip("Tarik objek Bintang_Malam dari Hierarchy ke sini")]
    public ParticleSystem bintangMalam;

    [Header("Pengaturan Waktu")]
    [Tooltip("Waktu dalam format 24 Jam (Misal 14.5 = Jam 14:30)")]
    [Range(0f, 24f)]
    public float currentTime = 8f; 

    [Header("Sistem Pengacakan (Saat Mulai)")]
    public bool randomizeOnStart = true;
    public float minRandomTime = 7.0f; // Jam 7 pagi
    public float maxRandomTime = 16.0f; // Jam 4 sore

    [Header("Pengaturan Warna Kabut (Fog)")]
    public bool gunakanWarnaKabutDinamis = true;
    public Color fogPagi = new Color(0.8f, 0.85f, 0.9f); 
    public Color fogSiang = new Color(0.9f, 0.9f, 0.95f);
    public Color fogSore = new Color(0.9f, 0.6f, 0.4f);  
    public Color fogMalam = new Color(0.05f, 0.05f, 0.08f); // Dibuat lebih gelap agar bintang terlihat jelas

    // Dipakai untuk menunda toggle bintang saat OnValidate (lihat UpdateBintang)
    private bool togglePendingSaatValidate;

    void Start()
    {
        if (Application.isPlaying && randomizeOnStart)
        {
            currentTime = Random.Range(minRandomTime, maxRandomTime);
            Debug.Log($"<color=yellow>[Sistem Cuaca]</color> Waktu dimulai secara acak pada pukul: <b>{currentTime:F1}</b>");
        }
        UpdateSunRotation();
    }

    void OnValidate()
    {
        // OnValidate berjalan di luar Play Mode (misalnya saat menggeser slider di Inspector).
        // Toggle GameObject bintang tidak boleh terjadi langsung di sini (lihat UpdateBintang),
        // jadi kita tunda satu frame lewat EditorApplication.delayCall.
        togglePendingSaatValidate = true;
        UpdateSunRotation();
    }

    void Update()
    {
        // Opsional: Jika Anda ingin membuat waktu berjalan otomatis (siang berganti malam secara realtime)
        // currentTime += Time.deltaTime * 0.05f; 
        // if (currentTime > 24f) currentTime = 0f;
        // UpdateSunRotation();
    }

    public void UpdateSunRotation()
    {
        if (sunLight == null) return;

        float sunRotationX = (currentTime / 24f) * 360f - 90f;
        sunLight.transform.rotation = Quaternion.Euler(sunRotationX, 30f, 0f);

        if (gunakanWarnaKabutDinamis)
        {
            UpdateFogColor();
        }
        
        UpdateBintang();
    }

    private void UpdateFogColor()
    {
        Color targetFogColor = fogMalam; 

        if (currentTime >= 5f && currentTime < 8f) 
        {
            float t = (currentTime - 5f) / 3f;
            targetFogColor = Color.Lerp(fogMalam, fogPagi, t);
        } 
        else if (currentTime >= 8f && currentTime < 15f) 
        {
            float t = (currentTime - 8f) / 7f;
            targetFogColor = Color.Lerp(fogPagi, fogSiang, t);
        } 
        else if (currentTime >= 15f && currentTime < 18f) 
        {
            float t = (currentTime - 15f) / 3f;
            targetFogColor = Color.Lerp(fogSiang, fogSore, t);
        } 
        else if (currentTime >= 18f && currentTime < 19f) 
        {
            float t = (currentTime - 18f) / 1f;
            targetFogColor = Color.Lerp(fogSore, fogMalam, t);
        } 
        else 
        {
            targetFogColor = fogMalam;
        }

        RenderSettings.fogColor = targetFogColor;
    }

    private void UpdateBintang()
    {
        if (bintangMalam == null) return;

        // Bintang aktif dari jam 18.00 sore hingga 05.30 pagi
        bool isMalam = (currentTime >= 18f || currentTime < 5.5f);

        if (bintangMalam.gameObject.activeSelf == isMalam) return;

#if UNITY_EDITOR
        if (togglePendingSaatValidate && !Application.isPlaying)
        {
            togglePendingSaatValidate = false;
            ParticleSystem target = bintangMalam;
            EditorApplication.delayCall += () =>
            {
                if (target == null) return;
                bool masihMalam = (currentTime >= 18f || currentTime < 5.5f);
                if (target.gameObject.activeSelf != masihMalam)
                {
                    target.gameObject.SetActive(masihMalam);
                    if (masihMalam && !target.isPlaying) target.Play();
                }
            };
            return;
        }
#endif

        bintangMalam.gameObject.SetActive(isMalam);
        if (isMalam && !bintangMalam.isPlaying)
        {
            bintangMalam.Play();
        }
    }
}