using MintECS;
using Racers;
using System.Collections;
using System.Collections.Generic;

namespace Racers;

public class RaceConfig
{
    public class UnitDef
    {
        public string id;
        public int x;
        public int y;
    }

    public int width;
    public int height;
    public UnitDef[] units;

    public string background;
}