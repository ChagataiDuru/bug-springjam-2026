using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Taiyun.SuckTheWater.Game
{
    /// <summary>Weapon firing modes.</summary>
    public enum WeaponShootType
    {
        Manual,
        Automatic,
        Charge,
    }

    [System.Serializable]
    public struct CrosshairData
    {
        [Tooltip("The image that will be used for this weapon's crosshair")]
        public Sprite CrosshairSprite;

        [Tooltip("The size of the crosshair image")]
        public int CrosshairSize;

        [Tooltip("The color of the crosshair image")]
        public Color CrosshairColor;
    }

    [RequireComponent(typeof(AudioSource))]
    public class WeaponController : MonoBehaviour
    {
        public enum ReplicatedImpactType
        {
            None,
            World,
            Character,
            Water
        }

        [Serializable]
        public struct ImpactPresentationData
        {
            public GameObject ImpactVfxPrefab;
            public float ImpactVfxLifetime;
            public float ImpactVfxSpawnOffset;
            public AudioClip ImpactSfxClip;

            public bool IsConfigured => ImpactVfxPrefab != null || ImpactSfxClip != null;
        }

        [Header("Information")] [Tooltip("The name that will be displayed in the UI for this weapon")]
        public string WeaponName;

        [Tooltip("The image that will be displayed in the UI for this weapon")]
        public Sprite WeaponIcon;

        [Tooltip("Default data for the crosshair")]
        public CrosshairData CrosshairDataDefault;

        [Tooltip("Data for the crosshair when targeting an enemy")]
        public CrosshairData CrosshairDataTargetInSight;

        [Header("Internal References")]
        [Tooltip("The root object for the weapon, this is what will be deactivated when the weapon isn't active")]
        public GameObject WeaponRoot;

        [Tooltip("Tip of the weapon, where the projectiles are shot")]
        public Transform WeaponMuzzle;

        [Header("Shoot Parameters")] [Tooltip("The type of weapon wil affect how it shoots")]
        public WeaponShootType ShootType;

        [Tooltip("The projectile prefab")] public ProjectileBase ProjectilePrefab;

        [Tooltip("Minimum duration between two shots")]
        public float DelayBetweenShots = 0.5f;

        [Tooltip("Angle for the cone in which the bullets will be shot randomly (0 means no spread at all)")]
        public float BulletSpreadAngle = 0f;

        [Tooltip("Amount of bullets per shot")]
        public int BulletsPerShot = 1;

        [Tooltip("Force that will push back the weapon after each shot")] [Range(0f, 2f)]
        public float RecoilForce = 1;

        [Tooltip("Ratio of the default FOV that this weapon applies while aiming")] [Range(0f, 1f)]
        public float AimZoomRatio = 1f;

        [Tooltip("Translation to apply to weapon arm when aiming with this weapon")]
        public Vector3 AimOffset;

        [Header("Ammo Parameters")]
        [Tooltip("Should the player manually reload")]
        public bool AutomaticReload = true;
        [Tooltip("Has physical clip on the weapon and ammo shells are ejected when firing")]
        public bool HasPhysicalBullets = false;
        [Tooltip("Number of bullets in a clip")]
        public int ClipSize = 30;
        [Tooltip("Bullet Shell Casing")]
        public GameObject ShellCasing;
        [Tooltip("Weapon Ejection Port for physical ammo")]
        public Transform EjectionPort;
        [Tooltip("Force applied on the shell")]
        [Range(0.0f, 5.0f)] public float ShellCasingEjectionForce = 2.0f;
        [Tooltip("Maximum number of shell that can be spawned before reuse")]
        [Range(1, 30)] public int ShellPoolSize = 1;
        [Tooltip("Amount of ammo reloaded per second")]
        public float AmmoReloadRate = 1f;

        [Tooltip("Delay after the last shot before starting to reload")]
        public float AmmoReloadDelay = 2f;

        [Tooltip("Maximum amount of ammo in the gun")]
        public int MaxAmmo = 8;

        [Header("Charging parameters (charging weapons only)")]
        [Tooltip("Trigger a shot when maximum charge is reached")]
        public bool AutomaticReleaseOnCharged;

        [Tooltip("Duration to reach maximum charge")]
        public float MaxChargeDuration = 2f;

        [Tooltip("Initial ammo used when starting to charge")]
        public float AmmoUsedOnStartCharge = 1f;

        [Tooltip("Additional ammo used when charge reaches its maximum")]
        public float AmmoUsageRateWhileCharging = 1f;

        [Header("Audio & Visual")] 
        [Tooltip("Optional weapon animator for OnShoot animations")]
        public Animator WeaponAnimator;

        [Tooltip("Prefab of the muzzle flash")]
        public GameObject MuzzleFlashPrefab;

        [Tooltip("Unparent the muzzle flash instance on spawn")]
        public bool UnparentMuzzleFlash;

        [Tooltip("sound played when shooting")]
        public AudioClip ShootSfx;

        [Tooltip("Sound played when changing to this weapon")]
        public AudioClip ChangeWeaponSfx;

        [Tooltip("Continuous Shooting Sound")] public bool UseContinuousShootSound = false;
        public AudioClip ContinuousShootStartSfx;
        public AudioClip ContinuousShootLoopSfx;
        public AudioClip ContinuousShootEndSfx;
        AudioSource m_ContinuousShootAudioSource = null;
        bool m_WantsToShoot = false;

        public UnityAction OnShoot;
        public event Action OnShootProcessed;

        int m_CarriedPhysicalBullets;
        float m_CurrentAmmo;
        float m_LastTimeShot = Mathf.NegativeInfinity;
        float m_LastPredictedPresentationShotTime = Mathf.NegativeInfinity;
        float m_PredictedChargeStartTime;
        bool m_IsPredictivePresentationCharge;
        public float LastChargeTriggerTimestamp { get; private set; }
        Vector3 m_LastMuzzlePosition;

        public GameObject Owner { get; set; }
        public GameObject SourcePrefab { get; set; }
        public bool IsCharging { get; private set; }
        public float CurrentAmmoRatio { get; private set; }
        public bool IsWeaponActive { get; private set; }
        public bool IsCooling { get; private set; }
        public float CurrentCharge { get; private set; }
        public Vector3 MuzzleWorldVelocity { get; private set; }

        public float GetAmmoNeededToShoot() =>
            (ShootType != WeaponShootType.Charge ? 1f : Mathf.Max(1f, AmmoUsedOnStartCharge)) /
            (MaxAmmo * BulletsPerShot);

        public int GetCarriedPhysicalBullets() => m_CarriedPhysicalBullets;
        public int GetCurrentAmmo() => Mathf.FloorToInt(m_CurrentAmmo);

        AudioSource m_ShootAudioSource;

        public bool IsReloading { get; private set; }

        const string k_AnimAttackParameter = "Attack";
        const float k_ReplicatedTracerLifetime = 0.06f;
        const float k_ReplicatedTracerStartWidth = 0.03f;
        const float k_ReplicatedTracerEndWidth = 0.005f;
        static readonly Color k_ReplicatedTracerStartColor = new Color(0.75f, 0.95f, 1f, 0.9f);
        static readonly Color k_ReplicatedTracerEndColor = new Color(0.75f, 0.95f, 1f, 0f);
        static Material s_ReplicatedTracerMaterial;

        private Queue<Rigidbody> m_PhysicalAmmoPool;

        void Awake()
        {
            m_CurrentAmmo = MaxAmmo;
            m_CarriedPhysicalBullets = HasPhysicalBullets ? ClipSize : 0;
            m_LastMuzzlePosition = WeaponMuzzle.position;

            m_ShootAudioSource = GetComponent<AudioSource>();
            DebugUtility.HandleErrorIfNullGetComponent<AudioSource, WeaponController>(m_ShootAudioSource, this,
                gameObject);

            if (UseContinuousShootSound)
            {
                m_ContinuousShootAudioSource = gameObject.AddComponent<AudioSource>();
                m_ContinuousShootAudioSource.playOnAwake = false;
                m_ContinuousShootAudioSource.clip = ContinuousShootLoopSfx;
                m_ContinuousShootAudioSource.outputAudioMixerGroup =
                    AudioUtility.GetAudioGroup(AudioUtility.AudioGroups.WeaponShoot);
                m_ContinuousShootAudioSource.loop = true;
            }

            if (HasPhysicalBullets)
            {
                m_PhysicalAmmoPool = new Queue<Rigidbody>(ShellPoolSize);

                for (int i = 0; i < ShellPoolSize; i++)
                {
                    GameObject shell = Instantiate(ShellCasing, transform);
                    shell.SetActive(false);
                    m_PhysicalAmmoPool.Enqueue(shell.GetComponent<Rigidbody>());
                }
            }
        }

        public void AddCarriablePhysicalBullets(int count) => m_CarriedPhysicalBullets = Mathf.Max(m_CarriedPhysicalBullets + count, MaxAmmo);

        void ShootShell()
        {
            Rigidbody nextShell = m_PhysicalAmmoPool.Dequeue();

            nextShell.transform.position = EjectionPort.transform.position;
            nextShell.transform.rotation = EjectionPort.transform.rotation;
            nextShell.gameObject.SetActive(true);
            nextShell.transform.SetParent(null);
            nextShell.collisionDetectionMode = CollisionDetectionMode.Continuous;
            nextShell.AddForce(nextShell.transform.up * ShellCasingEjectionForce, ForceMode.Impulse);

            m_PhysicalAmmoPool.Enqueue(nextShell);
        }

        void PlaySFX(AudioClip sfx) => AudioUtility.CreateSFX(sfx, transform.position, AudioUtility.AudioGroups.WeaponShoot, 0.0f);


        void Reload()
        {
            if (m_CarriedPhysicalBullets > 0)
            {
                m_CurrentAmmo = Mathf.Min(m_CarriedPhysicalBullets, ClipSize);
            }

            IsReloading = false;
        }

        public void StartReloadAnimation()
        {
            if (m_CurrentAmmo < m_CarriedPhysicalBullets)
            {
                GetComponent<Animator>().SetTrigger("Reload");
                IsReloading = true;
            }
        }

        void Update()
        {
            UpdateAmmo();
            UpdateCharge();
            UpdateContinuousShootSound();

            if (Time.deltaTime > 0)
            {
                MuzzleWorldVelocity = (WeaponMuzzle.position - m_LastMuzzlePosition) / Time.deltaTime;
                m_LastMuzzlePosition = WeaponMuzzle.position;
            }
        }

        void UpdateAmmo()
        {
            if (AutomaticReload && m_LastTimeShot + AmmoReloadDelay < Time.time && m_CurrentAmmo < MaxAmmo && !IsCharging)
            {
                // reloads weapon over time
                m_CurrentAmmo += AmmoReloadRate * Time.deltaTime;

                // limits ammo to max value
                m_CurrentAmmo = Mathf.Clamp(m_CurrentAmmo, 0, MaxAmmo);

                IsCooling = true;
            }
            else
            {
                IsCooling = false;
            }

            if (MaxAmmo == Mathf.Infinity)
            {
                CurrentAmmoRatio = 1f;
            }
            else
            {
                CurrentAmmoRatio = m_CurrentAmmo / MaxAmmo;
            }
        }

        void UpdateCharge()
        {
            if (m_IsPredictivePresentationCharge)
            {
                return;
            }

            if (IsCharging)
            {
                if (CurrentCharge < 1f)
                {
                    float chargeLeft = 1f - CurrentCharge;

                    // Calculate how much charge ratio to add this frame
                    float chargeAdded = 0f;
                    if (MaxChargeDuration <= 0f)
                    {
                        chargeAdded = chargeLeft;
                    }
                    else
                    {
                        chargeAdded = (1f / MaxChargeDuration) * Time.deltaTime;
                    }

                    chargeAdded = Mathf.Clamp(chargeAdded, 0f, chargeLeft);

                    // See if we can actually add this charge
                    float ammoThisChargeWouldRequire = chargeAdded * AmmoUsageRateWhileCharging;
                    if (ammoThisChargeWouldRequire <= m_CurrentAmmo)
                    {
                        // Use ammo based on charge added
                        UseAmmo(ammoThisChargeWouldRequire);

                        // set current charge ratio
                        CurrentCharge = Mathf.Clamp01(CurrentCharge + chargeAdded);
                    }
                }
            }
        }

        void UpdateContinuousShootSound()
        {
            if (UseContinuousShootSound)
            {
                if (m_WantsToShoot && m_CurrentAmmo >= 1f)
                {
                    if (!m_ContinuousShootAudioSource.isPlaying)
                    {
                        m_ShootAudioSource.PlayOneShot(ShootSfx);
                        m_ShootAudioSource.PlayOneShot(ContinuousShootStartSfx);
                        m_ContinuousShootAudioSource.Play();
                    }
                }
                else if (m_ContinuousShootAudioSource.isPlaying)
                {
                    m_ShootAudioSource.PlayOneShot(ContinuousShootEndSfx);
                    m_ContinuousShootAudioSource.Stop();
                }
            }
        }

        public void ShowWeapon(bool show)
        {
            if (!show)
            {
                StopUsePresentationImmediate();
            }

            WeaponRoot.SetActive(show);

            if (show && ChangeWeaponSfx)
            {
                m_ShootAudioSource.PlayOneShot(ChangeWeaponSfx);
            }

            IsWeaponActive = show;
        }

        public void UseAmmo(float amount)
        {
            m_CurrentAmmo = Mathf.Clamp(m_CurrentAmmo - amount, 0f, MaxAmmo);
            m_CarriedPhysicalBullets -= Mathf.RoundToInt(amount);
            m_CarriedPhysicalBullets = Mathf.Clamp(m_CarriedPhysicalBullets, 0, MaxAmmo);
            m_LastTimeShot = Time.time;
        }

        /// <summary>
        /// Local predicted fire presentation path used by NetworkedWeaponDriver.
        /// Card 2 keeps this presentation-only (no gameplay projectile spawn / no hit authority).
        /// </summary>
        public bool PlayLocalPredictedUseInput(bool inputDown, bool inputHeld, bool inputUp)
        {
            m_WantsToShoot = inputDown || inputHeld;

            switch (ShootType)
            {
                case WeaponShootType.Manual:
                    if (inputDown)
                    {
                        return TryPlayPredictedShotPresentation();
                    }

                    return false;

                case WeaponShootType.Automatic:
                    if (inputHeld)
                    {
                        return TryPlayPredictedShotPresentation();
                    }

                    return false;

                case WeaponShootType.Charge:
                    if (inputHeld)
                    {
                        TryBeginPredictedCharge();
                        UpdatePredictedChargeRatio();
                    }

                    if (inputUp || (AutomaticReleaseOnCharged && IsCharging && CurrentCharge >= 1f))
                    {
                        return TryReleasePredictedCharge();
                    }

                    return false;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Stops local predicted use presentation (charge state, loops, intent flags).
        /// </summary>
        public void PlayLocalPredictedUseStop()
        {
            StopUsePresentationImmediate();
        }

        /// <summary>
        /// Placeholder for future observer start presentation.
        /// </summary>
        public void PlayRemoteUseStart()
        {
            // Intentionally empty in Card 1. Remote VFX/timing is added in later cards.
        }

        /// <summary>
        /// Placeholder for future observer stop presentation.
        /// </summary>
        public void PlayRemoteUseStop()
        {
            StopUsePresentationImmediate();
        }

        /// <summary>
        /// Observer/authoritative replicated fire presentation.
        /// Presentation only: no projectile gameplay or hit authority.
        /// </summary>
        public void PlayReplicatedObserverFirePresentation(Vector3 shotOrigin, Vector3 shotDirection, Vector3 shotEndPoint)
        {
            if (!IsFiniteVector3(shotOrigin))
            {
                return;
            }

            Vector3 resolvedDirection = shotDirection;
            if (!IsFiniteVector3(resolvedDirection) || resolvedDirection.sqrMagnitude <= Mathf.Epsilon)
            {
                resolvedDirection = shotEndPoint - shotOrigin;
            }

            if (resolvedDirection.sqrMagnitude <= Mathf.Epsilon)
            {
                resolvedDirection = transform.forward;
            }

            resolvedDirection = resolvedDirection.normalized;
            Quaternion shotRotation = Quaternion.LookRotation(resolvedDirection);

            SpawnReplicatedMuzzleFlash(MuzzleFlashPrefab, shotOrigin, shotRotation);
            PlayReplicatedShootSfx(ResolveReplicatedFireSfx(), shotOrigin);
            SpawnReplicatedTracer(shotOrigin, shotEndPoint);
        }

        /// <summary>
        /// Observer/authoritative replicated impact presentation.
        /// Presentation only: never applies gameplay damage.
        /// </summary>
        public void PlayAuthoritativeImpactPresentation(Vector3 hitPoint, Vector3 hitNormal,
            ReplicatedImpactType impactType, ImpactPresentationData impactData)
        {
            if (impactType == ReplicatedImpactType.None || !impactData.IsConfigured)
            {
                return;
            }

            SpawnImpactPresentation(hitPoint, hitNormal, impactData);
        }

        public static void SpawnReplicatedMuzzleFlash(GameObject muzzleFlashPrefab, Vector3 shotOrigin,
            Quaternion shotRotation)
        {
            if (muzzleFlashPrefab == null || !IsFiniteVector3(shotOrigin))
            {
                return;
            }

            GameObject muzzleFlashInstance = Instantiate(muzzleFlashPrefab, shotOrigin, shotRotation);
            Destroy(muzzleFlashInstance, 2f);
        }

        public static void PlayReplicatedShootSfx(AudioClip shootClip, Vector3 shotOrigin)
        {
            if (shootClip == null || !IsFiniteVector3(shotOrigin))
            {
                return;
            }

            AudioUtility.CreateSFX(shootClip, shotOrigin, AudioUtility.AudioGroups.WeaponShoot, 1f, 3f);
        }

        public static void SpawnReplicatedTracer(Vector3 shotOrigin, Vector3 shotEndPoint)
        {
            if (!IsFiniteVector3(shotOrigin) || !IsFiniteVector3(shotEndPoint))
            {
                return;
            }

            if ((shotEndPoint - shotOrigin).sqrMagnitude <= 0.0001f)
            {
                return;
            }

            GameObject tracerObject = new GameObject("ReplicatedShotTracer");
            LineRenderer tracer = tracerObject.AddComponent<LineRenderer>();
            tracer.useWorldSpace = true;
            tracer.positionCount = 2;
            tracer.SetPosition(0, shotOrigin);
            tracer.SetPosition(1, shotEndPoint);
            tracer.startWidth = k_ReplicatedTracerStartWidth;
            tracer.endWidth = k_ReplicatedTracerEndWidth;
            tracer.startColor = k_ReplicatedTracerStartColor;
            tracer.endColor = k_ReplicatedTracerEndColor;
            tracer.numCapVertices = 2;
            tracer.alignment = LineAlignment.View;
            tracer.textureMode = LineTextureMode.Stretch;

            if (s_ReplicatedTracerMaterial == null)
            {
                Shader tracerShader = Shader.Find("Sprites/Default");
                if (tracerShader != null)
                {
                    s_ReplicatedTracerMaterial = new Material(tracerShader);
                }
            }

            if (s_ReplicatedTracerMaterial != null)
            {
                tracer.sharedMaterial = s_ReplicatedTracerMaterial;
            }

            TimedSelfDestruct timedSelfDestruct = tracerObject.AddComponent<TimedSelfDestruct>();
            timedSelfDestruct.LifeTime = k_ReplicatedTracerLifetime;
        }

        public static void SpawnImpactPresentation(Vector3 hitPoint, Vector3 hitNormal, ImpactPresentationData impactData)
        {
            if (!impactData.IsConfigured || !IsFiniteVector3(hitPoint))
            {
                return;
            }

            Vector3 resolvedNormal = hitNormal.sqrMagnitude > Mathf.Epsilon ? hitNormal.normalized : Vector3.up;
            if (!IsFiniteVector3(resolvedNormal))
            {
                resolvedNormal = Vector3.up;
            }

            if (impactData.ImpactVfxPrefab != null)
            {
                GameObject impactVfxInstance = Instantiate(
                    impactData.ImpactVfxPrefab,
                    hitPoint + (resolvedNormal * impactData.ImpactVfxSpawnOffset),
                    Quaternion.LookRotation(resolvedNormal));

                if (impactData.ImpactVfxLifetime > 0f)
                {
                    Destroy(impactVfxInstance, impactData.ImpactVfxLifetime);
                }
            }

            if (impactData.ImpactSfxClip != null)
            {
                AudioUtility.CreateSFX(impactData.ImpactSfxClip, hitPoint, AudioUtility.AudioGroups.Impact, 1f, 3f);
            }
        }

        void StopUsePresentationImmediate()
        {
            m_WantsToShoot = false;
            m_IsPredictivePresentationCharge = false;

            if (IsCharging)
            {
                CurrentCharge = 0f;
                IsCharging = false;
            }

            if (UseContinuousShootSound && m_ContinuousShootAudioSource != null && m_ContinuousShootAudioSource.isPlaying)
            {
                if (ContinuousShootEndSfx)
                {
                    m_ShootAudioSource.PlayOneShot(ContinuousShootEndSfx);
                }

                m_ContinuousShootAudioSource.Stop();
            }
        }

        bool TryPlayPredictedShotPresentation()
        {
            if (!CanPlayPredictedShotNow())
            {
                return false;
            }

            m_LastPredictedPresentationShotTime = Time.time;
            PlayShotPresentation(consumePhysicalBullet: false);
            return true;
        }

        bool CanPlayPredictedShotNow()
        {
            if (DelayBetweenShots <= 0f)
            {
                return true;
            }

            return m_LastPredictedPresentationShotTime + DelayBetweenShots < Time.time;
        }

        bool TryBeginPredictedCharge()
        {
            if (IsCharging || !CanPlayPredictedShotNow())
            {
                return false;
            }

            m_IsPredictivePresentationCharge = true;
            IsCharging = true;
            CurrentCharge = 0f;
            m_PredictedChargeStartTime = Time.time;
            LastChargeTriggerTimestamp = Time.time;
            return true;
        }

        void UpdatePredictedChargeRatio()
        {
            if (!IsCharging || !m_IsPredictivePresentationCharge)
            {
                return;
            }

            if (MaxChargeDuration <= 0f)
            {
                CurrentCharge = 1f;
                return;
            }

            CurrentCharge = Mathf.Clamp01((Time.time - m_PredictedChargeStartTime) / MaxChargeDuration);
        }

        bool TryReleasePredictedCharge()
        {
            if (!IsCharging || !m_IsPredictivePresentationCharge)
            {
                return false;
            }

            bool shotAttempted = TryPlayPredictedShotPresentation();
            CurrentCharge = 0f;
            IsCharging = false;
            m_IsPredictivePresentationCharge = false;
            return shotAttempted;
        }

        /// <summary>
        /// Legacy direct-use entrypoint.
        /// For player-owned networked weapons, route intent through NetworkedWeaponDriver instead.
        /// </summary>
        public bool HandleShootInputs(bool inputDown, bool inputHeld, bool inputUp)
        {
            m_WantsToShoot = inputDown || inputHeld;
            switch (ShootType)
            {
                case WeaponShootType.Manual:
                    if (inputDown)
                    {
                        return TryShoot();
                    }

                    return false;

                case WeaponShootType.Automatic:
                    if (inputHeld)
                    {
                        return TryShoot();
                    }

                    return false;

                case WeaponShootType.Charge:
                    if (inputHeld)
                    {
                        TryBeginCharge();
                    }

                    // Check if we released charge or if the weapon shoot autmatically when it's fully charged
                    if (inputUp || (AutomaticReleaseOnCharged && CurrentCharge >= 1f))
                    {
                        return TryReleaseCharge();
                    }

                    return false;

                default:
                    return false;
            }
        }

        bool TryShoot()
        {
            if (m_CurrentAmmo >= 1f
                && m_LastTimeShot + DelayBetweenShots < Time.time)
            {
                HandleShoot();
                m_CurrentAmmo -= 1f;

                return true;
            }

            return false;
        }

        bool TryBeginCharge()
        {
            if (!IsCharging
                && m_CurrentAmmo >= AmmoUsedOnStartCharge
                && Mathf.FloorToInt((m_CurrentAmmo - AmmoUsedOnStartCharge) * BulletsPerShot) > 0
                && m_LastTimeShot + DelayBetweenShots < Time.time)
            {
                m_IsPredictivePresentationCharge = false;
                UseAmmo(AmmoUsedOnStartCharge);

                LastChargeTriggerTimestamp = Time.time;
                IsCharging = true;

                return true;
            }

            return false;
        }

        bool TryReleaseCharge()
        {
            if (IsCharging)
            {
                m_IsPredictivePresentationCharge = false;
                HandleShoot();

                CurrentCharge = 0f;
                IsCharging = false;

                return true;
            }

            return false;
        }

        void PlayShotPresentation(bool consumePhysicalBullet)
        {
            // muzzle flash
            if (MuzzleFlashPrefab != null)
            {
                GameObject muzzleFlashInstance = Instantiate(MuzzleFlashPrefab, WeaponMuzzle.position,
                    WeaponMuzzle.rotation, WeaponMuzzle.transform);
                if (UnparentMuzzleFlash)
                {
                    muzzleFlashInstance.transform.SetParent(null);
                }

                Destroy(muzzleFlashInstance, 2f);
            }

            if (HasPhysicalBullets && m_PhysicalAmmoPool != null)
            {
                ShootShell();
                if (consumePhysicalBullet)
                {
                    m_CarriedPhysicalBullets--;
                }
            }

            // play shoot SFX
            if (ShootSfx && !UseContinuousShootSound)
            {
                m_ShootAudioSource.PlayOneShot(ShootSfx);
            }

            // Trigger attack animation if there is any
            if (WeaponAnimator)
            {
                WeaponAnimator.SetTrigger(k_AnimAttackParameter);
            }

            OnShoot?.Invoke();
            OnShootProcessed?.Invoke();
        }

        void HandleShoot()
        {
            int bulletsPerShotFinal = ShootType == WeaponShootType.Charge
                ? Mathf.CeilToInt(CurrentCharge * BulletsPerShot)
                : BulletsPerShot;

            // spawn all bullets with random direction
            for (int i = 0; i < bulletsPerShotFinal; i++)
            {
                Vector3 shotDirection = GetShotDirectionWithinSpread(WeaponMuzzle);
                ProjectileBase newProjectile = Instantiate(ProjectilePrefab, WeaponMuzzle.position,
                    Quaternion.LookRotation(shotDirection));
                newProjectile.Shoot(this);
            }

            PlayShotPresentation(consumePhysicalBullet: true);

            m_LastTimeShot = Time.time;
        }

        AudioClip ResolveReplicatedFireSfx()
        {
            if (ShootSfx != null)
            {
                return ShootSfx;
            }

            if (ContinuousShootStartSfx != null)
            {
                return ContinuousShootStartSfx;
            }

            return null;
        }

        static bool IsFiniteVector3(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        public Vector3 GetShotDirectionWithinSpread(Transform shootTransform)
        {
            float spreadAngleRatio = BulletSpreadAngle / 180f;
            Vector3 spreadWorldDirection = Vector3.Slerp(shootTransform.forward, UnityEngine.Random.insideUnitSphere,
                spreadAngleRatio);

            return spreadWorldDirection;
        }
    }
}
