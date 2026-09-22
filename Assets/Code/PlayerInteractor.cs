using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;

public class PlayerInteractor : MonoBehaviour
{
    [Header("Pengaturan Interaksi")]
    [Tooltip("Jarak maksimal tangan/mata bisa menjangkau benda (dalam meter)")]
    public float interactRange = 3f;

    [Tooltip("Sumber arah ray: Main Camera (PC) atau Right Controller (VR)")]
    public Transform rayOrigin;

    [Header("Input PC (opsional, kosongkan di instance VR)")]
    public bool gunakanInputPC = true;

    [Header("   Input VR (opsional, kosongkan di instance PC)")]
    [Tooltip("Contoh: XRI RightHand Interaction/Activate, atau tombol grip/trigger yang Anda pakai")]
    public InputActionReference tombolInteraksiVR;

    [Header("   Lari & Jongkok VR (isi HANYA di instance VR, kosongkan di instance PC)")]
    [Tooltip("Action tombol X controller kiri (Action Type: Button). Tahan untuk lari.")]
    public InputActionReference tombolLari;
    [Tooltip("Action tombol Y controller kiri (Action Type: Button). Tahan untuk jongkok.")]
    public InputActionReference tombolJongkok;
    [Tooltip("GameObject 'XR Origin (XR Rig)'. Kosongkan untuk dicari otomatis dari parent.")]
    public Transform akarXROrigin;

    [Tooltip("Kecepatan saat lari = kecepatan jalan x angka ini")]
    public float pengaliKecepatanLari = 1.8f;
    [Tooltip("Seberapa cepat kecepatan berubah saat tombol ditekan/dilepas. Makin kecil makin halus.")]
    public float akselerasi = 8f;
    [Tooltip("Seberapa turun (meter) posisi mata saat jongkok")]
    public float kedalamanJongkok = 0.6f;
    [Tooltip("Kecepatan turun/naik saat jongkok (meter per detik)")]
    public float kecepatanTransisiJongkok = 2.5f;
    [Range(0.1f, 1f)]
    [Tooltip("Kecepatan jalan saat jongkok = kecepatan jalan x angka ini")]
    public float pengaliKecepatanJongkok = 0.5f;

    // Diisi otomatis dari XR Origin (tidak perlu diisi manual)
    private ContinuousMoveProviderBase moveProvider;
    private Transform cameraOffsetVR;
    private CharacterController characterController;
    private CapsuleCollider capsuleCollider;
    private bool punyaDriver;

    private float kecepatanDasar;
    private float offsetJongkokSaatIni;
    private float ccHeightAwal;
    private Vector3 ccCenterAwal;
    private float capHeightAwal;
    private Vector3 capCenterAwal;

    private bool FiturLariJongkokAktif
    {
        get { return tombolLari != null || tombolJongkok != null; }
    }

    void Awake()
    {
        if (!FiturLariJongkokAktif) return;   // instance PC: fitur ini tidak dipakai

        if (akarXROrigin == null)
        {
            CharacterController ccInduk = GetComponentInParent<CharacterController>(true);
            akarXROrigin = ccInduk != null ? ccInduk.transform : transform.root;
        }

        moveProvider = akarXROrigin.GetComponentInChildren<ContinuousMoveProviderBase>(true);
        cameraOffsetVR = akarXROrigin.Find("Camera Offset");
        characterController = akarXROrigin.GetComponent<CharacterController>();
        capsuleCollider = akarXROrigin.GetComponent<CapsuleCollider>();

        // Kalau ada CharacterControllerDriver, dia sudah menyesuaikan CharacterController
        // dengan tinggi kamera secara otomatis, jadi tidak perlu diubah manual di sini.
        punyaDriver = akarXROrigin.GetComponent<CharacterControllerDriver>() != null;

        if (moveProvider != null) kecepatanDasar = moveProvider.moveSpeed;

        if (characterController != null)
        {
            ccHeightAwal = characterController.height;
            ccCenterAwal = characterController.center;
        }
        if (capsuleCollider != null)
        {
            capHeightAwal = capsuleCollider.height;
            capCenterAwal = capsuleCollider.center;
        }
    }

