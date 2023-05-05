using System.Collections.Generic;
namespace BeamGameCode
{
    public static class BikeDemoData
    {
        private static readonly List<string> firstNames = new List<string>() {
            "Alice", "Bob", "Carol", "Don", "Evan", "Frank", "Gayle", "Herb",
            "Inez", "Jim", "Kayla", "Lara", "Mike", "Noel", "Orlando", "Paul",
            "Quentin", "Rachel", "Sam", "Terry", "Umberto", "Vera", "Will", "Xavier",
            "Yasmin", "Zack"
        };

        private static readonly List<string> lastNames = new List<string>() {
            "A.", "B.", "C.", "D.", "E.", "F.", "G.", "H.",
            "I.", "J.", "K.", "L.", "M.", "N.", "O.", "P.",
            "Q.", "R.", "S.", "T.", "U.", "V.", "W.", "X.",
            "Y.", "Z."
        };

        public static string RandomName()
        {
            return string.Format("{0} {1}",
                firstNames[(int)UnityEngine.Random.Range(0,firstNames.Count)],
                lastNames[(int)UnityEngine.Random.Range(0,lastNames.Count)] );
        }

        public static Team RandomTeam()
        {
            return Team.teamData[(int)UnityEngine.Random.Range(1.0f,Team.teamData.Count)]; // 0 is kTeamNone
        }

    }
}