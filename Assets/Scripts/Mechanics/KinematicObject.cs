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
        /// A custom gravity coefficient applied to this entity - DO NOT USE for zero gravity environments, see "zeroGToggle"
        /// </summary>
        public float personalGravityModifier = 1f;

        /// <summary>
        /// A gravity coefficient that should ONLY be used for turning gravity on and off, to preserve the value of the gravity modifier
        /// </summary>
        public float zeroGToggle = 1f;

        /// <summary>
        /// A custom gravity coefficient applied to this entity.
        /// </summary>
        protected Vector2 lastSurfacePoint = Vector2.zero;

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
        /// this is the only platform allowed to freely update our position
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
        public Vector2 PersonalGravity
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
        public Vector2 PersonalGravityDirection
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

        protected Vector2 AlongGround = new(1f, 0f); //default to +X
        protected Vector2 _groundNormal;
        public Vector2 GroundNormal
        {
            get => _groundNormal;
            set
            {
                _groundNormal = value;
                AlongGround = Vector2.Perpendicular(-_groundNormal); //Perpendicular is a 90 degree CCW rotation, but we expect the RIGHT vector which is CCW from the ANTI-normal
            }
        }


        /// <summary>
        /// The current velocity of the entity.
        /// </summary>
        public Vector2 velocity;

        bool _wasGrounded = false;
        bool _isGrounded = false;

        /// <summary>
        /// Is the entity currently sitting on a surface?
        /// </summary>
        /// <value></value>
        public bool IsGrounded 
        {
            get => _isGrounded;
            protected set
            {
                //_wasGrounded = _isGrounded;
                _isGrounded = value;
            }
        }

        public bool WasGrounded
        {
            get => _wasGrounded;
            set
            {
                _wasGrounded = value;
            }
        }

        protected Vector2 targetVelocity;
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
            velocity += value * (-PersonalGravityDirection);
        }

        /// <summary>
        /// Bounce the object's vertical velocity away from a point
        /// </summary>
        /// <param name="value"></param>
        public void Bounce(float value, Vector3 origin)
        {
            velocity = GetLateralComponent(velocity);
            velocity += value * PersonalGravityDirection * Mathf.Sign(Vector2.Dot(PersonalGravity, transform.position - origin));
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

        protected void AlignVelocityToGround()
        {
            float dir = Mathf.Sign(Vector2.Dot(velocity, AlongGround)); //we shouldn't be snapping more than 90 degrees so this should still work
            velocity = dir * velocity.magnitude * AlongGround;
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
            PersonalGravity = Physics2D.gravity;
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
            return Vector2.Dot(PersonalGravityDirection, vec) * PersonalGravityDirection;
        }

        protected Vector2 GetLateralComponent(in Vector2 vec)
        {
            return Vector2.Dot(lateralDirection, vec) * lateralDirection;
        }

        protected Vector2 GetAlongGroundComponent(in Vector2 vec)
        {
            return Vector2.Dot(AlongGround, vec) * AlongGround;
        }

        protected virtual void FixedUpdate()
        {
            targetVelocity = Vector2.zero;
            ComputeVelocity();

            //velocity.x = targetVelocity.x;
            velocity = (IsGrounded ? velocity - GetAlongGroundComponent(velocity) : GetVerticalComponent(velocity));
            Vector2 lateralVel = IsGrounded ? GetAlongGroundComponent(targetVelocity) : GetLateralComponent(targetVelocity);
            velocity += lateralVel;
            
            //gravity
            velocity += zeroGToggle * personalGravityModifier * Time.deltaTime * (IsGrounded ? -GroundNormal  * (Vector2.Dot(PersonalGravity, PersonalGravityDirection)) : PersonalGravity); //since we pre-calculate gravity direction every time we set gravity, this dot product is much faster than .magnitude

            if (bLeftPlatform)
            {
                if(riddenPlatform == null) velocity += platformVelocity;
                bLeftPlatform = false;
            }

            Vector2 pseudoXVel = (IsGrounded ? GetAlongGroundComponent(velocity) : lateralVel);
            var pseudoXMove = pseudoXVel * Time.deltaTime;
            var pseudoYMove = (velocity - pseudoXVel) * Time.deltaTime;

            Vector2 groundVel = GetAlongGroundComponent(velocity);
            bool canLandOnX = groundVel.magnitude > (velocity - groundVel).magnitude;

            bool snapped = snapAction.IsPressed();
            if (!drewVelThisFrame && snapped)
            {
                //draw what should be a box with a diagonal if all is working
                const float drawVelocityScale = 30.0f;

                Collider2D collider2d = gameObject.GetComponent<Collider2D>();
                Vector2 origin = collider2d ? collider2d.bounds.center : transform.position;

                Vector2 nominalMovement = velocity * Time.deltaTime;
                Vector2 vertical = origin + (pseudoYMove * drawVelocityScale);
                Vector2 horizontal = origin + (pseudoXMove * drawVelocityScale);
                Vector2 corner = (Vector2)(transform.position) + (nominalMovement * drawVelocityScale);
                Debug.DrawLine(origin, vertical, Color.blue, 1f);  // origin -> top left
                Debug.DrawLine(vertical, corner, Color.cyan, 1f);// top left -> top right 
                Debug.DrawLine(origin, horizontal, Color.red, 1f);  // origin -> bottom right
                Debug.DrawLine(horizontal, corner, Color.yellow, 1f); // bottom left -> top right
                Debug.DrawLine(origin, corner, Color.green, 1f);    // origin -> top right
                drewVelThisFrame = true;
            }

            WasGrounded = IsGrounded;
            IsGrounded = false;

            PerformMovement(pseudoXMove, false, canLandOnX);

            PerformMovement(pseudoYMove, true, true);

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

        protected virtual void HandleLanding()
        {
            velocity = GetAlongGroundComponent(velocity);
        }

        void PerformMovement(Vector2 move, bool yMovement, bool canHitGround) //true is movement on the axis of gravity, false is movement perpendicular to gravity
        {
            Vector2 slopeMovement = Vector2.zero;
            Vector2 direction = move.normalized;
            var distance = move.magnitude;
            Vector2 originalGroundNormal = GroundNormal;

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

                    float groundedness = Vector2.Dot(currentNormal, -(PersonalGravityDirection));

                    //is this surface flat enough to land on?
                    bool hitGround = groundedness > minFloorSurfaceness;
                    
                    if(!yMovement)
                    {
                        if(hitGround) //hit some kind of slope
                        {
                            //uphill direction vector
                            Vector2 uphillSlope = Vector2.Reflect(-1f * currentNormal, direction).normalized;

                            //move full distance up slope
                            modifiedSlopeMovement = uphillSlope * (Mathf.Sign(Vector2.Dot(uphillSlope, direction)) * distance);
                        }
                        else //hit a surface we couldn't land on while airborne
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
                        if (hitGround && yMovement)
                        {
                            IsGrounded = hitGround;
                            lastSurfacePoint = hitBuffer[i].point;
                            GroundNormal = currentNormal;
                        }
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

                if (IsGrounded && WasGrounded && (originalGroundNormal != GroundNormal)) //if we were already on the ground, this is just a surface change and we should keep our speed along the new surface
                {
                    AlignVelocityToGround();
                }

                if (bHitAWall)
                {
                    velocity = PersonalGravityDirection * Mathf.Max(Vector2.Dot(PersonalGravityDirection, velocity), 0.0f);
                }
                if(bHitATopOrBottom)
                {
                    HandleLanding();
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

                //make sure this isn't actually part of the slope
                if(Mathf.Abs(Vector2.Dot(direction, currentNormal)) > 0.9f)
                {
                    continue;
                }

                float modifiedDistance = hitBuffer[i].distance - (shellRadius);

                //remove shellDistance from actual move distance.
                if (modifiedDistance < distance)
                {
                    //collisions on an up-slope move must necessarily be against walls (rel. to gravity) so we can never land on/slide up them in this move
                    distance = modifiedDistance;
                    //lastSurfacePoint = hitBuffer[i].point;
                    //GroundNormal = hitBuffer[i].normal;
                    bHitAWall = true;
                }
            }

            body.position += direction * distance;
            lastSurfacePoint += direction * distance; //also update where we're standing
            if (bHitAWall)
            {
                velocity = PersonalGravityDirection * Mathf.Max(Vector2.Dot(PersonalGravityDirection, velocity), 0.0f);
            }
        }

    }
}