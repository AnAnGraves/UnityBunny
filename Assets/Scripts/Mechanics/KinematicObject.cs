using Platformer.Core;
using Platformer.Model;
using SuperMovingPlatform;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using static UnityEditor.ShaderGraph.Internal.KeywordDependentCollection;

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
        public float minFloorSurfaceness = .65f;

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
        /// backs parametere personalGravity
        /// </summary>
        private Vector2 _pGrav = Vector2.down;

        /// <summary>
        /// backs parameter personalGravityDirection
        /// </summary>
        private Vector2 _pGravDir = Vector2.down;

        /// <summary>
        /// backs parameter lateralDirection
        /// </summary>
        private Vector2 _pLateralDir = Vector2.right;

        /// <summary>
        /// Objects that want to parent the object but currently are not doing so
        /// </summary>
        private List<GameObject> potentialParents = new();

        /// <summary>
        /// Is this object a player?
        /// </summary>
        private bool bIsPlayer = false;

        /// <summary>
        /// this is the only platform allowed to freely upda
        /// </summary>
        GameObject riddenPlatform = null;

        /// <summary>
        /// Stores calculated platform velocity to be imparted when the platform is disconnected from
        /// </summary>
        protected Vector2 platformVelocity = Vector2.zero;

        /// <summary>
        /// Flag to add platform velocity on this update
        /// </summary>
        bool bLeftPlatform = false;

        InputAction snapAction;

        /// <summary>
        /// environmentally dependent player gravity. automatically sets the gravity and lateral directions when updated
        /// </summary>
        public Vector2 personalGravity
        {
            get => _pGrav;

            set
            {
                _pGrav = value;
                _pGravDir = _pGrav.normalized;
                _pLateralDir = Vector2.Perpendicular(_pGravDir);
            }
        }

        /// <summary>
        /// direction of environmentally dependent player gravity. cannot be manually set.
        /// </summary>
        public Vector2 personalGravityDirection
        {
            get => _pGravDir;
        }

        /// <summary>
        /// direction of environmentally dependent player gravity. cannot be manually set.
        /// </summary>
        public Vector2 lateralDirection
        {
            get => _pLateralDir;
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

        bool drewVelThisFrame = false;

        /// <summary>
        /// Bounce the object's vertical velocity.
        /// </summary>
        /// <param name="value"></param>
        public void Bounce(float value)
        {
            velocity = GetLateralComponent(velocity);
            velocity += value * (-personalGravityDirection);
        }

        /// <summary>
        /// Bounce the object's vertical velocity away from a point
        /// </summary>
        /// <param name="value"></param>
        public void Bounce(float value, Vector3 origin)
        {
            velocity = GetLateralComponent(velocity);
            velocity += value * personalGravityDirection * Mathf.Sign(Vector2.Dot(personalGravity, transform.position - origin));
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
            if(transform.parent && transform.parent.gameObject.activeSelf) transform.SetParent(null);
            potentialParents.Clear();
        }

        protected virtual void Start()
        {
            personalGravity = Physics2D.gravity;
            contactFilter.useTriggers = false;
            contactFilter.SetLayerMask(Physics2D.GetLayerCollisionMask(gameObject.layer));
            contactFilter.useLayerMask = true;
            bIsPlayer = (gameObject.GetComponent<PlayerController>() != null);

            snapAction = InputSystem.actions.FindAction("Player/DebugSnapshot");
            snapAction.Enable();
        }

        protected virtual void Update()
        {
            drewVelThisFrame = false;
        }

        protected virtual void ComputeVelocity()
        {

        }

        public void RequestAddToPlatform(GameObject goParent)
        {
            if (riddenPlatform == null)
            {
                riddenPlatform = goParent;
            }
            else if(riddenPlatform != goParent)
            {
                potentialParents.Add(goParent);
            }
        }

        public void RemoveFromPlatform(GameObject goParent)
        {
            if (riddenPlatform == goParent)
            {
                if(potentialParents.Count > 0)
                {
                    riddenPlatform = potentialParents[0];
                    potentialParents.RemoveAt(0);
                }
                else
                {
                    riddenPlatform = null;
                    bLeftPlatform = true;
                }
            }
            else
            {
                potentialParents.Remove(goParent);
            }
        }

        protected Vector2 GetVerticalComponent(in Vector2 vec)
        {
            return Vector2.Dot(personalGravityDirection, vec) * personalGravityDirection;
        }

        protected Vector2 GetLateralComponent(in Vector2 vec)
        {
            return Vector2.Dot(lateralDirection, vec) * lateralDirection;
        }

        protected virtual void FixedUpdate()
        {
            targetVelocity = Vector2.zero;
            ComputeVelocity();

            velocity += gravityModifier * personalGravity * Time.deltaTime;

            //velocity.x = targetVelocity.x;
            velocity = GetVerticalComponent(velocity);
            velocity += GetLateralComponent(targetVelocity);

            if(bLeftPlatform)
            {
                if(riddenPlatform == null) velocity += platformVelocity;
                bLeftPlatform = false;
            }

            IsGrounded = false;

            var moveAlongGravity = (personalGravityDirection * Vector2.Dot(personalGravityDirection, velocity)) * Time.deltaTime;

            var moveAlongGround = (lateralDirection * Vector2.Dot(lateralDirection, velocity)) * Time.deltaTime;
            Vector2 nominalMovement = velocity * Time.deltaTime;

            bool snapped = snapAction.IsPressed();
            if (!drewVelThisFrame && snapped)
            {
                //draw what should be a box with a diagonal if all is working
                float drawVelocityScale = 30.0f;

                Collider2D collider2d = gameObject.GetComponent<Collider2D>();
                Vector2 origin = collider2d ? collider2d.bounds.center : transform.position;
                
                Vector2 vertical = origin + (moveAlongGravity * drawVelocityScale);
                Vector2 horizontal = origin + (moveAlongGround * drawVelocityScale);
                Vector2 corner = (Vector2)(transform.position) + (nominalMovement * drawVelocityScale);
                Debug.DrawLine(origin, vertical, Color.blue, 1f);  // origin -> top left
                Debug.DrawLine(vertical, corner, Color.cyan, 1f);// top left -> top right 
                Debug.DrawLine(origin, horizontal, Color.red, 1f);  // origin -> bottom right
                Debug.DrawLine(horizontal, corner, Color.yellow, 1f); // bottom left -> top right
                Debug.DrawLine(origin, corner, Color.green, 1f);    // origin -> top right
                drewVelThisFrame = true;
            }

            PerformMovement(moveAlongGround, false);

            PerformMovement(moveAlongGravity, true);

        }

        //parenting the player to 
        public void PlatformRideMovement(Vector2 move, GameObject source, bool force = false)
        {
            if (force || source == riddenPlatform)
            {
                body.position = body.position + move;
                platformVelocity = move / Time.deltaTime;
            }
        }

        void PerformMovement(Vector2 move, bool yMovement) //true is movement on the axis of gravity, false is movement perpendicular to gravity
        {
            Vector2 slopeMovement = Vector2.zero;
            Vector2 direction = move.normalized;
            var distance = move.magnitude;

            if (distance > minMoveDistance)
            {
                //check if we hit anything in current direction of travel
                var count = body.Cast(move, contactFilter, hitBuffer, distance + shellRadius);
                bool bHitAWall = false; //for airborne collisions, if we hit something horizontally then start 'falling' (we might still Stick instead)
                bool bHitATopOrBottom = false; //if we hit something vertically, kill vertical velocity
                for (var i = 0; i < count; i++)
                {
                    var currentNormal = hitBuffer[i].normal;
                    float modifiedDistance = hitBuffer[i].distance - (shellRadius);
                    Vector2 modifiedSlopeMovement = Vector2.zero; //if we hit something before the slope we always want to cancel the slope move

                    float groundedness = Vector2.Dot(currentNormal, -(personalGravityDirection));

                    //is this surface flat enough to land on?
                    if (groundedness > minFloorSurfaceness)
                    {
                        IsGrounded = true;
                        groundNormal = currentNormal;
                    }
                    
                    if(!yMovement)
                    {
                        if(IsGrounded) //hit some kind of slope
                        {
                            //uphill direction vector
                            Vector2 uphillSlope = Vector2.Reflect(-1f * currentNormal, direction).normalized;

                            //project movement onto slope
                            modifiedSlopeMovement = uphillSlope * (Vector2.Dot(uphillSlope, direction) * distance);
                        }
                        else //hit a surface we couldn't land on in while airborne
                        {
                            bHitAWall = true;
                        }
                    }
                    else
                    {
                        bHitATopOrBottom = true;
                    }

                    //if this is a new shortest move, replace the move info
                    if (modifiedDistance < distance)
                    {
                        distance = modifiedDistance;
                        slopeMovement = modifiedSlopeMovement;
                        lastSurfacePoint = hitBuffer[i].point;
                        lastSurfaceNormal = hitBuffer[i].normal;
                    }

                    //if we touch a moving platform not carrying us, let it know so that it doesn't move away without noticing
                    //I made moving platforms so I get to say they can onnly be root level GameObjects, saving some type checks
                    GameObject MaybePlatform = hitBuffer[i].collider.gameObject;
                    while (MaybePlatform && MaybePlatform.transform.parent != null)
                    {
                        MaybePlatform = MaybePlatform.transform.parent.gameObject;
                    }

                    MovingPlatformOnTrack Platform = MaybePlatform.GetComponent<MovingPlatformOnTrack>();
                    if (Platform && Platform.gameObject != riddenPlatform)
                    {
                        Platform.HandleKOContactMidUpdate(this);
                    }

                }

                if (bHitAWall)
                {
                    velocity = personalGravityDirection * Mathf.Max(Vector2.Dot(personalGravityDirection, velocity), 0.0f);
                }
                if(bHitATopOrBottom)
                {
                    velocity = lateralDirection * Vector2.Dot(lateralDirection, velocity);
                }
            }

            body.position += direction * distance;
            if(slopeMovement.magnitude > minMoveDistance)
            {
                PerformSlopeMovement(slopeMovement);
            }
        }

        //needed so movement that hits a slope can redo hit tests on that move but not cascade via recursion
        void PerformSlopeMovement(Vector2 move)
        {
            Vector2 direction = move.normalized;
            var distance = move.magnitude;

            //all we do is check if we hit anything in current direction of travel
            //and if we do cut off movement at the shortest distance
            var count = body.Cast(move, contactFilter, hitBuffer, distance + shellRadius);
            bool bHitAWall = false; //for airborne collisions, if we hit something horizontally then start 'falling' (we might still Stick instead)
            for (var i = 0; i < count; i++)
            {
                var currentNormal = hitBuffer[i].normal;

                //make sure this isn't a slight variation in slope or minor overlap
                if(Mathf.Abs(Vector2.Dot(direction, currentNormal)) > 0.9f)
                {
                    continue;
                }

                float modifiedDistance = hitBuffer[i].distance - (shellRadius);

                //remove shellDistance from actual move distance.
                if (modifiedDistance < distance)
                {
                    distance = modifiedDistance;
                    lastSurfacePoint = hitBuffer[i].point;
                    lastSurfaceNormal = hitBuffer[i].normal;
                    bHitAWall = true;
                }
            }

            body.position += direction * distance;
            if (bHitAWall)
            {
                velocity = personalGravityDirection * Mathf.Max(Vector2.Dot(personalGravityDirection, velocity), 0.0f);
            }
        }

    }
}