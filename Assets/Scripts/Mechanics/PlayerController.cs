using Platformer.Core;
using Platformer.Gameplay;
using Platformer.Model;
using SuperTiled2Unity;
using System;
using System.Collections.Generic;
using TMPro;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using static Platformer.Core.Simulation;
using static UtilityFunctions;

namespace Platformer.Mechanics
{
    /// <summary>
    /// This is the main class used to implement control of the player.
    /// It is a superset of the AnimationController class, but is inlined to allow for any kind of customisation.
    /// </summary>
    public class PlayerController : KinematicObject
    {
        public AudioClip jumpAudio;
        public AudioClip respawnAudio;
        public AudioClip ouchAudio;

        /// <summary>
        /// Max horizontal speed of the player. (u/s)
        /// </summary>
        public float maxSpeed = 3;

        /// <summary>
        /// Max horizontal acceleration of the player on the ground (u/s^2).
        /// </summary>
        public float groundAccel = 14;

        /// <summary>
        /// increased acceleration force when lowering speed.
        /// </summary>
        public float groundBraking = 28;

        /// <summary>
        /// Max horizontal acceleration per second in air when allowed
        /// </summary>
        public float airControlLateral = 3;

        /// <summary>
        /// Max drag from overspeed
        /// </summary>
        public float maxDragLateral = 9;

        /// <summary>
        /// Amount by which speed exceeds max speed before hitting max drag
        /// </summary>
        public float maxDragThreshold = 10;

        /// <summary>
        /// Initial jump velocity at the start of a b-hop.
        /// </summary>
        public float jumpTakeOffSpeed = 2.0f;

        /// <summary>
        /// How long the player charged this jump
        /// </summary>
        private float jumpChargeTime = 0;

        /// <summary>
        /// Initial jump velocity at the start of a launch, based on charge level.
        /// </summary>
        public float[] launchSpeeds = { 6.0f, 9.0f, 12.0f, 15.0f};

        /// <summary>
        /// How long a stage 1 charge takes
        /// </summary>
        public float[] chargeTimes = { 0.05f, 0.5f, 1.25f,  2.25f };

        /// <summary>
        /// How long to fly straight (and block move input) before becoming subject to gravity again
        /// </summary>
        public float[] preBallisticTimes = { 0.0f, 0.33f, 0.67f, 1.0f };

        /// <summary>
        /// Whether to limit launch directions to a range around the normal vector
        /// </summary>
        public bool limitLaunchAngle = true;

        /// <summary>
        /// Angular range for launch centered on surface normal
        /// </summary>
        public float launchAngleRange = 90.0f;

        /// <summary>
        /// Highest possible charge state
        /// </summary>
        public int maxChargeStage = 3;

        /// <summary>
        /// Current charge state
        /// </summary>
        private int m_chargeStage = 0;

        /// <summary>
        /// Last calculated charge state, for identifying changes
        /// </summary>
        private int m_lastChargeStage = 0;

        /// <summary>
        /// Is the player traveling in a straight line (no air-control, no gravity, until collision or state ends)
        /// </summary>
        private bool m_bIsPreBallistic = false;
        public bool AttackMode
        {
            get => m_bIsPreBallistic;
        }

        /// <summary>
        /// Countdown to going ballistic
        /// </summary>
        private float m_preBallisticTimeRemaining = 0.0f;

        /// <summary>
        /// Used to enforce pre-ballistic constant velocity
        /// </summary>
        private Vector2 m_LastLaunchVelocity;

        /// <summary>
        /// Used in collision events as velocity becomes 0
        /// </summary>
        private Vector2 m_LastFrameVelocity;

        /// <summary>
        /// required magnitude of velocity component pointing into surface to stick
        /// </summary>
        public float StickSpeedThreshold = 2.0f;

        /// <summary>
        /// how long to stay stuck before falling if stick charge isn't entered
        /// </summary>
        public float StickTime = 0.5f;

        /// <summary>
        /// degree increments from 0 to snap aim to
        /// for obvious reasons should be a factor of 360
        /// </summary>
        public int AimSnapIncrement = 45;

        /// <summary>
        /// degree increments from 0 to snap movement to
        /// for obvious reasons should be a factor of 360
        /// </summary>
        public int MoveSnapIncrement = 45;

        /// <summary>
        /// countdown to unsticking
        /// </summary>
        private float m_timeUntilFall = 0.5f;

        /// <summary>
        /// latch to prevent processing multiple reflect hits per frame
        /// </summary>
        private bool m_reflectedThisFrame = false;

        public float RunAnimThreshold = 0.1f;

        /// <summary>
        ///  How many invincibility stacks have been added by GrantInvincibility
        ///  GrantInvincibility also schedules an event that will remove the stack
        ///  When any stacks remain, player is invincible. When all are gone, invincibility ends;
        /// </summary>
        private int m_InvincibilityStacks = 0;

