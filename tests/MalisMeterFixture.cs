// Synthetic public interface only; no third-party source or game code.
using System.Collections.Generic;
namespace MalisDamageMeter {
    public class Counter {public int Total;}
    public class DamageCounter:Counter {public int PetTotal;public Counter Nanobots=new Counter();}
    public class SimpleCharData {public DamageCounter DamageSources=new DamageCounter();public Counter HealSource=new Counter();}
    public class PlayerCharData:SimpleCharData {}
    public class PetData:SimpleCharData {}
    public static class HitRegisters {public static Dictionary<int,SimpleCharData> Characters=new Dictionary<int,SimpleCharData>();}
    public class Config {public bool IsPaused;public float Seconds;public float GetElapsedTimeSeconds()=>Seconds;}
    public class MainWindow {public Config ViewSettings=new Config();}
    public class DamageMeterWindow:MainWindow {}
    public static class Main {public static DamageMeterWindow Window=new DamageMeterWindow();}
}
