# Prototype Third-Person Movement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task by task. Steps use checkbox syntax for tracking.

**Goal:** Add a temporary third-person controller to `Prototype` using WASD + mouse and `CharacterController`.

**Architecture:** One `MonoBehaviour` on the existing capsule reads `Move` and `Look` from the existing Input System asset. It applies camera-relative movement, gravity and rotation, and orbits the assigned Main Camera around the capsule. Keeping this as one prototype component avoids adding a camera package or restructuring the scene.

**Tech Stack:** Unity `6000.3.28f1`, C#, Universal Render Pipeline `17.3.0`, Input System `1.20.1`.

**Spec:** `Docs/superpowers/specs/2026-11-04-player-movement-design.md`

## Global Constraints

- Use `Assets/Scenes/Prototype.unity`; preserve `SampleScene`.
- Use the existing `Assets/InputSystem_Actions.inputactions` actions `Player/Move` and `Player/Look`.
- The current prototype uses WASD + mouse and `CharacterController`; it does not add jump, combat, animation, or network synchronization.
- The project-wide multiplayer requirement (up to five players) remains; this movement experiment is local.
- Add no third-party packages.

## Review Focus

- Missing input asset, map, or action: report the missing reference clearly instead of throwing a null-reference exception.
- Missing camera reference: report it clearly and stop controller initialization.
- Diagonal input: cap the direction vector so diagonal movement is not faster than straight movement.
- Grounded with no input: gravity keeps the controller on the plane without pulling it through the floor.
- Mouse pitch limits: looking up/down cannot invert the camera.

---

### Task 1: Build and wire the prototype controller

**Files:**
- Create: `Game/ImNotAnAlien/Assets/Scripts/Prototype/PrototypeThirdPersonController.cs`
- Scene setup: attach the component to the existing `Capsule` in `Assets/Scenes/Prototype.unity` and assign the existing Input System asset and `Main Camera` in the Inspector.

**Interfaces:**
- Consumes: `InputActionAsset` with action map `Player` and actions `Move` (`Vector2`) and `Look` (`Vector2`); a `Transform` for Main Camera.
- Produces: `PrototypeThirdPersonController : MonoBehaviour`, requiring a `CharacterController` on its GameObject.
- Serialized fields: `inputActions`, `cameraTransform`, `moveSpeed = 4.5`, `lookSensitivity = 0.1` degrees per pointer pixel, `cameraDistance = 6`, `cameraFocusHeight = 1.2`, `minPitch = 5`, `maxPitch = 65`, and `turnSpeed = 12`.

- [x] **Step 1: Create `PrototypeThirdPersonController`.** Add the serialized fields above, find `Player/Move` and `Player/Look`, enable only the `Player` action map, and log a clear error then disable the component if the asset, map, either action, or camera is missing.
- [x] **Step 2: Implement camera-relative walking.** Read `Move`, project camera forward/right onto the ground plane, cap the combined vector at magnitude 1, rotate the capsule toward movement using `turnSpeed`, apply gravity at `-20 m/s²` with a grounded downward bias of `-2 m/s`, and call `CharacterController.Move` each frame.
- [x] **Step 3: Implement mouse orbit.** Read `Look` and update yaw and pitch using `lookSensitivity` in `Update`; clamp pitch to `5–65` degrees. In `LateUpdate`, position the camera at `cameraDistance` from a focus point `cameraFocusHeight` above the capsule. Initialize yaw and pitch from the assigned camera so it does not snap to a new angle on the first frame.
- [x] **Step 4: Fit the controller to the existing capsule.** Set controller height to `2`, radius to `0.5`, center to `(0, 0, 0)`, step offset to `0.3`, and slope limit to `45` degrees. Disable the primitive `CapsuleCollider` to avoid duplicate collision shapes.
- [x] **Step 5: Wire the scene in Unity.** Attach the script to `Capsule`, assign `InputSystem_Actions.inputactions` and `Main Camera`, and save `Prototype`.
- [ ] **Step 6: Human acceptance check in Play Mode.** Confirm WASD walks relative to camera direction, mouse orbits without exceeding the pitch limits, diagonals do not exceed straight-line speed, and the capsule stays on the plane. Temporarily clear each required reference in the Inspector and confirm the component reports the missing reference without a null-reference exception, then restore it. No automated test files are added in this plan.