        /// <summary>
        /// public accessor to invincibility state
        /// </summary>
        public bool IsInvincible
        {
            get
            {
                return m_InvincibilityStacks > 0;
            }
        }

        //PlayerDownDirection is used when calculating sprite rotation and aim angle bounds
        protected Vector2 PlayerDownDirection = new(0f,-1f);

        //How fast the sprite rotates to match the physical orientation of the character
        public float SpriteRotationSpeedHz = 4.0f;

        //******* DEBUG *******

        Vector2 LastEffectiveVelocity;
        Vector2 LastWorldPosition;
        Vector2 LastScreenPosition;
        Vector2 LastMousePosition;
        Vector2 LastLaunchComponents;

        protected TextMeshProUGUI m_DebugText;

        //***** END DEBUG *****

        public JumpState m_state = JumpState.Grounded;
        private bool m_doLaunch;
        internal Collider2D m_collider2d;
        internal AudioSource m_audioSource;
        internal ParticleSystem m_chargeParticles;
        internal ParticleSystem.MainModule m_chargePFX;
        public Health m_health;
        public bool m_controlEnabled = true;

        //TODO: everything in UI should go into a new component
        internal Canvas m_UICanvas;
        internal SpriteRenderer m_AimArrowSprite;

        public Color[] chargeLevelColors = { Color.red, Color.green, Color.blue, Color.white };

        Vector2 m_move;
        Vector2 m_aim;
        internal SpriteRenderer m_spriteRenderer;
        internal Animator m_animator;
        readonly PlatformerModel m_model = Simulation.GetModel<PlatformerModel>();
        bool m_bIsPaused = false;

        private InputAction m_MoveAction;
        private InputAction m_JumpAction;
        private InputAction m_HopAction;
        private InputAction m_StickAimAction;
        private InputAction m_PauseAction;

        public Bounds Bounds => m_collider2d.bounds;

        void Awake()
        {
            m_health = GetComponent<Health>();
            m_audioSource = GetComponent<AudioSource>();
            m_collider2d = GetComponent<Collider2D>();
            m_spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            m_animator = GetComponentInChildren<Animator>();
            m_DebugText = GetComponentInChildren<TextMeshProUGUI>();
            m_chargeParticles = GetComponentInChildren<ParticleSystem>();
            m_chargePFX = m_chargeParticles.main;

            m_chargeParticles.Stop();

            //UI
            m_UICanvas = GetComponentInChildren<Canvas>();
            m_AimArrowSprite = m_UICanvas.gameObject.GetComponentInChildren<SpriteRenderer>();

            m_MoveAction = InputSystem.actions.FindAction("Player/Move");
            m_JumpAction = InputSystem.actions.FindAction("Player/Jump");
            m_HopAction = InputSystem.actions.FindAction("Player/Hop");
            m_StickAimAction = InputSystem.actions.FindAction("Player/StickAim");
            m_PauseAction = InputSystem.actions.FindAction("Player/Pause");

            m_MoveAction.Enable();
            m_JumpAction.Enable();
            m_HopAction.Enable();
            m_StickAimAction.Enable();
            m_PauseAction.Enable();
        }

        protected override void OnDisable()
        {
            //if(transform.parent) transform.SetParent(null);
            base.OnDisable();
        }

        protected void DebugDraw()
        {
            //surface contact debug UI
            //Debug.DrawLine(lastSurfacePoint, lastSurfacePoint + lastSurfaceNormal, Color.red);

            //true velocity debug UI
            //most draw calls the position hasnt actually updated
            //Vector2 effectiveVelocity = ((Vector2)(transform.position) - LastWorldPosition);
            //if (effectiveVelocity.sqrMagnitude > 0.001f) LastEffectiveVelocity = effectiveVelocity;
            //Debug.DrawLine(transform.position, (Vector2)(transform.position) + LastEffectiveVelocity * 30.0f, Color.magenta);
            //LastWorldPosition = transform.position;

            //reported velocity debug UI
            //Debug.DrawLine(collider2d.bounds.center, (Vector2)(collider2d.bounds.center) + LastFrameVelocity.normalized * 2.0f, Color.cyan);

            //chargiing debug UI
            //if (m_chargeStage >= 0 && (m_state == JumpState.Charging || m_state == JumpState.StickCharge))
            //{
            //   Debug.DrawLine(m_collider2d.bounds.center, (Vector2)(m_collider2d.bounds.center) + ((m_chargeStage + 1) * 0.75f * m_aim), chargeLevelColors[m_chargeStage]);
            //}

            //speeds and move inputs are displayed in player character space to make them more intelligible as movement
            //gravity is in world space because it SHOULD be (0,-1) always in player character space
            float lateralMove = Vector2.Dot(m_move, IsGrounded ? AlongGround : lateralDirection);
            float verticalMove = Vector2.Dot(m_move, IsGrounded ? GroundNormal : -personalGravityDirection); //dot with UP vector for readability
            float lateralVel = Vector2.Dot(velocity, IsGrounded ? AlongGround : lateralDirection);
            float verticalVel = Vector2.Dot(velocity, IsGrounded ? GroundNormal : -personalGravityDirection);

            m_DebugText.SetText(String.Format("IS GROUNDED: {0}\n{1}", IsGrounded, String.Format("POSITION: {0:F2}, {1:F2} \nMOVE: {2:F2}, {3:F2} \nVELOCITY: {4:F2}, {5:F2} \nGRAVITY: {6:F2}, {7:F2} \nAIM {8:F2}, {9:F2}",
                transform.position.x, transform.position.y, lateralMove, verticalMove, lateralVel, verticalVel, personalGravity.x, personalGravity.y, m_aim.x, m_aim.y)));

            m_DebugText.ForceMeshUpdate();
        }

