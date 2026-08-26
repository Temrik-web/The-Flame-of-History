using UnityEngine;

namespace FlameOfHistory.AI
{
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(CharacterHealth))]
public sealed class PlayerCharacter : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera playerCamera;
    [SerializeField] private HitscanWeapon weapon;

    [Header("Movement")]
    [SerializeField, Min(0f)] private float walkSpeed = 4f;
    [SerializeField, Min(0f)] private float runSpeed = 7f;
    [SerializeField, Min(0f)] private float gravity = 20f;

    [Header("Look")]
    [SerializeField, Min(0f)] private float mouseSensitivity = 2f;
    [SerializeField, Range(1f, 89f)] private float verticalLimit = 85f;

    [Header("Noise")]
    [SerializeField, Min(0f)] private float runningNoiseRadius = 10f;
    [SerializeField, Min(0.05f)] private float footstepInterval = 0.4f;

    private CharacterController _controller;
    private CharacterHealth _health;

    private float _verticalVelocity;
    private float _pitch;
    private float _nextFootstepTime;

    private void Awake()
    {
        _controller = GetComponent<CharacterController>();
        _health = GetComponent<CharacterHealth>();
    }

    private void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Update()
    {
        if (!_health.IsAlive)
            return;

        UpdateLook();
        UpdateMovement();
        UpdateWeapon();
    }

    private void UpdateLook()
    {
        float yaw = Input.GetAxisRaw("Mouse X") * mouseSensitivity;
        float pitchDelta = Input.GetAxisRaw("Mouse Y") * mouseSensitivity;

        transform.Rotate(Vector3.up, yaw);

        _pitch = Mathf.Clamp(_pitch - pitchDelta, -verticalLimit, verticalLimit);
        playerCamera.transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
    }

    private void UpdateMovement()
    {
        Vector2 input = new(
            Input.GetAxisRaw("Horizontal"),
            Input.GetAxisRaw("Vertical"));

        input = Vector2.ClampMagnitude(input, 1f);

        bool running = Input.GetKey(KeyCode.LeftShift) && input.y > 0f;
        float speed = running ? runSpeed : walkSpeed;

        Vector3 horizontalVelocity =
            (transform.right * input.x + transform.forward * input.y) * speed;

        if (_controller.isGrounded && _verticalVelocity < 0f)
            _verticalVelocity = -2f;
        else
            _verticalVelocity -= gravity * Time.deltaTime;

        Vector3 velocity = horizontalVelocity + Vector3.up * _verticalVelocity;
        _controller.Move(velocity * Time.deltaTime);

        if (running && _controller.isGrounded &&
            input.sqrMagnitude > 0.1f && Time.time >= _nextFootstepTime)
        {
            _nextFootstepTime = Time.time + footstepInterval;
            NoiseSystem.Emit(transform.position, runningNoiseRadius, gameObject);
        }
    }

    private void UpdateWeapon()
    {
        if (weapon == null || playerCamera == null)
            return;

        if (Input.GetKey(KeyCode.R))
            weapon.BeginReload();

        if (!Input.GetMouseButton(0))
            return;

        Ray aimRay = new(
            playerCamera.transform.position,
            playerCamera.transform.forward);

        Vector3 targetPoint = aimRay.origin + aimRay.direction * 500f;
        weapon.TryFire(targetPoint, gameObject);
    }
}
}
