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
using UnityEngine.Splines.Interpolators;

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
        /// Max horizontal speed of the player.
        /// </summary>
        public float maxSpeed = 7;

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
        public int chargeStage = 0;

        /// <summary>
        /// Is the player traveling in a straight line (no air-control, no gravity, until collision or state ends)
        /// </summary>
        public bool isPreBallistic = false;

        /// <summary>
        /// Countdown to going ballistic
        /// </summary>
        private float preBallisticTimeRemaining = 0.0f;


        //******* DEBUG *******

        Vector2 LastScreenPosition;
        Vector2 LastMousePosition;
        Vector2 LastLaunchComponents;
        Vector2 LastLaunchVelocity;

        /* internal new */ public TextMeshProUGUI DebugText;

        //***** END DEBUG *****

        public JumpState jumpState = JumpState.Grounded;
        private bool doLaunch;
        /*internal new*/ public Collider2D collider2d;
        /*internal new*/ public AudioSource audioSource;
        public Health health;
        public bool controlEnabled = true;

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
                if(jumpState == JumpState.Grounded || (!isPreBallistic && jumpState == JumpState.InFlight) || jumpState == JumpState.Falling)
                { 
                    move.x = m_MoveAction.ReadValue<Vector2>().x; 
                }


                if (jumpState == JumpState.Grounded && m_JumpAction.WasPressedThisFrame())
                {
                    //begin charging
                    jumpState = JumpState.PrepareToJump;
                }
                else if ((jumpState == JumpState.Charging || jumpState == JumpState.PrepareToJump) && m_JumpAction.WasReleasedThisFrame())
                {
                    //end charge and either b-hop or launch
                    doLaunch = true;
                }
            }
            else
            {
                //cancel charges without launching
                if(jumpState == JumpState.Charging || jumpState == JumpState.PrepareToJump)
                {
                    //stop charging on the ground
                    jumpChargeTime = 0;
                    jumpState = JumpState.Grounded;
                } else if (jumpState == JumpState.StickCharge || jumpState == JumpState.Stick)
                {
                    //stop charging/clinging on walls and fall immediately
                    jumpChargeTime = 0;
                    jumpState = JumpState.Falling;
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
            switch (jumpState)
            {
                case JumpState.PrepareToJump:
                    jumpChargeTime += Time.deltaTime;
                    chargeStage = CalculateChargeStage();

                    if(!IsGrounded) //run off a cliff before charge stops you
                    {
                        jumpState = JumpState.InFlight;
                        jumpChargeTime = 0;
                        chargeStage = -1;
                        doLaunch = false;
                        break;
                    }

                    if(chargeStage >= 0)
                    {
                        jumpState = JumpState.Charging;
                    } 
                    else if(doLaunch)
                    {
                        jumpState = JumpState.Launch;
                    }
                    break;
                case JumpState.Charging:
                    jumpChargeTime += Time.deltaTime;
                    chargeStage = CalculateChargeStage();
                    if (doLaunch)
                    {
                        jumpState = JumpState.Launch;
                    }
                    break;
                case JumpState.Launch:
                    if (!IsGrounded)
                    {
                        Schedule<PlayerJumped>().player = this;
                        jumpState = JumpState.InFlight;
                    }
                    break;
                case JumpState.InFlight: //TODO: handle walls
                    if (IsGrounded)
                    {
                        Schedule<PlayerLanded>().player = this;
                        jumpState = JumpState.Landed;
                        preBallisticTimeRemaining = 0.0f;
                        isPreBallistic = false;
                    }
                    break;
                case JumpState.Landed:
                    jumpState = JumpState.Grounded;
                    break;
            }
        }

        protected override void ComputeVelocity()
        {
            if (!IsGrounded)
            {
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
                else if(jumpState == JumpState.InFlight)
                {
                    velocity = LastLaunchVelocity;
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
            else if(jumpState == JumpState.Charging)
            {
                velocity.x = 0;
            }
            else 
            {
                //just walkin'
                velocity.x = move.x * maxSpeed;
            }

            if (move.x > 0.01f)
                spriteRenderer.flipX = false;
            else if (move.x < -0.01f)
                spriteRenderer.flipX = true;

            animator.SetBool("grounded", IsGrounded);
            animator.SetFloat("velocityX", Mathf.Abs(velocity.x) / maxSpeed);

            gravityModifier = isPreBallistic ? 0 : 1;

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
            Falling,
            Landed
        }
    }
}