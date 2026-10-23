using Engine;
using System;
using System.Reflection;
using System.Text;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

namespace Vo;

public static class Ease
{
    public enum Type
    {
        None,
        Linear,
        In, Out, InOut,
        InCubic, OutCubic, InOutCubic
    }

    public readonly static Dictionary< Type, System.Func< float, float > > Function = new Dictionary< Type, System.Func< float, float > > { 
        { Type.Linear, Linear },
        { Type.In, In }, { Type.Out, Out }, { Type.InOut, InOut },
        { Type.InCubic, InCubic }, { Type.OutCubic, OutCubic }, { Type.InOutCubic, InOutCubic },
    };

    public static float Linear ( float p )
    {
        return p;
    }

    public static float In ( float p )
    {
        return 1f - MathF.Cos( ( p * MathF.PI ) * 0.5f );
    }

    public static float Out ( float p )
    {
        return MathF.Sin( ( p * MathF.PI ) * 0.5f );
    }

    public static float InOut ( float p )
    {
        return -( MathF.Cos( MathF.PI * p ) - 1f ) * 0.5f;
    }

    public static float InCubic ( float p )
    {
        return p * p * p;
    }

    public static float OutCubic ( float p )
    {
        return 1f - MathF.Pow( 1f - p, 3 );
    }

    public static float InOutCubic ( float p )
    {
        return p < 0.5f ? 4f * p * p * p : 1f - MathF.Pow( -2f * p + 2f, 3 ) * 0.5f;
    }
}