using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Platformer.Gameplay;
using static Platformer.Core.Simulation;
using Platformer.Model;
using Platformer.Core;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using TMPro;
using JetBrains.Annotations;
using System.Text;
using System;
using System.Linq;
using UnityEngine.Splines.Interpolators;
using UnityEditor.Animations;

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
        public float[] chargeTimes = { 0.1f, 0.5f, 1.25f,  2.5f };

        /// <summary>
        /// How long to fly straight (and block move input) before becoming subject to gravity again
        /// </summary>
        public float[] preBallisticTimes = { 0.0f, 0.2f, 0.4f, 0.8f };

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
        /// Highest possible charge state
        /// </summary>
        private int chargeStage = 0;

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
        /// Used to enforce pre-ballistic constant velocity
        /// </summary>
        public PhysicsMaterial2D[] StickyMaterials = { null };

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
        /*internal new*/ public ParticleSystemRenderer chargeParticleRenderer;
        public Health health;
        public bool controlEnabled = true;

        public Material[] chargeLevelMaterials = { null, null, null, null };

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
            spriteRenderer = GetComponent<SpriteRenderer>();
            animator = GetComponent<Animator>();
            DebugText = GetComponentInChildren<TextMeshProUGUI>();
            chargeParticles = GetComponent<ParticleSystem>();
            chargeParticleRenderer = GetComponent<ParticleSystemRenderer>();

            chargeParticles.Stop();

            m_MoveAction = InputSystem.actions.FindAction("Player/Move");
            m_JumpAction = InputSystem.actions.FindAction("Player/Jump");
            
            m_MoveAction.Enable();
            m_JumpAction.Enable();
        }

        protected override void Update()
        {
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
                else if (state == JumpState.Stick && m_JumpAction.WasPressedThisFrame())
                {
                    //begin charging
                    state = JumpState.StickCharge;
                }
                else if ((state == JumpState.Charging || state == JumpState.PrepareToJump || state == JumpState.StickCharge) && m_JumpAction.WasReleasedThisFrame())
                {
                    //end charge and either b-hop or launch
                    doLaunch = true;
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
        private void OnCollisionEnter2D(Collision2D collision)
        {
            if(state == JumpState.InFlight || state == JumpState.Falling)
            {
                if (StickyMaterials.Contains<PhysicsMaterial2D>(collision.collider.attachedRigidbody.sharedMaterial))
                {
                    Vector2 averageContactPoint = Vector2.zero;
                    foreach (ContactPoint2D point in collision.contacts)
                    {
                        averageContactPoint += point.point;
                    }
                    averageContactPoint /= collision.contacts.Length;

                    Vector2 effectiveAntiNormal = averageContactPoint - (Vector2)(collider2d.bounds.center);
                    effectiveAntiNormal.Normalize();

                    float stickVelocity = Vector2.Dot(LastFrameVelocity, effectiveAntiNormal); 
                    if(state == JumpState.InFlight && stickVelocity >= StickSpeedThreshold)
                    {
                        velocity = Vector2.zero;
                        state = JumpState.Stick;
                        isPreBallistic = false;
                        timeUntilFall = StickTime;
                    }
                    else
                    {
                        if(isPreBallistic)
                        {
                            isPreBallistic = false;
                        }

                        //remove all velocity towards collision point
                        LastFrameVelocity -= (effectiveAntiNormal * stickVelocity);
                        if(LastFrameVelocity.sqrMagnitude > Mathf.Pow(maxSpeed,2))
                        {
                            LastFrameVelocity = maxSpeed * LastFrameVelocity.normalized;
                        }
                        velocity = LastFrameVelocity;
                    }
                }
            }
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
                    return i;
                }
            }

            return -1; //hop not launch
        }

        void UpdateJumpState()
        {
            switch (state)
            {
                case JumpState.PrepareToJump:
                    jumpChargeTime += Time.deltaTime;
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
                    chargeStage = CalculateChargeStage();

                    chargeParticleRenderer.material = chargeLevelMaterials[Math.Clamp(chargeStage, 0, maxChargeStage)];

                    if (!chargeParticles.isPlaying)
                    {
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
                    chargeStage = CalculateChargeStage();

                    chargeParticleRenderer.material = chargeLevelMaterials[Math.Clamp(chargeStage, 0, maxChargeStage)];

                    if (!chargeParticles.isPlaying)
                    {
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
                    else if (!isPreBallistic)
                    {
                        chargeParticles.Stop();
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
                            //no b-hop when stickcharging

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

            animator.SetBool("grounded", IsGrounded);
            animator.SetFloat("velocityX", Mathf.Abs(velocity.x) / maxSpeed);

            gravityModifier = (isPreBallistic || state == JumpState.Stick || state == JumpState.StickCharge || state == JumpState.StickLaunch) ? 0 : 1;

            targetVelocity.x = velocity.x;
            targetVelocity.y = velocity.y;
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