using JetBrains.Annotations;
using Platformer.Core;
using Platformer.Gameplay;
using Platformer.Model;
using SuperTiled2Unity;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.Splines.Interpolators;
using static Platformer.Core.Simulation;
using static UnityEditor.ShaderGraph.Internal.KeywordDependentCollection;

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
        public float maxSpeed = 7;

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
        public float[] chargeTimes = { 0.5f, 1.0f, 1.75f,  2.75f };

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
        private int chargeStage = 0;

        /// <summary>
        /// Last calculated charge state, for identifying changes
        /// </summary>
        private int lastChargeStage = 0;

        /// <summary>
        /// Is the player traveling in a straight line (no air-control, no gravity, until collision or state ends)
        /// </summary>
        private bool isPreBallistic = false;

        /// <summary>
        /// Countdown to going ballistic
        /// </summary>
        private float preBallisticTimeRemaining = 0.0f;

        /// <summary>
        /// Used to enforce pre-ballistic constant velocity
        /// </summary>
        private Vector2 LastLaunchVelocity;

        /// <summary>
        /// Used in collision events as velocity becomes 0
        /// </summary>
        private Vector2 LastFrameVelocity;

        /// <summary>
        /// required magnitude of velocity component pointing into surface to stick
        /// </summary>
        public float StickSpeedThreshold = 2.0f;

        /// <summary>
        /// how long to stay stuck before falling if stick charge isn't entered
        /// </summary>
        public float StickTime = 0.5f;

        /// <summary>
        /// countdown to unsticking
        /// </summary>
        private float timeUntilFall = 0.5f;

        /// <summary>
        /// normal of the surface you're launching from 
        /// </summary>
        private Vector2 launchNormal = Vector2.zero;

        /// <summary>
        /// latch to prevent processing multiple reflect hits per frame
        /// </summary>
        private bool reflectedThisFrame = false;

        public float RunAnimThreshold = 0.1f;

        //******* DEBUG *******

        Vector2 LastScreenPosition;
        Vector2 LastMousePosition;
        Vector2 LastLaunchComponents;

        /* internal new */ public TextMeshProUGUI DebugText;

        //***** END DEBUG *****

        public JumpState state = JumpState.Grounded;
        private bool doLaunch;
        /*internal new*/ public Collider2D collider2d;
        /*internal new*/ public AudioSource audioSource;
        /*internal new*/ public ParticleSystem chargeParticles;
        /*internal new*/ public ParticleSystem.MainModule chargePFX;
        public Health health;
        public bool controlEnabled = true;

        public Color[] chargeLevelColors = { Color.white, Color.red, Color.green, Color.blue };

        bool jump;
        Vector2 move;
        Vector2 launchVector;
        SpriteRenderer spriteRenderer;
        internal Animator animator;
        readonly PlatformerModel model = Simulation.GetModel<PlatformerModel>();

        private InputAction m_MoveAction;
        private InputAction m_JumpAction;

        public Bounds Bounds => collider2d.bounds;

        void Awake()
        {
            health = GetComponent<Health>();
            audioSource = GetComponent<AudioSource>();
            collider2d = GetComponent<Collider2D>();
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            animator = GetComponentInChildren<Animator>();
            DebugText = GetComponentInChildren<TextMeshProUGUI>();
            chargeParticles = GetComponentInChildren<ParticleSystem>();
            chargePFX = chargeParticles.main;

            chargeParticles.Stop();

            m_MoveAction = InputSystem.actions.FindAction("Player/Move");
            m_JumpAction = InputSystem.actions.FindAction("Player/Jump");
            
            m_MoveAction.Enable();
            m_JumpAction.Enable();
        }

        protected void DebugDraw()
        {
            Debug.DrawLine(lastSurfacePoint, lastSurfacePoint + lastSurfaceNormal, Color.red, 0.05f);
        }
        protected override void Update()
        {
            DebugDraw();
            UpdateGravity();

            reflectedThisFrame = false;

            //update whether the player is pre-ballistic
            if(isPreBallistic)
            {
                preBallisticTimeRemaining = Mathf.Max(preBallisticTimeRemaining - Time.deltaTime, 0.0f);
                if(preBallisticTimeRemaining == 0.0f)
                {
                    isPreBallistic = false; 
                    Schedule<PlayerStopJump>().player = this;
                }
            }

            //update inputs
            if (controlEnabled)
            {
                //ground movement or air control
                if(state == JumpState.Grounded || (!isPreBallistic && state == JumpState.InFlight) || state == JumpState.Falling)
                { 
                    move.x = m_MoveAction.ReadValue<Vector2>().x; 
                }

                if (state == JumpState.Grounded && m_JumpAction.WasPressedThisFrame())
                {
                    //begin charging
                    state = JumpState.PrepareToJump;
                }
                else if (state == JumpState.Stick)
                {
                    move.x = 0f;
                    if (m_JumpAction.WasPressedThisFrame())
                    {
                        //begin charging
                        state = JumpState.StickCharge;
                    }
                }
                else if ((state == JumpState.Charging || state == JumpState.PrepareToJump || state == JumpState.StickCharge))
                {
                    move.x = state == JumpState.PrepareToJump ? m_MoveAction.ReadValue<Vector2>().x : 0f; //when in charge anim, no move inputs
                    if (m_JumpAction.WasReleasedThisFrame())
                    {
                        //end charge and either b-hop or launch
                        doLaunch = true;
                    }
                }
            }
            else
            {
                //WHEN CONTROL DISABLED: 
                //cancel charges without launching
                if(state == JumpState.Charging || state == JumpState.PrepareToJump)
                {
                    //stop charging on the ground
                    jumpChargeTime = 0;
                    state = JumpState.Grounded;
                } else if (state == JumpState.StickCharge || state == JumpState.Stick)
                {
                    //stop charging/clinging on walls and fall immediately
                    jumpChargeTime = 0;
                    state = JumpState.Falling;
                }

                //kill horizontal momentum
                move.x = 0;
                targetVelocity.x = 0;
            }

            UpdateJumpState();

            LastScreenPosition = Camera.main.WorldToScreenPoint(body.position);
            LastMousePosition = Mouse.current.position.value;

            DebugText.SetText(String.Format( "POSITION: {0:F2}, {1:F2} \nMOUSE: {2:F2}, {3:F2} \nCOMPONENTS: {4:F2}, {5:F2} \nL. VELOCITY: {6:F2}, {7:F2} \nVELOCITY {8:F2}, {9:F2}", 
                LastScreenPosition.x, LastScreenPosition.y, LastMousePosition.x, LastMousePosition.y, LastLaunchComponents.x, LastLaunchComponents.y, LastLaunchVelocity.x, LastLaunchVelocity.y, velocity.x, velocity.y));

            DebugText.ForceMeshUpdate();

            base.Update();
        }

        private void UpdateGravity()
        {
            float gravMagnitude = Physics2D.gravity.magnitude;
            const float diagonalAxisMagnitude = 0.70710678f; // 1 / sqrt(2), the absolute value of both axes for normalized diagonals
            
            //if we don't find any gravity tile, we should have gravity down
            personalGravity = gravMagnitude * Vector2.down;


            List<SuperTile> triggerTiles = UtilityFunctions.FindTriggerTilesAtPoint(Bounds.center);
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
                        personalGravity = gravMagnitude * (new Vector2(-diagonalAxisMagnitude, -diagonalAxisMagnitude));
                        found = true;
                        break;
                    case GravityDirection.L:
                        personalGravity = gravMagnitude * Vector2.left;
                        found = true;
                        break;
                    case GravityDirection.UL:
                        personalGravity = gravMagnitude * (new Vector2(-diagonalAxisMagnitude, diagonalAxisMagnitude));
                        found = true;
                        break;
                    case GravityDirection.U:
                        personalGravity = gravMagnitude * Vector2.up;
                        found = true;
                        break;
                    case GravityDirection.UR:
                        personalGravity = gravMagnitude * (new Vector2(diagonalAxisMagnitude, diagonalAxisMagnitude));
                        found = true;
                        break;
                    case GravityDirection.R:
                        personalGravity = gravMagnitude * Vector2.right;
                        found = true;
                        break;
                    case GravityDirection.DR:
                        personalGravity = gravMagnitude * (new Vector2(diagonalAxisMagnitude, -diagonalAxisMagnitude));
                        found = true;
                        break;
                }
            }

            var spriteTx = spriteRenderer.transform;
            Vector2 downVector = IsStateOnGround() ? -lastSurfaceNormal : personalGravityDirection;
            float angle = Mathf.Atan2(downVector.y, downVector.x) * Mathf.Rad2Deg;
            angle += 90.0f; //otherwise faces down in normal gravity
            Quaternion targetRotation = Quaternion.Euler(0f, 0f, angle);
            spriteTx.rotation = targetRotation;
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            if(collision.collider.isTrigger)
            {
                return;
            }

            SuperTileLayer asTileLayer = null;
            var ancestor = collision.collider.transform.parent;

            while(ancestor)
            {
                if(ancestor.gameObject)
                {
                    asTileLayer = ancestor.gameObject.GetComponent<SuperTileLayer>();
                    if(asTileLayer)
                    {
                        HandleTileCollision(in asTileLayer, in collision);
                        break;
                    }
                    else
                    {
                        ancestor = ancestor.parent;
                    }
                }
            }

            
        }

        protected void HandleTileCollision(in SuperTileLayer tileLayer, in Collision2D collision)
        {

            SuperTiled2Unity.CustomProperty physicsProp;
            SuperCustomProperties TiledProps = tileLayer ? tileLayer.gameObject.GetComponent<SuperCustomProperties>() : null; 

            bool validPhysics = TiledProps.TryGetCustomProperty(UtilityFunctions.SurfaceTypeKey, out physicsProp) ? physicsProp.m_Type == "int" : false;
            SurfaceType surfaceType = validPhysics ? (SurfaceType)(physicsProp.GetValueAsInt()) : SurfaceType.Invalid;

            if (surfaceType != SurfaceType.Invalid)
            {
                if(surfaceType == SurfaceType.DeadlySurface)
                {
                    //kill you
                    Schedule<PlayerDeath>();
                    return;
                }

                //attempt to knock player out of weird edge cases with... edges...
                if(Mathf.Abs(Vector2.Dot(LastFrameVelocity.normalized, collision.contacts[0].normal)) < 0.01f)
                {
                    body.position = body.position + collision.contacts[0].normal * 0.05f;
                }

                if (state == JumpState.InFlight || state == JumpState.Falling)
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


            if (Vector2.Dot(personalGravityDirection, antiNormal) > minGroundNormalY)
            {
                //grounded - don't need to stick
                
                //cancel out component of velocity into the ground
                Vector2 velocityIntoGround = Vector2.Dot(LastFrameVelocity, antiNormal) * antiNormal;
                LastFrameVelocity -= velocityIntoGround;

                //slow ground velocity if needed
                LastFrameVelocity = LastFrameVelocity.normalized * Mathf.Clamp(LastFrameVelocity.magnitude, -maxSpeed, maxSpeed);
                velocity = LastFrameVelocity;

                isPreBallistic = false;
                return;
            }

            float stickVelocity = Vector2.Dot(LastFrameVelocity, antiNormal);
            if (state == JumpState.InFlight && stickVelocity >= StickSpeedThreshold)
            {
                velocity = Vector2.zero;
                state = JumpState.Stick;
                isPreBallistic = false;
                timeUntilFall = StickTime;
            }
            else
            {
                if (isPreBallistic)
                {
                    isPreBallistic = false;
                }

                //remove all velocity towards collision point
                LastFrameVelocity -= (antiNormal * stickVelocity);
                if (LastFrameVelocity.sqrMagnitude > Mathf.Pow(maxSpeed, 2))
                {
                    LastFrameVelocity = maxSpeed * LastFrameVelocity.normalized;
                }
                velocity = LastFrameVelocity;
            }
        }

        protected void HandleAirborneRepelSurfaceCollision(in Collision2D collision)
        {
            if (reflectedThisFrame) return;
            reflectedThisFrame = true;

            ContactPoint2D[] contactPoints = new ContactPoint2D[collision.contactCount];
            collision.GetContacts(contactPoints);
            Vector2 normal = Vector2.zero;

            foreach (ContactPoint2D point in contactPoints)
            {
                normal += point.normal;
            }
            normal.Normalize();

            // reflection math
            Vector2 normalVelocity = (normal * Vector2.Dot(normal, LastFrameVelocity));
            LastFrameVelocity = LastFrameVelocity - (2 * normalVelocity);
            velocity = LastFrameVelocity;
            LastLaunchVelocity = LastFrameVelocity; //otherwise this will stomp the reflection
        }

        protected override void FixedUpdate()
        {
            base.FixedUpdate();
        }

        int CalculateChargeStage()
        {
            for( int i = maxChargeStage; i >= 0; --i)
            {
                if(jumpChargeTime > chargeTimes[i])
                {
                    chargePFX.startColor = new ParticleSystem.MinMaxGradient(chargeLevelColors[Math.Clamp(i, 0, maxChargeStage)]);
                    return i;
                }
            }

            chargePFX.startColor = new ParticleSystem.MinMaxGradient(chargeLevelColors[0]);
            return -1; //hop not launch
        }

        void UpdateJumpState()
        {
            switch (state)
            {
                case JumpState.PrepareToJump:
                    jumpChargeTime += Time.deltaTime;
                    lastChargeStage = chargeStage;
                    chargeStage = CalculateChargeStage();

                    if(!IsGrounded) //run off a cliff before charge stops you
                    {
                        state = JumpState.InFlight;
                        jumpChargeTime = 0;
                        chargeStage = -1;
                        doLaunch = false;
                        break;
                    }

                    if(chargeStage >= 0)
                    {
                        state = JumpState.Charging;
                    } 
                    else if(doLaunch)
                    {
                        state = JumpState.Launch;
                    }
                    break;

                case JumpState.Charging:

                    jumpChargeTime += Time.deltaTime;
                    lastChargeStage = chargeStage;
                    chargeStage = CalculateChargeStage();

                    if (!chargeParticles.isPlaying)
                    {
                        chargeParticles.Simulate(0.25f * chargeParticles.main.startLifetime.Evaluate(0),true,true);
                        chargeParticles.Play();
                    }

                    if (doLaunch)
                    {
                        state = JumpState.Launch;
                    }
                    break;

                case JumpState.Launch:
                    if (!IsGrounded)
                    {
                        Schedule<PlayerJumped>().player = this;
                        state = JumpState.InFlight;
                    }
                    else
                    {
                        body.position = body.position + lastSurfaceNormal * 0.05f;
                    }
                    break;

                case JumpState.InFlight: 
                    if (IsGrounded)
                    {
                        Schedule<PlayerLanded>().player = this;
                        state = JumpState.Landed;
                        preBallisticTimeRemaining = 0.0f;
                        isPreBallistic = false;
                        velocity.y = 0;
                    }
                    else if(!isPreBallistic)
                    {
                        chargeParticles.Stop();
                    }
                    break;

                case JumpState.Stick:
                    timeUntilFall = Mathf.Max(timeUntilFall - Time.deltaTime, 0.0f);
                    if(timeUntilFall == 0.0f)
                    {
                        state = JumpState.Falling;
                    }
                    break;

                case JumpState.StickCharge:
                    jumpChargeTime += Time.deltaTime;
                    lastChargeStage = chargeStage;
                    chargeStage = CalculateChargeStage();

                    if (!chargeParticles.isPlaying)
                    {
                        chargeParticles.Simulate(0.25f * chargeParticles.main.startLifetime.Evaluate(0), true, true);
                        chargeParticles.Play();
                    }
                    else if (lastChargeStage != chargeStage)
                    {
                        chargePFX.startColor = new ParticleSystem.MinMaxGradient(chargeLevelColors[Math.Clamp(chargeStage, 0, maxChargeStage)]);
                        chargeParticles.Play();
                    }
                    break;

                case JumpState.StickLaunch:
                    Schedule<PlayerJumped>().player = this;
                    state = JumpState.Falling;
                    break;

                case JumpState.Falling: //this is basically the same as InFlight but you can't stick again
                    if (IsGrounded)
                    {
                        Schedule<PlayerLanded>().player = this;
                        state = JumpState.Landed;
                        preBallisticTimeRemaining = 0.0f;
                        isPreBallistic = false;
                        velocity.y = 0;
                    }
                    break;

                case JumpState.Landed:
                    state = JumpState.Grounded;
                    chargeParticles.Stop();
                    break;
            }
        }

        protected override void ComputeVelocity()
        {
            LastFrameVelocity = velocity;
            if (!IsGrounded)
            {
                switch(state)
                {
                    case JumpState.InFlight:
                    case JumpState.Falling:
                    case JumpState.StickLaunch:

                        if (!isPreBallistic)
                        {
                            // apply air control and drag
                            velocity.x = velocity.x + (move.x * airControlLateral * Time.deltaTime);
                            float overspeed = Math.Abs(velocity.x) - maxSpeed;
                            float dragRatio = overspeed / (maxDragThreshold - maxSpeed);
                            float drag = Mathf.Lerp(airControlLateral, maxDragLateral, dragRatio);
                            if (velocity.x > maxSpeed)
                            {
                                //force left accel
                                float maxDecel = velocity.x - (drag * Time.deltaTime);
                                velocity.x = Mathf.Min(Mathf.Max(maxDecel, maxSpeed), velocity.x);
                            }
                            else if (velocity.x < -maxSpeed)
                            {
                                //force right accel
                                //force left accel
                                float maxDecel = velocity.x + (drag * Time.deltaTime);
                                velocity.x = Mathf.Max(Mathf.Min(maxDecel, -maxSpeed), velocity.x);
                            }
                        }
                        else
                        {
                            velocity = LastLaunchVelocity;
                        }

                        break;

                    case JumpState.Stick:
                        velocity = Vector2.zero;
                        break;

                    case JumpState.StickCharge:
                        if(doLaunch)
                        {
                            if (chargeStage >= 0)
                            {
                                //TODO: constrain angle, gamepad controls
                                Vector3 playerScreenPos = Camera.main.WorldToScreenPoint(this.body.position);
                                Vector2 launchComponents = (Mouse.current.position.value - new Vector2(playerScreenPos.x, playerScreenPos.y)).normalized;

                                velocity.x = launchSpeeds[chargeStage] * model.jumpModifier * launchComponents.x;
                                velocity.y = launchSpeeds[chargeStage] * model.jumpModifier * launchComponents.y;
                                preBallisticTimeRemaining = preBallisticTimes[chargeStage];
                                if (preBallisticTimeRemaining > 0.0f)
                                {
                                    isPreBallistic = true;
                                }

                                LastLaunchComponents = launchComponents;
                                LastLaunchVelocity = velocity;

                                jumpChargeTime = 0.0f;
                                chargeStage = -1;
                                doLaunch = false;
                                state = JumpState.StickLaunch;
                            }
                            else
                            {
                                //no b-hop, just fall
                                jumpChargeTime = 0.0f;
                                chargeStage = -1;
                                doLaunch = false;
                                state = JumpState.Falling;
                            }
                        }
                        else 
                        {
                            velocity = Vector2.zero;
                        }
                        break;

                    default:
                        break;
                }

            }
            else if (doLaunch)
            {
                //b-hop
                if (chargeStage < 0)
                {
                    velocity.y = jumpTakeOffSpeed * model.jumpModifier;
                    velocity.x = move.x * maxSpeed;
                }
                else
                {
                    //calculate launch angle based on mouse
                    //TODO: constrain angle, gamepad controls
                    Vector3 playerScreenPos = Camera.main.WorldToScreenPoint(this.body.position);
                    Vector2 launchComponents = (Mouse.current.position.value - new Vector2(playerScreenPos.x, playerScreenPos.y)).normalized;

                    velocity.x = launchSpeeds[chargeStage] * model.jumpModifier * launchComponents.x;
                    velocity.y = launchSpeeds[chargeStage] * model.jumpModifier * launchComponents.y;
                    preBallisticTimeRemaining = preBallisticTimes[chargeStage];
                    if (preBallisticTimeRemaining > 0.0f)
                    {
                        isPreBallistic = true;
                    }

                    LastLaunchComponents = launchComponents;
                    LastLaunchVelocity = velocity;
                }

                jumpChargeTime = 0.0f;
                chargeStage = -1;
                doLaunch = false;
            }
            else if(state == JumpState.Charging)
            {
                velocity.x = 0;
            }
            else
            {
                //ground movement w/ accel
                float accel = (Math.Sign(velocity.x) != 0 && (Math.Sign(move.x) != Math.Sign(move.x))) ? groundBraking : groundAccel;
                float targetXVelocity = move.x * maxSpeed;
                if(targetXVelocity > velocity.x)
                {
                    velocity.x = Mathf.Min(targetXVelocity, velocity.x + (accel * Time.deltaTime));
                }
                else if (targetXVelocity < velocity.x)
                {
                    velocity.x = Mathf.Max(targetXVelocity, velocity.x - (accel * Time.deltaTime)); 
                }
            }

            if (move.x > 0.01f)
                spriteRenderer.flipX = false;
            else if (move.x < -0.01f)
                spriteRenderer.flipX = true;

            animator.SetBool("grounded", IsStateOnGround());
            animator.SetFloat("velocityX", (Mathf.Abs(move.x) > RunAnimThreshold ? Mathf.Abs(move.x) : 0.0f) / maxSpeed);

            gravityModifier = (isPreBallistic || state == JumpState.Stick || state == JumpState.StickCharge || state == JumpState.StickLaunch) ? 0 : 1;

            targetVelocity.x = velocity.x;
            targetVelocity.y = velocity.y;
        }

        //for use in animation to prevent flickering
        protected bool IsStateOnGround()
        {
            return !( state == JumpState.InFlight || state == JumpState.Falling || state == JumpState.Launch || state == JumpState.StickLaunch );
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