using Platformer.Mechanics;
using SuperTiled2Unity;
using SuperTiled2Unity.Editor;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using Unity.Tutorials.Core.Editor;
using Unity.U2D.Physics;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Tilemaps;

namespace SuperMovingPlatform
{
    [RequireComponent(typeof(Rigidbody2D))]
    public class MovingPlatformOnTrack : MonoBehaviour
    {
        // How close we need to be to a track to start off attached to it
        public float MaxDistanceFromTrack = 0.2f;

        // Speed is in pixels per second
        public float m_Speed = 1f;
        public Vector2 m_InitialDirection = Vector2.right;

        public bool DebugDrawPath = false;

        // The path of this platform is made up of points from an edge collider
        private List<Vector2> m_Points;

        // Our current index between two edges of a path
        private int m_CurrentPointIndex = -1;

        // How we are going to advance through our edges (-1 or 1)
        private int m_IndexAdvance = 1;

        //list of game objects currently considered to be riding on this platform - this is not quite guaranteed to equal the KOs in contact with it
        Dictionary<KinematicObject, int> carriedKOs = new Dictionary<KinematicObject, int>();

        public Vector2 lastVelocity
        {
            get;
            protected set;
        }

        public void Start()
        {
            IEnumerable<Collider2D> TrackCollidersInRange = FindObjectsByType<EdgeCollider2D>().Where(edge => edge.gameObject.layer == LayerMask.NameToLayer("Rail"));
            bool foundTrack = false;
            foreach(Collider2D col in TrackCollidersInRange)
            {
                if(col is EdgeCollider2D edge)
                {
                    foundTrack = AssignTrackIfClose(edge);
                    if (foundTrack) break;
                }
            }

            if(!foundTrack)
            {
                Debug.LogWarning("Could not find a track for platform.");
            }
        }

        public void Reset()
        {
            Assert.IsNotNull(gameObject);

            Rigidbody2D rbComp = gameObject.GetComponent<Rigidbody2D>();
            if(!rbComp)
            {
                rbComp = gameObject.AddComponent<Rigidbody2D>();
            }
            Assert.IsNotNull(rbComp);
            rbComp.bodyType = RigidbodyType2D.Static;
            rbComp.sharedMaterial = ST2USettings.instance.m_DefaultPhysMat;

            GameObject goTilemap = GetComponentInChildren<SuperMap>().gameObject;
            if (!goTilemap)
            {
                Debug.LogError("Moving platform requires an attached SuperMap - add one as a child and then reset the platform component.");
                return;
            }

            Tilemap[] tileLayers = goTilemap.GetComponentsInChildren<Tilemap>();
            Assert.IsTrue(tileLayers.Length > 0);

            
            Vector3 offset = Vector3.zero;
            bool pivotFound = false;
            foreach(Tilemap layer in tileLayers)
            {
                foreach(Vector3Int point in layer.cellBounds.allPositionsWithin)
                {
                    SuperTile tile = layer.GetTile<SuperTile>(point);
                    if (tile != null && tile.GetPropertyValueAsString(TiledStringDefinitions.SpecialTileKey) == TiledStringDefinitions.PlatformRegistrationKey)
                    {
                        pivotFound = true;

                        //for some reason "cell center" is the bottom left corner actually.
                        var halfCell = layer.layoutGrid.cellSize;
                        halfCell.Scale(goTilemap.transform.lossyScale);
                        halfCell /= 2.0f;

                        var regPoint = layer.GetCellCenterWorld(point)+halfCell;
                        offset = regPoint - transform.position;
                        goTilemap.transform.position -= offset; //translate tile map so that offset is at the origin

                        break;
                    }
                }

                if (pivotFound) break;
            }

            if(!pivotFound)
            {
                Debug.LogWarning("Platform does not contain a registration point.");
            }
            
        }