        protected override void FixedUpdate()
        {
            UpdateGravity();
            UpdateJumpState();
            base.FixedUpdate();
        }

        protected override void Update()
        {

            m_reflectedThisFrame = false;

            //update whether the player is pre-ballistic
            if(m_bIsPreBallistic)
            {
                m_preBallisticTimeRemaining = Mathf.Max(m_preBallisticTimeRemaining - Time.deltaTime, 0.0f);
                if(m_preBallisticTimeRemaining == 0.0f)
                {
                    m_bIsPreBallistic = false; 
                    Schedule<PlayerStopJump>().player = this;
                }
            }

            UpdateInputs();

            if (m_bIsPaused) return; //halt update immediately

            //THESE GO IN FIXED UPDATE WHY WAS IT LIKE THIS
            //UpdateGravity();
            //UpdateJumpState();

            LastScreenPosition = Camera.main.WorldToScreenPoint(m_collider2d.bounds.center);
            LastMousePosition = Mouse.current.position.value;

            base.Update();

            UpdateUI();
            DebugDraw();
        }

        private void UpdateUI()
        {
            //TODO: the UI should be in its own script but for now....
            //update aim arrow
            if (m_chargeStage >= 0 && (m_state == JumpState.Charging || m_state == JumpState.StickCharge))
            {
                m_AimArrowSprite.size = new(1f, 2f * (m_chargeStage + 1f));
                m_AimArrowSprite.color = chargeLevelColors[m_chargeStage];

                //to scale AND rotate properly requires the arrow's rotation be handled by an empty parent so it cna be offset
                //which is to say: set the parent rotation not the arrow's
                float angle = Vector2.SignedAngle(Vector2.up, m_aim);
                m_AimArrowSprite.transform.parent.rotation = Quaternion.AngleAxis(angle, Vector3.forward);
            }
            else
            {
                m_AimArrowSprite.size = Vector2.zero;
            }
        }

