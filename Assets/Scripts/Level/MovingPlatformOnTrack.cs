using SuperTiled2Unity;
using SuperTiled2Unity.Editor;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using Unity.Tutorials.Core.Editor;
using Unity.U2D.Physics;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Tilemaps;

namespace SuperMovingPlatform
{
    public class MovingPlatformOnTrack : MonoBehaviour
    {
        // How close we need to be to a track to start off attached to it
        public float MaxDistanceFromTrack = 16.0f;

        // Speed is in pixels per second
        public float m_Speed = 1.0f;
        public Vector2 m_InitialDirection = Vector2.right;

        public bool DebugDrawPath = false;

        // The path of this platform is made up of points from an edge collider
        private List<Vector2> m_Points;

        // Our current index between two edges of a path
        private int m_CurrentPointIndex = -1;

        // How we are going to advance through our edges (-1 or 1)
        private int m_IndexAdvance = 1;

        public void Start()
        {
            Setup();

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

        public void Setup()
        {
            Assert.IsNotNull(gameObject);
            
            GameObject goTilemap = GetComponentInChildren<SuperMap>().gameObject;
            Assert.IsNotNull(goTilemap);

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
            var P2 = new Vector2(B.x - A.x, B.y - A.y);
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

        private void Update()
        {
            if (m_CurrentPointIndex == -1)
            {
                //Debug.LogError("Platform is not attached to a track.");
                return;
            }

            float t = 1.0f;
            while (t > 0.0f)
            {
                t = MoveAlongTrack(t);
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
    }
}
