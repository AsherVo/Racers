using RectangleF = System.Drawing.RectangleF;
using System.Numerics;
using System.Runtime.InteropServices;
using Engine;
using MintECS;

namespace Racers;

public class Toolbox
{
    public static Toolbox I { get; private set; }

    public RacersGame game;
    public Loader load;

    public static void Setup ( RacersGame game )
    {
        I = new Toolbox();
        I.game = game;
        I.load = new Loader();
    }

    public void Cleanup ()
    {
        load.Dispose();
    }   
}
