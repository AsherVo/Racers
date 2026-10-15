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
    public Data data;

    public static void Setup ( RacersGame game )
    {
        I = new Toolbox();
        I.game = game;
        I.load = new Loader();
        I.data = new Data();
    }

    public void Cleanup ()
    {
        load.Dispose();
        data.Dispose();
    }   
}