        private void UpdateInputs()
        {
            if(m_PauseAction.WasPerformedThisFrame())
            {
                if(!m_bIsPaused)
                {
                    m_bIsPaused = true;
                    Time.timeScale = 0.0f;
                }
                else
                {
                    m_bIsPaused = false;
                    Time.timeScale = 1.0f;
                }
            }

            if (m_bIsPaused) return; //halt update immediately

            //get move inputs
            m_move = m_MoveAction.ReadValue<Vector2>();

            //snap move inputs
            float moveAngle = Vector2.SignedAngle(Vector2.up, m_move);
            moveAngle /= MoveSnapIncrement;
            moveAngle = Mathf.Floor(moveAngle + 0.5f);
            m_move = Quaternion.AngleAxis(moveAngle * MoveSnapIncrement, Vector3.forward) * Vector2.up * m_move.magnitude;

            if (m_controlEnabled)
            {
                //ground movement or air control
                //if (m_state == JumpState.Grounded || (!m_bIsPreBallistic && m_state == JumpState.InFlight) || m_state == JumpState.Falling)
                //{
                //    m_move = m_state == JumpState.Grounded ? GetAlongGroundComponent(m_MoveAction.ReadValue<Vector2>()) : GetLateralComponent(m_MoveAction.ReadValue<Vector2>());
                //}

                if (m_state == JumpState.Grounded && m_JumpAction.WasPressedThisFrame())
                {
                    //begin charging
                    m_state = JumpState.PrepareToJump;
                }
                else if (m_state == JumpState.Grounded && m_HopAction.WasPressedThisFrame()) //Unchanged bc there's something fucky with velocity when jumping on landing //IsPressed()) //Changed from WasPressedThisFrame bc bunnies can do this
                {
                    //got to Prepare but skip charging
                    m_state = JumpState.PrepareToJump;
                    m_doLaunch = true;
                }
                else if (m_state == JumpState.Stick)
                {
                    //m_move = GetVerticalComponent(m_move);
                    if (m_JumpAction.WasPressedThisFrame())
                    {
                        //begin charging
                        m_state = JumpState.StickCharge;
                    }
                }
                else if ((m_state == JumpState.Charging || m_state == JumpState.PrepareToJump || m_state == JumpState.StickCharge))
                {
                    //m_move = GetVerticalComponent(m_move);
                    //m_move = m_state == JumpState.PrepareToJump ? GetAlongGroundComponent(m_move) : Vector2.zero; //when in charge anim, no move inputs
                    if (!m_JumpAction.IsPressed()) //changed from WasReleasedThisFrame just in case
                    {
                        //end charge and either b-hop or launch
                        if (m_chargeStage < 0)
                        {
                            //dont launch when accidentally tapped for a frame
                            m_state = IsGrounded ? JumpState.Grounded : JumpState.Stick;
                        }
                        m_doLaunch = true;
                    }
                }
            }
            else
            {
                //WHEN CONTROL DISABLED: 
                //cancel charges without launching
                if (m_state == JumpState.Charging || m_state == JumpState.PrepareToJump)
                {
                    //stop charging on the ground
                    jumpChargeTime = 0;
                    m_state = JumpState.Grounded;
                }
                else if (m_state == JumpState.StickCharge || m_state == JumpState.Stick)
                {
                    //stop charging/clinging on walls and fall immediately
                    jumpChargeTime = 0;
                    m_state = JumpState.Falling;
                }

                //kill horizontal momentum
                m_move = Vector2.zero; //GetVerticalComponent(m_move);
                targetVelocity = Vector2.zero; // GetVerticalComponent(targetVelocity);
            }

            //update aim vector
            //TODO: constrain angle
            //Check for gamepad aim input, use mouse aim if stick not moved
            m_aim = m_StickAimAction.ReadValue<Vector2>();
            bool useStick = m_aim.magnitude > 0.05f;
            Vector3 playerScreenPos = Camera.main.WorldToScreenPoint(this.body.position);
            m_aim = useStick ? m_aim.normalized : (Mouse.current.position.value - new Vector2(playerScreenPos.x, playerScreenPos.y)).normalized;

            float aimAngle = Vector2.SignedAngle(Vector2.up, m_aim);
            aimAngle /= AimSnapIncrement;
            aimAngle = Mathf.Floor(aimAngle + 0.5f);
            m_aim = Quaternion.AngleAxis(aimAngle * AimSnapIncrement, Vector3.forward) * Vector2.up;            
        }

        private void UpdateGravity()
        {
            float gravMagnitude = Physics2D.gravity.magnitude;

            //if we don't find any gravity tile, we should have gravity down
            personalGravity = gravMagnitude * Vector2.down;

            List<SuperTile> triggerTiles = FindTriggerTilesAtPoint(Bounds.center);
            bool found = false;
            foreach(SuperTile tile in triggerTiles)
            {
                //we should only ever find one gravity tile, so break if we hit one
                if (found) break;

                switch((SuperTiled2Unity.GravityDirection)(tile.GetPropertyValueAsInt("Gravity")))
                {
                    case GravityDirection.INVALID:
                        continue;
                    case GravityDirection.D:
                        personalGravity = gravMagnitude * Vector2.down;
                        found = true;
                        break;
                    case GravityDirection.DL:
                        personalGravity = gravMagnitude * (Vector2.down + Vector2.left) / math.SQRT2; //we know this is the magnitude already and can skip the sqrt call
                        found = true;
                        break;
                    case GravityDirection.L:
                        personalGravity = gravMagnitude * Vector2.left;
                        found = true;
                        break;
                    case GravityDirection.UL:
                        personalGravity = gravMagnitude * (Vector2.up + Vector2.left) / math.SQRT2;
                        found = true;
                        break;
                    case GravityDirection.U:
                        personalGravity = gravMagnitude * Vector2.up;
                        found = true;
                        break;
                    case GravityDirection.UR:
                        personalGravity = gravMagnitude * (Vector2.up + Vector2.right) / math.SQRT2;
                        found = true;
                        break;
                    case GravityDirection.R:
                        personalGravity = gravMagnitude * Vector2.right;
                        found = true;
                        break;
                    case GravityDirection.DR:
                        personalGravity = gravMagnitude * (Vector2.down + Vector2.right) / math.SQRT2;
                        found = true;
                        break;
                }
            }

            var spriteTx = m_spriteRenderer.transform;
            Vector2 downVector = IsStateOnGround() ? -GroundNormal : personalGravityDirection;
            float angle = Mathf.Atan2(downVector.y, downVector.x) * Mathf.Rad2Deg;
            angle += 90.0f; //otherwise faces down in normal gravity
            Quaternion targetRotation = Quaternion.Euler(0f, 0f, angle);
            spriteTx.rotation = targetRotation;
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (collision.collider.isTrigger)
            {
                return;
            }

            SuperTileLayer asTileLayer = null;
            var ancestor = collision.collider.transform.parent;

            while (ancestor)
            {
                if (ancestor.gameObject)
                {
                    asTileLayer = ancestor.gameObject.GetComponent<SuperTileLayer>();
                    if (asTileLayer)
                    {
                        HandleBeginTileCollision(in asTileLayer, in collision);
                        break;
                    }
                    else
                    {
                        ancestor = ancestor.parent;
                    }
                }
            }


        }

