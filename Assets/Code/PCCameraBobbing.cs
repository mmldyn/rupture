using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

public class VRHeadBob : MonoBehaviour
{
    [Header("Referensi")]
    public Transform cameraOffsetTransform;

    [Header("Pengaturan Kecepatan Normal")]
    public float normalBobSpeed = 8f;
    public float normalBobAmount = 0.03f;

    [Header("Pengaturan Saat Lari (Shift)")]
    public float sprintBobSpeed = 15f;
    public float sprintBobAmount = 0.06f;
    
    private float defaultPosY = 0f;
    private float timer = 0f;

    void Start()
    {
        if (cameraOffsetTransform == null) cameraOffsetTransform = transform;
        defaultPosY = cameraOffsetTransform.localPosition.y;
    }

    void Update()
    {
        bool isMoving = CheckPlayerMoving();
        bool isSprinting = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        if (isMoving)
        {
            // Pilih kecepatan & besar goyangan berdasarkan apakah tombol Shift ditekan
            float currentSpeed = isSprinting ? sprintBobSpeed : normalBobSpeed;
            float currentAmount = isSprinting ? sprintBobAmount : normalBobAmount;

            timer += Time.deltaTime * currentSpeed;
            float targetY = defaultPosY + Mathf.Sin(timer) * currentAmount;
            
            cameraOffsetTransform.localPosition = new Vector3(
                cameraOffsetTransform.localPosition.x, 
                targetY, 
                cameraOffsetTransform.localPosition.z
            );
        }
        else
        {
            // Kembalikan perlahan ke posisi normal saat berhenti
            timer = 0;
            Vector3 pos = cameraOffsetTransform.localPosition;
            pos.y = Mathf.Lerp(pos.y, defaultPosY, Time.deltaTime * normalBobSpeed);
            cameraOffsetTransform.localPosition = pos;
        }
    }

    bool CheckPlayerMoving()
    {
        // Mendeteksi gerakan dari keyboard atau thumbstick analog
        return Input.GetAxis("Vertical") != 0 || Input.GetAxis("Horizontal") != 0;
    }
}