using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(MeshFilter))]
public class GeneratorTitikSesarOtomatis : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("Komponen AlurSesar yang list Titik Sesar-nya mau diisi otomatis")]
    public AlurSesar targetSesar;

    [Header("Pengaturan")]
    [Range(3, 20)] public int jumlahTitik = 6;
    [Tooltip("Hapus titik lama di target sebelum generate yang baru")]
    public bool hapusTitikLamaSebelumGenerate = true;

    [ContextMenu("Generate Titik dari Mesh Otomatis")]
    public void GenerateTitik()
    {
        MeshFilter mf = GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null)
        {
            Debug.LogError("<color=red>[GeneratorTitikSesar]</color> Tidak ada Mesh Filter/mesh di objek ini.");
            return;
        }

        Vector3[] verticesLocal = mf.sharedMesh.vertices;
        if (verticesLocal.Length < 2)
        {
            Debug.LogError("<color=red>[GeneratorTitikSesar]</color> Mesh tidak punya cukup vertex.");
            return;
        }

        // Konversi semua vertex mesh ke world space
        List<Vector3> verticesWorld = new List<Vector3>(verticesLocal.Length);
        foreach (Vector3 v in verticesLocal)
        {
            verticesWorld.Add(transform.TransformPoint(v));
        }

        // Cari sumbu memanjang tabung lewat algoritma "approximate diameter":
        // dari vertex sembarang, cari yang terjauh (P1); dari P1, cari yang
        // terjauh lagi (P2). Garis P1-P2 akan mendekati sumbu terpanjang
        // bentuk tabung/sesar, berapapun arah/orientasinya di scene.
        Vector3 acuan = verticesWorld[0];
        Vector3 p1 = CariTerjauh(verticesWorld, acuan);
        Vector3 p2 = CariTerjauh(verticesWorld, p1);
        Vector3 arah = (p2 - p1).normalized;

        // Proyeksikan tiap vertex ke sumbu arah tadi, dapat rentang posisi (t)
        float tMin = float.MaxValue, tMax = float.MinValue;
        List<float> daftarT = new List<float>(verticesWorld.Count);
        foreach (Vector3 v in verticesWorld)
        {
            float t = Vector3.Dot(v - p1, arah);
            daftarT.Add(t);
            if (t < tMin) tMin = t;
            if (t > tMax) tMax = t;
        }

        // Bagi sepanjang sumbu jadi 'jumlahTitik' segmen, tiap segmen ambil
        // RATA-RATA posisi semua vertex yang masuk situ - hasilnya pas di
        // tengah tabung (centerline), bukan menempel di permukaan luar mesh.
        List<Vector3> titikTengah = new List<Vector3>();
        for (int i = 0; i < jumlahTitik; i++)
        {
            float tAwal = Mathf.Lerp(tMin, tMax, i / (float)jumlahTitik);
            float tAkhir = Mathf.Lerp(tMin, tMax, (i + 1) / (float)jumlahTitik);

            Vector3 jumlahPosisi = Vector3.zero;
            int jumlahMasuk = 0;

            for (int j = 0; j < verticesWorld.Count; j++)
            {
                if (daftarT[j] >= tAwal && daftarT[j] <= tAkhir)
                {
                    jumlahPosisi += verticesWorld[j];
                    jumlahMasuk++;
                }
            }

            if (jumlahMasuk > 0)
            {
                titikTengah.Add(jumlahPosisi / jumlahMasuk);
            }
        }

        if (targetSesar == null)
        {
            Debug.LogError("<color=red>[GeneratorTitikSesar]</color> Target Sesar Lembang belum di-assign.");
            return;
        }

        if (hapusTitikLamaSebelumGenerate && targetSesar.titikSesar != null)
        {
            foreach (Transform t in targetSesar.titikSesar)
            {
                if (t != null) DestroyImmediate(t.gameObject);
            }
            targetSesar.titikSesar.Clear();
        }

        if (targetSesar.titikSesar == null) targetSesar.titikSesar = new List<Transform>();

        for (int i = 0; i < titikTengah.Count; i++)
        {
            GameObject titikBaru = new GameObject($"Titik_Auto_{i + 1}");
            titikBaru.transform.SetParent(targetSesar.transform, true);
            titikBaru.transform.position = titikTengah[i];
            targetSesar.titikSesar.Add(titikBaru.transform);
        }

        Debug.Log($"<color=green>[GeneratorTitikSesar]</color> Berhasil generate {titikTengah.Count} titik otomatis dari mesh.");
    }

    private Vector3 CariTerjauh(List<Vector3> daftarTitik, Vector3 dariTitik)
    {
        Vector3 terjauh = daftarTitik[0];
        float jarakTerjauh = 0f;
        foreach (Vector3 v in daftarTitik)
        {
            float jarak = Vector3.SqrMagnitude(v - dariTitik);
            if (jarak > jarakTerjauh)
            {
                jarakTerjauh = jarak;
                terjauh = v;
            }
        }
        return terjauh;
    }
}