        private void OnCollisionExit2D (Collision2D collision)
        {
            if (collision.collider.isTrigger)
            {
                return;
            }

            SuperTileLayer asTileLayer = null;
            var ancestor = collision.collider.transform.parent;

            while (ancestor)
            {
                if (ancestor.gameObject)
                {
                    asTileLayer = ancestor.gameObject.GetComponent<SuperTileLayer>();
                    if (asTileLayer)
                    {
                        HandleEndTileCollision(in asTileLayer, in collision);
                        break;
                    }
                    else
                    {
                        ancestor = ancestor.parent;
                    }
                }
            }


        }

        protected void HandleEndTileCollision(in SuperTileLayer tileLayer, in Collision2D collision)
        {
            if(transform.parent == collision.collider.gameObject.transform)
            {
                //transform.SetParent(null);
            }
        }

        protected void HandleBeginTileCollision(in SuperTileLayer tileLayer, in Collision2D collision)
        {

            SuperTiled2Unity.CustomProperty physicsProp;
            SuperCustomProperties TiledProps = tileLayer ? tileLayer.gameObject.GetComponent<SuperCustomProperties>() : null; 

            bool validPhysics = TiledProps.TryGetCustomProperty(TiledStringDefinitions.SurfaceTypeKey, out physicsProp) ? physicsProp.m_Type == "int" : false;
            SurfaceType surfaceType = validPhysics ? (SurfaceType)(physicsProp.GetValueAsInt()) : SurfaceType.Invalid;

            if (surfaceType != SurfaceType.Invalid)
            {
                if(surfaceType == SurfaceType.DeadlySurface)
                {
                    //kill you
                    Schedule<PlayerDeath>();
                    return;
                }

                float surfaceness = Vector2.Dot(-personalGravityDirection, collision.contacts[0].normal);

                //attempt to knock player out of weird edge cases with... edges...
                //check whether you are moving roughly parallel to the surface AND the surface is not (currently) a valid walkable surface
                if (Mathf.Abs(Vector2.Dot(m_LastFrameVelocity.normalized, collision.contacts[0].normal)) < 0.01f && Math.Abs(surfaceness) < minFloorSurfaceness)
                {
                    body.position = body.position + collision.contacts[0].normal * 0.05f;
                }

                if (m_state == JumpState.InFlight || m_state == JumpState.Falling)
                {
                    switch (surfaceType)
                    {
                        case SurfaceType.NormalSurface:
                            HandleAirborneNormalSurfaceCollision(collision);
                            return;                            
                        case SurfaceType.RepelSurface:
                            HandleAirborneRepelSurfaceCollision(collision);
                            break;
                        default:
                            return;
                    }
                }
                else if(surfaceness > minFloorSurfaceness)
                {
                    //if the surface doesn't kill you and you're walking and it's a floor, attach to it
                    //transform.SetParent(collision.collider.gameObject.transform);
                }
            }
        }

