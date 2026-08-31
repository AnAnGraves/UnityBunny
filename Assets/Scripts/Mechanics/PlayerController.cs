using Platformer.Core;
using Platformer.Gameplay;
using Platformer.Model;
using SuperTiled2Unity;
using System;
using System.Collections.Generic;
using TMPro;
using Unity.Mathematics;
using Unity.Mathematics.Geometry;
using UnityEngine;
using UnityEngine.InputSystem;
using static Platformer.Core.Simulation;
using static UtilityFunctions;
using static UtilityFunctions.OrderThreeResult;

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
        public float maxSpeed = 3f;

        /// <summary>
        /// Max horizontal speed of the player. (u/s)
        /// </summary>
        public float slideSpeedModifier = 1.5f;

        /// <summary>
        /// Max horizontal acceleration of the player on the ground (u/s^2).
        /// </summary>
        public float groundAccel = 14f;

        /// <summary>
        /// increased acceleration force when lowering speed.
        /// </summary>
        public float groundBraking = 28f;

        /// <summary>
        /// reduced braking force when in slide across flat ground
        /// </summary>
        public float slideDownhillAccelModifier = 1.5f;

        /// <summary>
        /// reduced braking force when in slide across flat ground
        /// </summary>
        public float slideFlatDecelModifier = 0.5f;

        /// <summary>
        /// reduced braking force when in slide across flat ground
        /// </summary>
        public float slideUphillDecelModifier = 2.0f;

        /// <summary>
        /// running start needed to enter slide
        /// </summary>
        public float minSlideSpeed = 1.0f;

        /// <summary>
        /// Max horizontal acceleration per second in air when allowed
        /// </summary>
        public float airControlLateral = 3;

        /// <summary>
        /// Max drag from overspeed
        /// </summary>
        public float maxDragLateral = 9f;

        /// <summary>
        /// Amount by which speed exceeds max speed before hitting max drag
        /// </summary>
        public float maxDragThreshold = 10f;

        /// <summary>
        /// Initial jump velocity at the start of a b-hop.
        /// </summary>
        public float jumpTakeOffSpeed = 2.0f;

        /// <summary>
        /// How long the player charged this jump
        /// </summary>
        private float jumpChargeTime = 0f;

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
        private int m_chargeStage = -1;

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
        bool m_bFrameAdvance = false;

        private InputAction m_MoveAction;
        private InputAction m_JumpAction;
        private InputAction m_HopAction;
        private InputAction m_StickAimAction;
        private InputAction m_PauseAction;
        private InputAction m_FrameAdvanceAction;
        private InputAction m_SlideAction;

        private ContactFilter2D m_terrainFilter;

        public Bounds Bounds => m_collider2d.bounds;
        public float CapsuleHeight
        {
            get => ((BoxCollider2D)m_collider2d).size.y;
        }
        public float CapsuleHalfWidth
        {
            get => ((BoxCollider2D)m_collider2d).size.x / 2f;
        }

        public Vector2 CapsuleCenter
        {
            get => Bounds.center;
        }

        Vector3[] _BoundsCorners = new Vector3[4];
        Vector3[] _TxBoundsCorners = new Vector3[4];
        protected Vector3[] BoundsCorners
        {
            get => _BoundsCorners;
        }

        protected Vector3[] TransformedBoundsCorners
        {
            get
            {
                Quaternion rotate = Quaternion.AngleAxis(body.rotation, Vector3.forward);
                for (int i = 0; i < 4; ++i)
                {
                    _TxBoundsCorners[i] = (rotate * BoundsCorners[i]) + (Vector3)body.position;
                }
                return _TxBoundsCorners;
            }
        }

        protected Vector2 CapsuleUpVector
        {
            get
            {
                return body.transform.up;
            }
        }

        protected Vector2 CapsuleRightVector
        {
            get
            {
                return body.transform.right;
            }
        }

        bool m_bIsSliding = false;

        protected float CurrentMaxSpeed {
            get => maxSpeed * (m_bIsSliding && IsStateOnGround() ? slideSpeedModifier : 1.0f);
        }

        //when accessing this it is assumed you've already determined you are decelerating against the direction of velocity
        protected float BrakingRate
        {
            get
            {
                if(!IsStateOnGround())
                {
                    return groundAccel;
                }
                else if(!m_bIsSliding)
                {
                    return groundBraking;
                }
                else
                {
                    if(Vector2.Dot(velocity, PersonalGravityDirection) < -0.1f)
                    {
                        return groundAccel * slideUphillDecelModifier;
                    }

                    return groundAccel * slideFlatDecelModifier;
                }
            }
        }

        //when accessing this it is assumed you've already determined you are accelerating in the direction of velocity
        protected float AccelRate
        {
            get
            {
                if (!IsStateOnGround() || !m_bIsSliding)
                {
                    return groundAccel;
                }
                else
                {
                    return groundAccel * slideDownhillAccelModifier;
                }
            }
        }
        
        //tests points from leading edge to trailing, but returns at the first hit rather than checking they all match
        protected bool SlidingSlopeTest(out Vector2 newDownVector)
        {
            const int maxTests = 7;
            Vector2 downVec = -GroundNormal; //player can rotate due to gravity in air before this check, but GroundNormal should be unchanged
            float castOffsetDist = 0.1f; //amount to raise the cast points to avoid them intersecting the terrain
            Vector2 castOffset = castOffsetDist * -downVec; //actual offset vector
            float dist = CapsuleHeight + castOffsetDist; //raycast distance, which is extended by the offset
            float dir = Mathf.Sign(Vector2.Dot(velocity, AlongGround)); //direction of travel in local space, + is right - is left
            Vector2 startOffset = dir * ((Vector2)(CapsuleHalfWidth * body.transform.right)); 
            Vector2 startingPoint = body.position + startOffset; //starting point to test, which should be the leading edge
            Vector2 step = -2f * (startOffset / (maxTests - 1)); //with this, the last test will be the other bottom corner
            RaycastHit2D hit;
            Vector2 castPoint;

            for (int i = 0; i < maxTests; ++i)
            {
                castPoint = startingPoint + (i * step) + castOffset;
                hit = Physics2D.Raycast(castPoint, downVec, dist, LayerMask.GetMask("Terrain"));
                Debug.DrawLine(castPoint, castPoint + (dist * downVec), Color.blue, 2.0f);
                if (hit)
                {
                    Debug.DrawLine(hit.point, hit.point + (dist * hit.normal), Color.red, 2.0f);
                    newDownVector = -hit.normal;
                    return true;
                }
            }

            //no point hit, return false
            newDownVector = Vector2.zero;
            return false;
        }

        protected bool ThreePointSlopeTest(out Vector2 newDownVector)
        {
            float castOffsetDist = 0.1f; //amount to raise the cast points to avoid them intersecting the terrain
            Vector2 castOffset = castOffsetDist * -PersonalGravityDirection; //actual offset vector
            float dist = CapsuleHeight + castOffsetDist; //raycast distance, which is extended by the offset
            RaycastHit2D leftHit = Physics2D.Raycast(body.position + castOffset + (Vector2)(CapsuleHalfWidth * -transform.right), PersonalGravityDirection, dist, LayerMask.GetMask("Terrain"));
            RaycastHit2D rightHit = Physics2D.Raycast(body.position + castOffset + (Vector2)(CapsuleHalfWidth * transform.right), PersonalGravityDirection, dist, LayerMask.GetMask("Terrain"));
            RaycastHit2D midHit = Physics2D.Raycast(body.position + castOffset, PersonalGravityDirection, dist, LayerMask.GetMask("Terrain"));

            if(leftHit)
            {
                if((!rightHit || rightHit.normal == leftHit.normal) && (!midHit || midHit.normal == leftHit.normal))
                {
                    newDownVector = -leftHit.normal;
                    return true;
                }
            }
            else if(rightHit)
            {
                if(!midHit || midHit.normal == rightHit.normal)
                {
                    newDownVector = -rightHit.normal;
                    return true;
                }
            }
            else if(midHit)
            {
                newDownVector = -midHit.normal;
                return true;
            }

            newDownVector = Vector2.zero;
            return false;
        }

        //zero vector is not const and can't be a default argument, so make a convenience function
        protected void ResolveCollisions()
        {
            ResolveCollisions(Vector2.zero);
        }


        protected void ResolveCollisions(Vector2 bump)
        {
            List<Collider2D> overlaps = new();
            Physics2D.OverlapCollider(m_collider2d, m_terrainFilter, overlaps);

            foreach(Collider2D col in overlaps)
            {
                ColliderDistance2D dist = m_collider2d.Distance(col);
                if(dist.distance < -0.01f)
                {
                    body.position += (Vector2)(1.1f * dist.distance * dist.normal);

                    if(col is PolygonCollider2D polcol)
                    {
                        for(int i = 0; i < polcol.pathCount; ++i)
                        {
                            List<Vector2> points = new();
                            polcol.GetPath(i, points);
                            for(int k = 0; k < points.Count - 1; ++k)
                            {
                                Debug.DrawLine((Vector2)polcol.transform.position + points[k], (Vector2)polcol.transform.position + points[k + 1], Color.magenta);
                            }
                        }
                    }
                }
            }

            Physics2D.SyncTransforms();
        }

        protected float RotateCapsuleAndUnstick(Vector2 downVector, bool bump = false)
        {
            float res = RotateCapsule(downVector);
            ResolveCollisions();

            if(bump)
            {
                float dir = Mathf.Sign(Vector2.Dot(velocity, AlongGround));
                body.position += (Vector2)(dir * 0.05f * AlongGround);
            }

            return res;
        }

        //returns the signed angle of change
        protected float RotateCapsule(Vector2 downVector)
        {
            float angle = Mathf.Atan2(downVector.y, downVector.x) * Mathf.Rad2Deg;
            angle += 90.0f; //otherwise faces down in normal gravity

            //get the signed angled from old to new
            float delta = SignedAngleBetweenAngles(body.rotation, angle);

            //rotate and sync
            body.rotation = angle;
            Physics2D.SyncTransforms();

            //return the change
            return delta;
        }

        //rotates
        protected float RotateCapsuleWithoutClippingGround(Vector2 downVector, float extraPush = 0f)
        {
            float angle = Mathf.Atan2(downVector.y, downVector.x) * Mathf.Rad2Deg;
            angle += 90.0f; //otherwise faces down in normal gravity

            //get the signed angled from old to new
            float delta = angle - body.rotation;
            delta = Mod((delta + 180f), 360f) - 180f;

            Vector2 oldUp = CapsuleUpVector;
            body.rotation = angle;
            Physics2D.SyncTransforms();
            //Vector2 newUp = CapsuleUpVector;
            //float delta = Mathf.Abs(Vector2.SignedAngle(oldUp, newUp));

            //if(delta > 50f) //really 45 but since it's 45 degree increments may as well leave room
            {
                //calculate furthest distance a corner is "below" position based on old Up and move that far 
                Vector3[] corners = TransformedBoundsCorners;
                float maxDist = 0f;
                foreach(Vector2 corner in corners)
                {
                    float dist = Vector2.Dot((body.position - corner), oldUp);
                    if(dist > maxDist)
                    {
                        maxDist = dist;
                    }
                }

                maxDist += extraPush;
                if(maxDist > 0.01f)
                {
                    Vector2 move = maxDist * oldUp;
                    body.position = body.position + move;
                    Physics2D.SyncTransforms();
                }
            }

            return delta;
        }

        protected void SnapToSurfaceUnderPoint(Vector2 point, Vector2 antinormal)
        {
            RaycastHit2D surfaceHit = Physics2D.Raycast(point, antinormal, 0.2f, LayerMask.GetMask("Terrain"));

            if(surfaceHit)
            {
                Debug.DrawLine(point, surfaceHit.point, Color.red, 2.0f);
                body.position = surfaceHit.point - (0.02f * antinormal);
            }
            else
            {
                Debug.DrawLine(point, point + (0.2f * antinormal), Color.blue, 2.0f);
                body.position = point - (0.02f * antinormal);
            }

            Physics2D.SyncTransforms();
        }

        void Awake()
        {
            m_health = GetComponent<Health>();
            m_audioSource = GetComponentInChildren<AudioSource>();
            m_collider2d = GetComponentInChildren<Collider2D>();
            m_spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            m_animator = GetComponentInChildren<Animator>();
            m_DebugText = GetComponentInChildren<TextMeshProUGUI>();
            m_chargeParticles = GetComponentInChildren<ParticleSystem>();
            m_chargePFX = m_chargeParticles.main;

            m_chargeParticles.Stop();

            m_terrainFilter.ClearDepth();
            m_terrainFilter.ClearNormalAngle();
            m_terrainFilter.SetLayerMask(LayerMask.GetMask("Terrain"));

            //UI
            m_UICanvas = GetComponentInChildren<Canvas>();
            m_AimArrowSprite = m_UICanvas.gameObject.GetComponentInChildren<SpriteRenderer>();

            m_MoveAction = InputSystem.actions.FindAction("Player/Move");
            m_JumpAction = InputSystem.actions.FindAction("Player/Jump");
            m_HopAction = InputSystem.actions.FindAction("Player/Hop");
            m_StickAimAction = InputSystem.actions.FindAction("Player/StickAim");
            m_PauseAction = InputSystem.actions.FindAction("Player/Pause");
            m_FrameAdvanceAction = InputSystem.actions.FindAction("Player/FrameAdvance");
            m_SlideAction = InputSystem.actions.FindAction("Player/Slide");

            m_MoveAction.Enable();
            m_JumpAction.Enable();
            m_HopAction.Enable();
            m_StickAimAction.Enable();
            m_PauseAction.Enable();
            m_FrameAdvanceAction.Enable();
            m_SlideAction.Enable();

            _BoundsCorners[2] = new(-CapsuleHalfWidth,  0            , 0f); //bottom left
            _BoundsCorners[1] = new(CapsuleHalfWidth ,  0            , 0f); //bottom right
            _BoundsCorners[0] = new(CapsuleHalfWidth ,  CapsuleHeight, 0f); //top right
            _BoundsCorners[3] = new(-CapsuleHalfWidth,  CapsuleHeight, 0f); //top left
        }

        protected void UpdatePersonalGravityModifier()
        {
            personalGravityModifier = (m_bIsPreBallistic || m_state == JumpState.Stick || m_state == JumpState.StickCharge || m_state == JumpState.StickLaunch) ? 0f : 1f;
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

            //speeds and move inputs are displayed in player character space to make them more intelligible as movement
            //gravity is in world space because it SHOULD be (0,-1) always in player character space
            float lateralMove = Vector2.Dot(m_move, IsGrounded ? AlongGround : lateralDirection);
            float verticalMove = Vector2.Dot(m_move, IsGrounded ? GroundNormal : -PersonalGravityDirection); //dot with UP vector for readability
            float lateralVel = Vector2.Dot(velocity, IsGrounded ? AlongGround : lateralDirection);
            float verticalVel = Vector2.Dot(velocity, IsGrounded ? GroundNormal : -PersonalGravityDirection);

            m_DebugText.SetText(String.Format("IS GROUNDED: {0}\n{1}", IsGrounded, String.Format("POSITION: {0:F2}, {1:F2} \nMOVE: {2:F2}, {3:F2} \nVELOCITY: {4:F2}, {5:F2} \nGRAVITY: {6:F2}, {7:F2} \nAIM {8:F2}, {9:F2}",
                transform.position.x, transform.position.y, lateralMove, verticalMove, lateralVel, verticalVel, PersonalGravity.x, PersonalGravity.y, m_aim.x, m_aim.y)));

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

            m_reflectedThisFrame = false; //this arguably belongs in FixedUpdate? not sure it will ever matter and technically it's a LITTLE faster to only do it here

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

            if (m_bIsPaused && !m_bFrameAdvance) return; //halt update immediately

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
            if (m_chargeStage >= 0 && IsStateCharging())
            {
                m_AimArrowSprite.size = new(1f, 2f * (m_chargeStage + 1f));
                m_AimArrowSprite.material.SetColor("_MainColor",chargeLevelColors[m_chargeStage]);

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
            m_bFrameAdvance = m_FrameAdvanceAction.WasPerformedThisFrame();

            if (m_PauseAction.WasPerformedThisFrame())
            {
                m_bIsPaused = !m_bIsPaused;
            }

            if (m_bIsPaused && !m_bFrameAdvance)
            {
                Time.timeScale = 0.0f;
                return; //halt update immediately
            }
            else
            {
                Time.timeScale = 1.0f;
            }

            bool wasSliding = m_bIsSliding;
            m_bIsSliding = false;
            //check slide state
            if((IsGrounded || IsStateOnGround()) && m_SlideAction.IsPressed())
            {
                if(!WasGrounded)
                {
                    m_bIsSliding = true;
                }
                else if(velocity.magnitude > minSlideSpeed) //with enough speed you can always slide
                {
                    if (wasSliding || m_SlideAction.WasPressedThisFrame()) //can slide by holding if you either were sliding already or were airborne
                    {
                        m_bIsSliding = true;
                    }
                }
                else if(wasSliding && (velocity.magnitude > 0.05f || Mathf.Abs(Vector2.Dot(AlongGround, PersonalGravityDirection)) > 0.05f)) //if you were sliding and you're on a slope, keep sliding even if your velocity is passing 0
                {
                    m_bIsSliding = true;
                }
            }

            if(!m_bIsSliding)
            {
                //get move inputs
                m_move = m_MoveAction.ReadValue<Vector2>();

                //snap move inputs
                float moveAngle = Vector2.SignedAngle(Vector2.up, m_move);
                moveAngle /= MoveSnapIncrement;
                moveAngle = Mathf.Floor(moveAngle + 0.5f);
                m_move = Quaternion.AngleAxis(moveAngle * MoveSnapIncrement, Vector3.forward) * Vector2.up * m_move.magnitude;

                //make diagonals also count as fully in the right direction
                float alignment = Vector2.Dot(m_move, AlongGround);
                if (Mathf.Abs(alignment) > 0.5f)
                {
                    m_move = m_move.magnitude * Mathf.Sign(alignment) * AlongGround;
                }
            }
            else
            {
                //in essence, all sliding actually is is automatically setting your move input and applying some speed/accel modifiers
                float slope = Vector2.Dot(AlongGround, PersonalGravityDirection);
                m_move = Mathf.Abs(slope) > 0.05f ? AlongGround * Mathf.Sign(slope) : Vector2.zero;
            }

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
                    m_state = JumpState.StartHop;
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

        protected GravityDirection GetOppositeGravity(int nominalDir)
        {
            return GetOppositeGravity((GravityDirection)nominalDir);
        }

        protected GravityDirection GetOppositeGravity(GravityDirection nominalDir)
        {
            GravityDirection outGrav = nominalDir;

            switch(outGrav)
            {
                case GravityDirection.INVALID:
                    break;
                case GravityDirection.D:
                    outGrav = GravityDirection.U;
                    break;
                case GravityDirection.DL:
                    outGrav = GravityDirection.UR;
                    break;
                case GravityDirection.L:
                    outGrav = GravityDirection.R;
                    break;
                case GravityDirection.UL:
                    outGrav = GravityDirection.DR;
                    break;
                case GravityDirection.U:
                    outGrav = GravityDirection.D;
                    break;
                case GravityDirection.UR:
                    outGrav = GravityDirection.DL;
                    break;
                case GravityDirection.R:
                    outGrav = GravityDirection.L;
                    break;
                case GravityDirection.DR:
                    outGrav = GravityDirection.UL;
                    break;
            }

            return outGrav;
        }

        private void UpdateGravity()
        {
            float gravMagnitude = Physics2D.gravity.magnitude;
            UpdatePersonalGravityModifier();

            //if we don't find any gravity tile use Down
            Vector2 oldGravity = PersonalGravity;
            Vector2 oldDir = PersonalGravityDirection;
            PersonalGravity = gravMagnitude * Vector2.down;

            List<SuperTile> triggerTiles = FindTriggerTilesAtPoint(body.position + (CapsuleUpVector * 0.02f));
            bool found = false;
            foreach(SuperTile tile in triggerTiles)
            {
                //we should only ever find one gravity tile, so break if we hit one
                if (found) break;

                switch((GravityDirection)(tile.GetPropertyValueAsInt("Gravity")))
                {
                    case GravityDirection.INVALID:
                        continue;
                    case GravityDirection.D:
                        zeroGToggle = 1f;
                        PersonalGravity = gravMagnitude * Vector2.down;
                        found = true;
                        break;
                    case GravityDirection.DL:
                        zeroGToggle = 1f;
                        PersonalGravity = gravMagnitude * (Vector2.down + Vector2.left) / math.SQRT2; //we know this is the magnitude already and can skip the sqrt call
                        found = true;
                        break;
                    case GravityDirection.L:
                        zeroGToggle = 1f;
                        PersonalGravity = gravMagnitude * Vector2.left;
                        found = true;
                        break;
                    case GravityDirection.UL:
                        zeroGToggle = 1f;
                        PersonalGravity = gravMagnitude * (Vector2.up + Vector2.left) / math.SQRT2;
                        found = true;
                        break;
                    case GravityDirection.U:
                        zeroGToggle = 1f;
                        PersonalGravity = gravMagnitude * Vector2.up;
                        found = true;
                        break;
                    case GravityDirection.UR:
                        zeroGToggle = 1f;
                        PersonalGravity = gravMagnitude * (Vector2.up + Vector2.right) / math.SQRT2;
                        found = true;
                        break;
                    case GravityDirection.R:
                        zeroGToggle = 1f;
                        PersonalGravity = gravMagnitude * Vector2.right;
                        found = true;
                        break;
                    case GravityDirection.DR:
                        zeroGToggle = 1f; 
                        PersonalGravity = gravMagnitude * (Vector2.down + Vector2.right) / math.SQRT2;
                        found = true;
                        break;
                    case GravityDirection.ZERO:
                        zeroGToggle = 0f; //don't need to alter direction
                        found = true;
                        break;
                    case GravityDirection.KEEP:
                        PersonalGravity = oldGravity;
                        found = true;
                        break;
                }
            }

            UpdateRotations(oldDir);
        }

        protected void UpdateRotations(Vector2 LastGravity)
        {
            //var spriteTx = m_spriteRenderer.transform;
            float sameness = Vector2.Dot(LastGravity, PersonalGravityDirection);
            bool skipSpriteOffset = false; //on the frame thw capsule rotates the offset math breaks and also generally the offset should be zero

            if (!IsStateSticking()) //don't ever try to rotate the player while they're sticking to a surface!
            {
                if (IsStateOnGround() && sameness < 0.1f) //when gravity inverts while on a surface
                {
                    RotateCapsuleWithoutClippingGround(PersonalGravityDirection);
                    skipSpriteOffset = true;
                }
                else if (!IsStateOnGround() && !IsGrounded)
                {
                    //see if we're likely to hit a surface and attach to it soon
                    Vector2 originalUp = CapsuleUpVector;
                    RaycastHit2D velCastHit = Physics2D.Raycast(CapsuleCenter, velocity.normalized, CapsuleHeight, LayerMask.GetMask("Terrain"));
                    //Debug.DrawLine(CapsuleCenter, CapsuleCenter + (CapsuleHeight * velocity.normalized), Color.yellow, 1.0f);
                    if (velCastHit)
                    {
                        if (Vector2.Dot(-PersonalGravityDirection, velCastHit.normal) > 0.5f)
                        {
                            //Debug.DrawLine(velCastHit.point, velCastHit.point + (velCastHit.normal * CapsuleHeight), Color.green, 1.0f);
                            RotateCapsuleAndUnstick(-velCastHit.normal);
                            skipSpriteOffset = true;
                        }
                    }
                    else
                    {
                        //check if we're falling and there's a surface below us
                        Vector2 vertVel = GetVerticalComponent(velocity);
                        float sign = -Mathf.Sign(Vector2.Dot(vertVel, PersonalGravityDirection)); //negate to get dot with Up
                        float gravSpeed = sign * vertVel.magnitude;
                        if (gravSpeed < 0 || gravSpeed < (velocity - vertVel).magnitude) //if we're falling or if lateral velocity is dominant (don't need to Abs gravSpeed bc if it's negative it already passed)
                        {
                            RaycastHit2D gravCastHit = Physics2D.Raycast(CapsuleCenter, PersonalGravityDirection, CapsuleHeight * 1.5f, LayerMask.GetMask("Terrain"));
                            //Debug.DrawLine(CapsuleCenter, CapsuleCenter + (CapsuleHeight * 1.5f * PersonalGravityDirection), Color.darkCyan, 1.0f);
                            if (gravCastHit)
                            {
                                if (Vector2.Dot(-PersonalGravityDirection, gravCastHit.normal) > 0.5f)
                                {
                                    //Debug.DrawLine(gravCastHit.point, gravCastHit.point + (gravCastHit.normal * CapsuleHeight), Color.darkMagenta, 1.0f);
                                    RotateCapsuleAndUnstick(-gravCastHit.normal);
                                    skipSpriteOffset = true;
                                }
                            }
                            else if (Vector2.Dot(originalUp, -PersonalGravityDirection) < 0.9f)
                            {
                                RotateCapsuleAndUnstick(PersonalGravityDirection);
                                skipSpriteOffset = true;
                            }
                        }
                        else if (Vector2.Dot(originalUp, -PersonalGravityDirection) < 0.9f)
                        {
                            RotateCapsuleAndUnstick(PersonalGravityDirection);
                            skipSpriteOffset = true;
                        }
                    }
                }
                else if (IsStateOnGround() && !IsGrounded)
                {
                    Vector2 newDown;
                    if (SlidingSlopeTest(out newDown) && (Vector2.Dot(-CapsuleUpVector, newDown) < 0.9f)) 
                    {
                        float angle = RotateCapsuleAndUnstick(newDown);
                        SnapToSurfaceUnderPoint(body.position, newDown);
                        GroundNormal = -newDown;
                        AlignVelocityToGround();
                        IsGrounded = true;
                        skipSpriteOffset = true;
                    }
                }
                else if (IsStateOnGround())
                {
                    Vector2 newDown;
                    if (ThreePointSlopeTest(out newDown) && (Vector2.Dot(-CapsuleUpVector, newDown) < 0.9f))
                    {
                        RotateCapsuleAndUnstick(newDown);
                        SnapToSurfaceUnderPoint(body.position, newDown);
                        skipSpriteOffset = true;
                    }
                }
            }

            ResolveCollisions();
            UpdateSpriteRotation(skipSpriteOffset);
        }

        //check the distance to ground of the corners and middle and use those distance
        protected void UpdateSpriteRotation(bool skipSpriteOffset)
        {

            if(IsStateSticking() || !IsStateOnGround())
            {
                m_spriteRenderer.transform.parent.parent.localPosition = Vector3.zero;
                m_spriteRenderer.transform.parent.localRotation = Quaternion.identity;
                return;
            }

            float castOffsetDist = 0.1f; //amount to raise the cast points to avoid them intersecting the terrain
            Vector2 castOffset = castOffsetDist * -PersonalGravityDirection; //actual offset vector
            float dist = CapsuleHeight + castOffsetDist; //raycast distance, which is extended by the offset

            Vector2 spriteOffset = (Vector2)(m_spriteRenderer.transform.parent.localPosition.x * transform.right);
            Vector2 leftOrigin = body.position + (Vector2)(CapsuleHalfWidth * -transform.right) + spriteOffset;
            Vector2 midOrigin = body.position + spriteOffset;
            Vector2 rightOrigin = body.position + (Vector2)(CapsuleHalfWidth * transform.right) + spriteOffset;

            RaycastHit2D leftHit =  Physics2D.Raycast(leftOrigin    + castOffset, PersonalGravityDirection, dist, LayerMask.GetMask("Terrain"));
            RaycastHit2D midHit =   Physics2D.Raycast(midOrigin + castOffset, PersonalGravityDirection, dist, LayerMask.GetMask("Terrain"));
            RaycastHit2D rightHit = Physics2D.Raycast(rightOrigin + castOffset, PersonalGravityDirection, dist, LayerMask.GetMask("Terrain"));

            float leftDist = leftHit ? leftHit.distance - castOffsetDist : -1f;
            float midDist = midHit ? midHit.distance - castOffsetDist : -1f;
            float rightDist = rightHit ? rightHit.distance - castOffsetDist : -1f;

            Vector2 spriteUp = Vector2.up;
            Vector2 offset = Vector2.zero;

            int count = (leftHit ? 1 : 0) + (midHit ? 1 : 0) + (rightHit ? 1 : 0);
            OrderThreeResult ordering = OrderThree(leftDist, midDist, rightDist);

            //since we expect distance >= 0 (or if there's clipping at least > -1) any null hits should come first
            if(count == 0)
            {
                //no surface found, let transform reset
            }
            else if(count == 1)
            {
                //only _LAST cast hit surface
                if((ordering & LEFT_LAST) != 0)
                {
                    //up vector is normal
                    spriteUp = leftHit.normal;

                    //make middle level with right corner
                    offset = leftDist * -leftHit.normal;

                }
                else if ((ordering & MIDDLE_LAST) != 0)
                {
                    //up vector is normal
                    spriteUp = midHit.normal;
                }
                else
                {
                    //up vector is normal
                    spriteUp = rightHit.normal;

                    //make middle level with right corner
                    offset = rightDist * -rightHit.normal;
                }
            }
            else if(count == 2)
            {
                //only _FIRST cast did NOT hit surface
            }
            else // count == 3
            {
                //all casts hit surface
                Vector2 spriteRight = rightHit.point - leftHit.point;
                spriteUp = Vector2.Perpendicular(spriteRight.normalized); //90 degree CCW rotation brings right to up
                Vector2 midpoint = leftHit.point + (spriteRight / 2f);

                float offsetDist = Mathf.Min(midDist, Vector2.Dot(midpoint - midOrigin, PersonalGravityDirection));
                offset = offsetDist * PersonalGravityDirection;

                Debug.DrawLine(leftHit.point, leftHit.point + spriteRight, Color.yellow);
                Debug.DrawLine(midOrigin, midOrigin + (midDist * PersonalGravityDirection), Color.purple);
                Debug.DrawLine(leftOrigin, rightOrigin, Color.red);
            }

            //get signed angle between capsule up and desired sprite up
            float angle = Mathf.Atan2(spriteUp.y, spriteUp.x) * Mathf.Rad2Deg;
            angle -= 90.0f; //otherwise faces down in normal gravity
            float spriteRotAngle = SignedAngleBetweenAngles(body.rotation, angle);

            m_spriteRenderer.transform.parent.parent.localPosition = skipSpriteOffset ? Vector3.zero : m_spriteRenderer.transform.parent.parent.InverseTransformVector(offset);
            m_spriteRenderer.transform.parent.localRotation = Quaternion.AngleAxis(spriteRotAngle, Vector3.forward);
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

        //converts your lateral air velocity into velocity along ground multiplied by the move input percentage
        protected override void HandleLanding()
        {
            if (!WasGrounded)
            {
                Vector2 groundVel = GetLateralComponent(velocity);
                float groundSpeed = Mathf.Min(groundVel.magnitude, maxSpeed);
                float dir = Mathf.Sign(Vector2.Dot(groundVel, AlongGround));
                float moveFactor = m_SlideAction.IsPressed() ? 1.0f : (Vector2.Dot(groundVel.normalized, m_move) > 0.1f ? m_move.magnitude : 0.0f);
                velocity = dir * moveFactor * groundSpeed * AlongGround;
            }
            else
            {
                velocity = GetAlongGroundComponent(velocity);
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

                float surfaceness = Vector2.Dot(-PersonalGravityDirection, collision.contacts[0].normal);

                //attempt to knock player out of weird edge cases with... edges...
                //check whether you are moving roughly parallel to the surface AND the surface is not (currently) a valid walkable surface
                if (Mathf.Abs(Vector2.Dot(m_LastFrameVelocity.normalized, collision.contacts[0].normal)) < 0.01f && Mathf.Abs(surfaceness) < minFloorSurfaceness)
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
            Vector2 centroid = Vector2.zero;

            foreach (ContactPoint2D point in contactPoints)
            {
                antiNormal += point.normal;
                centroid += point.point;
            }

            antiNormal.Normalize();
            antiNormal *= -1;
            centroid /= collision.contactCount;

            m_bIsPreBallistic = false;

            //if landing, end pre-ballistic and return
            if (Vector2.Dot(PersonalGravityDirection, antiNormal) > minFloorSurfaceness)
            {
                return;
            }

            //if hitting any other surface, check if we should stick to it
            float stickVelocity = Vector2.Dot(m_LastFrameVelocity, antiNormal);
            if (m_state == JumpState.InFlight && stickVelocity >= StickSpeedThreshold)
            {
                velocity = Vector2.zero;
                m_state = JumpState.Stick;
                m_timeUntilFall = StickTime;
                PlayerDownDirection = antiNormal;
                RotateCapsuleAndUnstick(antiNormal);
                SnapToSurfaceUnderPoint(centroid, antiNormal);
            }
            else
            {
                //remove all velocity towards collision point
                velocity -= (antiNormal * stickVelocity);
                if (velocity.sqrMagnitude > maxSpeed*maxSpeed)
                {
                    velocity = maxSpeed * velocity.normalized;
                }
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
                    m_chargePFX.startColor = new(chargeLevelColors[Mathf.Clamp(i, 0, maxChargeStage)]);
                    return i;
                }
            }

            m_chargePFX.startColor = new(chargeLevelColors[0]);
            return -1; //hop not launch
        }

        void UpdateJumpState()
        {
            //before anything, if we're in 'Grounded' state but the last movement update made took us off a ledge, become airborne IF there isn't a walkable surface we should follow below us
            if(m_state == JumpState.Grounded && !IsGrounded)
            {
                Vector2 originalUp = GroundNormal;
                Vector2 newDown;
                bool snapped = false;
                if (SlidingSlopeTest(out newDown) && (Vector2.Dot(originalUp, newDown) > -0.9f)) //just check if they're opposites rather than flip one to match the other
                {
                    if(Vector2.Dot(newDown, PersonalGravityDirection) > 0.5f)
                    {
                        float angle = RotateCapsuleAndUnstick(newDown);
                        SnapToSurfaceUnderPoint(body.position, newDown);
                        //velocity = GetLateralComponent(velocity); // remove vertical component
                        GroundNormal = -newDown; // set new ground alignment
                        AlignVelocityToGround(); //align previous lateral component to ground
                        IsGrounded = true;
                        snapped = true;
                    }
                }

                if (!snapped)
                {
                    m_state = JumpState.InFlight;
                }
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
                case JumpState.StartHop:
                    Simulation.Schedule<PlayerJumped>().player = this;
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
                        //velocity = GetLateralComponent(velocity);
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
                        PlayerDownDirection = PersonalGravityDirection;
                        RotateCapsuleWithoutClippingGround(PersonalGravityDirection, 0.075f);
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
                        m_chargePFX.startColor = new(chargeLevelColors[Mathf.Clamp(m_chargeStage, 0, maxChargeStage)]);
                        m_chargeParticles.Play();
                    }
                    break;

                case JumpState.StickLaunch:
                    Schedule<PlayerJumped>().player = this;
                    m_state = JumpState.Falling;
                    PlayerDownDirection = PersonalGravityDirection;
                    RotateCapsuleWithoutClippingGround(PersonalGravityDirection, 0.075f);
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
                            float overspeed = Mathf.Abs(lateralVel.magnitude) - maxSpeed;
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
                            velocity = targetVelocity;
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
            else if(m_state == JumpState.StartHop)
            {
                const float jumpMoveFactor = math.SQRT2 / 2f;

                //allow players to jump with a certain amount of lateral velocity even from a stand-still, but if the current velocity is better use that
                Vector2 coldStartVelocity = GetLateralComponent(maxSpeed * jumpMoveFactor * m_move); //GetLateralComponent(velocity);
                velocity = GetLateralComponent(velocity);
                float currentSpeed = Vector2.Dot(velocity, lateralDirection);
                float coldStartSpeed = Vector2.Dot(coldStartVelocity, lateralDirection);
                if (Mathf.Sign(currentSpeed) != Mathf.Sign(coldStartSpeed) || Mathf.Abs(coldStartSpeed) > Mathf.Abs(currentSpeed))
                {
                    velocity = coldStartVelocity;
                }

                targetVelocity = velocity;
                velocity += jumpTakeOffSpeed * m_model.jumpModifier * (-PersonalGravityDirection);
                body.position += velocity * Time.deltaTime;
                Physics2D.SyncTransforms();
                IsGrounded = false;
                m_state = JumpState.InFlight;
            }
            else if (m_doLaunch && m_chargeStage >= 0)
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
            else if(m_state == JumpState.Charging)
            {
                //slide to a stop
                velocity = GetAlongGroundComponent(velocity);
                targetVelocity = Vector2.zero;
            }
            else if(m_bIsSliding)
            {
                //ground slide
                float lateralSpeed = Vector2.Dot(velocity, AlongGround); //magnitude won't give us the sign
                Vector2 lateralVel = lateralSpeed * AlongGround;

                float slope = Vector2.Dot(AlongGround, PersonalGravityDirection);
                slope = Mathf.Abs(slope) > 0.05f ? Mathf.Sign(slope) : 0f;

                float targetXVelocity = slideSpeedModifier * maxSpeed * lateralMove;
                if (targetXVelocity > lateralSpeed)
                {
                    float accel = Mathf.Sign(lateralSpeed) == 1 ? groundAccel * slideDownhillAccelModifier : (groundBraking * (slope == 0 ? slideFlatDecelModifier : slideUphillDecelModifier));
                    lateralVel = Mathf.Min(targetXVelocity, lateralSpeed + (accel * Time.deltaTime)) * AlongGround;
                }
                else if (targetXVelocity < lateralSpeed)
                {
                    float accel = Mathf.Sign(lateralSpeed) == -1 ? groundAccel * slideDownhillAccelModifier : (groundBraking * (slope == 0 ? slideFlatDecelModifier : slideUphillDecelModifier));
                    lateralVel = Mathf.Max(targetXVelocity, lateralSpeed - (accel * Time.deltaTime)) * AlongGround;
                }

                targetVelocity = lateralVel;
            }
            else
            {
                //ground movement w/ accel
                float lateralSpeed = Vector2.Dot(velocity, AlongGround); //magnitude won't give us the sign
                Vector2 lateralVel = lateralSpeed * AlongGround;

                float targetXVelocity = lateralMove * maxSpeed;
                if(targetXVelocity > lateralSpeed)
                {
                    float accel = Mathf.Sign(lateralSpeed) == 1 ? groundAccel : groundBraking;
                    lateralVel = Mathf.Min(targetXVelocity, lateralSpeed + (accel * Time.deltaTime)) * AlongGround;
                }
                else if (targetXVelocity < lateralSpeed)
                {
                    float accel = Mathf.Sign(lateralSpeed) == -1 ? groundAccel : groundBraking;
                    lateralVel = Mathf.Max(targetXVelocity, lateralSpeed - (accel * Time.deltaTime)) * AlongGround; 
                }

                targetVelocity = lateralVel;
            }

            //clean up ended charge whether you jumped or not
            if(m_doLaunch)
            {
                jumpChargeTime = 0.0f;
                m_chargeStage = -1;
                m_doLaunch = false;
            }

            if(m_bIsSliding)
            {
                bool flipped = Vector2.Dot(lateralDirection, velocity) < 0f;
                m_spriteRenderer.flipX = flipped;
                Vector3 baseOffset = m_spriteRenderer.transform.localPosition;
                baseOffset.x = flipped ? -Mathf.Abs(baseOffset.x) : Mathf.Abs(baseOffset.x);
                m_spriteRenderer.transform.localPosition = baseOffset;
            }
            //note that the below can't be combined like this bc facing should not change when the stick is not pressed
            else if (lateralMove > 0.01f)
            {
                m_spriteRenderer.flipX = false;
                Vector3 baseOffset = m_spriteRenderer.transform.localPosition;
                baseOffset.x = Mathf.Abs(baseOffset.x);
                m_spriteRenderer.transform.localPosition = baseOffset;
            }
            else if (lateralMove < -0.01f)
            {
                m_spriteRenderer.flipX = true;
                Vector3 baseOffset = m_spriteRenderer.transform.localPosition;
                baseOffset.x = -Mathf.Abs(baseOffset.x);
                m_spriteRenderer.transform.localPosition = baseOffset;
            }

            //TODO: at some point when the character structure is finalized I should have references to each renderer
            foreach(SpriteRenderer ren in m_spriteRenderer.gameObject.GetComponentsInChildren<SpriteRenderer>())
            {
                ren.flipX = m_spriteRenderer.flipX;
            }

            m_animator.SetBool("grounded", IsStateOnGround());
            m_animator.SetBool("sliding", m_bIsSliding);
            m_animator.SetFloat("velocityX", IsStateCharging() ? 0f : (Mathf.Abs(lateralMove) > RunAnimThreshold ? Mathf.Abs(lateralMove) : 0.0f) / maxSpeed);

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
            return !(m_state == JumpState.InFlight || m_state == JumpState.Falling || m_state == JumpState.Launch || m_state == JumpState.StickLaunch || m_state == JumpState.StartHop);
        }

        protected bool IsStateSticking()
        {
            return (m_state == JumpState.Stick || m_state == JumpState.StickCharge || m_state == JumpState.StickLaunch);
        }

        protected bool IsStateCharging()
        {
            return (m_state == JumpState.Charging || m_state == JumpState.StickCharge);
        }   
        
        protected bool IsStateFlippable()
        {
            return m_state == JumpState.Grounded || m_state == JumpState.Landed;
        }

        public enum JumpState
        {
            Grounded,
            PrepareToJump,
            StartHop,
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