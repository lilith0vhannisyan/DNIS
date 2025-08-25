namespace ClocktowerDemo.Domain
{
    public class NPCState
    {
        public int Trust { get; set; } = 0;         // -1..+1
        public int PoliteStreak { get; set; } = 0;
        public int ImpoliteStreak { get; set; } = 0;
    }
}
