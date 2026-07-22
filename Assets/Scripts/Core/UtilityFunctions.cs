using NUnit.Framework;
using SuperTiled2Unity;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

class UtilityFunctions
{
    public const string SurfaceTypeKey = "SurfaceType";

    //modified from code shared by Michael Borgwardt on Stack Overflow
    public static bool NearlyEqual(in float a, in float b)
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
            return diff < (float.Epsilon);
        }
        else
        { // use relative error
            return diff / (absA + absB) < float.Epsilon;
        }
    }

    public static bool NearlyEqual(in Vector2 a, in Vector2 b)
    {
        return NearlyEqual(a.x, b.x) && NearlyEqual(a.y, b.y);
    }

    public static List<SuperTile> FindTriggerTilesAtPoint(Vector2 point)
    {
        // Directly finds all 2D colliders overlapping the point
        Collider2D[] hitColliders = Physics2D.OverlapPointAll(point);
        List<Tilemap> overlappingTilemaps = new List<Tilemap>();
        List<SuperTile> outTiles = new List<SuperTile>();

        foreach (Collider2D col in hitColliders)
        {
            if (col.isTrigger && col.gameObject && col.gameObject.transform.parent && col.gameObject.transform.parent.parent && col.gameObject.transform.parent.parent.gameObject)
            {
                Tilemap asTileMap = col.gameObject.transform.parent.parent.gameObject.GetComponent<Tilemap>();
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
}