        public bool AssignTrackIfClose(EdgeCollider2D track)
        {
            if (m_CurrentPointIndex != -1)
            {
                // Already assigned to a track
                return false;
            }

            // Get the points of the track in world position
            // Bunny Custom: AND ACCOUNT FOR SCALE IN THAT!!!
            var points = track.points.Select(pt => {
                var tmp = pt;
                tmp.Scale(track.transform.lossyScale);
                return tmp + (Vector2)track.transform.position;
            }
            ).ToArray();
            Assert.IsTrue(points.Length > 1);

            var pos = gameObject.transform.position;
            var minDistance = float.MaxValue;
            var ptOnTrack = Vector2.zero;
            var ptIndex = -1;

            // Find closest position in the line segments passed in
            for (int i = 0; i < points.Length - 1; i++)
            {
                var A = points[i];
                var B = points[i + 1];
                var ptPotential = ClosestPointOnLineSegment(pos, A, B);

                var distance = Vector2.Distance(pos, ptPotential);

                if (distance < minDistance)
                {
                    minDistance = distance;
                    ptOnTrack = ptPotential;
                    ptIndex = i;
                }
            }

            // Are we close enough to the track to be attached to it?
            if (minDistance < MaxDistanceFromTrack)
            {
                // Use projection and initial direction to determine how we should travel from one edge to another along our track
                var nextIndex = (ptIndex + 1) % points.Length;
                var A = points[ptIndex];
                var B = points[nextIndex];
                if (Vector2.Dot(B - A, m_InitialDirection) < 0)
                {
                    // Reverse track direction
                    m_IndexAdvance = -1;
                    ptIndex = nextIndex;
                }
                else
                {
                    m_IndexAdvance = 1;
                }


                //TODO: remove point assign loop if not needed after all
                m_CurrentPointIndex = ptIndex;
                m_Points = points.Select(pt => pt).ToList();
                gameObject.transform.position = ptOnTrack;
                return true;
            }

            return false;
        }

        private Vector2 ClosestPointOnLineSegment(Vector2 P, Vector2 A, Vector2 B)
        {
            Vector2 P2 = new(B.x - A.x, B.y - A.y);
            var dot = P2.x * P2.x + P2.y * P2.y;
            var u = ((P.x - A.x) * P2.x + (P.y - A.y) * P2.y) / dot;

            if (u > 1)
            {
                u = 1;
            }
            else if (u < 0)
            {
                u = 0;
            }

            return A + (u * P2);
        }

        private void FixedUpdate()
        {
            Debug.DrawLine(transform.position, transform.position + (Vector3.up * MaxDistanceFromTrack), Color.aquamarine);
            Debug.DrawLine(transform.position, transform.position + (Vector3.down * MaxDistanceFromTrack), Color.aquamarine);
            Debug.DrawLine(transform.position, transform.position + (Vector3.left * MaxDistanceFromTrack), Color.aquamarine);
            Debug.DrawLine(transform.position, transform.position + (Vector3.right * MaxDistanceFromTrack), Color.aquamarine);
            if (m_CurrentPointIndex == -1)
            {
                //Debug.LogError("Platform is not attached to a track.");
                lastVelocity = Vector2.zero;
                return;
            }

            Vector2 startPos = transform.position;

            float t = 1.0f;
            while (t > 0.0f)
            {
                t = MoveAlongTrack(t);
            }

            Vector2 delta = (Vector2)(transform.position) - startPos;
            lastVelocity = delta / Time.deltaTime;

            List<KinematicObject> toRemove = new(); //can't modify list while iterating
            foreach(var pair in carriedKOs)
            {
                pair.Key.PlatformRideMovement(delta, gameObject, pair.Value == 0);

                //remove zero count KOs added mid-update
                if(pair.Value < 1)
                {
                    toRemove.Add(pair.Key);
                }
            }

            //NOW we can remove them
            foreach(var kine in toRemove)
            {
                carriedKOs.Remove(kine);
                kine.RemoveFromPlatform(gameObject);
            }

            for(int i = 0; i < m_Points.Count && DebugDrawPath; ++i)
            {
                Debug.DrawLine(m_Points[i], m_Points[(i + 1) % m_Points.Count], Color.magenta);
            }
        }

