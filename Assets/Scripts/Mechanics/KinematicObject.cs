using Platformer.Core;
using Platformer.Model;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Platformer.Mechanics
{
    /// <summary>
    /// Implements game physics for some in game entity.
    /// </summary>
    public class KinematicObject : MonoBehaviour
    {
        /// <summary>
        /// The minimum normal (dot product) considered suitable for the entity sit on.
        /// </summary>
        public float minGroundNormalY = .65f;

        /// <summary>
        /// A custom gravity coefficient applied to this entity.
        /// </summary>
        public float gravityModifier = 1f;

        /// <summary>
        /// A custom gravity coefficient applied to this entity.
        /// </summary>
        protected Vector2 lastSurfacePoint = Vector2.zero;

        /// <summary>
        /// A custom gravity coefficient applied to this entity.
        /// </summary>
        protected Vector2 lastSurfaceNormal = Vector2.up;

        /// <summary>
        /// How much moving up a slope should hamper speed, where 1.0f is the base behavior and 0.0f is no penalty
        /// </summary>
        public float slopeEffect = 1.0f;

        /// <summary>
        /// environmentally dependent player gravity. automatically sets the normalized gravity direction when updated.
        /// </summary>
        private Vector2 _pGrav = Vector2.down;
        public Vector2 personalGravity
        {
            get => _pGrav;

            set
            {
                _pGrav = value;
                _pGravDir = _pGrav.normalized;
            }
        }

        /// <summary>
        /// direction of environmentally dependent player gravity. cannot be manually set.
        /// </summary>
        private Vector2 _pGravDir = Vector2.down;
        public Vector2 personalGravityDirection
        {
            get => _pGravDir;
        }


        /// <summary>
        /// The current velocity of the entity.
        /// </summary>
        public Vector2 velocity;

        /// <summary>
        /// Is the entity currently sitting on a surface?
        /// </summary>
        /// <value></value>
        public bool IsGrounded { get; private set; }

        protected Vector2 targetVelocity;
        protected Vector2 groundNormal;
        protected Rigidbody2D body;
        protected ContactFilter2D contactFilter;
        protected RaycastHit2D[] hitBuffer = new RaycastHit2D[16];

        protected const float minMoveDistance = 0.001f;
        protected const float shellRadius = 0.01f;


        /// <summary>
        /// Bounce the object's vertical velocity.
        /// </summary>
        /// <param name="value"></param>
        public void Bounce(float value)
        {
            velocity.y = value;
        }

        /// <summary>
        /// Bounce the objects velocity in a direction.
        /// </summary>
        /// <param name="dir"></param>
        public void Bounce(Vector2 dir)
        {
            velocity.y = dir.y;
            velocity.x = dir.x;
        }

        /// <summary>
        /// Teleport to some position.
        /// </summary>
        /// <param name="position"></param>
        public void Teleport(Vector3 position)
        {
            body.position = position;
            velocity *= 0;
            body.linearVelocity *= 0;
        }

        protected virtual void OnEnable()
        {
            body = GetComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
        }

        protected virtual void OnDisable()
        {
            body.bodyType = RigidbodyType2D.Dynamic;
        }

        protected virtual void Start()
        {
            personalGravity = Physics2D.gravity;
            contactFilter.useTriggers = false;
            contactFilter.SetLayerMask(Physics2D.GetLayerCollisionMask(gameObject.layer));
            contactFilter.useLayerMask = true;
        }

        protected virtual void Update()
        {
            targetVelocity = Vector2.zero;
            ComputeVelocity();
        }

        protected virtual void ComputeVelocity()
        {

        }

        protected virtual void FixedUpdate()
        {
            velocity += gravityModifier * personalGravity * Time.deltaTime;

            velocity.x = targetVelocity.x;

            IsGrounded = false;

            var moveAlongGravity = (personalGravityDirection * Vector2.Dot(personalGravityDirection, velocity)) * Time.deltaTime;

            var moveAlongGround = (velocity - moveAlongGravity) * Time.deltaTime;//new Vector2(groundNormal.y, -groundNormal.x);

            PerformMovement(moveAlongGround, false);

            PerformMovement(moveAlongGravity, true);

        }

        void PerformMovement(Vector2 move, bool yMovement) //true is movement on the axis of gravity, false is movement perpendicular to gravity
        {
            var distance = move.magnitude;

            if (distance > minMoveDistance)
            {
                //check if we hit anything in current direction of travel
                var count = body.Cast(move, contactFilter, hitBuffer, distance + shellRadius);
                for (var i = 0; i < count; i++)
                {
                    var currentNormal = hitBuffer[i].normal;

                    lastSurfacePoint = hitBuffer[0].point;
                    lastSurfaceNormal = hitBuffer[0].normal;

                    float groundedness = Vector2.Dot(currentNormal, -(personalGravityDirection));

                    //is this surface flat enough to land on?
                    if (groundedness > minGroundNormalY)
                    {
                        IsGrounded = true;
                        // if moving up, change the groundNormal to new surface normal.
                        if (yMovement)
                        {
                            groundNormal = currentNormal;
                            //currentNormal.x = 0;
                        }
                    }
                    if (IsGrounded)
                    {
                        //how much of our velocity aligns with surface normal?
                        var projection = Vector2.Dot(velocity, currentNormal);
                        if (projection < 0)
                        {
                            //slower velocity if moving against the normal (up a hill).
                            velocity = velocity - (slopeEffect * (projection * currentNormal));

                            //slight boost up to ease transition
                            velocity += personalGravityDirection * -0.05f;
                        }
                    }
                    else
                    {
                        //We are airborne, but hit something, so cancel any velocity not in the direction of gravity -- FORMERLY: cancel vertical up and horizontal velocity.
                        velocity = personalGravityDirection * Mathf.Max(Vector2.Dot(personalGravityDirection, velocity), 0.0f);
                    }
                    //remove shellDistance from actual move distance.
                    var modifiedDistance = hitBuffer[i].distance - shellRadius;
                    distance = modifiedDistance < distance ? modifiedDistance : distance;
                }
            }
            body.position = body.position + move.normalized * distance;
        }

    }
}