    void OnDisable()
    {
        if (!FiturLariJongkokAktif) return;

        // Kembalikan ke kondisi normal supaya tidak "nyangkut" saat pindah mode VR/PC
        if (moveProvider != null) moveProvider.moveSpeed = kecepatanDasar;
        if (offsetJongkokSaatIni != 0f) TerapkanOffsetJongkok(0f);
    }

    void Reset()
    {
        // fallback supaya field lama (playerCamera) tidak hilang total kalau ada referensi lama
        if (rayOrigin == null && GetComponent<Camera>() != null)
            rayOrigin = transform;
    }

    void Update()
    {
        if (FiturLariJongkokAktif) UpdateLariJongkok();

        if (rayOrigin == null) return;

        Ray ray = new Ray(rayOrigin.position, rayOrigin.forward);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, interactRange))
        {
            InteractableObject interactable = hit.collider.GetComponentInParent<InteractableObject>();

            if (interactable != null)
            {
                Debug.DrawLine(ray.origin, hit.point, Color.green);

                bool inputPC = gunakanInputPC && (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.E));
                bool inputVR = tombolInteraksiVR != null && tombolInteraksiVR.action != null
                               && tombolInteraksiVR.action.WasPressedThisFrame();

                if (inputPC || inputVR)
                {
                    interactable.Interact();
                }
            }
            else
            {
                Debug.DrawLine(ray.origin, hit.point, Color.yellow);
            }
        }
    }

    // ---------------------------------------------------------------
    //  LARI (tahan X) & JONGKOK (tahan Y) - khusus VR
    // ---------------------------------------------------------------

    private static bool TahanDitekan(InputActionReference referensi)
    {
        return referensi != null && referensi.action != null && referensi.action.IsPressed();
    }

    private void UpdateLariJongkok()
    {
        bool lari = TahanDitekan(tombolLari);
        bool jongkok = TahanDitekan(tombolJongkok);

        // Kecepatan: jongkok menang atas lari kalau keduanya ditahan
        if (moveProvider != null)
        {
            float target = kecepatanDasar;
            if (jongkok) target = kecepatanDasar * pengaliKecepatanJongkok;
            else if (lari) target = kecepatanDasar * pengaliKecepatanLari;

            moveProvider.moveSpeed = Mathf.MoveTowards(moveProvider.moveSpeed, target, akselerasi * Time.deltaTime);
        }

        // Tinggi mata: turun/naik halus
        float targetOffset = jongkok ? kedalamanJongkok : 0f;
        float baru = Mathf.MoveTowards(offsetJongkokSaatIni, targetOffset, kecepatanTransisiJongkok * Time.deltaTime);

        if (!Mathf.Approximately(baru, offsetJongkokSaatIni)) TerapkanOffsetJongkok(baru);
    }

    private void TerapkanOffsetJongkok(float offsetBaru)
    {
        float selisih = offsetBaru - offsetJongkokSaatIni;

        if (cameraOffsetVR != null)
        {
            Vector3 p = cameraOffsetVR.localPosition;
            p.y -= selisih;
            cameraOffsetVR.localPosition = p;
        }

        offsetJongkokSaatIni = offsetBaru;

        if (characterController != null && !punyaDriver)
        {
            float tinggi = Mathf.Max(ccHeightAwal - offsetJongkokSaatIni, characterController.radius * 2f);
            characterController.height = tinggi;
            characterController.center = new Vector3(ccCenterAwal.x, ccCenterAwal.y - (ccHeightAwal - tinggi) / 2f, ccCenterAwal.z);
        }

        if (capsuleCollider != null)
        {
            float tinggi = Mathf.Max(capHeightAwal - offsetJongkokSaatIni, capsuleCollider.radius * 2f);
            capsuleCollider.height = tinggi;
            capsuleCollider.center = new Vector3(capCenterAwal.x, capCenterAwal.y - (capHeightAwal - tinggi) / 2f, capCenterAwal.z);
        }
    }
}