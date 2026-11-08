/*
===============================================================================
Script:
RTSCamera

Purpose:
Handles keyboard, edge-scroll, zoom, rotation, and drag camera movement.

Folder:
Assets/Scripts/Camera/

Scene Hierarchy:
_Systems
- Main Camera

Attach To:
Main Camera

Required Children:
None

Dependencies:
- Unity Input System

Automatically Resolved:
None

Inspector References:
- Movement, zoom, rotation, and drag tuning values
===============================================================================
*/

using UnityEngine;
using UnityEngine.InputSystem;

public class RTSCamera : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 20f;
    [SerializeField] private float edgeSize = 10f;

    [Header("Zoom")]
    [SerializeField] private float zoomStep = 5f;
    [SerializeField] private float zoomSmoothSpeed = 10f;
    [SerializeField] private float minHeight = 8f;
    [SerializeField] private float maxHeight = 40f;

    [Header("Rotation")]
    [SerializeField] private float rotationSpeed = 90f;

    [Header("Drag")]
    [SerializeField] private float dragSpeed = 0.03f;

    private Vector2 lastMousePosition;
    private float targetHeight;

    private void Start()
    {
        targetHeight = transform.position.y;
    }

    private void Update()
    {
        if (Keyboard.current == null || Mouse.current == null)
            return;

        Move();
        ReadZoomInput();
        ApplySmoothZoom();
        Rotate();
        Drag();
    }

    private void Move()
    {
        Vector3 move = Vector3.zero;

        if (Keyboard.current.wKey.isPressed)
            move += transform.forward;

        if (Keyboard.current.sKey.isPressed)
            move -= transform.forward;

        if (Keyboard.current.aKey.isPressed)
            move -= transform.right;

        if (Keyboard.current.dKey.isPressed)
            move += transform.right;

        Vector2 mouse = Mouse.current.position.ReadValue();

        if (mouse.x <= edgeSize)
            move -= transform.right;

        if (mouse.x >= Screen.width - edgeSize)
            move += transform.right;

        if (mouse.y <= edgeSize)
            move -= transform.forward;

        if (mouse.y >= Screen.height - edgeSize)
            move += transform.forward;

        move.y = 0f;

        transform.position += move.normalized * moveSpeed * Time.deltaTime;
    }

    private void ReadZoomInput()
    {
        float scroll = Mouse.current.scroll.ReadValue().y;

        if (Mathf.Abs(scroll) < 0.01f)
            return;

        // Scrolling up zooms closer to the ground.
        targetHeight -= Mathf.Sign(scroll) * zoomStep;
        targetHeight = Mathf.Clamp(targetHeight, minHeight, maxHeight);
    }

    private void ApplySmoothZoom()
    {
        Vector3 pos = transform.position;

        pos.y = Mathf.Lerp(
            pos.y,
            targetHeight,
            zoomSmoothSpeed * Time.deltaTime
        );

        transform.position = pos;
    }

    private void Rotate()
    {
        if (Keyboard.current.qKey.isPressed)
            transform.Rotate(Vector3.up, -rotationSpeed * Time.deltaTime, Space.World);

        if (Keyboard.current.eKey.isPressed)
            transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.World);
    }

    private void Drag()
    {
        if (Mouse.current.middleButton.wasPressedThisFrame)
            lastMousePosition = Mouse.current.position.ReadValue();

        if (Mouse.current.middleButton.isPressed)
        {
            Vector2 current = Mouse.current.position.ReadValue();
            Vector2 delta = current - lastMousePosition;

            Vector3 move =
                (-transform.right * delta.x +
                 -transform.forward * delta.y) * dragSpeed;

            move.y = 0f;

            transform.position += move;

            lastMousePosition = current;
        }
    }
}