        private float MoveAlongTrack(float t)
        {
            // Move along an edge of our track as much as we can
            // If we end up stopping at an edge then return the portion of movement that is left over
            int numPoints = m_Points.Count;

            if(numPoints < 2)
            {
                return 0f;
            }

            int i = m_CurrentPointIndex;
            int j = m_CurrentPointIndex + m_IndexAdvance;

            if (j < 0)
            {
                j = numPoints - 1;
            }
            else if (j >= numPoints)
            {
                j = 0;
            }

            var A = m_Points[i];
            var B = m_Points[j];

            var BA = B - A;
            var dv = BA.normalized;

            var posCurrent = (Vector2)gameObject.transform.position;
            var posDesired = posCurrent + (dv * m_Speed * Time.deltaTime * t);

            var V1A = posCurrent - A;
            var V2A = posDesired - A;

            float dotLimit = Vector2.Dot(dv, BA);
            float dotStart = Vector2.Dot(dv, V1A);
            float dotDesired = Vector2.Dot(dv, V2A);

            if (dotDesired < dotLimit)
            {
                // We are within the bounds of the edge we are moving across
                // Fully move to our desired position
                gameObject.transform.position = posDesired;
                return 0;
            }
            else
            {
                // Our desired position is out out bounds
                // Lock to end position
                gameObject.transform.position = B;

                // Advance to the next edge in our track
                m_CurrentPointIndex = j;

                // How much movement do we have left over as a ratio?
                float leftOverRatio = (dotDesired - dotLimit) / (dotDesired - dotStart);
                return leftOverRatio * t;
            }
        }

        //it's possible for a KO to update before this platform, touch it and stop, and then have this platform move away before the collision is registered
        //to handle this, when KOs hit a moving platform they notify it, and if the platform is not tracking it the KO it is added to the carried map with
        //a contact count of zero, which causes it to update this frame then be removed, which should preserve their contact to be handled by the normal
        //collision handling. if we were tracking it already do nothing.
        public void HandleKOContactMidUpdate(KinematicObject kine)
        {
            if(!carriedKOs.ContainsKey(kine))
            {
                carriedKOs.Add(kine, 0);
            }
        }

        protected void OnCollisionEnter2D(Collision2D collision)
        {
            GameObject maybeKinematic = collision.collider.gameObject;
            while (maybeKinematic)
            {
                KinematicObject kine = maybeKinematic.GetComponent<KinematicObject>();
                if (kine)
                {
                    kine.RequestAddToPlatform(gameObject);
                    int count = 0;
                    carriedKOs.TryGetValue(kine, out count);
                    carriedKOs[kine] = ++count;
                    return; //shouldn't be a case where a KO is a child of a KO so we can just exit after the first
                }

                maybeKinematic = maybeKinematic.transform.parent ? maybeKinematic.transform.parent.gameObject : null;
            }
        }

        protected void OnCollisionExit2D(Collision2D collision)
        {
            GameObject maybeKinematic = collision.collider.gameObject;
            while (maybeKinematic)
            {
                KinematicObject kine = maybeKinematic.GetComponent<KinematicObject>();
                if (kine)
                {
                    int count = 0;
                    if(carriedKOs.TryGetValue(kine, out count))
                    {
                        --count;
                        if (count < 1)
                        {
                            carriedKOs.Remove(kine);
                            kine.RemoveFromPlatform(this.gameObject);
                        }
                        else
                        {
                            carriedKOs[kine] = count;
                        }
                    }
                    return; //shouldn't be a case where a KO is a child of a KO so we can just exit after the first
                }

                maybeKinematic = maybeKinematic.transform.parent ? maybeKinematic.transform.parent.gameObject : null;
            }
        }
    }
}