        protected void HandleAirborneNormalSurfaceCollision(in Collision2D collision)
        {
            ContactPoint2D[] contactPoints = new ContactPoint2D[collision.contactCount];
            collision.GetContacts(contactPoints);
            Vector2 antiNormal = Vector2.zero;

            foreach (ContactPoint2D point in contactPoints)
            {
                antiNormal += point.normal;
            }

            antiNormal.Normalize();
            antiNormal *= -1;


            if (Vector2.Dot(personalGravityDirection, antiNormal) > minFloorSurfaceness)
            {
                //grounded - don't need to stick
                
                //cancel out component of velocity into the ground
                Vector2 velocityIntoGround = Vector2.Dot(m_LastFrameVelocity, antiNormal) * antiNormal;
                m_LastFrameVelocity -= velocityIntoGround;

                //slow ground velocity if needed
                m_LastFrameVelocity = m_LastFrameVelocity.normalized * Mathf.Clamp(m_LastFrameVelocity.magnitude, -maxSpeed, maxSpeed);
                velocity = m_LastFrameVelocity;

                m_bIsPreBallistic = false;
                return;
            }

            float stickVelocity = Vector2.Dot(m_LastFrameVelocity, antiNormal);
            if (m_state == JumpState.InFlight && stickVelocity >= StickSpeedThreshold)
            {
                velocity = Vector2.zero;
                m_state = JumpState.Stick;
                m_bIsPreBallistic = false;
                m_timeUntilFall = StickTime;
                PlayerDownDirection = antiNormal;
            }
            else
            {
                if (m_bIsPreBallistic)
                {
                    m_bIsPreBallistic = false;
                }

                //remove all velocity towards collision point
                m_LastFrameVelocity -= (antiNormal * stickVelocity);
                if (m_LastFrameVelocity.sqrMagnitude > Mathf.Pow(maxSpeed, 2))
                {
                    m_LastFrameVelocity = maxSpeed * m_LastFrameVelocity.normalized;
                }
                velocity = m_LastFrameVelocity;
            }
        }

        protected void HandleAirborneRepelSurfaceCollision(in Collision2D collision)
        {
            if (m_reflectedThisFrame) return;
            m_reflectedThisFrame = true;

            ContactPoint2D[] contactPoints = new ContactPoint2D[collision.contactCount];
            collision.GetContacts(contactPoints);
            Vector2 normal = Vector2.zero;

            foreach (ContactPoint2D point in contactPoints)
            {
                normal += point.normal;
            }
            normal.Normalize();

            // reflection math
            Vector2 normalVelocity = (normal * Vector2.Dot(normal, m_LastFrameVelocity));
            m_LastFrameVelocity = m_LastFrameVelocity - (2 * normalVelocity);
            velocity = m_LastFrameVelocity;
            m_LastLaunchVelocity = m_LastFrameVelocity; //otherwise this will stomp the reflection
        }

        int CalculateChargeStage()
        {
            for( int i = maxChargeStage; i >= 0; --i)
            {
                if(jumpChargeTime > chargeTimes[i])
                {
                    m_chargePFX.startColor = new(chargeLevelColors[Math.Clamp(i, 0, maxChargeStage)]);
                    return i;
                }
            }

            m_chargePFX.startColor = new(chargeLevelColors[0]);
            return -1; //hop not launch
        }

        void UpdateJumpState()
        {
            //before anything, if we're in 'Grounded' state but the last movement update made took us off a ledge, become airborne
            if(m_state == JumpState.Grounded && !IsGrounded)
            {
                m_state = JumpState.InFlight;
            }

            switch (m_state)
            {
                case JumpState.PrepareToJump:
                    jumpChargeTime += Time.deltaTime;
                    m_lastChargeStage = m_chargeStage;
                    m_chargeStage = CalculateChargeStage();

                    if(!IsGrounded) //run off a cliff before charge stops you
                    {
                        m_state = JumpState.InFlight;
                        jumpChargeTime = 0;
                        m_chargeStage = -1;
                        m_doLaunch = false;
                        break;
                    }

                    if(m_chargeStage >= 0)
                    {
                        m_state = JumpState.Charging;
                    } 
                    else if(m_doLaunch)
                    {
                        m_state = JumpState.Launch;
                    }
                    break;

                case JumpState.Charging:

                    jumpChargeTime += Time.deltaTime;
                    m_lastChargeStage = m_chargeStage;
                    m_chargeStage = CalculateChargeStage();

                    if (!m_chargeParticles.isPlaying)
                    {
                        m_chargeParticles.Simulate(0.25f * m_chargeParticles.main.startLifetime.Evaluate(0),true,true);
                        m_chargeParticles.Play();
                    }

                    if (m_doLaunch)
                    {
                        m_state = JumpState.Launch;
                    }
                    break;

                case JumpState.Launch:
                    if (!IsGrounded)
                    {
                        Schedule<PlayerJumped>().player = this;
                        m_state = JumpState.InFlight;
                    }
                    else
                    {
                        //if we're supposed to be launching but didn't, bump the player up to unstick them
                        body.position = body.position + GroundNormal * 0.005f;
                    }
                    break;

                case JumpState.InFlight: 
                    if (IsGrounded)
                    {
                        Schedule<PlayerLanded>().player = this;
                        m_state = JumpState.Landed;
                        m_preBallisticTimeRemaining = 0.0f;
                        m_bIsPreBallistic = false;
                        velocity = GetLateralComponent(velocity);
                    }
                    else if(!m_bIsPreBallistic)
                    {
                        m_chargeParticles.Stop();
                    }
                    break;

                case JumpState.Stick:
                    m_timeUntilFall = Mathf.Max(m_timeUntilFall - Time.deltaTime, 0.0f);
                    if(m_timeUntilFall == 0.0f)
                    {
                        m_state = JumpState.Falling;
                    }
                    break;

                case JumpState.StickCharge:
                    jumpChargeTime += Time.deltaTime;
                    m_lastChargeStage = m_chargeStage;
                    m_chargeStage = CalculateChargeStage();

                    if (!m_chargeParticles.isPlaying)
                    {
                        m_chargeParticles.Simulate(0.25f * m_chargeParticles.main.startLifetime.Evaluate(0), true, true);
                        m_chargeParticles.Play();
                    }
                    else if (m_lastChargeStage != m_chargeStage)
                    {
                        m_chargePFX.startColor = new(chargeLevelColors[Math.Clamp(m_chargeStage, 0, maxChargeStage)]);
                        m_chargeParticles.Play();
                    }
                    break;

                case JumpState.StickLaunch:
                    Schedule<PlayerJumped>().player = this;
                    m_state = JumpState.Falling;
                    break;

                case JumpState.Falling: //this is basically the same as InFlight but you can't stick again
                    if (IsGrounded)
                    {
                        Schedule<PlayerLanded>().player = this;
                        m_state = JumpState.Landed;
                        m_preBallisticTimeRemaining = 0.0f;
                        m_bIsPreBallistic = false;
                        velocity = GetLateralComponent(velocity);
                    }
                    break;

                case JumpState.Landed:
                    m_state = JumpState.Grounded;
                    m_chargeParticles.Stop();
                    break;
            }
        }

