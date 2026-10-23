using RectangleF = System.Drawing.RectangleF;
using System.Runtime.InteropServices;
using Engine;

namespace Racers;

public class Toolbox
{
    public static Toolbox I { get; private set; }

    public RacersGame game;
    public ContentManager content;
    public Loader load;
    public Data data;

    public static void Setup ( RacersGame game )
        => Setup( game.content, game );

    public static void Setup ( ContentManager content, RacersGame game = null )
    {
        I = new Toolbox();
        I.game = game;
        I.content = content;
        I.load = new Loader();
        I.data = new Data();
    }

    public void Cleanup ()
    {
        load.Dispose();
        data.Dispose();
    }   
}
