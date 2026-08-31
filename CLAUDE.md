# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project overview

A Unity 2D game project (working title "超人貓大亂鬥" / "Super Cat Brawl"). Unity Editor version **2022.3.62f3**. The project has a working combat loop: one playable character (SuperCat) with three functioning skills, and one enemy (DogeEnemy) with a two-phase attack pattern, playable in `Assets/Scenes/Battle.unity`.

## Development environment

This is a Unity project, not a CLI-buildable package — there are no npm/dotnet CLI build, lint, or test commands to run from the terminal. Building, running, and testing happen inside the Unity Editor:

- Open the project via Unity Hub with Editor version `2022.3.62f3` (see `ProjectSettings/ProjectVersion.txt`).
- Play mode in the Editor (on `Assets/Scenes/Battle.unity`) is the primary way to run and manually test the game.
- The `com.unity.test-framework` package is installed, so Unity Test Runner (Window > General > Test Runner) is available for EditMode/PlayMode tests, but no test assemblies exist yet.
- C# scripts target `netstandard2.1` / `LangVersion 9.0` (see `Assembly-CSharp.csproj`). The `.sln`/`.csproj` files are Unity-generated — treat them as build artifacts, not hand-maintained project files.
- Do not edit files under `Library/`, `Temp/`, `Logs/`, `UserSettings/`, or `obj/` — these are Unity-generated caches, not source.

## Code architecture

Gameplay scripts live under `Assets/Script/`, split into `Player/`, `Enemy/`, and `Common/`.

### Stats/skill composition pattern (players)

- `Assets/Script/Player/PlayerBase.cs` — `PlayerBase : MonoBehaviour, IDamageable` is the shared base for all playable characters. Requires `Rigidbody2D`, `CircleCollider2D`, `SpriteRenderer`, `Animator`. It holds a `PlayerStats` instance and three skill slots (`normalAttack`, `dashSkill`, `ultimateSkill`), each typed as `ISkill`. `Update()` reads WASD movement input, tracks `FacingDirection`, flips the sprite, and drives all three skills' `TryExecute()` every frame; `FixedUpdate()` applies movement (unless a skill is mid-movement via `IsActive`) and calls `Tick()` on dash/ultimate. Wall collisions call `Interrupt()`; enemy collisions call `OnHitEnemy()` on both movement skills.
- `ISkill` (defined in `PlayerBase.cs`) is the contract every skill implements: `Cooldown`, `TryExecute()`, `IsActive`, `Tick()`, `Interrupt()`, `OnHitEnemy(Collider2D)`. Skills that involve a movement/dash phase (dash, ultimate) use `IsActive`/`Tick()`/`Interrupt()`/`OnHitEnemy` to drive their own `Rigidbody2D.MovePosition` during `FixedUpdate` while suppressing normal WASD movement; instant-effect skills (normal attack) implement these as no-ops. Skills are independent, swappable classes (composition over inheritance) — a character is assembled by wiring concrete `ISkill` implementations into the base's skill slots in `Awake()`, not by subclassing behavior.
- `IDamageable` (also in `PlayerBase.cs`) is the contract for anything that can take damage (`TakeDamage(float amount)`); implemented by both `PlayerBase` and `EnemyBase`.
- `PlayerStats` is a plain data class holding `baseAttack`, `baseHealth`, `moveSpeed`, `Health`, and a shared **charge** resource (`Charge`, capped at `MaxCharge`) with `AddCharge`/`SpendCharge` — charge is spent by ultimates and earned via normal-attack hits and dash usage.
- `Assets/Script/Player/SuperCat.cs` is the one implemented character: subclasses `PlayerBase`, and in `Awake()` wires up `AutoLockShootSkill` (normal attack), `DashSkill` (dash), and `ChargeRamSkill` (ultimate) — all under `Assets/Script/Player/Skills/`. Player-facing values (attack/health/moveSpeed) are currently hardcoded per-character in `Awake()` rather than driven by a leveling system.
- Skill implementations follow a drag-gesture convention: mouse-button-down starts tracking a world-space drag origin, mouse-button-up computes direction from drag delta (falling back to `owner.FacingDirection` for a short "tap" instead of a drag) and triggers the skill. `DashSkill` uses left-click + a cooldown; `ChargeRamSkill` uses right-click + a charge cost (no cooldown) and grants 50% damage reduction on `PlayerBase.TakeDamage` while active.

