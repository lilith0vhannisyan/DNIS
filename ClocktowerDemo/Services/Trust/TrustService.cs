using System;
using ClocktowerDemo.Domain;

namespace ClocktowerDemo.Services.Trust
{
    public static class TrustService
    {
        public static bool IrisProgressive = true;

        public static void UpdateTrust(GameState state, string npc, string politenessLabel)
        {
            if (!state.Trust.TryGetValue(npc, out var st))
            {
                st = new NPCState();
                state.Trust[npc] = st;
            }

            if (npc == "Iris" && IrisProgressive)
            {
                if (politenessLabel == "polite")
                {
                    st.PoliteStreak++; st.ImpoliteStreak = 0;
                    if (st.Trust == -1 && st.PoliteStreak >= 2) st.Trust = 0;
                    else if (st.Trust == 0 && st.PoliteStreak >= 3) st.Trust = 1;
                }
                else if (politenessLabel == "impolite")
                {
                    st.ImpoliteStreak++; st.PoliteStreak = 0;
                    if (st.ImpoliteStreak >= 2) st.Trust = -1;
                }
                else
                {
                    st.PoliteStreak = 0; st.ImpoliteStreak = 0;
                }
            }
            else
            {
                if (politenessLabel == "polite") st.Trust = Math.Min(1, st.Trust + 1);
                else if (politenessLabel == "impolite") st.Trust = Math.Max(-1, st.Trust - 1);
                st.PoliteStreak = 0; st.ImpoliteStreak = 0;
            }
        }
    }
}
