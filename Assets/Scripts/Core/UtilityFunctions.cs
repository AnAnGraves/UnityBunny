using System;
using System.Collections;
using UnityEngine;

class UtilityFunctions
{
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
}