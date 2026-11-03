using System;
using AOSharp.Core;
using AOSharp.Core.UI;
using AOSharp.Core.Combat;

// Compiled against the previous runtime, then loaded unchanged by the maintained runtime.
public class FixturePlugin : IAOPluginEntry
{
    public void Init(string directory)
    {
#if FAIL
        throw new InvalidOperationException("inert startup failure");
#else
        Chat.RegisterCommand("nexusfixture", (command, arguments, window) => { });
        CombatHandler.Set(new CombatHandler());
#endif
    }
    public void Teardown() { }
}