        protected override void ComputeVelocity()
        {
            m_LastFrameVelocity = velocity;
            float lateralMove = Vector2.Dot(m_move, IsGrounded ? AlongGround : lateralDirection);
            if (!IsGrounded)
            {
                switch(m_state)
                {
                    case JumpState.InFlight:
                    case JumpState.Falling:
                    case JumpState.StickLaunch:

                        if (!m_bIsPreBallistic)
                        {
                            //decompose and get the signed magnitude of the lateral component with lateralDir being positive
                            Vector2 verticalVel = GetVerticalComponent(velocity);
                            float lateralSpeed = Vector2.Dot(lateralDirection, velocity);
                            Vector2 lateralVel = lateralSpeed * lateralDirection;

                            // apply air control and drag
                            lateralSpeed += lateralMove * airControlLateral * Time.deltaTime;
                            float overspeed = Math.Abs(lateralVel.magnitude) - maxSpeed;
                            float dragRatio = overspeed / (maxDragThreshold - maxSpeed);
                            float drag = Mathf.Lerp(airControlLateral, maxDragLateral, dragRatio);
                            if (lateralSpeed > maxSpeed)
                            {
                                //force left accel
                                float maxDecel = lateralSpeed - (drag * Time.deltaTime);
                                lateralVel = Mathf.Min(Mathf.Max(maxDecel, maxSpeed), lateralSpeed) * lateralDirection;
                            }
                            else if (lateralSpeed < -maxSpeed)
                            {
                                //force right accel
                                float maxDecel = lateralSpeed + (drag * Time.deltaTime);
                                lateralVel = Mathf.Max(Mathf.Min(maxDecel, -maxSpeed), lateralSpeed) * lateralDirection;
                            }
                            else
                            {
                                lateralVel = lateralDirection * lateralSpeed;
                            }

                            //recompose
                            targetVelocity = lateralVel + verticalVel;
                        }
                        else
                        {
                            targetVelocity = m_LastLaunchVelocity;
                        }

                        break;

                    case JumpState.Stick:
                        velocity = Vector2.zero;
                        targetVelocity = Vector2.zero;
                        break;

                    case JumpState.StickCharge:
                        if(m_doLaunch)
                        {
                            if (m_chargeStage >= 0)
                            {
                                velocity.x = launchSpeeds[m_chargeStage] * m_model.jumpModifier * m_aim.x;
                                velocity.y = launchSpeeds[m_chargeStage] * m_model.jumpModifier * m_aim.y;
                                m_preBallisticTimeRemaining = preBallisticTimes[m_chargeStage];
                                if (m_preBallisticTimeRemaining > 0.0f)
                                {
                                    m_bIsPreBallistic = true;
                                }

                                LastLaunchComponents = m_aim;
                                m_LastLaunchVelocity = velocity;

                                jumpChargeTime = 0.0f;
                                m_chargeStage = -1;
                                m_doLaunch = false;
                                m_state = JumpState.StickLaunch;
                            }
                            else
                            {
                                //no b-hop, just fall
                                jumpChargeTime = 0.0f;
                                m_chargeStage = -1;
                                m_doLaunch = false;
                                m_state = JumpState.Falling;
                            }
                        }
                        else 
                        {
                            velocity = Vector2.zero;
                            targetVelocity = Vector2.zero;
                        }
                        break;

                    default:
                        break;
                }

            }
            else if (m_doLaunch)
            {
                //b-hop
                if (m_chargeStage < 0)
                {
                    const float jumpMoveFactor = math.SQRT2 / 2f;
                    
                    //allow players to jump with a certain amount of lateral velocity even from a stand-still, but if the current velocity is better use that
                    Vector2 coldStartVelocity = GetLateralComponent(GetAlongGroundComponent(m_move) * maxSpeed * jumpMoveFactor); //GetLateralComponent(velocity);
                    velocity = GetLateralComponent(velocity);
                    float currentSpeed = Vector2.Dot(velocity, lateralDirection);
                    float coldStartSpeed = Vector2.Dot(coldStartVelocity, lateralDirection);
                    if(Mathf.Sign(currentSpeed) != Mathf.Sign(coldStartSpeed) || Mathf.Abs(coldStartSpeed) > Mathf.Abs(currentSpeed))
                    {
                        velocity = coldStartVelocity;
                    }

                    targetVelocity = velocity;
                    velocity += jumpTakeOffSpeed * m_model.jumpModifier * (-personalGravityDirection);
                    IsGrounded = false;
                }
                else
                {
                    velocity.x = launchSpeeds[m_chargeStage] * m_model.jumpModifier * m_aim.x;
                    velocity.y = launchSpeeds[m_chargeStage] * m_model.jumpModifier * m_aim.y;
                    targetVelocity = velocity;
                    m_preBallisticTimeRemaining = preBallisticTimes[m_chargeStage];
                    if (m_preBallisticTimeRemaining > 0.0f)
                    {
                        m_bIsPreBallistic = true;
                    }

                    LastLaunchComponents = m_aim;
                    m_LastLaunchVelocity = velocity;
                }

                jumpChargeTime = 0.0f;
                m_chargeStage = -1;
                m_doLaunch = false;
            }
            else if(m_state == JumpState.Charging)
            {
                //slide to a stop
                velocity = GetAlongGroundComponent(velocity);
                targetVelocity = Vector2.zero;
            }
            else
            {
                //ground movement w/ accel
                Vector2 verticalVel = Vector2.zero;// GetVerticalComponent(velocity);
                float lateralSpeed = Vector2.Dot(velocity, AlongGround); //magnitude won't give us the sign
                Vector2 lateralVel = lateralSpeed * AlongGround;

                float targetXVelocity = lateralMove * maxSpeed;
                if(targetXVelocity > lateralSpeed)
                {
                    float accel = Math.Sign(lateralSpeed) == 1 ? groundAccel : groundBraking;
                    lateralVel = Mathf.Min(targetXVelocity, lateralSpeed + (accel * Time.deltaTime)) * AlongGround;
                }
                else if (targetXVelocity < lateralSpeed)
                {
                    float accel = Math.Sign(lateralSpeed) == -1 ? groundAccel : groundBraking;
                    lateralVel = Mathf.Max(targetXVelocity, lateralSpeed - (accel * Time.deltaTime)) * AlongGround; 
                }

                targetVelocity = lateralVel;// + verticalVel;
            }

            if (lateralMove > 0.01f)
                m_spriteRenderer.flipX = false;
            else if (lateralMove < -0.01f)
                m_spriteRenderer.flipX = true;

            m_animator.SetBool("grounded", IsStateOnGround());
            m_animator.SetFloat("velocityX", (Mathf.Abs(lateralMove) > RunAnimThreshold ? Mathf.Abs(lateralMove) : 0.0f) / maxSpeed);

            gravityModifier = (m_bIsPreBallistic || m_state == JumpState.Stick || m_state == JumpState.StickCharge || m_state == JumpState.StickLaunch) ? 0 : 1;

            //targetVelocity = velocity;
        }

        public void GrantInvincibility(float duration)
        {
            //add a stack of invincibility and schedule its removal
            m_InvincibilityStacks++;
            Schedule<PlayerRevokeInvincibility>(duration);
        }

        public void RevokeInvincibility()
        {
            m_InvincibilityStacks--;
        }

        //for use in animation to prevent flickering
        protected bool IsStateOnGround()
        {
            return !( m_state == JumpState.InFlight || m_state == JumpState.Falling || m_state == JumpState.Launch || m_state == JumpState.StickLaunch );
        }

        public enum JumpState
        {
            Grounded,
            PrepareToJump,
            Charging,
            Launch,
            InFlight,
            Stick,
            StickCharge,
            StickLaunch,
            Falling,
            Landed
        }
    }
}