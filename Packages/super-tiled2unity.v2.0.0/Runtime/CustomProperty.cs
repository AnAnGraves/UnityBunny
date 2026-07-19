using System;
using System.Collections.Generic;



namespace SuperTiled2Unity
{
    //Translates my custom Tiled enum Physics
    //these AREN'T independent flags, but were initially created as flags in Tiled, so they're still Bit values
    public enum SurfaceType : ushort
    {
        Invalid = 0,           //used if a valid surface type can't be found
        NormalSurface = 1 << 0,      //Surface you can stick to
        DeadlySurface = 1 << 1,      //Surface what kills you on contact
        NonStickSurface = 1 << 2,      //Surface you CAN'T stick to
        RepelSurface = 1 << 3       //Surface that reverses your velocity then reflects it across the surface normal (like light hitting a mirror)
    }

    //Translates my custom Tiled enum Gravity
    public enum GravityDirection : ushort
    {
        INVALID = 0,
        D       = 1,           
        DL      = 2,      
        L       = 3,      
        UL      = 4,      
        U       = 5,      
        UR      = 6,      
        R       = 7,      
        DR      = 8      
    }

    [Serializable]
    public class CustomProperty
    {
        public string m_Name;
        public string m_Type;
        public string m_Value;

        public bool IsEmpty => string.IsNullOrEmpty(m_Name);
    }

    // Helper extension methods
    public static class CustomPropertyListExtensions
    {
        public static bool TryGetProperty(this List<CustomProperty> list, string propertyName, out CustomProperty property)
        {
            if (list != null)
            {
                property = list.Find(p => String.Equals(p.m_Name, propertyName, StringComparison.OrdinalIgnoreCase));
                return property != null;
            }

            property = null;
            return false;
        }
    }
}
