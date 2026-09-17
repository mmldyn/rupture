using System.Collections.Generic;
using UnityEngine;

public class SesarLembang : MonoBehaviour
{
    [Header("Titik-titik Sepanjang Alur Sesar")]
    [Tooltip("Urutkan sesuai alur garis sesar dari satu ujung ke ujung lain")]
    public List<Transform> titikSesar = new List<Transform>();

    [Header("Visualisasi (opsional, cuma untuk Editor)")]
    public bool tampilkanGarisDiEditor = true;
    public Color warnaGaris = Color.red;

    /// <summary>
    /// Ambil satu titik acak di SEPANJANG garis sesar (interpolasi antar dua
    /// titik terdekat yang berurutan), bukan cuma salah satu titik yang sudah
    /// ada. Mengembalikan Vector3.zero kalau titik sesar kurang dari 2.
    /// </summary>
    public Vector3 AmbilTitikAcakSepanjangSesar()
    {
        if (titikSesar == null || titikSesar.Count < 2)
        {
            Debug.LogWarning("<color=yellow>[SesarLembang]</color> Titik sesar kurang dari 2, tidak bisa sampling sepanjang garis.");
            return titikSesar != null && titikSesar.Count == 1 ? titikSesar[0].position : Vector3.zero;
        }

        // Pilih segmen acak (antara titik ke-i dan ke-i+1)
        int indexSegmen = Random.Range(0, titikSesar.Count - 1);
        Vector3 titikA = titikSesar[indexSegmen].position;
        Vector3 titikB = titikSesar[indexSegmen + 1].position;

        float t = Random.Range(0f, 1f);
        return Vector3.Lerp(titikA, titikB, t);
    }

    /// <summary>
    /// Total panjang alur sesar (meter/unit), berguna kalau ingin sampling
    /// yang proporsional terhadap panjang tiap segmen (segmen panjang lebih
    /// sering kepilih daripada segmen pendek). Opsional, tidak dipakai versi dasar.
    /// </summary>
    public float HitungPanjangTotal()
    {
        float total = 0f;
        for (int i = 0; i < titikSesar.Count - 1; i++)
        {
            total += Vector3.Distance(titikSesar[i].position, titikSesar[i + 1].position);
        }
        return total;
    }

    void OnDrawGizmos()
    {
        if (!tampilkanGarisDiEditor || titikSesar == null || titikSesar.Count < 2) return;

        Gizmos.color = warnaGaris;
        for (int i = 0; i < titikSesar.Count - 1; i++)
        {
            if (titikSesar[i] != null && titikSesar[i + 1] != null)
            {
                Gizmos.DrawLine(titikSesar[i].position, titikSesar[i + 1].position);
                Gizmos.DrawSphere(titikSesar[i].position, 0.5f);
            }
        }
        if (titikSesar[titikSesar.Count - 1] != null)
        {
            Gizmos.DrawSphere(titikSesar[titikSesar.Count - 1].position, 0.5f);
        }
    }
}