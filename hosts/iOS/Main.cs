using Engine;
using Racers;

// The .NET iOS runtime needs Microsoft.iOS.dll at startup, but since SDL owns UIApplicationMain this
// host otherwise never touches an iOS API and the trimmer would drop the assembly.
Log.Info($"iOS host starting ({ObjCRuntime.Runtime.Arch})");

// SDL_RunApp enters UIApplicationMain with SDL's app delegate and never returns; frames are then
// driven by SDL's CADisplayLink.
GameHost.Run(() => new RacersGame(), RacersGame.Options);
