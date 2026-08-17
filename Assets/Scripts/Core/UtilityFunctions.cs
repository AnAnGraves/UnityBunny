using NUnit.Framework;
using SuperTiled2Unity;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UIElements;
using static UtilityFunctions;

class UtilityFunctions
{
    //modified from code shared by Michael Borgwardt on Stack Overflow
    public static bool NearlyEqual(in float a, in float b, float epsilon = float.Epsilon)
    {
        float absA = Mathf.Abs(a);
        float absB = Mathf.Abs(b);
        float diff = Mathf.Abs(a - b);

        if (a == b)
        { // shortcut, handles infinities
            return true;
        }
        else if (a == 0 || b == 0 || absA + absB < float.MinValue)
        {
            // a or b is zero or both are extremely close to it
            // relative error is less meaningful here
            return diff < (epsilon);
        }
        else
        { // use relative error
            return diff / (absA + absB) < epsilon;
        }
    }

    public static bool NearlyEqual(in Vector2 a, in Vector2 b, float epsilon = float.Epsilon)
    {
        return NearlyEqual(a.x, b.x, epsilon) && NearlyEqual(a.y, b.y, epsilon);
    }

    public static List<SuperTile> FindTriggerTilesAtPoint(Vector2 point)
    {
        // Directly finds all 2D colliders overlapping the point
        Collider2D[] hitColliders = Physics2D.OverlapPointAll(point);
        List<Tilemap> overlappingTilemaps = new();
        List<SuperTile> outTiles = new();

        foreach (Collider2D col in hitColliders)
        {
            Transform MaybeTilemap = col.transform;
            while(MaybeTilemap && !MaybeTilemap.gameObject.GetComponent<SuperTileLayer>())
            {
                MaybeTilemap = MaybeTilemap.parent;
            }

            if (MaybeTilemap)
            {
                Tilemap asTileMap = MaybeTilemap.gameObject.GetComponent<Tilemap>();
                if(asTileMap)
                {
                    overlappingTilemaps.Add(asTileMap);
                }
            }
        }

        foreach(Tilemap map in overlappingTilemaps)
        {
            Vector3Int cell = map.WorldToCell(new Vector3(point.x, point.y, 0.0f));
            TileBase tile = map.GetTile<TileBase>(cell);
            if(tile is SuperTile sTile)
            {
                outTiles.Add(sTile);
            }
        }

        return outTiles;
    }

    public static Transform FindDeepChild(Transform root, string name)
    {
        Transform outTx = root.Find(name);
        if (outTx)
        { 
            return outTx; 
        }

        foreach (Transform child in root)
        {
            outTx = FindDeepChild(child, name);
            if (outTx)
            {
                break;
            }
        }

        return outTx;
    }

    //returns shortest edge-to-edge distance if bounds do not touch or overlap, or zero if they do
    //in other words, if output > 0 they are not in contact
    public static float ShortestSquareDistanceBetweenTwoBounds(Bounds A, Bounds B)
    {
        // Calculate the distance or overlap on each axis
        float distX = Mathf.Max(0, Mathf.Abs(A.center.x - B.center.x) - (A.extents.x + B.extents.x));
        float distY = Mathf.Max(0, Mathf.Abs(A.center.y - B.center.y) - (A.extents.y + B.extents.y));
        float distZ = Mathf.Max(0, Mathf.Abs(A.center.z - B.center.z) - (A.extents.z + B.extents.z));

        // Combine the axis distances into a 3D Euclidean distance
        return (distX * distX) + (distY * distY) + (distZ * distZ);
    }

    //modulo that keeps repeating the same series if you go below zero rather than inverting, e.g. Mod(-1,3) = 2, while -1 % 3 = -1
    public static float Mod(float a, float b)
    {
        float res = a % b;
        return res < 0 ? res + b : res;
    }

    //flags for encoding a complete ordering of three floats (named left middle and right) in one ushort value
    //could technically say X_LAST -> !(X_FIRST || X_LAST) and fit it into a byte (6 flags) but I don't think it's worth it unless it's like the last memory optimization available
    [Flags]
    public enum OrderThreeResult : ushort
    {
        LEFT_FIRST      = 1 << 1,
        MIDDLE_FIRST    = 1 << 2,
        RIGHT_FIRST     = 1 << 3,
        LEFT_SECOND     = 1 << 4,
        MIDDLE_SECOND   = 1 << 5,
        RIGHT_SECOND    = 1 << 6,
        LEFT_LAST       = 1 << 7,
        MIDDLE_LAST     = 1 << 8,
        RIGHT_LAST      = 1 << 9,
    }

    //returns a ushort of flags from OrderThreeResult that tell you the ordering of the three values
    public static OrderThreeResult OrderThree(float left, float middle, float right)
    {
        if(left < middle)
        {
            if(left < right) //left first
            {
                if(middle < right) //left, middle, right
                {
                    return OrderThreeResult.LEFT_FIRST | OrderThreeResult.MIDDLE_SECOND | OrderThreeResult.RIGHT_LAST;
                }
                else //left, right, middle
                {
                    return OrderThreeResult.LEFT_FIRST | OrderThreeResult.RIGHT_SECOND | OrderThreeResult.MIDDLE_LAST;
                }
            }
            else //left < middle && left > right -> right, left, middle
            {
                return OrderThreeResult.RIGHT_FIRST | OrderThreeResult.LEFT_SECOND | OrderThreeResult.MIDDLE_LAST;
            }
        }
        else // middle < left
        {
            if(middle < right) //middle first
            {
                if(left < right) //middle, left, right
                {
                    return OrderThreeResult.MIDDLE_FIRST | OrderThreeResult.LEFT_SECOND | OrderThreeResult.RIGHT_LAST;
                }
                else //middle, right, left
                {
                    return OrderThreeResult.MIDDLE_FIRST | OrderThreeResult.RIGHT_SECOND | OrderThreeResult.LEFT_LAST;
                }
            }
            else // middle < left && middle > right -> right, middle, left
            {
                return OrderThreeResult.RIGHT_FIRST | OrderThreeResult.MIDDLE_SECOND | OrderThreeResult.LEFT_LAST;
            }
        }
    }

    public static float SignedAngleBetweenAngles(float start, float end)
    {
        float delta = end - start;
        delta = Mod((delta + 180f), 360f) - 180f;
        return delta;
    }
}