When adding a new playable character, follow the `SuperCat` pattern: subclass `PlayerBase`, and in `Awake()` assign concrete `ISkill` implementations to `normalAttack`, `dashSkill`, and `ultimateSkill`. When adding a new skill, implement `ISkill` as an independent class (not a `MonoBehaviour`) that takes the owning `PlayerBase` and its tuning parameters via constructor, matching the existing `AutoLockShootSkill(this, bulletPrefab, damageMultiplier: ...)` call-site style.

### Enemies

- `Assets/Script/Enemy/EnemyBase.cs` — minimal base with `health`, `IsDead`, and `TakeDamage`; has no attack behavior of its own (used to test player normal-attack logic in isolation). Requires `Rigidbody2D` (kinematic, no gravity), `CircleCollider2D`, `SpriteRenderer`.
- `Assets/Script/Enemy/DogeEnemy.cs` subclasses `EnemyBase` and is the only enemy with real behavior: it wanders/chases within a hardcoded ground rectangle, has a jump attack on a timer, and — once health drops below `phase2HealthThreshold` — gains a second-phase "big bark" attack that spawns a rotating `SonicWave` beam. Long attacks run as coroutines (`JumpRoutine`, `BigBarkRoutine`) and set `isPerformingAction` to suspend wandering/attack-cycle logic while they play.
- `Assets/Script/Enemy/SonicWave.cs` is a standalone `MonoBehaviour` (not an `ISkill`) representing the rotating beam hazard spawned by `DogeEnemy`'s phase-2 attack; it rotates around a pivot transform and deals tick damage to anything tagged `Player` inside its trigger collider.

New enemies should subclass `EnemyBase` the way `DogeEnemy` does, rather than implementing `IDamageable` directly.

### Shared/common

- `Assets/Script/Common/Bullet.cs` — projectile spawned by `AutoLockShootSkill` via the static `Bullet.Spawn(...)` factory; flies in a straight line, damages colliders tagged `Enemy`, and is destroyed on hitting `Enemy` or `Wall`. Falls back to a runtime-generated placeholder visual if no prefab is supplied.
- `Assets/Script/Common/SmokePuff.cs` — a scale-up/fade-out hit-effect placeholder that self-destructs after `lifeTime`.
- `Assets/Script/Common/PlaceholderSprite.cs` — generates and caches solid-color 1×1-world-unit square sprites, used by scripts (`Bullet`, `SonicWave`, `SmokePuff`) as a stand-in visual whenever no art prefab/sprite has been assigned yet. Prefer wiring real prefabs/sprites when art is available rather than relying on this permanently.

Collision/trigger logic throughout depends on GameObject tags: `Player`, `Enemy`, and `Wall` — make sure new prefabs are tagged correctly for damage and collision interactions to work.

## Asset layout

- `Assets/Images/Characters/`, `Assets/Images/Enemys/`, `Assets/Images/BackGround/`, `Assets/Images/Effects/` — sprite assets, organized by category/entity name (e.g. `Enemys/1_Doge/`).
- `Assets/Animator/` — Animator Controllers and clips per character (e.g. `Animator/SuperCat/`, `Animator/Doge/`), matching the animation trigger names used in code (`KB`, `OpenMouth`, `CloseMouth`).
- `Assets/Prefabs/` — playable/enemy/projectile/effect prefabs: `SuperCat.prefab`, `1_Doge.prefab`, `Bullet.prefab`, `SonicWave.prefab`, `Smoke.prefab`.
- `Assets/Scenes/Battle.unity` — the only scene currently in the project.
- `Assets/Audio/` — present but currently empty.

Comments in source are written in Traditional Chinese (zh-TW); match this convention when editing existing files with Chinese comments.
